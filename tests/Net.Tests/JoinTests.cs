using Sim.Core;

namespace Net.Tests;

/// <summary>Before the match: seats, joining, readiness and the start.</summary>
public class JoinTests
{
    [Fact]
    public void EachClientLearnsItsSeatAndPlayersAreNumberedFromZero()
    {
        var match = new TestMatch(clients: 3, options: new HostOptions { PlayerCount = 4, CommandDelay = 2 });
        var joined = new List<int>();
        match.Host.PlayerJoined += joined.Add;

        Assert.All(match.Clients, client => Assert.Equal(-1, client.Session.Player));
        match.Join();

        Assert.Equal([0, 1, 2], match.Clients.Select(client => client.Session.Player));
        Assert.All(match.Clients, client => Assert.Equal(4, client.Session.PlayerCount));
        Assert.Equal([0, 1, 2], joined.Order());
        Assert.True(match.Host.IsAttached(2));
        Assert.False(match.Host.IsAttached(3));
        Assert.False(match.Host.IsReady(3));
    }

    [Fact]
    public void TheMatchCannotStartUntilEveryClientIsReady()
    {
        var network = new LoopbackNetwork();
        var host = new HostSession();
        var link = network.Connect();
        host.Attach(0, link.HostEnd);
        var client = new ClientSession(link.ClientEnd);

        host.Advance(TimeSpan.Zero);
        client.Advance(TimeSpan.Zero);
        host.Advance(TimeSpan.Zero);

        Assert.Equal(0, client.Player);
        Assert.False(host.CanStart);
        Assert.Throws<InvalidOperationException>(host.Start);

        // The world can be built after the host's answer, which is when a real client learns what to build.
        client.ReportReady(new ToyGame().Simulation);
        host.Advance(TimeSpan.Zero);

        Assert.True(host.IsReady(0));
        Assert.True(host.CanStart);
        host.Start();
        client.Advance(TimeSpan.Zero);
        Assert.Equal(SessionPhase.Running, host.Phase);
        Assert.Equal(SessionPhase.Running, client.Phase);
    }

    [Fact]
    public void TheStartTellsEveryClientTheSettingsOfTheMatch()
    {
        var options = new HostOptions { CommandDelay = 4, HashInterval = 7, TickInterval = TimeSpan.FromMilliseconds(25) };
        var match = new TestMatch(options: options);
        var started = 0;
        match.Clients[1].Session.Started += () => started++;

        match.Start();
        match.Step();

        Assert.Equal(1, started);
        Assert.All(match.Clients, client =>
        {
            Assert.Equal(SessionPhase.Running, client.Session.Phase);
            Assert.Equal(4, client.Session.CommandDelay);
            Assert.Equal(7, client.Session.HashInterval);
            Assert.Equal(TimeSpan.FromMilliseconds(25), client.Session.TickInterval);
        });
    }

    [Fact]
    public void ASeatTakesOneConnectionAndOnlyBeforeTheStart()
    {
        var network = new LoopbackNetwork();
        var host = new HostSession(new HostOptions { PlayerCount = 2 });
        host.Attach(0, network.Connect().HostEnd);

        Assert.Throws<InvalidOperationException>(() => host.Attach(0, network.Connect().HostEnd));
        Assert.Throws<ArgumentOutOfRangeException>(() => host.Attach(2, network.Connect().HostEnd));
        Assert.Throws<ArgumentOutOfRangeException>(() => host.Attach(-1, network.Connect().HostEnd));

        var match = new TestMatch(clients: 1);
        match.Start();
        Assert.Throws<InvalidOperationException>(() => match.Host.Attach(1, match.Network.Connect().HostEnd));
        Assert.Throws<InvalidOperationException>(match.Host.Start);
    }

    [Fact]
    public void AClientThatLeavesBeforeTheStartFreesItsSeat()
    {
        var match = new TestMatch(clients: 2);
        match.Join();

        match.Clients[1].Session.Leave();
        match.Step();

        Assert.Equal(SessionEndReason.Left, match.Clients[1].Session.EndInfo!.Reason);
        Assert.False(match.Host.IsAttached(1));
        Assert.Equal([(1, SessionEndReason.ConnectionLost)], match.HostDepartures);
        Assert.Equal([(1, SessionEndReason.ConnectionLost)], match.Clients[0].Departures);

        // The seat can be given to someone else, and the match starts without it otherwise.
        var link = match.Network.Connect();
        match.Host.Attach(1, link.HostEnd);
        var replacement = new ClientSession(link.ClientEnd);
        replacement.ReportReady(new ToyGame().Simulation);
        for (var i = 0; i < 4; i++)
        {
            match.Step();
            replacement.Advance(TestMatch.Frame);
        }

        Assert.Equal(1, replacement.Player);
        Assert.True(match.Host.CanStart);
    }

    [Fact]
    public void TheHostCanEndTheSessionBeforeTheMatch()
    {
        var match = new TestMatch(clients: 2);
        match.Join();
        SessionEndInfo? hostEnd = null;
        match.Host.Stopped += info => hostEnd = info;

        match.Host.EndSession();
        match.Step();

        Assert.Equal(new SessionEndInfo(SessionEndReason.HostEnded, -1), hostEnd);
        Assert.Equal(SessionPhase.Stopped, match.Host.Phase);
        Assert.All(match.Clients, client =>
        {
            Assert.Equal(new SessionEndInfo(SessionEndReason.HostEnded, -1), client.Session.EndInfo);
            Assert.Equal(0, client.Tick);
        });
    }

    [Fact]
    public void NothingCanBeSubmittedBeforeTheStart()
    {
        var match = new TestMatch(clients: 1);
        match.Join();

        Assert.False(match.Clients[0].Spawn(1));
        Assert.False(match.Host.Submit(new Command(ToyGame.Spawn, 1)));

        match.Start();
        match.Step();

        Assert.True(match.Clients[0].Spawn(1));
        Assert.True(match.Host.Submit(new Command(ToyGame.Spawn, 1)));
    }

    [Fact]
    public void OptionsOutOfRangeAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostSession(new HostOptions { PlayerCount = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostSession(new HostOptions { FirstTick = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostSession(new HostOptions { CommandDelay = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostSession(new HostOptions { CommandDelay = ProtocolInfo.MaxCommandDelay + 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostSession(new HostOptions { HashInterval = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostSession(new HostOptions { TickInterval = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostSession(new HostOptions { HashInterval = 20, MaxLagTicks = 39 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostSession(new HostOptions { PingInterval = TimeSpan.Zero }));

        var connection = new LoopbackNetwork().Connect().ClientEnd;
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientSession(connection, new ClientOptions { MaxTicksPerAdvance = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientSession(connection, new ClientOptions { CatchUpSlack = -1 }));
    }
}
