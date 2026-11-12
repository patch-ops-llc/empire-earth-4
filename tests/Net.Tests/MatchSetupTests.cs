using Sim.Serialization;
using Sim.Setup;

namespace Net.Tests;

/// <summary>The description of the match that the host sends a client when it joins, and what the client does with it.</summary>
public class MatchSetupTests
{
    [Fact]
    public void TheHostDescribesTheMatchStraightAfterAcceptingTheJoin()
    {
        var description = TestData.Description();
        var link = new LoopbackNetwork().Connect();
        var host = new HostSession(new HostOptions { Match = description });
        host.Attach(1, link.HostEnd);

        link.ClientEnd.SendMessage(new JoinRequest(ProtocolInfo.Version, CommandEncoding.Version));
        host.Advance(TimeSpan.Zero);

        var sent = link.ClientEnd.ReceiveMessages();
        Assert.Equal(new JoinAccept(1, 2), sent[0]);
        Assert.Equal(Bytes(description), Bytes(Assert.IsType<MatchNotice>(sent[1]).Description));
    }

    [Fact]
    public void AHostWithoutAMatchDescribesNone()
    {
        var match = new TestMatch();
        match.Join();

        Assert.All(match.Clients, client => Assert.Null(client.Session.Description));
    }

    [Fact]
    public void TheDescriptionMustHaveAPlayerForEverySeat()
    {
        Assert.Throws<ArgumentException>(() => new HostSession(new HostOptions { PlayerCount = 3, Match = TestData.Description(players: 2) }));
        _ = new HostSession(new HostOptions { PlayerCount = 3, Match = TestData.Description(players: 3) });
    }

    [Fact]
    public void EveryClientBuildsTheMatchTheHostDescribed()
    {
        var description = new MatchDescription(TestData.GameVersion, TestData.Data.ContentHash, "ridge", 987654321, [new PlayerSetup(1), new PlayerSetup(0)]);
        var match = new RealMatch(description: description);
        var built = 0;
        match.Clients[0].Player.MatchBuilt += () => built++;

        match.Start();

        Assert.Equal(1, built);
        var reference = description.CreateMatch(TestData.Data);
        Assert.All(match.Clients, client =>
        {
            Assert.True(client.Player.IsReady);
            Assert.Null(client.Player.Refusal);
            Assert.Equal(Bytes(description), Bytes(client.Player.Description!));
            Assert.Equal(reference.World.ComputeHash(), client.Hash);
            Assert.Equal([new PlayerSetup(1), new PlayerSetup(0)], client.Match.Players);
        });
    }

    [Fact]
    public void AClientThatIsNotAskedToBeReadyAtOnceWaitsForThePlayer()
    {
        var link = new LoopbackNetwork().Connect();
        var host = new HostSession(new HostOptions { Match = TestData.Description() });
        host.Attach(1, link.HostEnd);
        var client = new MatchClient(link.ClientEnd, TestData.Data, TestData.GameVersion);
        Assert.Throws<InvalidOperationException>(client.Ready);

        host.Advance(TimeSpan.Zero);
        client.Advance(TimeSpan.Zero);
        host.Advance(TimeSpan.Zero);

        Assert.NotNull(client.Match);
        Assert.False(client.IsReady);
        Assert.False(host.IsReady(1));
        Assert.False(host.CanStart);

        client.Ready();
        client.Ready();
        host.Advance(TimeSpan.Zero);

        Assert.True(client.IsReady);
        Assert.True(host.IsReady(1));
        Assert.True(host.CanStart);
    }

    [Fact]
    public void AClientOfAnotherBuildRefusesAndSaysWhy()
    {
        var match = new RealMatch(versionOf: seat => seat == 1 ? "dev-0000001" : TestData.GameVersion);
        match.Until(() => match.HostDepartures.Count > 0, "the client refuses");

        var refused = match.Clients[1].Player;
        Assert.Equal(
            "The host runs version 'test-build' of the game and this is version 'dev-0000001'. Both players need the same build.",
            refused.Refusal);
        Assert.Null(refused.Match);
        Assert.Equal(new SessionEndInfo(SessionEndReason.SetupMismatch, -1), refused.Session.EndInfo);

        // The host knows why the seat is free again, and so does the other player.
        Assert.Equal([(1, SessionEndReason.SetupMismatch)], match.HostDepartures);
        Assert.False(match.Host.IsAttached(1));
        match.Until(() => match.Clients[0].Departures.Count > 0, "the other player hears of it");
        Assert.Equal([(1, SessionEndReason.SetupMismatch)], match.Clients[0].Departures);
        Assert.NotNull(match.Clients[0].Player.Match);
    }

    [Fact]
    public void AClientWithOtherDataRefusesAndSaysWhy()
    {
        var changed = TestData.ChangedData();
        Assert.NotEqual(TestData.Data.ContentHash, changed.ContentHash);
        var match = new RealMatch(dataOf: seat => seat == 0 ? changed : TestData.Data);
        match.Until(() => match.HostDepartures.Count > 0, "the client refuses");

        Assert.Equal([(0, SessionEndReason.SetupMismatch)], match.HostDepartures);
        Assert.StartsWith("The host's game data is not the same as this machine's", match.Clients[0].Player.Refusal);
        Assert.Contains(changed.ContentHash[..12], match.Clients[0].Player.Refusal);
    }

