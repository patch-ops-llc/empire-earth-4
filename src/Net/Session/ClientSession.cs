using System.Collections.Immutable;
using Sim.Core;
using Sim.Serialization;

namespace Net;

/// <summary>
/// A player's side of a lockstep session, on every machine including the host's. It drives the
/// player's <see cref="Sim.Core.Simulation"/>: it queues the commands of each bundle the host
/// sends, runs a tick only once it holds that tick's bundle, and reports the state hash for the
/// host to compare.
/// </summary>
/// <remarks>
/// <para>
/// Ticks run on the client's own time, one per tick interval, as long as their bundles are there.
/// When the next bundle is missing the client waits, and its ticks then follow from the moment the
/// bundle arrives. When more bundles are waiting than the command delay accounts for, by more than
/// <see cref="ClientOptions.CatchUpSlack"/>, the client is behind: it runs extra ticks, several in
/// one update, until it is level again.
/// </para>
/// <para>
/// It reads no clock and uses no thread. The caller says how much time has passed with
/// <see cref="Advance"/>, and nothing else may run ticks of the simulation or add to its command
/// queue while the session has it.
/// </para>
/// </remarks>
public sealed class ClientSession
{
    private readonly IConnection _connection;
    private readonly ByteWriter _writer = new();
    private TimeSpan _accumulator;
    private bool _readySent;
    private bool _catchingUp;
    private SessionEndInfo? _pendingEnd;

    /// <summary>Asks the host, at the other end of the connection, for a seat.</summary>
    public ClientSession(IConnection connection, ClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Options = options ?? new ClientOptions();
        Options.Validate();
        _connection = connection;
        Send(new JoinRequest(ProtocolInfo.Version, CommandEncoding.Version));
    }

    /// <summary>The host accepted the join. <see cref="Player"/> is set.</summary>
    public event Action? Joined;

    /// <summary>
    /// The host described the match (<see cref="HostOptions.Match"/>). Build it and call
    /// <see cref="ReportReady"/>, or call <see cref="Leave"/> with
    /// <see cref="SessionEndReason.SetupMismatch"/> if this machine cannot.
    /// </summary>
    public event Action<MatchDescription>? MatchDescribed;

    /// <summary>The match started. The first tick has not run yet.</summary>
    public event Action? Started;

    /// <summary>
    /// The bundle of a tick arrived and its commands were queued in the simulation, in this order.
    /// It is raised once for every tick, in tick order, before that tick runs: the complete record
    /// of what the match was given.
    /// </summary>
    public event Action<int, ImmutableArray<Command>>? CommandsScheduled;

    /// <summary>A tick, whose number is given, has run.</summary>
    public event Action<int>? TickCompleted;

    /// <summary>Another player left, its connection is gone, or the host dropped it. The match goes on.</summary>
    public event Action<int, SessionEndReason>? PlayerLeft;

    /// <summary>The session is over. No tick runs after this.</summary>
    public event Action<SessionEndInfo>? Stopped;

    public ClientOptions Options { get; }

    public SessionPhase Phase { get; private set; }

    /// <summary>This client's player number, counted from 0. -1 until the host accepts the join.</summary>
    public int Player { get; private set; } = -1;

    /// <summary>The number of seats in the match. 0 until the host accepts the join.</summary>
    public int PlayerCount { get; private set; }

    /// <summary>The match as the host described it, or null if the host has not, or never does.</summary>
    public MatchDescription? Description { get; private set; }

    /// <summary>The simulation the session drives, once <see cref="ReportReady"/> has given it one.</summary>
    public Simulation? Simulation { get; private set; }

    /// <summary>The command delay the host set, in ticks. 0 until the match starts.</summary>
    public int CommandDelay { get; private set; }

    /// <summary>The state hash is reported before every tick this divides. 0 until the match starts.</summary>
    public int HashInterval { get; private set; }

    /// <summary>The time between two ticks, as the host set it. Zero until the match starts.</summary>
    public TimeSpan TickInterval { get; private set; }

    /// <summary>The first tick whose bundle has not arrived. The simulation can run up to it.</summary>
    public int NextBundleTick { get; private set; }

    /// <summary>The number of ticks whose bundles have arrived and that have not run yet.</summary>
    public int BufferedTicks => Simulation is null || Phase != SessionPhase.Running ? 0 : NextBundleTick - Simulation.World.Tick;

