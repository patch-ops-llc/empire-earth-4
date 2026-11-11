namespace Net.Tests;

public class CommandDelayPolicyTests
{
    /// <summary>
    /// The technical plan's numbers: 2 ticks on a LAN and 3 to 5 over the internet. With 50 ms
    /// ticks the head start of d - 1 ticks has to cover half the round trip and half a tick.
    /// </summary>
    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 2)]
    [InlineData(50, 2)]
    [InlineData(51, 3)]
    [InlineData(100, 3)]
    [InlineData(150, 3)]
    [InlineData(151, 4)]
    [InlineData(250, 4)]
    [InlineData(251, 5)]
    [InlineData(400, 5)]
    [InlineData(5000, 5)]
    public void TheDelayCoversTheOneWayTripAndHalfATick(int roundTripMilliseconds, int expected)
    {
        var delay = CommandDelayPolicy.FromRoundTrip(TimeSpan.FromMilliseconds(roundTripMilliseconds), TestMatch.TickInterval);

        Assert.Equal(expected, delay);
    }

    [Fact]
    public void ALongerTickNeedsFewerTicksOfDelay()
    {
        var roundTrip = TimeSpan.FromMilliseconds(200);

        Assert.Equal(4, CommandDelayPolicy.FromRoundTrip(roundTrip, TimeSpan.FromMilliseconds(50)));
        Assert.Equal(3, CommandDelayPolicy.FromRoundTrip(roundTrip, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void ExtremeValuesStayInRange()
    {
        Assert.Equal(CommandDelayPolicy.Longest, CommandDelayPolicy.FromRoundTrip(TimeSpan.MaxValue, TimeSpan.FromTicks(1)));
        Assert.Equal(CommandDelayPolicy.Shortest, CommandDelayPolicy.FromRoundTrip(TimeSpan.Zero, TimeSpan.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommandDelayPolicy.FromRoundTrip(TimeSpan.FromTicks(-1), TestMatch.TickInterval));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommandDelayPolicy.FromRoundTrip(TimeSpan.Zero, TimeSpan.Zero));
    }
}
