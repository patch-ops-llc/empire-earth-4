using System.Collections.Immutable;
using Sim.Core;
using Sim.Serialization;

namespace Net.Tests;

/// <summary>
/// What each side does when the other does not follow the protocol. In these tests the test itself
/// is one end of a connection and sends whatever it likes.
/// </summary>
public class ProtocolTests
{
    private static readonly JoinRequest GoodJoin = new(ProtocolInfo.Version, CommandEncoding.Version);

    [Theory]
    [InlineData(ProtocolInfo.Version + 1, CommandEncoding.Version)]
    [InlineData(0, CommandEncoding.Version)]
    [InlineData(ProtocolInfo.Version, CommandEncoding.Version + 1)]
    public void AClientOfAnotherVersionIsTurnedAway(int protocolVersion, int commandEncodingVersion)
    {
        var match = new TestMatch(clients: 1);
        var link = match.Network.Connect();
        match.Host.Attach(1, link.HostEnd);

        link.ClientEnd.SendMessage(new JoinRequest(protocolVersion, commandEncodingVersion));
        match.Step();

        Assert.Equal([new SessionEnd(SessionEndReason.VersionMismatch, -1, -1)], link.ClientEnd.ReceiveMessages().Where(message => message is not Ping));
        Assert.False(link.ClientEnd.IsOpen);
        Assert.False(match.Host.IsAttached(1));
        Assert.Equal([(1, SessionEndReason.VersionMismatch)], match.HostDepartures);
    }

    /// <summary>
    /// A later version may add to the join request. The host must still read the version and
    /// answer with the reason, not fail on the bytes it does not know.
    /// </summary>
    [Fact]
    public void AJoinRequestOfAnotherVersionMayBeLaidOutDifferentlyAfterTheVersion()
    {
        var match = new TestMatch(clients: 1);
        var link = match.Network.Connect();
        match.Host.Attach(1, link.HostEnd);
        var writer = new ByteWriter();
        writer.WriteBytes(MessageCodec.Encode(new JoinRequest(ProtocolInfo.Version + 1, CommandEncoding.Version)).AsSpan(0, 5));
        writer.WriteString("something a later version sends");

        link.ClientEnd.Send(writer.WrittenSpan);
        match.Step();

        Assert.Equal([(1, SessionEndReason.VersionMismatch)], match.HostDepartures);
    }

    [Fact]
    public void AClientSessionOfThisVersionStatesBothVersions()
    {
        var link = new LoopbackNetwork().Connect();
        _ = new ClientSession(link.ClientEnd);

        Assert.Equal([new JoinRequest(ProtocolInfo.Version, CommandEncoding.Version)], link.HostEnd.ReceiveMessages());
        Assert.Equal(3, ProtocolInfo.Version);
    }

    [Fact]
    public void ACommandInAnotherPlayersNameIsRefused()
    {
        var (match, intruder) = MatchWithARawClient();

        intruder.SendMessage(new CommandRequest(new Command(ToyGame.Spawn, 0, contentId: 1)));
        intruder.SendMessage(new CommandRequest(new Command(ToyGame.Spawn, 1, contentId: 2)));
        match.Run(seconds: 0.5);

        Assert.Equal(1, match.Host.RejectedCommandCount);
        var ran = Assert.Single(match.Clients[0].Game.Log);
        Assert.Equal((1, 2), (ran.Player, ran.ContentId));
        Assert.True(match.Host.IsAttached(1));
    }

    public static TheoryData<string> Violations() =>
    [
        "command before the start", "ready before joining", "ready twice", "join twice", "a host's message",
        "hash for the wrong tick", "hash twice", "hash for a tick not issued", "bytes that are no message",
        "a message with bytes left over", "a message cut short", "a command that is too large", "a message that is too large",
        "a pause", "a speed",
    ];