    /// <summary>
    /// The time since the last tick ran, at most one <see cref="TickInterval"/>: how far the
    /// picture is between the last tick and the next, for interpolation. It stays at the full
    /// interval while the client waits for a bundle.
    /// </summary>
    public TimeSpan TimeIntoTick => _accumulator < TickInterval ? _accumulator : TickInterval;

    /// <summary>How the session ended, once it has.</summary>
    public SessionEndInfo? EndInfo { get; private set; }

    /// <summary>
    /// Gives the session the simulation of this match, built and at the tick the match starts at,
    /// and tells the host this client is ready. It can be called before or after the host accepts
    /// the join.
    /// </summary>
    public void ReportReady(Simulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (Simulation is not null)
        {
            throw new InvalidOperationException("The session already has a simulation.");
        }

        Simulation = simulation;
        SendReadyOnceJoined();
    }

    /// <summary>
    /// Sends a command of this player to the host, which decides the tick it runs in. Returns
    /// false, having sent nothing, when the match is not running.
    /// </summary>
    public bool Submit(in Command command)
    {
        if (Phase != SessionPhase.Running || _pendingEnd is not null)
        {
            return false;
        }

        if (command.Player != Player)
        {
            throw new ArgumentException($"The command is from player {command.Player}, and this client is player {Player}.", nameof(command));
        }

        if (CommandEncoding.SizeOf(command) > ProtocolInfo.MaxCommandSize)
        {
            throw new ArgumentException(
                $"A command with {command.Entities.Length} entities takes more than the {ProtocolInfo.MaxCommandSize} bytes the protocol allows.",
                nameof(command));
        }

        Send(new CommandRequest(command));
        return true;
    }

    /// <summary>
    /// Takes in what the host sent, then lets the given time pass, running the ticks that are due
    /// and have their bundles. Returns the number of ticks it ran.
    /// </summary>
    public int Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        if (Phase == SessionPhase.Stopped)
        {
            return 0;
        }

        while (Phase != SessionPhase.Stopped && _pendingEnd is null && _connection.TryReceive(out var data))
        {
            if (MessageCodec.TryRead(data, out var message))
            {
                Handle(message);
            }
            else
            {
                Stop(new SessionEndInfo(SessionEndReason.ProtocolError, -1));
            }
        }

        if (Phase != SessionPhase.Stopped && _pendingEnd is null && !_connection.IsOpen)
        {
            Stop(new SessionEndInfo(SessionEndReason.ConnectionLost, -1));
        }

