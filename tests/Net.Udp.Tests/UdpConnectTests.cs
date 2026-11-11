using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace Net.Udp.Tests;

/// <summary>The listener and the connector: who gets a connection, who is turned away, and why.</summary>
public class UdpConnectTests
{
    private static readonly IPEndPoint Nowhere = new(IPAddress.Loopback, 9);

    /// <summary>
    /// Risk R25, third part, for the listener: the library answers a client on its own thread
    /// only once the listener's poll has taken the request.
    /// </summary>
    [Fact]
    public void AConnectionIsTakenOnlyInThePoll()
    {
        using var listener = new UdpListener(IPAddress.Loopback);
        using var connector = new UdpConnector(IPAddress.Loopback);

        var connection = connector.Connect(Local(listener.Port));
        Wait.For(TimeSpan.FromMilliseconds(300), connector.Poll);

        Assert.Equal(0, listener.ConnectionCount);
        Assert.Equal(UdpConnectionState.Connecting, connection.State);
        Assert.True(connection.IsOpen);

        listener.Poll();
        Assert.True(listener.TryAccept(out var accepted));
        Assert.False(listener.TryAccept(out _));
        Assert.Equal(1, listener.ConnectionCount);
        Assert.Equal(UdpConnectionState.Connected, accepted.State);
        Assert.True(IPAddress.IsLoopback(accepted.RemoteEndPoint.Address));
        Assert.Equal(Local(listener.Port), connection.RemoteEndPoint);
        Wait.Until(() => connection.State == UdpConnectionState.Connected, connector.Poll, "the connector hears the answer");

        Shutdown(listener, connector);
    }

    /// <summary>
    /// A client session sends its request to join from its constructor, before the host has
    /// answered. The connection keeps it, and whatever else is sent, until it can go out.
    /// </summary>
    [Fact]
    public void WhatIsSentBeforeTheHostAnswersGoesOutInOrderOnceItDoes()
    {
        using var listener = new UdpListener(IPAddress.Loopback);
        using var relay = new UdpRelay(listener.Port, RelayConditions.Milliseconds(80));
        using var connector = new UdpConnector(IPAddress.Loopback);

        var connection = connector.Connect(Local(relay.Port));
        connection.SendMessage(new Pong(1));
        connection.Send(UdpConnectionTests.Pattern(ProtocolInfo.MaxMessageSize, 2));
        connection.SendMessage(new Pong(3));
        connection.Close();
        Assert.False(connection.IsOpen);

        UdpConnection? accepted = null;
        Until(() => listener.TryAccept(out accepted), "the listener has the connection", listener, connector);
        var received = new List<ReadOnlyMemory<byte>>();
        Until(
            () =>
            {
                while (accepted!.TryReceive(out var message))
                {
                    received.Add(message);
                }

                return !accepted.IsOpen;
            },
            "the host hears of the close",
            listener,
            connector);

        Assert.Equal(3, received.Count);
        Assert.Equal(new Pong(1), MessageCodec.Read(received[0]));
        Assert.Equal(UdpConnectionTests.Pattern(ProtocolInfo.MaxMessageSize, 2), received[1].ToArray());
        Assert.Equal(new Pong(3), MessageCodec.Read(received[2]));
        Shutdown(listener, connector);
    }

