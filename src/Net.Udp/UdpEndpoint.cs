using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using LiteNetLib;

namespace Net.Udp;

/// <summary>
/// One UDP socket and the connections that run over it: what <see cref="UdpListener"/> and
/// <see cref="UdpConnector"/> share. It owns the LiteNetLib manager and is the only code that
/// talks to it.
/// </summary>
/// <remarks>
/// <para>
/// LiteNetLib runs here without threads of its own, and every call into it is made under one
/// lock. Left to its own threads, version 2.1.5 can lose or repeat the first message of a
/// connection: its receive thread and the caller's thread each make the peer's reliable channel
/// when both need it for the first time at the same moment, and one of the two is thrown away
/// with what was put into it. Under the lock nothing in the library runs at the same time as
/// anything else in it.
/// </para>
/// <para>
/// Two threads take the lock. The caller's, in <see cref="Poll"/> and when it sends. And a
/// keep-alive thread of this class, which reads the socket, acknowledges, resends and answers the
/// other end's pings every few milliseconds, so that a connection survives a caller that does not
/// poll for a while, such as a game that hangs on a long frame. What the library reports on
/// either thread is only written down. It is acted on in <see cref="Poll"/>, on the caller's
/// thread, so no connection changes while the session is in the middle of an update.
/// </para>
/// <para>
/// Every payload starts with one byte: <see cref="DataFrame"/> before a session's message, or
/// <see cref="CloseFrame"/> alone for "this end sends nothing more". The close notice exists
/// because LiteNetLib's own disconnect drops whatever is still queued or unacknowledged, so a
/// message sent just before it can be lost. The notice travels in line behind the messages, and
/// the end that receives it is the one that disconnects.
/// </para>
/// </remarks>
internal sealed class UdpEndpoint : ILiteNetEventListener, IDisposable
{
    private const byte DataFrame = 0;
    private const byte CloseFrame = 1;

    /// <summary>
    /// The most pieces one message may arrive in. A whole message with its frame byte takes 65
    /// at the smallest packet size the library uses; anything that claims more is not ours, and
    /// is dropped before memory is set aside for it.
    /// </summary>
    private const ushort MaxFragments = 128;

    /// <summary>How long the keep-alive thread sleeps when nothing wakes it.</summary>
    private const int KeepAliveIntervalMilliseconds = 15;

    private static readonly byte[] CloseNotice = [CloseFrame];

    /// <summary>Held for every call into the library, and for <see cref="_reported"/>.</summary>
    private readonly Lock _gate = new();

    private readonly LiteNetManager _manager;
    private readonly UdpOptions _options;
    private readonly List<UdpConnection> _connections = [];
    private readonly Queue<UdpConnection> _incoming = new();
    private readonly byte[] _frame = new byte[ProtocolInfo.MaxMessageSize + 1];
    private readonly Thread _keepAlive;
    private readonly AutoResetEvent _wake = new(false);

    /// <summary>What the library has reported and <see cref="Poll"/> has not acted on yet, in order.</summary>
    private List<Report> _reported = [];

    /// <summary>The list <see cref="Poll"/> emptied last, to be filled again.</summary>
    private List<Report> _spare = [];

    private long _lastUpdate = Stopwatch.GetTimestamp();
    private volatile bool _stopKeepAlive;
    private volatile ExceptionDispatchInfo? _keepAliveFailure;
    private bool _disposed;

    internal UdpEndpoint(IPAddress address, int port, UdpOptions? options)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, IPEndPoint.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        _options = options ?? new UdpOptions();
        _options.Validate();

        var (addressV4, addressV6) = Families(address);
        var connectTimeout = (int)_options.ConnectTimeout.TotalMilliseconds;
        var reconnectDelay = Math.Clamp(connectTimeout / 10, 50, 500);
        var disconnectTimeout = Math.Max(1, (int)_options.DisconnectTimeout.TotalMilliseconds);
        _manager = new LiteNetManager(this)
        {
            AutoRecycle = true,
            IPv6Enabled = addressV6 is not null,
            DisconnectTimeout = disconnectTimeout,

            // The library's keep-alive. A quiet connection must hear several of them within the
            // timeout, or it would be given up on for being quiet.
            PingInterval = Math.Clamp(disconnectTimeout / 4, 1, 1000),
            ReconnectDelay = reconnectDelay,
            MaxConnectAttempts = Math.Max(0, ((connectTimeout + reconnectDelay - 1) / reconnectDelay) - 1),
            MaxFragmentsCount = MaxFragments,
        };

        // Manual mode: the library starts no thread and does nothing until it is called.
        if (!_manager.Start(addressV4, addressV6 ?? IPAddress.IPv6None, port, manualMode: true))
        {
            throw new IOException($"UDP port {port} on {address} could not be bound. Another program may be using it.");
        }

