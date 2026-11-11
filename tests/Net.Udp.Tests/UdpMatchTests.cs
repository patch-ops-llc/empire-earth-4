namespace Net.Udp.Tests;

/// <summary>
/// The lockstep session of <c>Net</c> on the real transport: whole matches of the toy game
/// between a host and clients that each have their own socket, driven by the real clock.
/// </summary>
public class UdpMatchTests
{
    private static readonly RelayConditions BadNetwork = RelayConditions.Milliseconds(20, jitter: 20, loss: 0.05);

    /// <summary>
    /// A short tick, so that a test gets through many of them, and a lag limit so wide that only
    /// a client that has really stopped runs into it, however slow the machine.
    /// </summary>
    private static HostOptions Options(int players = 2, int maxLagTicks = 100_000) => new()
    {
        PlayerCount = players,
        HashInterval = 1,
        TickInterval = TimeSpan.FromMilliseconds(20),
        MaxLagTicks = maxLagTicks,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHostAndTwoClientsPlayTheSameMatch(bool badNetwork)
    {
        using var match = new UdpMatch(2, Options(), badNetwork ? BadNetwork : null);
        match.Start();

        var lastCommandTick = new int[2];
        var commands = 0;
        match.Until(
            () =>
            {
                foreach (var client in match.Clients)
                {
                    if (client.Session.Phase == SessionPhase.Running && client.Tick >= lastCommandTick[client.Seat] + 4)
                    {
                        lastCommandTick[client.Seat] = client.Tick;
                        Assert.True(commands % 3 == 2 ? client.Cull() : client.Spawn(commands));
                        commands++;
                    }
                }

                return match.Clients.All(client => client.Tick >= 120);
            },
            "the clients have run 120 ticks");
        var finalTick = match.Finish();

        Assert.True(finalTick >= 120);
        Assert.Empty(match.HostDepartures);
        var replay = ToyGame.Replay(match.Clients[0].Bundles, finalTick);
        foreach (var client in match.Clients)
        {
            Assert.Equal(SessionEndReason.HostEnded, client.Session.EndInfo!.Reason);
            Assert.Equal(finalTick, client.Session.EndInfo.FinalTick);
            Assert.Equal(finalTick, client.Tick);
            Assert.Equal(replay.World.ComputeHash(), client.Game.World.ComputeHash());
            Assert.Equal(replay.Log, client.Game.Log);
            Assert.Equal(replay.World.ComputeHash(), ToyGame.Replay(client.Bundles, finalTick).World.ComputeHash());
        }

        Assert.Contains(replay.Log, executed => executed.Player == 0);
        Assert.Contains(replay.Log, executed => executed.Player == 1);
        Assert.True(replay.Log.Count >= 40, $"Only {replay.Log.Count} commands ran.");

        if (badNetwork)
        {
            Assert.All(match.Clients, client => Assert.True(client.Relay!.Dropped > 0, "The network lost nothing."));
            Assert.All(match.Clients, client => Assert.True(
                match.Host.GetRoundTrip(client.Seat) >= TimeSpan.FromMilliseconds(40),
                $"The host measured a round trip of {match.Host.GetRoundTrip(client.Seat)} over 20 ms of latency each way."));
        }
    }

    /// <summary>
    /// Risk R25, second part, at the level of the session. The host drops a client that hangs:
    /// it sends the reason and closes the connection in the same breath. The client must read
    /// the reason, not just find the connection gone.
    /// </summary>
    [Fact]
    public void AClientTheHostDropsLearnsWhy()
    {
        using var match = new UdpMatch(2, Options(maxLagTicks: 75), RelayConditions.Milliseconds(10, jitter: 20, loss: 0.1));
        match.Start();
        match.Until(() => match.Clients.All(client => client.Tick >= 5), "the match is running");
        var slow = match.Clients[1];
        var other = match.Clients[0];

        slow.Frozen = true;
        match.Until(() => match.HostDepartures.Count > 0, "the host drops the client that hangs");
        Assert.Equal([(slow.Seat, SessionEndReason.FellBehind)], match.HostDepartures);
        Assert.False(match.Host.IsAttached(slow.Seat));
        Assert.Equal(SessionPhase.Running, slow.Session.Phase);

        slow.Frozen = false;
        match.Until(() => slow.Session.Phase == SessionPhase.Stopped, "the dropped client hears of it");
        Assert.Equal(SessionEndReason.FellBehind, slow.Session.EndInfo!.Reason);
        match.Until(() => !slow.Connection.IsOpen, "the dropped client's connection is over");

        match.Until(() => other.Departures.Count > 0, "the other client hears of it");
        Assert.Equal([(slow.Seat, SessionEndReason.FellBehind)], other.Departures);
        var tick = other.Tick;
        match.Until(() => other.Tick >= tick + 10, "the match goes on without the dropped client");
        match.Finish();
        Assert.Equal(SessionEndReason.HostEnded, other.Session.EndInfo!.Reason);
    }

    [Fact]
    public void ADesyncStopsEveryClientWithTheTickItHappenedIn()
    {
        using var match = new UdpMatch(2, Options(), BadNetwork, seedOf: index => ToyGame.DefaultSeed + (ulong)index);
        match.Start();

        match.Until(
            () => match.Host.Phase == SessionPhase.Stopped && match.Clients.All(client => client.Session.Phase == SessionPhase.Stopped),
            "the host and every client have stopped");

        var end = match.Host.EndInfo!;
        Assert.Equal(SessionEndReason.Desync, end.Reason);
        Assert.True(end.DesyncTick >= 0);
        foreach (var client in match.Clients)
        {
            Assert.Equal(SessionEndReason.Desync, client.Session.EndInfo!.Reason);
            Assert.Equal(end.DesyncTick, client.Session.EndInfo.DesyncTick);
            Assert.Equal(end.FinalTick, client.Session.EndInfo.FinalTick);
        }
    }

    /// <summary>
    /// The session has no message for leaving: a client that leaves closes its connection, and
    /// the host reports it as lost, as it does over the loopback. The transport does know the
    /// difference, and says so in the reason the host's end closed with.
    /// </summary>
    [Fact]
    public void AClientThatLeavesIsGoneAtOnceAndTheMatchGoesOn()
    {
        using var match = new UdpMatch(3, Options(players: 3), BadNetwork);
        match.Start();
        match.Until(() => match.Clients.All(client => client.Tick >= 5), "the match is running");
        var leaver = match.Clients[2];
        var seat = leaver.Seat;

        leaver.Session.Leave();

        match.Until(() => match.HostDepartures.Count > 0, "the host sees the client go");
        Assert.Equal([(seat, SessionEndReason.ConnectionLost)], match.HostDepartures);
        Assert.Equal(UdpCloseReason.ClosedByRemote, match.HostConnections[seat].CloseReason);
        Assert.Equal(SessionEndReason.Left, leaver.Session.EndInfo!.Reason);
        foreach (var client in match.Clients.Take(2))
        {
            match.Until(() => client.Departures.Count > 0, "the others hear of it");
            Assert.Equal([(seat, SessionEndReason.ConnectionLost)], client.Departures);
        }

        var finalTick = match.Finish();
        Assert.Equal(match.Clients[0].Game.World.ComputeHash(), match.Clients[1].Game.World.ComputeHash());
        Assert.Equal(finalTick, match.Clients[0].Tick);
        Assert.Equal(finalTick, match.Clients[1].Tick);
    }

    [Fact]
    public void AClientWhoseNetworkFailsIsDroppedAsLostAndTheMatchGoesOn()
    {
        var udpOptions = new UdpOptions { DisconnectTimeout = TimeSpan.FromMilliseconds(800) };
        using var match = new UdpMatch(2, Options(), new RelayConditions(), udpOptions);
        match.Start();
        match.Until(() => match.Clients.All(client => client.Tick >= 5), "the match is running");
        var cut = match.Clients[1];
        var other = match.Clients[0];

        cut.Relay!.Conditions = new RelayConditions(Blocked: true);

        match.Until(() => match.HostDepartures.Count > 0 && cut.Session.Phase == SessionPhase.Stopped, "both ends give up on the connection");
        Assert.Equal([(cut.Seat, SessionEndReason.ConnectionLost)], match.HostDepartures);
        Assert.Equal(SessionEndReason.ConnectionLost, cut.Session.EndInfo!.Reason);
        Assert.Equal(UdpCloseReason.TimedOut, cut.Connection.CloseReason);
        Assert.Equal(UdpCloseReason.TimedOut, match.HostConnections[cut.Seat].CloseReason);

        match.Until(() => other.Departures.Count > 0, "the other client hears of it");
        Assert.Equal([(cut.Seat, SessionEndReason.ConnectionLost)], other.Departures);
        var tick = other.Tick;
        match.Until(() => other.Tick >= tick + 10, "the match goes on");
        match.Finish();
        Assert.Equal(SessionEndReason.HostEnded, other.Session.EndInfo!.Reason);
    }
}
