using Sim.Core;

namespace Net;

/// <summary>
/// How long a loopback link holds each message before the other end can receive it: a fixed
/// one-way latency, plus a random extra of up to <paramref name="Jitter"/> drawn for each message.
/// </summary>
public readonly record struct LinkConditions(TimeSpan Latency, TimeSpan Jitter);

/// <summary>
/// Connections inside one process, for single-player and for tests. Messages cross it as bytes,
/// as they would a real network. It has a clock of its own that only <see cref="Advance"/> moves,
/// so a test decides exactly when each message arrives, and a seeded generator for the jitter, so
/// the same seed gives the same run every time.
/// </summary>
public sealed class LoopbackNetwork
{
    private readonly DeterministicRandom _random;

    /// <param name="seed">Seeds the jitter. It has nothing to do with the simulation's seed.</param>
    public LoopbackNetwork(ulong seed = 0) => _random = new DeterministicRandom(seed);

    /// <summary>The time on the network's clock.</summary>
    public TimeSpan Now { get; private set; }

    /// <summary>Makes a connection and returns its two ends. With no conditions a message arrives at once.</summary>
    public LoopbackLink Connect(LinkConditions conditions = default) => new(this, conditions);

    /// <summary>Moves the clock forward. Messages whose time has come can then be received.</summary>
    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        Now += elapsed;
    }

    internal TimeSpan DrawDelay(LinkConditions conditions)
    {
        var jitter = conditions.Jitter.Ticks == 0
            ? 0L
            : (long)_random.NextUInt64(checked((ulong)conditions.Jitter.Ticks + 1));
        return conditions.Latency + TimeSpan.FromTicks(jitter);
    }
}

/// <summary>
/// One loopback connection: the end the host session gets and the end the client session gets.
/// </summary>
public sealed class LoopbackLink
{
    private readonly LoopbackNetwork _network;
    private readonly End _hostEnd;
    private readonly End _clientEnd;
    private LinkConditions _conditions;

    internal LoopbackLink(LoopbackNetwork network, LinkConditions conditions)
    {
        _network = network;
        Conditions = conditions;
        _hostEnd = new End(this);
        _clientEnd = new End(this);
        _hostEnd.Other = _clientEnd;
        _clientEnd.Other = _hostEnd;
    }

    public IConnection HostEnd => _hostEnd;

    public IConnection ClientEnd => _clientEnd;

    /// <summary>
    /// The latency and jitter of both directions. A change applies to messages sent after it; a
    /// message never overtakes one sent before it, so lowering the latency does not reorder them.
    /// </summary>
    public LinkConditions Conditions
    {
        get => _conditions;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value.Latency, TimeSpan.Zero, nameof(value));
            ArgumentOutOfRangeException.ThrowIfLessThan(value.Jitter, TimeSpan.Zero, nameof(value));
            _conditions = value;
        }
    }

    private sealed class End(LoopbackLink link) : IConnection
    {
        // A null message marks the point at which the other end closed.
        private readonly Queue<(TimeSpan ArrivesAt, byte[]? Message)> _inbox = new();
        private bool _open = true;

        public End Other { get; set; } = null!;

        public bool IsOpen => _open && !(HasArrival && _inbox.Peek().Message is null);

        private bool HasArrival => _inbox.Count > 0 && _inbox.Peek().ArrivesAt <= link._network.Now;

        public void Send(ReadOnlySpan<byte> message)
        {
            if (IsOpen)
            {
                Other.Accept(message.ToArray());
            }
        }

        public bool TryReceive(out ReadOnlyMemory<byte> message)
        {
            message = default;
            if (!_open || !HasArrival)
            {
                return false;
            }

            var arrived = _inbox.Dequeue().Message;
            if (arrived is null)
            {
                _open = false;
                _inbox.Clear();
                return false;
            }

            message = arrived;
            return true;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                _open = false;
                return;
            }

            _open = false;
            _inbox.Clear();
            Other.Accept(null);
        }

        private void Accept(byte[]? message)
        {
            if (!_open)
            {
                return;
            }

            // Only the oldest message is ever looked at, so one that would have been quicker waits
            // behind a slower one sent before it, as on any reliable ordered channel.
            _inbox.Enqueue((link._network.Now + link._network.DrawDelay(link._conditions), message));
        }
    }
}