    /// <summary>
    /// The request to connect is on its way and cannot be called back. If the client just went
    /// silent, the host would take the connection and hold a place for it until it timed out. So
    /// the close waits for the host's answer and then closes in the usual way.
    /// </summary>
    [Fact]
    public void AConnectionClosedBeforeTheHostAnsweredIsOverOnBothSidesWithoutWaitingForATimeout()
    {
        using var listener = new UdpListener(IPAddress.Loopback);
        using var connector = new UdpConnector(IPAddress.Loopback);

        var clock = Stopwatch.StartNew();
        var connection = connector.Connect(Local(listener.Port));
        connection.Close();

        Assert.False(connection.IsOpen);
        Assert.Equal(UdpCloseReason.ClosedHere, connection.CloseReason);
        Assert.Equal(UdpConnectionState.Closed, connection.State);

        UdpConnection? accepted = null;
        Until(() => listener.TryAccept(out accepted), "the listener has the connection", listener, connector);
        Until(() => listener.ConnectionCount == 0 && connector.ConnectionCount == 0, "both links are gone", listener, connector);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), $"It took {clock.Elapsed.TotalMilliseconds} ms, as long as a timeout.");
        Assert.False(accepted!.IsOpen);
        Assert.Equal(UdpCloseReason.ClosedByRemote, accepted.CloseReason);
        Assert.False(accepted.TryReceive(out _));
    }

    [Fact]
    public void ACloseBeforeAHostThatNeverAnswersGivesUpWhenTheCloseTimeoutPasses()
    {
        using var listener = new UdpListener(IPAddress.Loopback);
        using var relay = new UdpRelay(listener.Port, new RelayConditions(Blocked: true));
        using var connector = new UdpConnector(IPAddress.Loopback, new UdpOptions { CloseTimeout = TimeSpan.FromMilliseconds(300) });

        var clock = Stopwatch.StartNew();
        var connection = connector.Connect(Local(relay.Port));
        connection.SendMessage(new Pong(1));
        connection.Close();

        Assert.Equal(1, connector.ConnectionCount);
        Wait.Until(() => connector.ConnectionCount == 0, connector.Poll, "the connector gives up on the close");
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(300), $"It gave up after {clock.Elapsed.TotalMilliseconds} ms.");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), $"It gave up only after {clock.Elapsed.TotalMilliseconds} ms.");
        Assert.Equal(UdpCloseReason.ClosedHere, connection.CloseReason);
    }

    [Fact]
    public void AHostThatNeverAnswersEndsTheConnectionAsFailed()
    {
        using var listener = new UdpListener(IPAddress.Loopback);
        using var relay = new UdpRelay(listener.Port, new RelayConditions(Blocked: true));
        using var connector = new UdpConnector(IPAddress.Loopback, new UdpOptions { ConnectTimeout = TimeSpan.FromMilliseconds(500) });

        var clock = Stopwatch.StartNew();
        var connection = connector.Connect(Local(relay.Port));
        var session = new ClientSession(connection);
        SessionEndInfo? end = null;
        session.Stopped += info => end = info;

        Until(
            () =>
            {
                session.Advance(TimeSpan.FromMilliseconds(1));
                return !connection.IsOpen;
            },
            "the connector gives up",
            listener,
            connector);

        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(450), $"It gave up after {clock.Elapsed.TotalMilliseconds} ms.");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"It gave up only after {clock.Elapsed.TotalMilliseconds} ms.");
        Assert.Equal(UdpCloseReason.ConnectFailed, connection.CloseReason);
        Assert.Equal(0, connector.ConnectionCount);
        Assert.Equal(0, listener.ConnectionCount);

        session.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(SessionPhase.Stopped, session.Phase);
        Assert.Equal(SessionEndReason.ConnectionLost, end?.Reason);
    }

    /// <summary>
    /// With no program on the port, some systems say so and some say nothing. Either way the
    /// connection ends, and not as if a host had been there.
    /// </summary>
    [Fact]
    public void APortNobodyListensOnEndsTheConnection()
    {
        int port;
        using (var listener = new UdpListener(IPAddress.Loopback))
        {
            port = listener.Port;
        }

        using var connector = new UdpConnector(IPAddress.Loopback, new UdpOptions { ConnectTimeout = TimeSpan.FromMilliseconds(500) });
        var connection = connector.Connect(Local(port));

        Wait.Until(() => !connection.IsOpen, connector.Poll, "the connector gives up");
        Assert.Contains(connection.CloseReason, new[] { UdpCloseReason.ConnectFailed, UdpCloseReason.Unreachable });
    }

    [Fact]
    public void AListenerThatHasStoppedAcceptingRefusesAtOnce()
    {
        using var listener = new UdpListener(IPAddress.Loopback) { Accepting = false };
        using var connector = new UdpConnector(IPAddress.Loopback);

        var clock = Stopwatch.StartNew();
        var refused = connector.Connect(Local(listener.Port));
        Until(() => !refused.IsOpen, "the client is refused", listener, connector);

        Assert.Equal(UdpCloseReason.Refused, refused.CloseReason);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), "The client was left waiting for the whole connect timeout.");
        Assert.False(listener.TryAccept(out _));
        Assert.Equal(0, listener.ConnectionCount);

        listener.Accepting = true;
        var welcome = connector.Connect(Local(listener.Port));
        Until(() => welcome.State == UdpConnectionState.Connected, "the client is taken", listener, connector);
        Assert.True(listener.TryAccept(out _));
        Shutdown(listener, connector);
    }

    [Fact]
    public void AFullListenerRefusesUntilAConnectionEnds()
    {
        using var listener = new UdpListener(IPAddress.Loopback, options: new UdpOptions { MaxConnections = 2 });
        using var first = new UdpConnector(IPAddress.Loopback);
        using var second = new UdpConnector(IPAddress.Loopback);
        using var third = new UdpConnector(IPAddress.Loopback);

        var one = first.Connect(Local(listener.Port));
        var two = second.Connect(Local(listener.Port));
        Until(() => one.State == UdpConnectionState.Connected && two.State == UdpConnectionState.Connected, "two clients are in", listener, first, second);
        var three = third.Connect(Local(listener.Port));
        Until(() => !three.IsOpen, "the third is refused", listener, third);

        Assert.Equal(UdpCloseReason.Refused, three.CloseReason);
        Assert.Equal(2, listener.ConnectionCount);

        one.Close();
        Until(() => listener.ConnectionCount == 1, "the first has gone", listener, first);
        var again = third.Connect(Local(listener.Port));
        Until(() => again.State == UdpConnectionState.Connected, "the third is taken", listener, third);
        Assert.Equal(2, listener.ConnectionCount);
        Shutdown(listener, first, second, third);
    }

    [Fact]
    public void TwoClientsHaveSeparateConnections()
    {
        using var listener = new UdpListener(IPAddress.Loopback);
        using var first = new UdpConnector(IPAddress.Loopback);
        using var second = new UdpConnector(IPAddress.Loopback);

        var one = first.Connect(Local(listener.Port));
        one.SendMessage(new Pong(1));
        var two = second.Connect(Local(listener.Port));
        two.SendMessage(new Pong(2));

        var accepted = new List<UdpConnection>();
        Until(
            () =>
            {
                while (listener.TryAccept(out var connection))
                {
                    accepted.Add(connection);
                }

                return accepted.Count == 2;
            },
            "both are taken",
            listener,
            first,
            second);

        foreach (var connection in accepted)
        {
            NetMessage? greeting = null;
            Until(() => connection.TryReceiveMessage(out greeting), "the greeting arrives", listener, first, second);
            var sequence = ((Pong)greeting!).Sequence;
            connection.SendMessage(new Ping(sequence));
            Assert.False(connection.TryReceive(out _));
        }

        NetMessage? atOne = null;
        NetMessage? atTwo = null;
        Until(
            () => (atOne is not null || one.TryReceiveMessage(out atOne)) && (atTwo is not null || two.TryReceiveMessage(out atTwo)),
            "the answers arrive",
            listener,
            first,
            second);
        Assert.Equal(new Ping(1), atOne);
        Assert.Equal(new Ping(2), atTwo);
        Shutdown(listener, first, second);
    }

    [Fact]
    public void OneConnectorCannotConnectTwiceToTheSameHost()
    {
        using var listener = new UdpListener(IPAddress.Loopback);
        using var connector = new UdpConnector(IPAddress.Loopback);

        var connection = connector.Connect(Local(listener.Port));
        Assert.Throws<InvalidOperationException>(() => connector.Connect(Local(listener.Port)));
        Until(() => connection.State == UdpConnectionState.Connected, "the client is taken", listener, connector);
        Assert.Throws<InvalidOperationException>(() => connector.Connect(Local(listener.Port)));

        connection.Close();
        Until(() => connector.ConnectionCount == 0, "the connection has gone", listener, connector);
        var again = connector.Connect(Local(listener.Port));
        Until(() => again.State == UdpConnectionState.Connected, "the client is taken again", listener, connector);
        Shutdown(listener, connector);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    public void AHostCanBeGivenAsTextOrByName(string host)
    {
        using var listener = new UdpListener(IPAddress.Loopback);
        using var connector = new UdpConnector(IPAddress.Loopback);

        var connection = connector.Connect(host, listener.Port);

        Until(() => connection.State == UdpConnectionState.Connected, "the client is taken", listener, connector);
        Shutdown(listener, connector);
    }

    [Fact]
    public void ANameThatDoesNotExistIsAnError()
    {
        using var connector = new UdpConnector(IPAddress.Loopback);

        // The top-level domain "invalid" is reserved never to exist.
        Assert.ThrowsAny<SocketException>(() => connector.Connect("no-such-host.invalid", 4000));
        Assert.Throws<ArgumentException>(() => connector.Connect(" ", 4000));
    }

    [Fact]
    public void TheLoopbackListenerAlsoTakesConnectionsOverIPv6()
    {
        Assert.SkipUnless(CanBindIPv6Loopback(), "This machine has no IPv6 loopback address.");
        using var listener = new UdpListener(IPAddress.Loopback);
        using var connector = new UdpConnector(IPAddress.Loopback);

        var connection = connector.Connect(new IPEndPoint(IPAddress.IPv6Loopback, listener.Port));
        connection.SendMessage(new Pong(6));

        UdpConnection? accepted = null;
        Until(() => listener.TryAccept(out accepted), "the listener has the connection", listener, connector);
        NetMessage? message = null;
        Until(() => accepted!.TryReceiveMessage(out message), "the message arrives", listener, connector);
        Assert.Equal(new Pong(6), message);
        Assert.Equal(AddressFamily.InterNetworkV6, accepted!.RemoteEndPoint.AddressFamily);
        Shutdown(listener, connector);
    }

    [Fact]
    public void APortInUseIsAnError()
    {
        using var listener = new UdpListener(IPAddress.Loopback);

        Assert.Throws<IOException>(() => new UdpListener(IPAddress.Loopback, listener.Port));
    }

    [Fact]
    public void BadArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new UdpListener(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UdpListener(IPAddress.Loopback, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UdpListener(IPAddress.Loopback, 65536));
        Assert.Throws<ArgumentException>(() => new UdpListener(IPAddress.Parse("fe80::1")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UdpListener(IPAddress.Loopback, options: new UdpOptions { MaxConnections = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UdpListener(IPAddress.Loopback, options: new UdpOptions { DisconnectTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UdpConnector(new UdpOptions { ConnectTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UdpConnector(new UdpOptions { CloseTimeout = TimeSpan.FromSeconds(-1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UdpConnector(new UdpOptions { CloseTimeout = TimeSpan.FromDays(1) }));

        using var connector = new UdpConnector(IPAddress.Loopback);
        Assert.Throws<ArgumentNullException>(() => connector.Connect(null!));
    }

    /// <summary>
    /// Another program that happens to speak LiteNetLib, or another version of this one, is
    /// turned away when it asks, before it can send a message.
    /// </summary>
    [Fact]
    public void AClientWithTheWrongKeyIsRefused()
    {
        using var listener = new UdpListener(IPAddress.Loopback);
        using var stranger = new RawPeer();

        stranger.Connect(listener.Port, "some other game");

        Wait.Until(
            () => stranger.Disconnected is not null,
            () =>
            {
                listener.Poll();
                stranger.Poll();
            },
            "the stranger is refused");
        Assert.Equal(DisconnectReason.ConnectionRejected, stranger.Disconnected);
        Assert.False(listener.TryAccept(out _));
        Assert.Equal(0, listener.ConnectionCount);
    }

    /// <summary>
    /// A peer that has the key and then sends what this code never sends is cut off, and the
    /// session behind the connection sees it close.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 2, 0 }, DeliveryMethod.ReliableOrdered)]
    [InlineData(new byte[] { 1, 0 }, DeliveryMethod.ReliableOrdered)]
    [InlineData(new byte[] { 0, 7 }, DeliveryMethod.Unreliable)]
    [InlineData(new byte[] { 0, 7 }, DeliveryMethod.ReliableUnordered)]
    public void APeerThatBreaksTheFramingIsCutOff(byte[] payload, DeliveryMethod method)
    {
        using var listener = new UdpListener(IPAddress.Loopback);
        using var stranger = new RawPeer();
        void PollBoth()
        {
            listener.Poll();
            stranger.Poll();
        }

        stranger.Connect(listener.Port, UdpInfo.ConnectKey);
        UdpConnection? accepted = null;
        Wait.Until(() => listener.TryAccept(out accepted), PollBoth, "the listener has the connection");
        Wait.Until(() => stranger.Connected, PollBoth, "the stranger is connected");

        stranger.Send([0, 42], DeliveryMethod.ReliableOrdered);
        stranger.Send(payload, method);

        Wait.Until(() => stranger.Disconnected is not null, PollBoth, "the stranger is cut off");
        Assert.Equal(DisconnectReason.RemoteConnectionClose, stranger.Disconnected);
        Assert.Equal(UdpConnectionState.Closed, accepted!.State);
        Assert.Equal(0, listener.ConnectionCount);
        if (method == DeliveryMethod.ReliableOrdered)
        {
            Assert.True(accepted.TryReceive(out var message), "What came before the bad frame was well formed.");
            Assert.Equal([42], message.ToArray());
        }
    }

    /// <summary>A message over the limit, from a peer that does not hold to it, is not handed over.</summary>
    [Fact]
    public void AMessageOverTheLimitFromAPeerIsNotHandedOver()
    {
        using var listener = new UdpListener(IPAddress.Loopback);
        using var stranger = new RawPeer();
        void PollBoth()
        {
            listener.Poll();
            stranger.Poll();
        }

        stranger.Connect(listener.Port, UdpInfo.ConnectKey);
        UdpConnection? accepted = null;
        Wait.Until(() => listener.TryAccept(out accepted), PollBoth, "the listener has the connection");
        Wait.Until(() => stranger.Connected, PollBoth, "the stranger is connected");

        // One frame byte and one byte more than a message may have.
        stranger.Send(new byte[ProtocolInfo.MaxMessageSize + 2], DeliveryMethod.ReliableOrdered);

        Wait.Until(() => stranger.Disconnected is not null, PollBoth, "the stranger is cut off");
        Assert.False(accepted!.TryReceive(out _));
        Assert.False(accepted.IsOpen);
    }

    private static IPEndPoint Local(int port) => new(IPAddress.Loopback, port);

    private static bool CanBindIPv6Loopback()
    {
        if (!Socket.OSSupportsIPv6)
        {
            return false;
        }

        try
        {
            using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static void Poll(UdpListener listener, params UdpConnector[] connectors)
    {
        listener.Poll();
        foreach (var connector in connectors)
        {
            connector.Poll();
        }
    }

    private static void Until(Func<bool> condition, string what, UdpListener listener, params UdpConnector[] connectors) =>
        Wait.Until(condition, () => Poll(listener, connectors), what);

    /// <summary>
    /// Ends a test whose connections are still open without the wait that a dispose alone would
    /// cost: both sides are polled until every link is gone.
    /// </summary>
    private static void Shutdown(UdpListener listener, params UdpConnector[] connectors) =>
        Wait.DisposeWhilePolling(listener, () =>
        {
            foreach (var connector in connectors)
            {
                connector.Poll();
            }
        });

    /// <summary>A bare LiteNetLib peer, to send a listener what a <see cref="UdpConnector"/> never would.</summary>
    private sealed class RawPeer : IDisposable
    {
        private readonly EventBasedNetListener _events = new();
        private readonly NetManager _manager;
        private NetPeer? _peer;

        public RawPeer()
        {
            _manager = new NetManager(_events) { IPv6Enabled = false };
            _events.PeerConnectedEvent += _ => Connected = true;
            _events.PeerDisconnectedEvent += (_, info) => Disconnected = info.Reason;
            Assert.True(_manager.Start(IPAddress.Loopback, IPAddress.IPv6None, 0));
        }

        public bool Connected { get; private set; }

        public DisconnectReason? Disconnected { get; private set; }

        public void Connect(int port, string key) => _peer = _manager.Connect(Local(port), key);

        public void Send(byte[] payload, DeliveryMethod method) => _peer!.Send(payload, method);

        public void Poll() => _manager.PollEvents();

        public void Dispose() => _manager.Stop();
    }
}
