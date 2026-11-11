using System.Diagnostics;
using System.Net;

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
    /// Both ends send their first message at the same moment. LiteNetLib 2.1.5 on its own threads
    /// got this wrong about once in forty connections: the caller's thread and the library's
    /// receive thread each made the peer's reliable channel, one of the two was thrown away, and
    /// the first message was either never sent or handed over twice. It showed as one failure on
    /// Windows. The transport now runs the library under a lock, where it cannot happen; this
    /// test is there for whoever changes that. The pause between the two sends is varied so that
    /// the second one falls where the first one's arrival is being handled.
    /// </summary>
    [Fact]
    public void TheFirstMessagesOfBothEndsSentAtTheSameMomentArriveOnceEach()
    {
        const int Links = 64;
        using var listener = new UdpListener(IPAddress.Loopback, options: new UdpOptions { MaxConnections = Links });
        var connectors = new List<UdpConnector>();
        var ends = new List<(UdpConnection Client, UdpConnection Host)>();
        void Poll()
        {
            listener.Poll();
            connectors.ForEach(connector => connector.Poll());
        }

        try
        {
            for (var index = 0; index < Links; index++)
            {
                var connector = new UdpConnector(IPAddress.Loopback);
                connectors.Add(connector);
                var client = connector.Connect(new IPEndPoint(IPAddress.Loopback, listener.Port));
                UdpConnection? host = null;
                Wait.Until(() => host is not null || listener.TryAccept(out host), Poll, "the listener has the connection");
                Wait.Until(() => client.State == UdpConnectionState.Connected, Poll, "the connector hears the answer");
                ends.Add((client, host!));

                // From no pause to 375 microseconds, in steps of 25.
                var pause = Stopwatch.Frequency * (index % 16) / 40_000;
                client.Send([1, (byte)index]);
                var sentAt = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() - sentAt < pause)
                {
                }

                host!.Send([2, (byte)index]);
            }

            var atHost = ends.Select(_ => new List<byte[]>()).ToArray();
            var atClient = ends.Select(_ => new List<byte[]>()).ToArray();
            void Collect()
            {
                Poll();
                for (var index = 0; index < Links; index++)
                {
                    while (ends[index].Host.TryReceive(out var message))
                    {
                        atHost[index].Add(message.ToArray());
                    }

                    while (ends[index].Client.TryReceive(out var message))
                    {
                        atClient[index].Add(message.ToArray());
                    }
                }
            }

            Wait.Until(
                () => atHost.All(messages => messages.Count > 0) && atClient.All(messages => messages.Count > 0),
                Collect,
                "every first message has arrived");

            // Long enough for a message that was not acknowledged to be sent again.
            Wait.For(TimeSpan.FromMilliseconds(400), Collect);

            for (var index = 0; index < Links; index++)
            {
                Assert.Equal([[1, (byte)index]], atHost[index]);
                Assert.Equal([[2, (byte)index]], atClient[index]);
            }
        }
        finally
        {
            ends.ForEach(pair => pair.Client.Close());
            var clock = Stopwatch.StartNew();
            while ((listener.ConnectionCount > 0 || connectors.Exists(connector => connector.ConnectionCount > 0)) && clock.Elapsed < TimeSpan.FromSeconds(5))
            {
                Poll();
                Thread.Sleep(1);
            }

            connectors.ForEach(connector => connector.Dispose());
        }
    }

    /// <summary>
    /// A game that hangs on a long frame does not poll. Its connection must still be there when
    /// it comes back: the transport answers the other end's keep-alives on a thread of its own.
    /// What arrived in the meantime is handed over at the next poll and not before.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEndThatDoesNotPollForLongerThanTheTimeoutKeepsItsConnection(bool hostHangs)
    {
        var options = new UdpOptions { DisconnectTimeout = TimeSpan.FromMilliseconds(800) };
        using var link = new UdpLink(options: options);
        var (hung, awake) = hostHangs ? (link.HostEnd, link.ClientEnd) : (link.ClientEnd, link.HostEnd);

        awake.SendMessage(new Ping(1));
        Wait.For(TimeSpan.FromMilliseconds(2500), hostHangs ? link.Connector.Poll : link.Listener.Poll);

        Assert.Equal(UdpConnectionState.Connected, awake.State);
        Assert.Equal(UdpConnectionState.Connected, hung.State);
        Assert.False(hung.TryReceive(out _), "A message was handed over without a poll.");

        Assert.Equal(new Ping(1), link.ReceiveMessage(hung));
        hung.SendMessage(new Pong(1));
        Assert.Equal(new Pong(1), link.ReceiveMessage(awake));
    }

    /// <summary>
    /// Risk R25, third part. The transport reads the socket on a thread of its own, and none of
    /// it may show until the caller polls: the session must not see a connection change in the
    /// middle of its update.
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
