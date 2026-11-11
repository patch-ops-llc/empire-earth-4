using System.Diagnostics;

namespace Net.Udp.Tests;

/// <summary>
/// Risk R25, second part: the orderly close. The library's own disconnect throws away what it
/// has not got across yet, so the connection adds a close notice that travels behind the
/// messages. These tests close right after sending, over a network that loses and reorders.
/// </summary>
public class UdpCloseTests
{
    private static readonly RelayConditions BadNetwork = RelayConditions.Milliseconds(15, jitter: 30, loss: 0.2);

    [Theory]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    public void EverythingSentJustBeforeACloseArrivesOverABadNetwork(bool hostCloses, int seed)
    {
        using var link = new UdpLink(BadNetwork, seed: seed);
        var (closer, other) = hostCloses ? (link.HostEnd, link.ClientEnd) : (link.ClientEnd, link.HostEnd);
        var large = UdpConnectionTests.Pattern(ProtocolInfo.MaxMessageSize, seed);

        for (var sequence = 0; sequence < 30; sequence++)
        {
            closer.SendMessage(new Ping(sequence));
        }

        closer.Send(large);
        closer.SendMessage(new SessionEnd(SessionEndReason.FellBehind, 77, -1));
        closer.Close();

        Assert.False(closer.IsOpen);
        for (var sequence = 0; sequence < 30; sequence++)
        {
            Assert.Equal(new Ping(sequence), link.ReceiveMessage(other));
        }

        Assert.Equal(large, link.Receive(other).ToArray());
        Assert.True(other.IsOpen, "The reason for the close is still to be received.");
        Assert.Equal(new SessionEnd(SessionEndReason.FellBehind, 77, -1), link.ReceiveMessage(other));
        link.Until(() => !other.IsOpen, "the other end hears of the close");
        Assert.False(other.TryReceive(out _));
        Assert.True(link.Relay!.Dropped > 0, "The network lost nothing.");

        Assert.Equal(UdpCloseReason.ClosedHere, closer.CloseReason);
        Assert.Equal(UdpCloseReason.ClosedByRemote, other.CloseReason);
        link.Until(() => link.Listener.ConnectionCount == 0 && link.Connector.ConnectionCount == 0, "both links are gone");
    }

