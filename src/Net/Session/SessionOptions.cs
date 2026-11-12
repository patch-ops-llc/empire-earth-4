using Sim;
using Sim.Serialization;

namespace Net;

/// <summary>Where a session is in its life.</summary>
public enum SessionPhase
{
    /// <summary>Before the match: peers join and report ready. No tick has been issued.</summary>
    Joining,

    /// <summary>The match is running.</summary>
    Running,

    /// <summary>The session is over. <c>End</c> says why.</summary>
    Stopped,
}

/// <summary>What the host of a session decides. Every value is fixed once the session is created.</summary>
public sealed record HostOptions
{
    /// <summary>The number of seats. Players are numbered from 0.</summary>
    public int PlayerCount { get; init; } = 2;

    /// <summary>The tick the match starts at: the tick of every peer's world when it reports ready.</summary>
    public int FirstTick { get; init; }

    /// <summary>
    /// The match every client is to build, with one player for each seat. The host sends it to a
    /// client as soon as it has joined. Null when the peers agree on the match some other way, as
    /// single-player and tests do.
    /// </summary>
    public MatchDescription? Match { get; init; }

    /// <summary>
    /// How many ticks after the host receives a command it runs: 1 or more. The technical plan sets
    /// 1 for single-player, 2 on a LAN and 3 to 5 over the internet. Leave it null to have the host
    /// choose it when the match starts, from the longest round trip it measured
    /// (<see cref="CommandDelayPolicy"/>).
    /// </summary>
    public int? CommandDelay { get; init; }

    /// <summary>
    /// Peers report their state hash before every tick this number divides. 20, once a second, in
    /// normal play; 1 in tests.
    /// </summary>
    public int HashInterval { get; init; } = SimInfo.TicksPerSecond;

    /// <summary>The time between two ticks when the match starts. <see cref="HostSession.SetTickInterval"/> changes it while the match runs.</summary>
    public TimeSpan TickInterval { get; init; } = SessionClock.DefaultTickInterval;

    /// <summary>
    /// How many ticks a peer may be behind the host before the host drops it. A peer's progress is
    /// known from its hash reports, so this must leave room for <see cref="HashInterval"/>.
    /// </summary>
    public int MaxLagTicks { get; init; } = 10 * SimInfo.TicksPerSecond;

    /// <summary>How often the host measures the round trip to each peer.</summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The most bundles one call to <see cref="HostSession.Advance"/> issues. Time beyond that is
    /// dropped: when the host's machine is held up the match is held up with it, and it does not
    /// race to make up the time afterwards.
    /// </summary>
    public int MaxTicksPerAdvance { get; init; } = 10;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxTicksPerAdvance, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(PlayerCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(FirstTick);
        ArgumentOutOfRangeException.ThrowIfLessThan(HashInterval, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(TickInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxLagTicks, checked(2 * HashInterval));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(PingInterval, TimeSpan.Zero);
        if (Match is not null && Match.Players.Count != PlayerCount)
        {
            throw new ArgumentException($"The match has {Match.Players.Count} players and the session has {PlayerCount} seats.");
        }

        if (CommandDelay is { } delay)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(delay, 1, nameof(CommandDelay));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, ProtocolInfo.MaxCommandDelay, nameof(CommandDelay));
        }
    }
}

/// <summary>What a client decides for itself. None of it changes what the simulation computes.</summary>
public sealed record ClientOptions
{
    /// <summary>
    /// The most ticks one call to <see cref="ClientSession.Advance"/> runs. A client that is behind
    /// catches up this many ticks at a time, so one frame never takes unboundedly long.
    /// </summary>
    public int MaxTicksPerAdvance { get; init; } = 10;

    /// <summary>
    /// How many bundles more than the command delay provides may wait before the client runs extra
    /// ticks to catch up. A little slack keeps a brief hold-up on the network from causing a visible
    /// jump.
    /// </summary>
    public int CatchUpSlack { get; init; } = 2;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxTicksPerAdvance, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(CatchUpSlack);
    }
}

/// <summary>How a session ended.</summary>
/// <param name="Reason">Why.</param>
/// <param name="FinalTick">
/// The number of ticks the match ran for on every peer that finished it, or -1 if the session ended
/// before the match or cut this peer off in the middle of it.
/// </param>
/// <param name="Desync">The hashes that differed, when that is the reason and this is the host.</param>
/// <param name="DesyncTick">The tick whose hashes differed, or -1.</param>
public sealed record SessionEndInfo(SessionEndReason Reason, int FinalTick, int DesyncTick = -1, DesyncInfo? Desync = null);

/// <summary>
/// Two state hashes for the same tick that differ. Which of the two worlds is wrong, if either is
/// right, the hashes cannot say.
/// </summary>
public sealed record DesyncInfo(int Tick, int FirstPlayer, ulong FirstHash, int SecondPlayer, ulong SecondHash);

internal static class SessionClock
{
    public static readonly TimeSpan DefaultTickInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / SimInfo.TicksPerSecond);

    /// <summary>The length of a number of tick intervals, or the longest time there is if that is shorter.</summary>
    public static TimeSpan Times(TimeSpan interval, int count) =>
        interval.Ticks > long.MaxValue / count ? TimeSpan.MaxValue : TimeSpan.FromTicks(interval.Ticks * count);
}
