namespace Net.Tests;

public class LoopbackNetworkTests
{
    private static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    [Fact]
    public void WithoutLatencyAMessageArrivesAtOnce()
    {
        var link = new LoopbackNetwork().Connect();

        link.ClientEnd.SendMessage(new Pong(7));
        link.HostEnd.SendMessage(new Ping(8));

        Assert.True(link.HostEnd.TryReceiveMessage(out var atHost));
        Assert.Equal(new Pong(7), atHost);
        Assert.True(link.ClientEnd.TryReceiveMessage(out var atClient));
        Assert.Equal(new Ping(8), atClient);
        Assert.False(link.HostEnd.TryReceive(out _));
        Assert.False(link.ClientEnd.TryReceive(out _));
    }

    [Fact]
    public void AMessageArrivesOnlyAfterTheLatency()
    {
        var network = new LoopbackNetwork();
        var link = network.Connect(new LinkConditions(40 * Millisecond, TimeSpan.Zero));

        link.HostEnd.SendMessage(new Ping(1));
        network.Advance(39 * Millisecond);
        Assert.False(link.ClientEnd.TryReceive(out _));

        network.Advance(Millisecond);
        Assert.True(link.ClientEnd.TryReceiveMessage(out var message));
        Assert.Equal(new Ping(1), message);
    }

    [Fact]
    public void JitterDelaysMessagesByDifferentAmountsAndNeverReordersThem()
    {
        var network = new LoopbackNetwork(seed: 5);
        var link = network.Connect(new LinkConditions(20 * Millisecond, 30 * Millisecond));
        var arrivals = new List<(int Sequence, TimeSpan Delay)>();
        var sentAt = new Dictionary<int, TimeSpan>();

        for (var step = 0; step < 2000; step++)
        {
            if (step < 500)
            {
                sentAt[step] = network.Now;
                link.HostEnd.SendMessage(new Ping(step));
            }

            network.Advance(Millisecond);
            while (link.ClientEnd.TryReceiveMessage(out var message))
            {
                var sequence = ((Ping)message).Sequence;
                arrivals.Add((sequence, network.Now - sentAt[sequence]));
            }
        }

        Assert.Equal(Enumerable.Range(0, 500), arrivals.Select(arrival => arrival.Sequence));
        Assert.All(arrivals, arrival => Assert.True(arrival.Delay >= 20 * Millisecond));
        Assert.True(arrivals.Select(arrival => arrival.Delay).Distinct().Count() > 10, "The jitter did not vary.");

        // Held back only by the message before it, never by more than the longest single delay.
        Assert.All(arrivals, arrival => Assert.True(arrival.Delay <= 51 * Millisecond));
    }

    [Fact]
    public void TheSameSeedGivesTheSameArrivalTimes()
    {
        static List<TimeSpan> Arrivals(ulong seed)
        {
            var network = new LoopbackNetwork(seed);
            var link = network.Connect(new LinkConditions(TimeSpan.Zero, 20 * Millisecond));
            var arrivals = new List<TimeSpan>();
            for (var step = 0; step < 300; step++)
            {
                link.ClientEnd.SendMessage(new Pong(step));
                network.Advance(Millisecond);
                while (link.HostEnd.TryReceive(out _))
                {
                    arrivals.Add(network.Now);
                }
            }

            return arrivals;
        }

        Assert.Equal(Arrivals(3), Arrivals(3));
        Assert.NotEqual(Arrivals(3), Arrivals(4));
    }

    [Fact]
    public void LoweringTheLatencyDoesNotLetALaterMessageOvertake()
    {
        var network = new LoopbackNetwork();
        var link = network.Connect(new LinkConditions(100 * Millisecond, TimeSpan.Zero));

        link.HostEnd.SendMessage(new Ping(1));
        link.Conditions = default;
        link.HostEnd.SendMessage(new Ping(2));

        Assert.False(link.ClientEnd.TryReceive(out _));
        network.Advance(100 * Millisecond);
        Assert.True(link.ClientEnd.TryReceiveMessage(out var first));
        Assert.True(link.ClientEnd.TryReceiveMessage(out var second));
        Assert.Equal(new Ping(1), first);
        Assert.Equal(new Ping(2), second);
    }

    [Fact]
    public void ClosingDeliversWhatWasSentBeforeAndThenClosesTheOtherEnd()
    {
        var network = new LoopbackNetwork();
        var link = network.Connect(new LinkConditions(10 * Millisecond, TimeSpan.Zero));

        link.HostEnd.SendMessage(new Ping(1));
        link.HostEnd.Close();

        Assert.False(link.HostEnd.IsOpen);
        Assert.True(link.ClientEnd.IsOpen, "The client cannot know yet.");

        network.Advance(10 * Millisecond);
        Assert.True(link.ClientEnd.IsOpen, "A message sent before the close is still to be received.");
        Assert.True(link.ClientEnd.TryReceiveMessage(out var message));
        Assert.Equal(new Ping(1), message);
        Assert.False(link.ClientEnd.IsOpen);
        Assert.False(link.ClientEnd.TryReceive(out _));
    }

    [Fact]
    public void AClosedEndSendsAndReceivesNothing()
    {
        var link = new LoopbackNetwork().Connect();

        link.HostEnd.SendMessage(new Ping(1));
        link.ClientEnd.Close();
        link.ClientEnd.SendMessage(new Pong(1));
        link.HostEnd.SendMessage(new Ping(2));

        Assert.False(link.ClientEnd.TryReceive(out _));
        Assert.False(link.HostEnd.TryReceive(out _));
        Assert.False(link.HostEnd.IsOpen);
        Assert.False(link.ClientEnd.IsOpen);
    }

    [Fact]
    public void NegativeTimeIsRejected()
    {
        var network = new LoopbackNetwork();

        Assert.Throws<ArgumentOutOfRangeException>(() => network.Advance(-Millisecond));
        Assert.Throws<ArgumentOutOfRangeException>(() => network.Connect(new LinkConditions(-Millisecond, TimeSpan.Zero)));
        Assert.Throws<ArgumentOutOfRangeException>(() => network.Connect(new LinkConditions(TimeSpan.Zero, -Millisecond)));
    }
}
