namespace Net;

/// <summary>
/// Chooses the command delay from a measured round trip. The host issues the bundle of a tick one
/// tick fewer than the delay ahead of time, and that head start is what has to cover the trip to
/// the client: with too little of it a client waits for bundles and stutters, with too much every
/// order feels late.
/// </summary>
public static class CommandDelayPolicy
{
    /// <summary>With no network in between, a command runs in the next tick.</summary>
    public const int SinglePlayer = 1;

    /// <summary>The shortest delay for a match with a remote peer.</summary>
    public const int Shortest = 2;

    /// <summary>The longest delay the policy chooses. Past it a client runs behind instead.</summary>
    public const int Longest = 5;

    /// <summary>
    /// The delay whose head start covers the one-way trip, taken as half the round trip, and half a
    /// tick of margin for jitter. With 50 ms ticks: 2 up to a 50 ms round trip, 3 up to 150 ms,
    /// 4 up to 250 ms and 5 beyond.
    /// </summary>
    public static int FromRoundTrip(TimeSpan roundTrip, TimeSpan tickInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(roundTrip, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tickInterval, TimeSpan.Zero);

        // The whole ticks needed to cover (roundTrip + tickInterval) / 2, rounded up.
        var needed = (Int128)roundTrip.Ticks + tickInterval.Ticks;
        var perTick = (Int128)2 * tickInterval.Ticks;
        var headStart = (needed + perTick - 1) / perTick;
        return (int)Int128.Clamp(headStart + 1, Shortest, Longest);
    }
}
