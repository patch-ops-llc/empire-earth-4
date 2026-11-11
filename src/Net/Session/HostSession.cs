using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Sim.Core;
using Sim.Serialization;

namespace Net;

/// <summary>
/// The host's side of a lockstep session. It decides the tick every command runs in and nothing
/// else: it holds no world and runs no simulation. The machine that hosts also plays through a
/// <see cref="ClientSession"/> of its own, connected over the loopback, so the host's player goes
/// through exactly the path every other player does.
/// </summary>
/// <remarks>
/// <para>
/// Once the match runs, the host issues one <see cref="TickBundle"/> per tick interval, empty or
/// not, holding the commands that reached it since the last one, in the order they arrived. That
/// order is the order every peer runs them in. The host never waits for a client: one that falls
/// too far behind is dropped and the others play on.
/// </para>
/// <para>
/// The command delay is a head start. With a delay of d the host issues the bundle of a tick d - 1
/// ticks before that tick is due, so a command the host receives runs d ticks later, and a client
/// whose bundles take less than the head start to arrive never has to wait for one.
/// </para>
/// <para>
/// It reads no clock and uses no thread. The caller says how much time has passed with
/// <see cref="Advance"/>.
/// </para>
/// </remarks>
public sealed class HostSession
{
    private readonly Peer?[] _peers;
    private readonly List<Command> _pending = [];
    private readonly List<HashRecord> _hashes = [];
    private readonly ByteWriter _writer = new();
    private TimeSpan _now;
    private TimeSpan _accumulator;
    private int _receiveStart;

    public HostSession(HostOptions? options = null)
    {
        Options = options ?? new HostOptions();
        Options.Validate();
        _peers = new Peer?[Options.PlayerCount];
        NextTick = Options.FirstTick;
    }

    /// <summary>A client finished joining its seat.</summary>
    public event Action<int>? PlayerJoined;

    /// <summary>A player's connection is gone, or the host dropped it. The others were told.</summary>
    public event Action<int, SessionEndReason>? PlayerLeft;

    /// <summary>The session is over.</summary>
    public event Action<SessionEndInfo>? Stopped;

    public HostOptions Options { get; }

    public SessionPhase Phase { get; private set; }

    /// <summary>The command delay of the match in ticks. 0 until the match starts.</summary>
    public int CommandDelay { get; private set; }

    /// <summary>The next tick the host issues a bundle for. Every tick below it is final.</summary>
    public int NextTick { get; private set; }

    /// <summary>How the session ended, once it has.</summary>
    public SessionEndInfo? EndInfo { get; private set; }

    /// <summary>
    /// How many commands the host refused because a client sent them in another player's name. For
    /// diagnostics and tests.
    /// </summary>
    public long RejectedCommandCount { get; private set; }