    /// <summary>The close timeout is a limit for a bad day, not how long a close takes.</summary>
    [Fact]
    public void TheEndThatClosedLetsGoAsSoonAsTheOtherConfirms()
    {
        var options = new UdpOptions { CloseTimeout = TimeSpan.FromSeconds(20) };
        using var link = new UdpLink(options: options);

        var clock = Stopwatch.StartNew();
        link.ClientEnd.SendMessage(new Pong(1));
        link.ClientEnd.Close();

        link.Until(() => link.Connector.ConnectionCount == 0 && link.Listener.ConnectionCount == 0, "both links are gone");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"It took {clock.Elapsed.TotalSeconds:0.0} seconds.");
        Assert.Equal(new Pong(1), link.ReceiveMessage(link.HostEnd));
        Assert.False(link.HostEnd.IsOpen);
    }

    /// <summary>
    /// The library's keep-alive has to fit inside the timeout, or a connection would be given up
    /// on for having nothing to say.
    /// </summary>
    [Fact]
    public void AQuietConnectionIsNotGivenUpOn()
    {
        var options = new UdpOptions { DisconnectTimeout = TimeSpan.FromMilliseconds(800) };
        using var link = new UdpLink(options: options);

        link.For(2500);

        Assert.Equal(UdpConnectionState.Connected, link.HostEnd.State);
        Assert.Equal(UdpConnectionState.Connected, link.ClientEnd.State);
        link.HostEnd.SendMessage(new Ping(1));
        Assert.Equal(new Ping(1), link.ReceiveMessage(link.ClientEnd));
    }

    [Fact]
    public void BothEndsCanCloseAtOnce()
    {
        using var link = new UdpLink(RelayConditions.Milliseconds(30));

        link.HostEnd.SendMessage(new Ping(1));
        link.ClientEnd.SendMessage(new Pong(1));
        link.HostEnd.Close();
        link.ClientEnd.Close();

        link.Until(() => link.Listener.ConnectionCount == 0 && link.Connector.ConnectionCount == 0, "both links are gone");
        Assert.False(link.HostEnd.TryReceive(out _));
        Assert.False(link.ClientEnd.TryReceive(out _));
        Assert.Equal(UdpCloseReason.ClosedHere, link.HostEnd.CloseReason);
        Assert.Equal(UdpCloseReason.ClosedHere, link.ClientEnd.CloseReason);
    }

    [Fact]
    public void ClosingTwiceAndClosingAfterTheOtherEndAreHarmless()
    {
        using var link = new UdpLink();

        link.HostEnd.Close();
        link.HostEnd.Close();
        link.Until(() => !link.ClientEnd.IsOpen, "the client hears of the close");
        link.ClientEnd.Close();
        link.ClientEnd.Close();
        link.ClientEnd.SendMessage(new Pong(1));

        link.Until(() => link.Listener.ConnectionCount == 0 && link.Connector.ConnectionCount == 0, "both links are gone");
        Assert.Equal(UdpCloseReason.ClosedHere, link.HostEnd.CloseReason);
        Assert.Equal(UdpCloseReason.ClosedByRemote, link.ClientEnd.CloseReason);
    }

    /// <summary>
    /// The limit R25 asks for: an end that closed waits for the other to confirm, and not for
    /// ever.
    /// </summary>
    [Fact]
    public void ACloseTheOtherEndNeverConfirmsIsCutWhenTheCloseTimeoutPasses()
    {
        var options = new UdpOptions { CloseTimeout = TimeSpan.FromMilliseconds(400) };
        using var link = new UdpLink(new RelayConditions(), options);

        link.Relay!.Conditions = new RelayConditions(Blocked: true);
        var clock = Stopwatch.StartNew();
        link.HostEnd.SendMessage(new Ping(1));
        link.HostEnd.Close();

        Assert.False(link.HostEnd.IsOpen);
        Assert.Equal(1, link.Listener.ConnectionCount);
        link.Until(() => link.Listener.ConnectionCount == 0, "the host gives up on the close");
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(400), $"It gave up after {clock.Elapsed.TotalMilliseconds} ms.");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), $"It gave up only after {clock.Elapsed.TotalMilliseconds} ms.");
    }

    [Fact]
    public void WhenNothingGetsThroughBothEndsCloseAsTimedOut()
    {
        var options = new UdpOptions { DisconnectTimeout = TimeSpan.FromMilliseconds(600) };
        using var link = new UdpLink(new RelayConditions(), options);

        link.HostEnd.SendMessage(new Ping(1));
        link.Until(() => link.Relay!.Forwarded > 0, "the message is on its way");
        link.For(100);
        link.Relay!.Conditions = new RelayConditions(Blocked: true);
        link.HostEnd.SendMessage(new Ping(2));

        link.Until(() => !link.HostEnd.IsOpen && link.ClientEnd.State == UdpConnectionState.Closed, "both ends give up");
        Assert.Equal(UdpCloseReason.TimedOut, link.HostEnd.CloseReason);
        Assert.Equal(UdpCloseReason.TimedOut, link.ClientEnd.CloseReason);

        Assert.True(link.ClientEnd.IsOpen, "What arrived before the network failed is still to be received.");
        Assert.Equal(new Ping(1), link.ReceiveMessage(link.ClientEnd));
        Assert.False(link.ClientEnd.IsOpen);
        Assert.False(link.ClientEnd.TryReceive(out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisposingDeliversWhatWasSentFirst(bool disposeListener)
    {
        using var link = new UdpLink(BadNetwork, seed: 7);
        var (closer, other) = disposeListener ? (link.HostEnd, link.ClientEnd) : (link.ClientEnd, link.HostEnd);
        var large = UdpConnectionTests.Pattern(ProtocolInfo.MaxMessageSize, 11);

        closer.Send(large);
        closer.SendMessage(new Ping(1));

        // The two ends are two programs. The one that quits waits in its dispose while the other
        // goes on polling, so here the dispose needs a thread of its own.
        link.ListenerGone = disposeListener;
        link.ConnectorGone = !disposeListener;
        var received = new List<ReadOnlyMemory<byte>>();
        Wait.DisposeWhilePolling(disposeListener ? link.Listener : link.Connector, () =>
        {
            link.Poll();
            while (other.TryReceive(out var message))
            {
                received.Add(message);
            }
        });

        Assert.Equal(2, received.Count);
        Assert.Equal(large, received[0].ToArray());
        Assert.Equal(new Ping(1), MessageCodec.Read(received[1]));
        Assert.False(other.IsOpen);
        Assert.Equal(UdpCloseReason.ClosedByRemote, other.CloseReason);
        Assert.False(closer.IsOpen);
    }

    [Fact]
    public void DisposingReturnsWhenTheCloseTimeoutPassesIfTheOtherEndIsNotThere()
    {
        var options = new UdpOptions { CloseTimeout = TimeSpan.FromMilliseconds(300) };
        using var link = new UdpLink(options: options);

        link.HostEnd.SendMessage(new Ping(1));
        var clock = Stopwatch.StartNew();
        link.ListenerGone = true;
        link.Listener.Dispose();

        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(300), $"It returned after {clock.Elapsed.TotalMilliseconds} ms.");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), $"It returned only after {clock.Elapsed.TotalMilliseconds} ms.");
        Assert.False(link.HostEnd.IsOpen);
    }

    [Fact]
    public void ADisposedListenerOrConnectorRefusesToBeUsedAndItsConnectionsStaySilent()
    {
        using var link = new UdpLink(options: new UdpOptions { CloseTimeout = TimeSpan.Zero });

        link.ListenerGone = true;
        link.Listener.Dispose();
        link.Listener.Dispose();
        link.Until(() => !link.ClientEnd.IsOpen, "the client hears that the host is gone");
        link.ConnectorGone = true;
        link.Connector.Dispose();
        link.Connector.Dispose();

        Assert.Throws<ObjectDisposedException>(link.Listener.Poll);
        Assert.Throws<ObjectDisposedException>(() => link.Listener.TryAccept(out _));
        Assert.Throws<ObjectDisposedException>(link.Connector.Poll);
        Assert.Throws<ObjectDisposedException>(() => link.Connector.Connect("127.0.0.1", 9));
        link.HostEnd.SendMessage(new Ping(1));
        link.HostEnd.Close();
        Assert.False(link.HostEnd.IsOpen);
        Assert.False(link.HostEnd.TryReceive(out _));
        Assert.Equal(0, link.Listener.ConnectionCount);
        Assert.Equal(0, link.Connector.ConnectionCount);
    }
}