        return Phase == SessionPhase.Running ? RunTicks(elapsed) : 0;
    }

    /// <summary>
    /// Leaves the session and closes the connection. The host is told why and tells the other
    /// players.
    /// </summary>
    /// <param name="reason">
    /// <see cref="SessionEndReason.Left"/>, or <see cref="SessionEndReason.SetupMismatch"/> when
    /// this machine cannot build the match the host described.
    /// </param>
    public void Leave(SessionEndReason reason = SessionEndReason.Left)
    {
        if (reason is not (SessionEndReason.Left or SessionEndReason.SetupMismatch))
        {
            throw new ArgumentException($"A client cannot leave with the reason {reason}.", nameof(reason));
        }

        if (Phase != SessionPhase.Stopped)
        {
            Send(new LeaveNotice(reason));
            Stop(new SessionEndInfo(reason, -1));
        }
    }

    private void Handle(NetMessage message)
    {
        switch (message)
        {
            case JoinAccept accept when Phase == SessionPhase.Joining && Player < 0
                && accept.PlayerCount >= 1 && accept.Player >= 0 && accept.Player < accept.PlayerCount:
                Player = accept.Player;
                PlayerCount = accept.PlayerCount;
                Joined?.Invoke();
                SendReadyOnceJoined();
                break;

            case MatchNotice notice when Phase == SessionPhase.Joining && Player >= 0 && Description is null
                && notice.Description.Players.Count == PlayerCount:
                Description = notice.Description;
                MatchDescribed?.Invoke(notice.Description);
                break;

            case Ping ping:
                Send(new Pong(ping.Sequence));
                break;

            case MatchStart start when Phase == SessionPhase.Joining && _readySent
                && start.CommandDelay is >= 1 and <= ProtocolInfo.MaxCommandDelay
                && start.HashInterval >= 1 && start.TickInterval > TimeSpan.Zero
                && start.FirstTick == Simulation!.World.Tick:
                Start(start);
                break;

            case TickBundle bundle when Phase == SessionPhase.Running && bundle.Tick == NextBundleTick
                && !bundle.Commands.IsDefault:
                foreach (var command in bundle.Commands)
                {
                    Simulation!.Commands.Enqueue(bundle.Tick, command);
                }

                NextBundleTick = checked(NextBundleTick + 1);
                CommandsScheduled?.Invoke(bundle.Tick, bundle.Commands);
                break;

            case PeerLeft left:
                PlayerLeft?.Invoke(left.Player, left.Reason);
                break;

            // The match is over at a tick every peer can reach: run up to it, then stop.
            case SessionEnd end when Phase == SessionPhase.Running && end.FinalTick == NextBundleTick
                && end.Reason is SessionEndReason.HostEnded or SessionEndReason.Desync:
                _pendingEnd = new SessionEndInfo(end.Reason, end.FinalTick, end.DesyncTick);
                break;

            case SessionEnd end when end.FinalTick < 0 && end.Reason != SessionEndReason.None:
                Stop(new SessionEndInfo(end.Reason, -1, end.DesyncTick));
                break;

            default:
                Stop(new SessionEndInfo(SessionEndReason.ProtocolError, -1));
                break;
        }
    }

    private void SendReadyOnceJoined()
    {
        if (!_readySent && Simulation is not null && Player >= 0 && Phase == SessionPhase.Joining)
        {
            _readySent = true;
            Send(new ReadyNotice());
        }
    }

    private void Start(MatchStart start)
    {
        CommandDelay = start.CommandDelay;
        HashInterval = start.HashInterval;
        TickInterval = start.TickInterval;
        NextBundleTick = start.FirstTick;
        _accumulator = TimeSpan.Zero;
        Phase = SessionPhase.Running;
        Started?.Invoke();
        ReportHashIfDue();
    }

    private int RunTicks(TimeSpan elapsed)
    {
        var world = Simulation!.World;
        var limit = Options.MaxTicksPerAdvance;
        var ran = 0;

        // A handler of TickCompleted may leave the session, which must end the update there.
        bool MayRun() => ran < limit && Phase == SessionPhase.Running;

        if (_pendingEnd is { } end)
        {
            // Nothing to keep pace with any more: run what is left of the match.
            while (world.Tick < end.FinalTick && MayRun())
            {
                RunTick();
                ran++;
            }

            if (world.Tick >= end.FinalTick && Phase == SessionPhase.Running)
            {
                Stop(end);
            }

            return ran;
        }

        // Time the client could not use, because it was held up for longer than it may catch up in
        // one update, is not owed: the bundles that piled up meanwhile are what it catches up on.
        var most = SessionClock.Times(TickInterval, limit);
        _accumulator = elapsed >= most - _accumulator ? most : _accumulator + elapsed;

        while (_accumulator >= TickInterval && MayRun())
        {
            if (NextBundleTick == world.Tick)
            {
                // Waiting for the host. The tick runs as soon as its bundle is here, and the
                // ticks after it follow from then.
                _accumulator = TickInterval;
                break;
            }

            _accumulator -= TickInterval;
            RunTick();
            ran++;
        }

        // With a command delay of d, d - 1 bundles wait when the client is level with the host.
        // Once it is further behind than the slack allows, it catches up all the way to level,
        // over as many updates as that takes.
        var level = CommandDelay - 1;
        if (NextBundleTick - world.Tick > level + Options.CatchUpSlack)
        {
            _catchingUp = true;
        }

        if (_catchingUp)
        {
            while (NextBundleTick - world.Tick > level && MayRun())
            {
                RunTick();
                ran++;
            }

            if (Phase == SessionPhase.Running)
            {
                _catchingUp = NextBundleTick - world.Tick > level;
            }
        }

        return ran;
    }

    private void RunTick()
    {
        var simulation = Simulation!;
        var tick = simulation.World.Tick;
        simulation.Tick();
        TickCompleted?.Invoke(tick);
        ReportHashIfDue();
    }

    private void ReportHashIfDue()
    {
        var world = Simulation!.World;
        if (_pendingEnd is null && Phase == SessionPhase.Running && world.Tick % HashInterval == 0)
        {
            Send(new HashReport(world.Tick, world.ComputeHash()));
        }
    }

    private void Send(NetMessage message)
    {
        _writer.Clear();
        MessageCodec.Write(_writer, message);
        _connection.Send(_writer.WrittenSpan);
    }

    private void Stop(SessionEndInfo info)
    {
        Phase = SessionPhase.Stopped;
        EndInfo = info;
        _pendingEnd = null;
        _connection.Close();
        Stopped?.Invoke(info);
    }
}