        _keepAlive = new Thread(KeepAlive) { IsBackground = true, Name = $"Net.Udp keep-alive ({_manager.LocalPort})" };
        _keepAlive.Start();
    }

    private enum ReportKind
    {
        Request,
        Connected,
        Disconnected,
        Received,
    }

    /// <summary>The port the socket is bound to.</summary>
    internal int Port => _manager.LocalPort;

    /// <summary>Whether a request to connect is taken. A connector takes none.</summary>
    internal bool AcceptsConnections { get; set; }

    /// <summary>The time on a clock that only goes forward. Where it started does not matter.</summary>
    internal static TimeSpan Now => Stopwatch.GetElapsedTime(0);

    internal TimeSpan CloseTimeout => _options.CloseTimeout;

    /// <summary>The connections whose link is still up, those that are closing included.</summary>
    internal int ConnectionCount => _connections.Count;

    /// <summary>
    /// Takes in everything that has happened on the socket since the last call, and gives up on
    /// closes the other end has not confirmed in time.
    /// </summary>
    internal void Poll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Pump();
    }

    internal UdpConnection Connect(IPEndPoint remote)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(remote);

        LiteNetPeer? peer;
        lock (_gate)
        {
            peer = _manager.Connect(remote, UdpInfo.ConnectKey);
        }

        if (peer is null || peer.Tag is not null)
        {
            throw new InvalidOperationException($"There is already a connection with {remote}.");
        }

        var connection = new UdpConnection(this, peer, connected: false);
        peer.Tag = connection;
        _connections.Add(connection);
        return connection;
    }

    internal bool TryAccept(out UdpConnection connection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _incoming.TryDequeue(out connection!);
    }

    internal void SendData(LiteNetPeer peer, ReadOnlySpan<byte> message)
    {
        _frame[0] = DataFrame;
        message.CopyTo(_frame.AsSpan(1));
        lock (_gate)
        {
            peer.Send(_frame.AsSpan(0, message.Length + 1), DeliveryMethod.ReliableOrdered);
        }

        // The keep-alive thread puts it on the wire now rather than at its next turn. By the
        // time it has woken, whatever else the caller sends in the same breath is queued too.
        _wake.Set();
    }

    internal void SendCloseNotice(LiteNetPeer peer)
    {
        lock (_gate)
        {
            peer.Send(CloseNotice, DeliveryMethod.ReliableOrdered);
        }

        _wake.Set();
    }

    /// <summary>Cuts the link now. What has not reached the other end is lost.</summary>
    internal void Cut(UdpConnection connection)
    {
        if (connection.LinkGone)
        {
            return;
        }

        lock (_gate)
        {
            _manager.DisconnectPeer(connection.Peer);
        }

        Forget(connection, UdpCloseReason.ClosedHere);
    }

    /// <summary>
    /// Closes every connection in order, waits for the other ends to confirm for at most
    /// <see cref="UdpOptions.CloseTimeout"/>, and lets go of the socket and the keep-alive thread.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var connection in _connections.ToArray())
        {
            connection.Close();
        }

        var deadline = Now + CloseTimeout;
        while (_connections.Exists(connection => connection.IsClosing) && Now < deadline)
        {
            Thread.Sleep(1);
            Pump(rethrow: false);
        }

        _disposed = true;
        _stopKeepAlive = true;
        _wake.Set();
        _keepAlive.Join();
        _wake.Dispose();
        lock (_gate)
        {
            _manager.Stop();
        }

        foreach (var connection in _connections.ToArray())
        {
            Forget(connection, UdpCloseReason.ClosedHere);
        }
    }

    // The library calls these four from inside Drive, or from inside a call this class makes
    // into it: always under the lock, on the caller's thread or on the keep-alive thread. They
    // only write down what happened.
    void ILiteNetEventListener.OnConnectionRequest(LiteConnectionRequest request) =>
        _reported.Add(new Report(ReportKind.Request, Request: request));

    void ILiteNetEventListener.OnPeerConnected(LiteNetPeer peer) =>
        _reported.Add(new Report(ReportKind.Connected, peer));

    void ILiteNetEventListener.OnPeerDisconnected(LiteNetPeer peer, DisconnectInfo disconnectInfo) =>
        _reported.Add(new Report(ReportKind.Disconnected, peer, Reason: disconnectInfo.Reason));

    void ILiteNetEventListener.OnNetworkReceive(LiteNetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod) =>
        _reported.Add(new Report(ReportKind.Received, peer, Payload: reader.GetRemainingBytes(), Method: deliveryMethod));

    private static (IPAddress V4, IPAddress? V6) Families(IPAddress address)
    {
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return (IPAddress.Any, IPAddress.IPv6Any);
        }

        if (IPAddress.IsLoopback(address))
        {
            return (IPAddress.Loopback, IPAddress.IPv6Loopback);
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return (address, null);
        }

        throw new ArgumentException(
            "Only an IPv4 address, a loopback address or an 'any' address can be bound. To take IPv6 connections, bind IPAddress.Any, which binds both families.",
            nameof(address));
    }

    private static UdpCloseReason ReasonFor(DisconnectReason reason) => reason switch
    {
        DisconnectReason.DisconnectPeerCalled => UdpCloseReason.ClosedHere,
        DisconnectReason.Timeout => UdpCloseReason.TimedOut,
        DisconnectReason.ConnectionFailed or DisconnectReason.UnknownHost => UdpCloseReason.ConnectFailed,
        DisconnectReason.ConnectionRejected or DisconnectReason.InvalidProtocol => UdpCloseReason.Refused,
        DisconnectReason.HostUnreachable or DisconnectReason.NetworkUnreachable => UdpCloseReason.Unreachable,
        _ => UdpCloseReason.ClosedByRemote,
    };

    /// <summary>
    /// One turn of the library: reads what is on the socket and lets every peer acknowledge,
    /// resend, ping and time out. Call it with the lock held.
    /// </summary>
    private void Drive()
    {
        _manager.PollEvents();

        // The library keeps its timers in single-precision milliseconds, where a step much
        // smaller than a millisecond added to several seconds is rounded off. So time is handed
        // over a millisecond or more at a time. A call in between still sends what is queued.
        var now = Stopwatch.GetTimestamp();
        var elapsed = (float)Stopwatch.GetElapsedTime(_lastUpdate, now).TotalMilliseconds;
        if (elapsed >= 1f)
        {
            _lastUpdate = now;
        }
        else
        {
            elapsed = 0f;
        }

        _manager.ManualUpdate(elapsed);
    }

    private void KeepAlive()
    {
        try
        {
            while (!_stopKeepAlive)
            {
                _wake.WaitOne(KeepAliveIntervalMilliseconds);
                lock (_gate)
                {
                    if (!_stopKeepAlive)
                    {
                        Drive();
                    }
                }
            }
        }
        catch (Exception failure)
        {
            // Not swallowed: the caller's next Poll throws it, where it can be seen and handled.
            // A thread that died silently would only show as connections lost on a long frame.
            _keepAliveFailure = ExceptionDispatchInfo.Capture(failure);
        }
    }

    private void Pump(bool rethrow = true)
    {
        if (rethrow)
        {
            _keepAliveFailure?.Throw();
        }

        List<Report> reports;
        lock (_gate)
        {
            Drive();
            reports = _reported;
            _reported = _spare;
        }

        // Outside the lock. What acting on one report makes the library report in turn, such as
        // the disconnect it confirms after a cut, is written to the other list for the next call.
        foreach (var report in reports)
        {
            Act(report);
        }

        reports.Clear();
        _spare = reports;

        var time = Now;
        for (var index = _connections.Count - 1; index >= 0; index--)
        {
            var connection = _connections[index];
            if (connection.LingerUntil is { } until && time >= until)
            {
                Cut(connection);
            }
        }
    }

    private void Act(Report report)
    {
        switch (report.Kind)
        {
            case ReportKind.Request:
                Answer(report.Request!);
                break;

            // The library also reports a peer the listener accepted, which is connected already.
            case ReportKind.Connected when report.Peer!.Tag is UdpConnection { LinkGone: false } connection:
                connection.OnConnected();
                break;

            case ReportKind.Disconnected when report.Peer!.Tag is UdpConnection connection:
                Forget(connection, ReasonFor(report.Reason));
                break;

            case ReportKind.Received when report.Peer!.Tag is UdpConnection { LinkGone: false } connection:
                Receive(connection, report.Payload!, report.Method);
                break;

            default:
                break;
        }
    }

    private void Answer(LiteConnectionRequest request)
    {
        LiteNetPeer? peer = null;
        lock (_gate)
        {
            if (AcceptsConnections && _connections.Count < _options.MaxConnections)
            {
                // Refuses by itself when the key is not ours, and returns nothing.
                peer = request.AcceptIfKey(UdpInfo.ConnectKey);
            }
            else
            {
                request.Reject();
            }
        }

        if (peer is { Tag: null })
        {
            var connection = new UdpConnection(this, peer, connected: true);
            peer.Tag = connection;
            _connections.Add(connection);
            _incoming.Enqueue(connection);
        }
    }

    private void Receive(UdpConnection connection, byte[] payload, DeliveryMethod method)
    {
        var kind = payload.Length > 0 && method == DeliveryMethod.ReliableOrdered ? payload[0] : byte.MaxValue;
        if (kind == DataFrame && payload.Length - 1 <= ProtocolInfo.MaxMessageSize)
        {
            connection.OnData(payload.AsMemory(1));
        }
        else
        {
            // The close notice, after which the other end waits for this one to cut the link. Or
            // something this code never sends, from an end that cannot be trusted further.
            if (kind == CloseFrame && payload.Length == 1)
            {
                connection.OnCloseNotice();
            }

            Cut(connection);
        }
    }

    private void Forget(UdpConnection connection, UdpCloseReason reason)
    {
        if (!connection.LinkGone)
        {
            connection.OnLinkGone(reason);
            _connections.Remove(connection);
        }
    }

    private readonly record struct Report(
        ReportKind Kind,
        LiteNetPeer? Peer = null,
        LiteConnectionRequest? Request = null,
        byte[]? Payload = null,
        DeliveryMethod Method = default,
        DisconnectReason Reason = default);
}