    [Fact]
    public void AMatchThisMachineCanPlayHasNoProblem()
    {
        var description = TestData.Description();

        Assert.Null(MatchClient.FindProblem(description, TestData.GameVersion, TestData.Data));
        Assert.Contains("version", MatchClient.FindProblem(description, "", TestData.Data));
        Assert.Contains(
            "no map 'atoll'",
            MatchClient.FindProblem(new MatchDescription(TestData.GameVersion, TestData.Data.ContentHash, "atoll", 1, [new PlayerSetup(0)]), TestData.GameVersion, TestData.Data));
    }

    [Fact]
    public void ADescriptionIsOnlyTakenOnceAndOnlyWhileJoining()
    {
        var link = new LoopbackNetwork().Connect();
        var client = new ClientSession(link.ClientEnd);
        SessionEndInfo? end = null;
        client.Stopped += info => end = info;

        // Before the join is accepted the client has no seat count to hold it against.
        link.HostEnd.SendMessage(new MatchNotice(TestData.Description()));
        client.Advance(TimeSpan.Zero);
        Assert.Equal(SessionEndReason.ProtocolError, end?.Reason);

        link = new LoopbackNetwork().Connect();
        client = new ClientSession(link.ClientEnd);
        client.Stopped += info => end = info;
        end = null;
        link.HostEnd.SendMessage(new JoinAccept(0, 2));
        link.HostEnd.SendMessage(new MatchNotice(TestData.Description()));
        client.Advance(TimeSpan.Zero);
        Assert.Null(end);
        Assert.NotNull(client.Description);

        link.HostEnd.SendMessage(new MatchNotice(TestData.Description()));
        client.Advance(TimeSpan.Zero);
        Assert.Equal(SessionEndReason.ProtocolError, end?.Reason);
    }

    [Fact]
    public void ADescriptionForAnotherNumberOfSeatsIsAProtocolError()
    {
        var link = new LoopbackNetwork().Connect();
        var client = new ClientSession(link.ClientEnd);

        link.HostEnd.SendMessage(new JoinAccept(0, 2));
        link.HostEnd.SendMessage(new MatchNotice(TestData.Description(players: 3)));
        client.Advance(TimeSpan.Zero);

        Assert.Equal(SessionEndReason.ProtocolError, client.EndInfo?.Reason);
    }

    [Theory]
    [InlineData(SessionEndReason.Left)]
    [InlineData(SessionEndReason.SetupMismatch)]
    public void AClientSaysWhyItLeaves(SessionEndReason reason)
    {
        var link = new LoopbackNetwork().Connect();
        var client = new ClientSession(link.ClientEnd);
        link.HostEnd.ReceiveMessages();

        client.Leave(reason);

        Assert.Equal([new LeaveNotice(reason)], link.HostEnd.ReceiveMessages());
        Assert.False(link.HostEnd.IsOpen);
        Assert.Equal(new SessionEndInfo(reason, -1), client.EndInfo);
    }

    [Theory]
    [InlineData(SessionEndReason.None)]
    [InlineData(SessionEndReason.HostEnded)]
    [InlineData(SessionEndReason.Desync)]
    [InlineData(SessionEndReason.ConnectionLost)]
    [InlineData(SessionEndReason.FellBehind)]
    [InlineData(SessionEndReason.ProtocolError)]
    [InlineData(SessionEndReason.VersionMismatch)]
    public void AClientCanOnlyLeaveForAReasonOfItsOwn(SessionEndReason reason)
    {
        var match = new TestMatch();
        match.Join();

        Assert.Throws<ArgumentException>(() => match.Clients[1].Session.Leave(reason));
        Assert.Equal(SessionPhase.Joining, match.Clients[1].Session.Phase);

        // A client that says it anyway is dropped for breaking the protocol.
        match.Clients[1].Link.ClientEnd.SendMessage(new LeaveNotice(reason));
        match.Step();
        Assert.Equal([(1, SessionEndReason.ProtocolError)], match.HostDepartures);
    }

    [Fact]
    public void ALeaveBeforeTheJoinIsAProtocolError()
    {
        var link = new LoopbackNetwork().Connect();
        var host = new HostSession();
        var departures = new List<(int, SessionEndReason)>();
        host.PlayerLeft += (player, reason) => departures.Add((player, reason));
        host.Attach(0, link.HostEnd);

        link.ClientEnd.SendMessage(new LeaveNotice(SessionEndReason.Left));
        host.Advance(TimeSpan.Zero);

        Assert.Equal([(0, SessionEndReason.ProtocolError)], departures);
    }

    private static byte[] Bytes(MatchDescription description)
    {
        var writer = new ByteWriter();
        description.Write(writer);
        return writer.ToArray();
    }
}