    [Theory]
    [MemberData(nameof(Violations))]
    public void AClientThatBreaksTheProtocolIsDroppedAndTheOthersPlayOn(string violation)
    {
        var beforeTheStart = violation is "command before the start" or "ready before joining" or "ready twice" or "join twice";
        var match = new TestMatch(clients: 1, options: new HostOptions { CommandDelay = 2, HashInterval = 5 });
        var link = match.Network.Connect();
        var raw = link.ClientEnd;
        match.Host.Attach(1, link.HostEnd);
        if (violation != "ready before joining")
        {
            raw.SendMessage(GoodJoin);
            match.Step();
            if (violation != "command before the start")
            {
                raw.SendMessage(new ReadyNotice());
            }
        }

        if (!beforeTheStart)
        {
            match.Start();
            match.Run(seconds: 0.5);
            raw.SendMessage(new HashReport(0, new ToyGame().World.ComputeHash()));
            match.Step();
            Assert.Empty(match.HostDepartures);
        }

        switch (violation)
        {
            case "command before the start":
                raw.SendMessage(new CommandRequest(new Command(ToyGame.Spawn, 1)));
                break;
            case "ready before joining":
            case "ready twice":
                raw.SendMessage(new ReadyNotice());
                break;
            case "join twice":
                raw.SendMessage(GoodJoin);
                break;
            case "a host's message":
                raw.SendMessage(new TickBundle(0, []));
                break;
            case "hash for the wrong tick":
                raw.SendMessage(new HashReport(6, 0));
                break;
            case "hash twice":
                raw.SendMessage(new HashReport(0, new ToyGame().World.ComputeHash()));
                break;
            case "hash for a tick not issued":
                // A client that has run up to a tick has that tick's bundles, so the host has issued them.
                Assert.InRange(match.Host.NextTick, 10, 14);
                raw.SendMessage(new HashReport(5, ToyGame.Replay([], 5).World.ComputeHash()));
                raw.SendMessage(new HashReport(10, ToyGame.Replay([], 10).World.ComputeHash()));
                raw.SendMessage(new HashReport(15, 0));
                break;
            case "bytes that are no message":
                raw.Send([200, 1, 2, 3]);
                break;
            case "a message with bytes left over":
                raw.Send([.. MessageCodec.Encode(new Pong(1)), 0]);
                break;
            case "a message cut short":
                raw.Send(MessageCodec.Encode(new Pong(1)).AsSpan(0, 3));
                break;
            case "a command that is too large":
                raw.SendMessage(new CommandRequest(new Command(ToyGame.Spawn, 1, new EntityId[(ProtocolInfo.MaxCommandSize / 4) + 1])));
                break;
            case "a message that is too large":
                raw.Send(new byte[ProtocolInfo.MaxMessageSize + 1]);
                break;
            case "a pause":
                // The pace of the match is the host's alone.
                raw.SendMessage(new PauseNotice(true));
                break;
            case "a speed":
                raw.SendMessage(new SpeedNotice(TimeSpan.FromMilliseconds(1)));
                break;
            default:
                Assert.Fail(violation);
                break;
        }

        match.Step();

        Assert.Equal([(1, SessionEndReason.ProtocolError)], match.HostDepartures);
        Assert.False(match.Host.IsAttached(1));
        var received = raw.ReceiveMessages();
        Assert.Equal(new SessionEnd(SessionEndReason.ProtocolError, -1, -1), received[^1]);
        Assert.False(raw.IsOpen);

        // The player who did nothing wrong is told and carries on.
        match.Run(seconds: 0.5);
        var other = match.Clients[0];
        Assert.Equal([(1, SessionEndReason.ProtocolError)], other.Departures);
        Assert.NotEqual(SessionPhase.Stopped, match.Host.Phase);
        Assert.NotEqual(SessionPhase.Stopped, other.Session.Phase);
        if (!beforeTheStart)
        {
            Assert.InRange(other.Tick, 15, 25);
        }
    }

