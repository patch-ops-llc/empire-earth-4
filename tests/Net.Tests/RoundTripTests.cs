using Sim.Serialization;

namespace Net.Tests;

/// <summary>The host measures the round trip to each client and picks the command delay from it.</summary>
public class RoundTripTests
{
    private static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    [Fact]
    public void TheHostMeasuresTheRoundTripToEachClient()
    {
        var match = new TestMatch(clients: 2, options: new HostOptions { PlayerCount = 3 });
        match.Clients[1].Link.Conditions = new LinkConditions(60 * Millisecond, TimeSpan.Zero);
        Assert.Null(match.Host.GetRoundTrip(0));

        match.Run(seconds: 0.5);

        // Both sides only look at the connection once a frame, which the measurement includes.
        Assert.InRange(match.Host.GetRoundTrip(0)!.Value, TimeSpan.Zero, 2 * TestMatch.Frame);
        Assert.InRange(match.Host.GetRoundTrip(1)!.Value, 120 * Millisecond, (120 * Millisecond) + (2 * TestMatch.Frame));
        Assert.Null(match.Host.GetRoundTrip(2));

        // It is measured again every ping interval, so it follows the connection.
        match.Clients[1].Link.Conditions = new LinkConditions(10 * Millisecond, TimeSpan.Zero);
        match.Run(match.Host.Options.PingInterval + (0.2 * TimeSpan.FromSeconds(1)));
        Assert.InRange(match.Host.GetRoundTrip(1)!.Value, 20 * Millisecond, (20 * Millisecond) + (2 * TestMatch.Frame));
    }

    [Fact]
    public void TheRoundTripKeepsBeingMeasuredDuringTheMatch()
    {
        var match = new TestMatch(clients: 2);
        match.Start();
        match.Run(seconds: 1);
        match.Clients[0].Link.Conditions = new LinkConditions(80 * Millisecond, TimeSpan.Zero);

        match.Run(seconds: 2.5);

        Assert.InRange(match.Host.GetRoundTrip(0)!.Value, 160 * Millisecond, (160 * Millisecond) + (2 * TestMatch.Frame));
        Assert.Equal(SessionPhase.Running, match.Host.Phase);
    }

    /// <summary>
    /// 2 ticks on a local network and 3 to 5 over the internet (technical plan, section 2), from
    /// the slowest client's round trip.
    /// </summary>
    [Theory]
    [InlineData(0, 2)]
    [InlineData(10, 2)]
    [InlineData(40, 3)]
    [InlineData(90, 4)]
    [InlineData(200, 5)]
    [InlineData(2000, 5)]
    public void LeftToTheHostTheCommandDelayFollowsTheSlowestRoundTrip(int latency, int expectedDelay)
    {
        var match = new TestMatch(clients: 3, options: new HostOptions { PlayerCount = 3, HashInterval = 1 });
        match.Clients[1].Link.Conditions = new LinkConditions(latency * Millisecond, TimeSpan.Zero);
        match.Join();
        Assert.Equal(0, match.Host.CommandDelay);

        match.Start();
        match.Run(seconds: 5);

        Assert.Equal(expectedDelay, match.Host.CommandDelay);
        Assert.All(match.Clients, client => Assert.Equal(expectedDelay, client.Session.CommandDelay));
        Assert.Equal(
            CommandDelayPolicy.FromRoundTrip(match.Clients.Max(client => match.Host.GetRoundTrip(client.Seat)!.Value), TestMatch.TickInterval),
            expectedDelay);
    }

    [Fact]
    public void AMatchWithNoConnectionsHasTheShortestDelayThereIs()
    {
        var host = new HostSession(new HostOptions { PlayerCount = 4 });

        host.Start();

        Assert.Equal(CommandDelayPolicy.SinglePlayer, host.CommandDelay);
        Assert.Equal(1, host.CommandDelay);
        Assert.Equal(0, host.NextTick);
    }

    [Fact]
    public void AClientThatNeverAnsweredAPingIsTakenToBeFarAway()
    {
        var network = new LoopbackNetwork();
        var host = new HostSession();
        var link = network.Connect();
        host.Attach(0, link.HostEnd);
        link.ClientEnd.SendMessage(new JoinRequest(ProtocolInfo.Version, CommandEncoding.Version));
        link.ClientEnd.SendMessage(new ReadyNotice());
        link.ClientEnd.SendMessage(new Pong(12345));
        host.Advance(TimeSpan.FromSeconds(3));

        host.Start();

        Assert.Null(host.GetRoundTrip(0));
        Assert.Equal(CommandDelayPolicy.Longest, host.CommandDelay);
        Assert.Equal(4, host.NextTick);
        Assert.Single(link.ClientEnd.ReceiveMessages().OfType<Ping>());
    }

    /// <summary>
    /// One ping is out at a time and only its own answer counts, once. A round trip longer than
    /// the ping interval is measured like any other, and an answer nobody asked for changes nothing.
    /// </summary>
    [Fact]
    public void OnlyTheAnswerToThePingThatIsOutCounts()
    {
        var network = new LoopbackNetwork();
        var host = new HostSession(new HostOptions { PingInterval = TimeSpan.FromSeconds(1) });
        var link = network.Connect();
        host.Attach(0, link.HostEnd);
        host.Advance(TimeSpan.Zero);
        var first = Assert.Single(link.ClientEnd.ReceiveMessages().OfType<Ping>());

        // No answer for three seconds: no second ping meanwhile.
        host.Advance(TimeSpan.FromSeconds(3));
        Assert.Empty(link.ClientEnd.ReceiveMessages());
        link.ClientEnd.SendMessage(new Pong(first.Sequence + 1));
        host.Advance(TimeSpan.FromMilliseconds(10));
        Assert.Null(host.GetRoundTrip(0));

        link.ClientEnd.SendMessage(new Pong(first.Sequence));
        host.Advance(TimeSpan.FromMilliseconds(10));
        Assert.Equal(TimeSpan.FromMilliseconds(3020), host.GetRoundTrip(0));

        // The next ping goes out at once, the interval being long over, and the old answer is spent.
        var second = Assert.Single(link.ClientEnd.ReceiveMessages().OfType<Ping>());
        Assert.NotEqual(first.Sequence, second.Sequence);
        link.ClientEnd.SendMessage(new Pong(first.Sequence));
        host.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(TimeSpan.FromMilliseconds(3020), host.GetRoundTrip(0));
        Assert.True(host.IsAttached(0));
    }
}