    /// <summary>True when the match can start: every seat with a connection has reported ready.</summary>
    public bool CanStart
    {
        get
        {
            if (Phase != SessionPhase.Joining)
            {
                return false;
            }

            foreach (var peer in _peers)
            {
                if (peer is { Ready: false })
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Gives a seat to a connection, before the match starts. The client at the other end then
    /// joins it. A seat that has no connection when the match starts is run by the host: its
    /// commands come from <see cref="Submit"/>, which is how an AI player gives its orders.
    /// </summary>
    public void Attach(int player, IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ThrowIfNotASeat(player);
        if (Phase != SessionPhase.Joining)
        {
            throw new InvalidOperationException("A connection can only be attached before the match starts.");
        }

        if (_peers[player] is not null)
        {
            throw new InvalidOperationException($"Player {player} already has a connection.");
        }

        _peers[player] = new Peer(player, connection)
        {
            Progress = Options.FirstTick,
            NextReportTick = FirstReportTick(),
            NextPingAt = _now,
        };
    }

    /// <summary>True while the seat has a connection.</summary>
    public bool IsAttached(int player)
    {
        ThrowIfNotASeat(player);
        return _peers[player] is not null;
    }

    /// <summary>True once the seat's client has joined and reported ready.</summary>
    public bool IsReady(int player)
    {
        ThrowIfNotASeat(player);
        return _peers[player] is { Ready: true };
    }

    /// <summary>
    /// The last round trip measured to the seat's client, or null before the first answer. It is
    /// measured on the session's own time, so it includes the wait for both sides' next update.
    /// </summary>
    public TimeSpan? GetRoundTrip(int player)
    {
        ThrowIfNotASeat(player);
        return _peers[player]?.RoundTrip;
    }

    /// <summary>
    /// The tick the seat's client last reported a hash for, which it had reached by then. -1 for a
    /// seat without a connection.
    /// </summary>
    public int GetReportedTick(int player)
    {
        ThrowIfNotASeat(player);
        return _peers[player]?.Progress ?? -1;
    }

    /// <summary>
    /// Starts the match: tells every client, fixes the command delay and issues the bundles of the
    /// head start. Every attached seat must be ready.
    /// </summary>
    public void Start()
    {
        if (Phase != SessionPhase.Joining)
        {
            throw new InvalidOperationException("The match has already started.");
        }

        foreach (var peer in _peers)
        {
            if (peer is { Ready: false })
            {
                throw new InvalidOperationException($"Player {peer.Player} is not ready.");
            }
        }

        CommandDelay = Options.CommandDelay ?? ChooseCommandDelay();
        Phase = SessionPhase.Running;
        _accumulator = TimeSpan.Zero;
        Broadcast(new MatchStart(Options.FirstTick, CommandDelay, Options.HashInterval, Options.TickInterval));
        for (var i = 1; i < CommandDelay; i++)
        {
            IssueBundle();
        }
    }

    /// <summary>
    /// Gives a command for a seat the host runs itself, such as an AI player. It joins the next
    /// bundle like a command from a client. Returns false when the match is not running.
    /// </summary>
    public bool Submit(in Command command)
    {
        if (command.Player < 0 || command.Player >= _peers.Length)
        {
            throw new ArgumentException($"Player {command.Player} is not a seat of this match.", nameof(command));
        }

        if (_peers[command.Player] is not null)
        {
            throw new ArgumentException($"Player {command.Player} has a connection and gives its commands through it.", nameof(command));
        }

        if (Phase != SessionPhase.Running)
        {
            return false;
        }

        ThrowIfTooLarge(command);
        _pending.Add(command);
        return true;
    }

    /// <summary>
    /// Takes in what the clients sent, then lets the given time pass: one bundle is issued for each
    /// tick interval that has gone by.
    /// </summary>
    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        _now += elapsed;
        if (Phase == SessionPhase.Stopped)
        {
            return;
        }

        ReceiveFromPeers();

        if (Phase == SessionPhase.Running)
        {
            // Time the host could not use is not owed. See HostOptions.MaxTicksPerAdvance.
            var most = SessionClock.Times(Options.TickInterval, Options.MaxTicksPerAdvance);
            _accumulator = elapsed >= most - _accumulator ? most : _accumulator + elapsed;
            while (_accumulator >= Options.TickInterval)
            {
                _accumulator -= Options.TickInterval;
                IssueBundle();
            }

            DropPeersThatFellBehind();
        }

        if (Phase != SessionPhase.Stopped)
        {
            SendPings();
        }
    }

    /// <summary>
    /// Ends the session for everyone. A running match ends at <see cref="NextTick"/>: every client
    /// runs the bundles it was sent and stops in the same state.
    /// </summary>
    public void EndSession()
    {
        if (Phase != SessionPhase.Stopped)
        {
            var finalTick = Phase == SessionPhase.Running ? NextTick : -1;
            Stop(new SessionEndInfo(SessionEndReason.HostEnded, finalTick));
        }
    }

    private void ReceiveFromPeers()
    {
        // The seat that is read first moves on by one with every tick, so that no player's
        // commands always come first in a bundle.
        var count = _peers.Length;
        var start = _receiveStart;

        for (var i = 0; i < count && Phase != SessionPhase.Stopped; i++)
        {
            var peer = _peers[(start + i) % count];
            if (peer is null)
            {
                continue;
            }

            while (IsCurrent(peer) && peer.Connection.TryReceive(out var data))
            {
                // Whatever a peer sends that is not a message of the protocol ends its part.
                if (MessageCodec.TryRead(data, out var message))
                {
                    Handle(peer, message);
                }
                else
                {
                    Drop(peer, SessionEndReason.ProtocolError);
                }
            }

            if (IsCurrent(peer) && !peer.Connection.IsOpen)
            {
                Remove(peer, SessionEndReason.ConnectionLost);
            }
        }
    }

    /// <summary>False once the peer was removed or the session stopped, when its messages no longer count.</summary>
    private bool IsCurrent(Peer peer) => Phase != SessionPhase.Stopped && ReferenceEquals(_peers[peer.Player], peer);

    private void Handle(Peer peer, NetMessage message)
    {
        switch (message)
        {
            case JoinRequest join when !peer.Joined:
                if (join.ProtocolVersion != ProtocolInfo.Version || join.CommandEncodingVersion != CommandEncoding.Version)
                {
                    Drop(peer, SessionEndReason.VersionMismatch);
                    break;
                }

                peer.Joined = true;
                Send(peer, new JoinAccept(peer.Player, _peers.Length));
                PlayerJoined?.Invoke(peer.Player);
                break;

            case ReadyNotice when peer.Joined && !peer.Ready && Phase == SessionPhase.Joining:
                peer.Ready = true;
                break;

            case CommandRequest request when peer.Ready && Phase == SessionPhase.Running:
                if (request.Command.Player == peer.Player)
                {
                    _pending.Add(request.Command);
                }
                else
                {
                    RejectedCommandCount++;
                }

                break;

            // A client that has run up to a tick holds its bundle, so it cannot be past NextTick.
            case HashReport report when peer.Ready && Phase == SessionPhase.Running
                && report.Tick == peer.NextReportTick && report.Tick <= NextTick:
                peer.Progress = report.Tick;
                peer.NextReportTick = checked(report.Tick + Options.HashInterval);
                CompareHash(peer, report);
                break;

            case Pong pong:
                if (peer.PingPending && pong.Sequence == peer.PingSequence)
                {
                    peer.PingPending = false;
                    peer.RoundTrip = _now - peer.PingSentAt;
                }

                break;

            default:
                Drop(peer, SessionEndReason.ProtocolError);
                break;
        }
    }

    private void CompareHash(Peer peer, HashReport report)
    {
        var index = _hashes.Count;
        while (index > 0 && _hashes[index - 1].Tick > report.Tick)
        {
            index--;
        }

        if (index > 0 && _hashes[index - 1].Tick == report.Tick)
        {
            var first = _hashes[index - 1];
            if (first.Hash != report.Hash)
            {
                var desync = new DesyncInfo(report.Tick, first.Player, first.Hash, peer.Player, report.Hash);
                Stop(new SessionEndInfo(SessionEndReason.Desync, NextTick, report.Tick, desync));
                return;
            }
        }
        else
        {
            _hashes.Insert(index, new HashRecord(report.Tick, report.Hash, peer.Player));
        }

        ForgetComparedHashes();
    }

    /// <summary>Forgets the hashes of ticks that every remaining peer has reported, and so compared.</summary>
    private void ForgetComparedHashes()
    {
        var awaited = int.MaxValue;
        foreach (var peer in _peers)
        {
            if (peer is not null && peer.NextReportTick < awaited)
            {
                awaited = peer.NextReportTick;
            }
        }

        var compared = 0;
        while (compared < _hashes.Count && _hashes[compared].Tick < awaited)
        {
            compared++;
        }

        _hashes.RemoveRange(0, compared);
    }

    private void IssueBundle()
    {
        // As many of the waiting commands, in order, as one message may hold. The rest run a tick
        // later; each is small enough that some always fit.
        var size = MessageCodec.BundleOverhead;
        var count = 0;
        while (count < _pending.Count)
        {
            var commandSize = CommandEncoding.SizeOf(_pending[count]);
            if (size + commandSize > ProtocolInfo.MaxMessageSize)
            {
                break;
            }

            size += commandSize;
            count++;
        }

        Broadcast(new TickBundle(NextTick, ImmutableArray.Create(CollectionsMarshal.AsSpan(_pending)[..count])));
        _pending.RemoveRange(0, count);
        NextTick = checked(NextTick + 1);
        _receiveStart = (_receiveStart + 1) % _peers.Length;
    }

    private void DropPeersThatFellBehind()
    {
        // The tick that is due now. The bundles of the head start are issued but not yet due.
        var dueTick = NextTick - (CommandDelay - 1);
        foreach (var peer in _peers)
        {
            if (peer is not null && dueTick - peer.Progress > Options.MaxLagTicks)
            {
                Drop(peer, SessionEndReason.FellBehind);
            }
        }
    }

    private void SendPings()
    {
        foreach (var peer in _peers)
        {
            // One ping at a time. The connection loses nothing, so the answer always comes, and a
            // round trip longer than the ping interval is still measured.
            if (peer is null || peer.PingPending || _now < peer.NextPingAt)
            {
                continue;
            }

            peer.PingSequence = unchecked(peer.PingSequence + 1);
            peer.PingSentAt = _now;
            peer.PingPending = true;
            peer.NextPingAt = _now + Options.PingInterval;
            Send(peer, new Ping(peer.PingSequence));
        }
    }

    private int ChooseCommandDelay()
    {
        var longest = TimeSpan.Zero;
        var remote = false;
        foreach (var peer in _peers)
        {
            if (peer is null)
            {
                continue;
            }

            // A peer that never answered a ping is assumed to be far away.
            if (peer.RoundTrip is not { } roundTrip)
            {
                return CommandDelayPolicy.Longest;
            }

            remote = true;
            if (roundTrip > longest)
            {
                longest = roundTrip;
            }
        }

        return remote ? CommandDelayPolicy.FromRoundTrip(longest, Options.TickInterval) : CommandDelayPolicy.SinglePlayer;
    }

    /// <summary>Tells a peer why it is out, closes its connection and tells the others.</summary>
    private void Drop(Peer peer, SessionEndReason reason)
    {
        Send(peer, new SessionEnd(reason, -1, -1));
        Remove(peer, reason);
    }

    private void Remove(Peer peer, SessionEndReason reason)
    {
        _peers[peer.Player] = null;
        peer.Connection.Close();
        Broadcast(new PeerLeft(peer.Player, reason));
        ForgetComparedHashes();
        PlayerLeft?.Invoke(peer.Player, reason);
    }

    private void Stop(SessionEndInfo info)
    {
        Phase = SessionPhase.Stopped;
        EndInfo = info;
        _pending.Clear();
        _hashes.Clear();

        // The connections stay open: each client closes its own once it has run the last tick.
        Broadcast(new SessionEnd(info.Reason, info.FinalTick, info.DesyncTick));
        Stopped?.Invoke(info);
    }

    private void Send(Peer peer, NetMessage message)
    {
        _writer.Clear();
        MessageCodec.Write(_writer, message);
        peer.Connection.Send(_writer.WrittenSpan);
    }

    private void Broadcast(NetMessage message)
    {
        _writer.Clear();
        MessageCodec.Write(_writer, message);
        foreach (var peer in _peers)
        {
            peer?.Connection.Send(_writer.WrittenSpan);
        }
    }

    private static void ThrowIfTooLarge(in Command command)
    {
        if (CommandEncoding.SizeOf(command) > ProtocolInfo.MaxCommandSize)
        {
            throw new ArgumentException(
                $"A command with {command.Entities.Length} entities takes more than the {ProtocolInfo.MaxCommandSize} bytes the protocol allows.",
                nameof(command));
        }
    }

    private int FirstReportTick()
    {
        var interval = Options.HashInterval;
        return checked((Options.FirstTick + interval - 1) / interval * interval);
    }

    private void ThrowIfNotASeat(int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, _peers.Length);
    }

    private readonly record struct HashRecord(int Tick, ulong Hash, int Player);

    private sealed class Peer(int player, IConnection connection)
    {
        public int Player { get; } = player;

        public IConnection Connection { get; } = connection;

        public bool Joined { get; set; }

        public bool Ready { get; set; }

        /// <summary>The tick of the last hash report: the peer had run every tick below it.</summary>
        public int Progress { get; set; }

        public int NextReportTick { get; set; }

        public int PingSequence { get; set; }

        public bool PingPending { get; set; }

        public TimeSpan PingSentAt { get; set; }

        public TimeSpan NextPingAt { get; set; }

        public TimeSpan? RoundTrip { get; set; }
    }
}
