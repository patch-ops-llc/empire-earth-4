using System.Diagnostics;

namespace Net.Udp.Tests;

/// <summary>
/// What <see cref="IConnection"/> promises, held to over real sockets. The tests follow the
/// loopback's own in <c>LoopbackNetworkTests</c>, one for one where the promise is the same. The
/// two that are about the loopback only, its seeded arrival times and its clock that cannot run
/// backwards, have no counterpart here.
/// </summary>
public class UdpConnectionTests
{
    [Fact]
    public void MessagesArriveBothWays()
    {
        using var link = new UdpLink();

        link.ClientEnd.SendMessage(new Pong(7));
        link.HostEnd.SendMessage(new Ping(8));

        Assert.Equal(new Pong(7), link.ReceiveMessage(link.HostEnd));
        Assert.Equal(new Ping(8), link.ReceiveMessage(link.ClientEnd));
        link.For(50);
        Assert.False(link.HostEnd.TryReceive(out _));
        Assert.False(link.ClientEnd.TryReceive(out _));
    }

    [Fact]
    public void AMessageArrivesOnlyAfterTheLatency()
    {
        using var link = new UdpLink(RelayConditions.Milliseconds(120));

        var clock = Stopwatch.StartNew();
        link.HostEnd.SendMessage(new Ping(1));
        var message = link.ReceiveMessage(link.ClientEnd);

        Assert.Equal(new Ping(1), message);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(120), $"It arrived after {clock.Elapsed.TotalMilliseconds} ms.");
    }

    [Fact]
    public void JitterAndLossNeverReorderRepeatOrLoseAMessage()
    {
        using var link = new UdpLink(RelayConditions.Milliseconds(10, jitter: 40, loss: 0.1), seed: 5);
        var atHost = new List<NetMessage>();
        var atClient = new List<NetMessage>();

        const int Count = 400;
        var sent = 0;
        link.Until(
            () =>
            {
                // A few at a time, spread over many polls, so that they travel in many datagrams.
                for (var burst = 0; burst < 4 && sent < Count; burst++, sent++)
                {
                    link.HostEnd.SendMessage(new Ping(sent));
                    link.ClientEnd.SendMessage(new Pong(sent));
                }

                atHost.AddRange(link.HostEnd.ReceiveMessages());
                atClient.AddRange(link.ClientEnd.ReceiveMessages());
                return atHost.Count >= Count && atClient.Count >= Count;
            },
            "every message has arrived");
        link.For(150);
        atHost.AddRange(link.HostEnd.ReceiveMessages());
        atClient.AddRange(link.ClientEnd.ReceiveMessages());

        Assert.Equal(Enumerable.Range(0, Count).Select(sequence => (NetMessage)new Pong(sequence)), atHost);
        Assert.Equal(Enumerable.Range(0, Count).Select(sequence => (NetMessage)new Ping(sequence)), atClient);
        Assert.True(link.Relay!.Dropped > 5, $"The network lost only {link.Relay.Dropped} datagrams.");
        Assert.True(link.Relay.Overtaken > 5, $"Only {link.Relay.Overtaken} datagrams overtook another.");
    }

    /// <summary>
    /// The loopback's test of this lowers the latency between two messages. Here the network
    /// holds the first message's datagram, and every repeat of it, while the second message's
    /// passes at once: the second is at the receiver a quarter of a second before the first.
    /// </summary>
    [Fact]
    public void ALaterMessageThatArrivesFirstDoesNotOvertake()
    {
        using var link = new UdpLink(new RelayConditions(BulkyLatency: TimeSpan.FromMilliseconds(250)));
        var first = Pattern(RelayConditions.BulkySize, 1);

        var clock = Stopwatch.StartNew();
        link.HostEnd.Send(first);
        link.HostEnd.SendMessage(new Ping(2));
        link.For(150);

        Assert.False(link.ClientEnd.TryReceive(out _), "The second message was handed over before the first had arrived.");
        Assert.Equal(first, link.Receive(link.ClientEnd).ToArray());
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(250), "The first message was not held, so the second had nothing to overtake.");
        Assert.Equal(new Ping(2), link.ReceiveMessage(link.ClientEnd));
    }

    [Fact]
    public void ClosingDeliversWhatWasSentBeforeAndThenClosesTheOtherEnd()
    {
        using var link = new UdpLink(RelayConditions.Milliseconds(60));

        link.HostEnd.SendMessage(new Ping(1));
        link.HostEnd.Close();

        Assert.False(link.HostEnd.IsOpen);
        Assert.True(link.ClientEnd.IsOpen, "The client cannot know yet.");

        link.Until(() => link.ClientEnd.State == UdpConnectionState.Closed, "the client hears of the close");
        Assert.True(link.ClientEnd.IsOpen, "A message sent before the close is still to be received.");
        Assert.True(link.ClientEnd.TryReceiveMessage(out var message));
        Assert.Equal(new Ping(1), message);
        Assert.False(link.ClientEnd.IsOpen);
        Assert.False(link.ClientEnd.TryReceive(out _));
    }

    [Fact]
    public void AClosedEndSendsAndReceivesNothing()
    {
        using var link = new UdpLink();

        link.HostEnd.SendMessage(new Ping(1));
        link.For(100);
        link.ClientEnd.Close();
        link.ClientEnd.SendMessage(new Pong(1));
        link.HostEnd.SendMessage(new Ping(2));

        link.Until(() => !link.HostEnd.IsOpen, "the host hears of the close");
        link.For(100);
        Assert.False(link.ClientEnd.TryReceive(out _));
        Assert.False(link.HostEnd.TryReceive(out _));
        Assert.False(link.HostEnd.IsOpen);
        Assert.False(link.ClientEnd.IsOpen);
    }

    /// <summary>
    /// Risk R25, first part. A message of the full size takes 65 datagrams, and over the bad
    /// network some of them are lost and the rest arrive out of order.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLargestMessageArrivesWholeBothWays(bool badNetwork)
    {
        using var link = new UdpLink(badNetwork ? RelayConditions.Milliseconds(10, jitter: 20, loss: 0.1) : null, seed: 9);
        var toClient = Pattern(ProtocolInfo.MaxMessageSize, 3);
        var toHost = Pattern(ProtocolInfo.MaxMessageSize, 5);

        link.HostEnd.Send(toClient);
        link.HostEnd.SendMessage(new Ping(1));
        link.ClientEnd.Send(toHost);
        link.ClientEnd.SendMessage(new Pong(1));

        Assert.Equal(toClient, link.Receive(link.ClientEnd).ToArray());
        Assert.Equal(new Ping(1), link.ReceiveMessage(link.ClientEnd));
        Assert.Equal(toHost, link.Receive(link.HostEnd).ToArray());
        Assert.Equal(new Pong(1), link.ReceiveMessage(link.HostEnd));
        if (badNetwork)
        {
            Assert.True(link.Relay!.Dropped > 0, "The network lost nothing.");
        }
    }

    [Fact]
    public void AMessageOverTheLimitIsRejectedAndTheConnectionCarriesOn()
    {
        using var link = new UdpLink();

        Assert.Throws<ArgumentException>(() => link.HostEnd.Send(new byte[ProtocolInfo.MaxMessageSize + 1]));
        link.HostEnd.SendMessage(new Ping(1));

        Assert.Equal(new Ping(1), link.ReceiveMessage(link.ClientEnd));
        Assert.True(link.HostEnd.IsOpen);
    }

    /// <summary>Sizes on both sides of where one datagram stops being enough, and of where two do.</summary>
    [Fact]
    public void MessagesOfEverySizeAroundOneDatagramArriveWhole()
    {
        using var link = new UdpLink();
        var sizes = Enumerable.Range(0, 3).Concat(Enumerable.Range(1000, 40)).Concat(Enumerable.Range(2010, 40)).ToArray();

        foreach (var size in sizes)
        {
            link.ClientEnd.Send(Pattern(size, size));
        }

        foreach (var size in sizes)
        {
            Assert.Equal(Pattern(size, size), link.Receive(link.HostEnd).ToArray());
        }
    }

    /// <summary>
    /// Risk R25, third part. The library receives on a thread of its own, and none of it may show
    /// until the caller polls: the session must not see a connection change in the middle of its
    /// update.
    /// </summary>
    [Fact]
    public void NothingArrivesAndNothingChangesUntilThePoll()
    {
        using var link = new UdpLink();

        link.ClientEnd.SendMessage(new Pong(1));
        link.ClientEnd.Close();
        Wait.For(TimeSpan.FromMilliseconds(300), link.Connector.Poll);

        Assert.False(link.HostEnd.TryReceive(out _));
        Assert.True(link.HostEnd.IsOpen);
        Assert.Equal(UdpConnectionState.Connected, link.HostEnd.State);
        Assert.Equal(UdpCloseReason.None, link.HostEnd.CloseReason);

        link.Until(() => link.HostEnd.State == UdpConnectionState.Closed, "the host hears of the close");
        Assert.True(link.HostEnd.TryReceiveMessage(out var message));
        Assert.Equal(new Pong(1), message);
        Assert.False(link.HostEnd.IsOpen);
    }

    internal static byte[] Pattern(int length, int salt)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
        {
            bytes[index] = unchecked((byte)((index * 31) + (index >> 8) + salt));
        }

        return bytes;
    }
}