    public static TheoryData<string> HostFaults() =>
    [
        "bundle out of order", "bundle before the start", "start before ready", "start at another tick", "start with no delay",
        "start with too long a delay", "start with no hash interval", "start with no tick interval", "accept twice",
        "a client's message", "end at a tick not reached", "end without a reason", "bytes that are no message",
        "pause before the start", "speed before the start", "a speed of no time", "a speed below no time", "a pause that is neither",
    ];

    [Theory]
    [MemberData(nameof(HostFaults))]
    public void AClientStopsWhenTheHostBreaksTheProtocol(string fault)
    {
        var link = new LoopbackNetwork().Connect();
        var host = link.HostEnd;
        var game = new ToyGame();
        var client = new ClientSession(link.ClientEnd);
        var goodStart = new MatchStart(0, 2, 1, TestMatch.TickInterval);
        var afterTheStart = fault is "bundle out of order" or "a client's message" or "end at a tick not reached" or "bytes that are no message"
            or "a speed of no time" or "a speed below no time" or "a pause that is neither";

        if (fault != "start before ready")
        {
            client.ReportReady(game.Simulation);
        }

        if (fault != "bundle before the start")
        {
            host.SendMessage(new JoinAccept(0, 2));
        }

        if (afterTheStart)
        {
            host.SendMessage(goodStart);
            host.SendMessage(new TickBundle(0, []));
        }

        client.Advance(TimeSpan.Zero);
        Assert.NotEqual(SessionPhase.Stopped, client.Phase);

        switch (fault)
        {
            case "bundle out of order":
                host.SendMessage(new TickBundle(2, []));
                break;
            case "bundle before the start":
                host.SendMessage(new TickBundle(0, []));
                break;
            case "start before ready":
                host.SendMessage(goodStart);
                break;
            case "start at another tick":
                host.SendMessage(goodStart with { FirstTick = 1 });
                break;
            case "start with no delay":
                host.SendMessage(goodStart with { CommandDelay = 0 });
                break;
            case "start with too long a delay":
                host.SendMessage(goodStart with { CommandDelay = ProtocolInfo.MaxCommandDelay + 1 });
                break;
            case "start with no hash interval":
                host.SendMessage(goodStart with { HashInterval = 0 });
                break;
            case "start with no tick interval":
                host.SendMessage(goodStart with { TickInterval = TimeSpan.Zero });
                break;
            case "accept twice":
                host.SendMessage(new JoinAccept(0, 2));
                break;
            case "a client's message":
                host.SendMessage(new HashReport(0, 0));
                break;
            case "end at a tick not reached":
                host.SendMessage(new SessionEnd(SessionEndReason.HostEnded, 5, -1));
                break;
            case "end without a reason":
                host.SendMessage(new SessionEnd(SessionEndReason.None, -1, -1));
                break;
            case "bytes that are no message":
                host.Send([0]);
                break;
            case "pause before the start":
                host.SendMessage(new PauseNotice(true));
                break;
            case "speed before the start":
                host.SendMessage(new SpeedNotice(TestMatch.TickInterval));
                break;
            case "a speed of no time":
                host.SendMessage(new SpeedNotice(TimeSpan.Zero));
                break;
            case "a speed below no time":
                host.SendMessage(new SpeedNotice(TimeSpan.FromTicks(-1)));
                break;
            case "a pause that is neither":
                host.Send([14, 2]);
                break;
            default:
                Assert.Fail(fault);
                break;
        }

        var ran = client.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(new SessionEndInfo(SessionEndReason.ProtocolError, -1), client.EndInfo);
        Assert.Equal(SessionPhase.Stopped, client.Phase);
        Assert.Equal(0, ran);
        Assert.Equal(0, game.World.Tick);
        host.ReceiveMessages();
        Assert.False(host.IsOpen);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(2, 2)]
    [InlineData(0, 0)]
    public void AClientRejectsASeatThatDoesNotExist(int player, int playerCount)
    {
        var link = new LoopbackNetwork().Connect();
        var client = new ClientSession(link.ClientEnd);

        link.HostEnd.SendMessage(new JoinAccept(player, playerCount));
        client.Advance(TimeSpan.Zero);

        Assert.Equal(SessionEndReason.ProtocolError, client.EndInfo!.Reason);
        Assert.Equal(-1, client.Player);
    }

