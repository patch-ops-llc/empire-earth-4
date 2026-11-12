using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Net.Udp.Tests;

/// <summary>What a <see cref="UdpRelay"/> does to the datagrams that pass through it, in both directions.</summary>
/// <param name="Latency">How long every datagram is held.</param>
/// <param name="Jitter">The most a datagram is held on top of that. Each one draws its own, so they overtake each other.</param>
/// <param name="Loss">The chance, from 0 to 1, that a datagram is thrown away.</param>
/// <param name="Blocked">Nothing passes at all, like a pulled cable.</param>
/// <param name="KeepBulky">
/// A datagram of <see cref="BulkySize"/> bytes or more is kept back for as long as this is set,
/// and passed on when it is turned off. The library's own traffic, its acknowledgements and
/// keep-alives, is far smaller, and so is a short message. A test that sends one message of that
/// size and a short one after it decides which arrives first, and no clock has a say in it.
/// </param>
internal sealed record RelayConditions(
    TimeSpan Latency = default,
    TimeSpan Jitter = default,
    double Loss = 0,
    bool Blocked = false,
    bool KeepBulky = false)
{
    public const int BulkySize = 500;

    public static RelayConditions Milliseconds(int latency, int jitter = 0, double loss = 0) =>
        new(TimeSpan.FromMilliseconds(latency), TimeSpan.FromMilliseconds(jitter), loss);
}

/// <summary>
/// A bad network between real sockets on one machine. Clients send to the relay's port instead of
/// the host's; the relay passes each datagram on, late, out of order or not at all, and passes the
/// answers back the same way.
/// </summary>
/// <remarks>
/// LiteNetLib can simulate latency and loss itself, but only in a debug build of the library, and
/// the package is a release build. So the tests bring their own. The relay works on datagrams and
/// knows nothing of LiteNetLib, which is also the more honest test: the library sees exactly what
/// a bad network would show it. It runs on a thread of its own, binds the local address only, and
/// takes ports the system picks.
/// </remarks>
internal sealed class UdpRelay : IDisposable
{
    private readonly Socket _front = NewSocket();
    private readonly IPEndPoint _target;
    private readonly Dictionary<IPEndPoint, Socket> _upstreamOf = [];
    private readonly Dictionary<Socket, IPEndPoint> _clientOf = [];
    private readonly PriorityQueue<Held, (long Due, long Order)> _held = new();
    private readonly Queue<Held> _kept = new();
    private readonly Random _random;
    private readonly Thread _thread;
    private readonly byte[] _buffer = new byte[65536];
    private volatile RelayConditions _conditions;
    private volatile bool _stop;
    private long _order;
    private int _forwarded;
    private int _dropped;
    private int _overtaken;
    private int _largestPassedAKept;
    private long _lastOrderOut = -1;

    /// <param name="targetPort">The port of the listener on the local address.</param>
    public UdpRelay(int targetPort, RelayConditions? conditions = null, int seed = 1)
    {
        _target = new IPEndPoint(IPAddress.Loopback, targetPort);
        _conditions = conditions ?? new RelayConditions();
        _random = new Random(seed);
        Port = ((IPEndPoint)_front.LocalEndPoint!).Port;
        _thread = new Thread(Run) { IsBackground = true, Name = $"UdpRelay({Port})" };
        _thread.Start();
    }

    /// <summary>The port clients connect to in place of the host's.</summary>
    public int Port { get; }

    /// <summary>Can be changed while datagrams flow. Those already held keep the delay they drew.</summary>
    public RelayConditions Conditions
    {
        get => _conditions;
        set => _conditions = value;
    }

    /// <summary>How many datagrams were passed on.</summary>
    public int Forwarded => Volatile.Read(ref _forwarded);

    /// <summary>How many datagrams were thrown away.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>How many datagrams were passed on after one that had come in later.</summary>
    public int Overtaken => Volatile.Read(ref _overtaken);

    /// <summary>
    /// The size of the largest datagram that was passed on while another was being kept back,
    /// see <see cref="RelayConditions.KeepBulky"/>. It is set once the datagram has been handed
    /// to the system, so the receiver can read it.
    /// </summary>
    public int LargestPassedAKept => Volatile.Read(ref _largestPassedAKept);

    public void Dispose()
    {
        _stop = true;
        _thread.Join();
        _front.Dispose();
        foreach (var upstream in _clientOf.Keys)
        {
            upstream.Dispose();
        }
    }

    private static Socket NewSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (OperatingSystem.IsWindows())
        {
            // Windows otherwise fails the next receive on a socket after a datagram it sent found
            // nobody listening. SIO_UDP_CONNRESET, turned off.
            socket.IOControl(unchecked((int)0x9800000C), [0], null);
        }

        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return socket;
    }

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        var readable = new List<Socket>();
        while (!_stop)
        {
            readable.Clear();
            readable.Add(_front);
            readable.AddRange(_clientOf.Keys);
            Socket.Select(readable, null, null, 1000);

            foreach (var socket in readable)
            {
                Receive(socket, clock.Elapsed.Ticks);
            }

            var now = clock.Elapsed.Ticks;
            if (_kept.Count > 0 && !_conditions.KeepBulky)
            {
                // Let go, in the order they came. They are due at once.
                while (_kept.TryDequeue(out var kept))
                {
                    _held.Enqueue(kept, (now, kept.Order));
                }
            }

            while (_held.TryPeek(out var held, out var priority) && priority.Due <= now)
            {
                _held.Dequeue();
                Volatile.Write(ref _forwarded, _forwarded + 1);
                if (held.Order < _lastOrderOut)
                {
                    Volatile.Write(ref _overtaken, _overtaken + 1);
                }

                _lastOrderOut = Math.Max(_lastOrderOut, held.Order);
                try
                {
                    held.Via.SendTo(held.Data, held.To);
                }
                catch (SocketException)
                {
                    // The receiver has gone. A network does not care either.
                    continue;
                }

                if (_kept.Count > 0 && held.Data.Length > _largestPassedAKept)
                {
                    Volatile.Write(ref _largestPassedAKept, held.Data.Length);
                }
            }
        }
    }

    private void Receive(Socket socket, long now)
    {
        while (socket.Available > 0)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int length;
            try
            {
                length = socket.ReceiveFrom(_buffer, ref from);
            }
            catch (SocketException)
            {
                // Windows reports here that an earlier datagram found nobody at the other end.
                continue;
            }

            Socket via;
            IPEndPoint to;
            if (socket == _front)
            {
                var client = (IPEndPoint)from;
                if (!_upstreamOf.TryGetValue(client, out via!))
                {
                    via = NewSocket();
                    _upstreamOf.Add(client, via);
                    _clientOf.Add(via, client);
                }

                to = _target;
            }
            else
            {
                via = _front;
                to = _clientOf[socket];
            }

            var conditions = _conditions;
            if (conditions.Blocked || _random.NextDouble() < conditions.Loss)
            {
                Volatile.Write(ref _dropped, _dropped + 1);
                continue;
            }

            var delay = conditions.Latency + (conditions.Jitter * _random.NextDouble());
            var order = _order++;
            var held = new Held(_buffer.AsSpan(0, length).ToArray(), via, to, order);
            if (conditions.KeepBulky && length >= RelayConditions.BulkySize)
            {
                _kept.Enqueue(held);
                continue;
            }

            _held.Enqueue(held, (now + delay.Ticks, order));
        }
    }

    private sealed record Held(byte[] Data, Socket Via, IPEndPoint To, long Order);
}