    /// <summary>
    /// The encoding of a command sets no limit on its entities, so the protocol does: a client
    /// cannot submit a command larger than the limit, and neither can the host for its own seats.
    /// </summary>
    [Fact]
    public void ACommandLargerThanTheLimitCannotBeSubmitted()
    {
        var match = new TestMatch(clients: 1);
        match.Start();
        match.Step();
        var most = (ProtocolInfo.MaxCommandSize - CommandEncoding.MinimumSize) / 4;
        var client = match.Clients[0];

        Assert.True(client.Session.Submit(new Command(ToyGame.Spawn, 0, new EntityId[most])));
        Assert.True(match.Host.Submit(new Command(ToyGame.Spawn, 1, new EntityId[most])));
        Assert.Throws<ArgumentException>(() => client.Session.Submit(new Command(ToyGame.Spawn, 0, new EntityId[most + 1])));
        Assert.Throws<ArgumentException>(() => match.Host.Submit(new Command(ToyGame.Spawn, 1, new EntityId[most + 1])));

        match.Run(seconds: 0.5);
        Assert.Equal(2, client.Game.Log.Count);
        Assert.Equal(most, client.Bundles.SelectMany(bundle => bundle.Commands.AsEnumerable()).First().Entities.Length);
    }

    /// <summary>
    /// A bundle is one message and a message has a size limit. Commands that do not fit run a
    /// tick later, in the order they came, and none is lost.
    /// </summary>
    [Fact]
    public void CommandsThatDoNotFitInOneBundleRunInTheNextTicks()
    {
        var match = new TestMatch(clients: 2);
        match.Start();
        match.Step();
        var entities = new EntityId[1000];
        const int Commands = 40;
        var size = CommandEncoding.SizeOf(new Command(ToyGame.Spawn, 0, entities));
        Assert.True(size * Commands > 2 * ProtocolInfo.MaxMessageSize);

        for (var i = 0; i < Commands; i++)
        {
            Assert.True(match.Clients[0].Session.Submit(new Command(ToyGame.Spawn, 0, entities, contentId: i)));
        }

        match.Run(seconds: 1);

        var perBundle = (ProtocolInfo.MaxMessageSize - MessageCodec.BundleOverhead) / size;
        var filled = match.Clients[1].Bundles.Where(bundle => bundle.Commands.Length > 0).ToList();
        Assert.Equal([perBundle, perBundle, Commands - (2 * perBundle)], filled.Select(bundle => bundle.Commands.Length));
        Assert.Equal([filled[0].Tick, filled[0].Tick + 1, filled[0].Tick + 2], filled.Select(bundle => bundle.Tick));
        Assert.All(filled, bundle => Assert.True(MessageCodec.Encode(bundle).Length <= ProtocolInfo.MaxMessageSize));
        Assert.Equal(Enumerable.Range(0, Commands), match.Clients[1].Game.Log.Select(entry => entry.ContentId));
        Assert.Equal(match.Clients[0].Game.Log, match.Clients[1].Game.Log);
    }

    /// <summary>A match with one real client in seat 0 and the test itself as the client in seat 1.</summary>
    private static (TestMatch Match, IConnection RawClient) MatchWithARawClient()
    {
        var match = new TestMatch(clients: 1);
        var link = match.Network.Connect();
        match.Host.Attach(1, link.HostEnd);
        link.ClientEnd.SendMessage(GoodJoin);
        match.Step();
        link.ClientEnd.SendMessage(new ReadyNotice());
        match.Start();
        match.Step();
        Assert.Contains(new JoinAccept(1, 2), link.ClientEnd.ReceiveMessages());
        return (match, link.ClientEnd);
    }
}
