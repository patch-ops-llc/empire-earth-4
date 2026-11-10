using Sim;
using Sim.Core;

namespace Net.Tests;

/// <summary>
/// The property the session exists for: every peer runs the same commands in the same ticks in the
/// same order, and so reaches the same state, whatever the network does to the timing.
/// </summary>
public class LockstepTests
{
    private static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// The technical plan's network test: a host and clients in one process over the loopback with
    /// latency and jitter, comparing hashes every tick. Each client gives commands on its own
    /// schedule throughout.
    /// </summary>
    [Theory]
    [InlineData(2, 0, 0, 1)]
    [InlineData(2, 1, 0, 2)]
    [InlineData(3, 40, 30, 3)]
    [InlineData(3, 120, 80, 5)]
    [InlineData(4, 15, 100, 2)]
    public void EveryPeerEndsInTheSameStateUnderLatencyAndJitter(int clients, int latency, int jitter, int commandDelay)
    {
        var options = new HostOptions { PlayerCount = clients, CommandDelay = commandDelay, HashInterval = 1 };
        var match = new TestMatch(clients, options, new LinkConditions(latency * Millisecond, jitter * Millisecond), networkSeed: 42);
        match.Start();

        for (var frame = 0; frame < 900; frame++)
        {
            foreach (var client in match.Clients)
            {
                if (frame % (7 + (3 * client.Seat)) == 0)
                {
                    client.Spawn(frame);
                }

                if (frame % (31 + client.Seat) == 5)
                {
                    client.Cull();
                }
            }

            match.Step();
        }

        Assert.Equal(SessionPhase.Running, match.Host.Phase);
        var finalTick = match.Finish();

        Assert.Equal(SessionEndReason.HostEnded, match.Host.EndInfo!.Reason);
        Assert.InRange(finalTick, 280, 320);
        var first = match.Clients[0];
        Assert.True(first.Game.Log.Count > 100, "The commands did not run.");
        Assert.All(match.Clients, client =>
        {
            Assert.Equal(new SessionEndInfo(SessionEndReason.HostEnded, finalTick), client.Session.EndInfo);
            Assert.Equal(finalTick, client.Tick);
            Assert.Equal(first.Game.World.ComputeHash(), client.Game.World.ComputeHash());
            Assert.Equal(first.Game.Log, client.Game.Log);
            Assert.Equal(finalTick, client.Bundles.Count);
        });

        // Every player's commands ran.
        Assert.Equal(Enumerable.Range(0, clients), first.Game.Log.Select(entry => entry.Player).Distinct().Order());
    }

    /// <summary>
    /// The bundles are the whole match: run them through a plain simulation with no session and
    /// the state is the same. This is what makes a replay file possible.
    /// </summary>
    [Fact]
    public void TheBundlesAloneReproduceTheMatch()
    {
        var match = new TestMatch(clients: 2, conditions: new LinkConditions(30 * Millisecond, 20 * Millisecond));
        match.Start();
        for (var frame = 0; frame < 400; frame++)
        {
            match.Clients[frame % 2].Spawn(frame);
            if (frame % 9 == 0)
            {
                match.Clients[1].Cull();
            }

            match.Step();
        }

        var finalTick = match.Finish();
        var client = match.Clients[1];
        var replayed = ToyGame.Replay(client.Bundles, finalTick);

        Assert.Equal(Enumerable.Range(0, finalTick), client.Bundles.Select(bundle => bundle.Tick));
        Assert.Equal(client.Game.World.ComputeHash(), replayed.World.ComputeHash());
        Assert.Equal(client.Game.Log, replayed.Log);
        Assert.NotEqual(ToyGame.Replay([], finalTick).World.ComputeHash(), replayed.World.ComputeHash());
    }

    /// <summary>
    /// A command runs a fixed number of ticks after the host receives it (technical plan, section
    /// 2): with a delay of d, the host has already issued the d - 1 ticks after the one that is
    /// due, so the command goes into the tick after those.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(20)]
    public void ACommandRunsTheCommandDelayAfterTheHostReceivesIt(int commandDelay)
    {
        var match = new TestMatch(clients: 2, options: new HostOptions { CommandDelay = commandDelay, HashInterval = 1 });
        match.Start();
        match.Run(seconds: 1);

        var client = match.Clients[0];
        Assert.True(client.Spawn(contentId: 77));

        // The host takes the command in at its next update, before it issues anything in it.
        var dueTick = match.Host.NextTick - (commandDelay - 1);
        match.Run(seconds: 2);

        var ran = Assert.Single(client.Game.Log);
        Assert.Equal(dueTick + commandDelay - 1, ran.Tick);
        Assert.Equal(new ToyGame.Executed(ran.Tick, 0, ToyGame.Spawn, 77), ran);
        Assert.Equal(client.Game.Log, match.Clients[1].Game.Log);
    }

    [Fact]
    public void TheHostIssuesTheHeadStartAtTheStartAndThenOneBundlePerTickInterval()
    {
        var match = new TestMatch(clients: 1, options: new HostOptions { CommandDelay = 4, HashInterval = 1 });
        match.Start();

        Assert.Equal(4, match.Host.CommandDelay);
        Assert.Equal(3, match.Host.NextTick);

        match.Host.Advance(TimeSpan.FromMilliseconds(49));
        Assert.Equal(3, match.Host.NextTick);
        match.Host.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(4, match.Host.NextTick);

        // An update that took longer issues every tick that came due, none skipped and none twice.
        match.Host.Advance(TimeSpan.FromMilliseconds(420));
        Assert.Equal(12, match.Host.NextTick);
        match.Host.Advance(TimeSpan.FromMilliseconds(30));
        Assert.Equal(13, match.Host.NextTick);

        match.Step(TimeSpan.Zero);
        Assert.Equal(Enumerable.Range(0, 13), match.Clients[0].Bundles.Select(bundle => bundle.Tick));
    }

    /// <summary>
    /// The host is the clock of the match. When its machine is held up the match is held up with
    /// it: it issues a limited number of ticks for the time it missed and lets the rest go, so
    /// that nobody is dropped for being behind a host that was itself away.
    /// </summary>
    [Fact]
    public void AHostThatWasHeldUpDoesNotMakeUpTheTime()
    {
        var options = new HostOptions { CommandDelay = 2, HashInterval = 1, MaxLagTicks = 40 };
        var match = new TestMatch(clients: 2, options: options);
        match.Start();
        match.Run(seconds: 1);
        var before = match.Host.NextTick;

        match.Host.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(before + 10, match.Host.NextTick);
        match.Run(seconds: 1);

        Assert.Empty(match.HostDepartures);
        Assert.InRange(match.Host.NextTick, before + 30, before + 31);
        Assert.Equal(10, new HostOptions().MaxTicksPerAdvance);
        match.Finish();
        Assert.Equal(match.Clients[0].Game.World.ComputeHash(), match.Clients[1].Game.World.ComputeHash());
    }

    /// <summary>
    /// The order of commands inside a bundle is the order the host took them in, the same for every
    /// peer, and a player's own commands stay in the order that player gave them.
    /// </summary>
    [Fact]
    public void CommandsInOneTickRunInTheSameOrderOnEveryPeer()
    {
        var match = new TestMatch(clients: 3, options: new HostOptions { PlayerCount = 3, CommandDelay = 2, HashInterval = 1 });
        match.Start();
        match.Step();
        var next = 0;

        for (var frame = 0; frame < 300; frame++)
        {
            foreach (var client in match.Clients)
            {
                Assert.True(client.Spawn(next++));
                Assert.True(client.Spawn(next++));
            }

            match.Step();
        }

        // Commands the host has not put in a bundle when the match ends are never run.
        match.Run(seconds: 0.2);
        match.Finish();
        var reference = match.Clients[0].Game.Log;
        Assert.Equal(1800, reference.Count);
        Assert.All(match.Clients, client => Assert.Equal(reference, client.Game.Log));
        for (var player = 0; player < 3; player++)
        {
            var own = reference.Where(entry => entry.Player == player).Select(entry => entry.ContentId).ToList();
            Assert.Equal(own.Order(), own);
        }

        // No seat always goes first when several send in the same update.
        var firstInBundle = match.Clients[0].Bundles
            .Where(bundle => bundle.Commands.Length > 0)
            .Select(bundle => bundle.Commands[0].Player)
            .ToList();
        Assert.Equal([0, 1, 2], firstInBundle.Distinct().Order());
    }

    /// <summary>
    /// A seat without a connection is run by the host. This is how an AI player gives its orders:
    /// they join the bundles like anyone's and reach every peer.
    /// </summary>
    [Fact]
    public void TheHostGivesCommandsForSeatsItRunsItself()
    {
        var match = new TestMatch(clients: 2, options: new HostOptions { PlayerCount = 3, CommandDelay = 2, HashInterval = 1 });
        match.Start();
        match.Run(seconds: 0.5);

        Assert.True(match.Host.Submit(new Command(ToyGame.Spawn, 2, contentId: 9)));
        Assert.Throws<ArgumentException>(() => match.Host.Submit(new Command(ToyGame.Spawn, 0)));
        Assert.Throws<ArgumentException>(() => match.Host.Submit(new Command(ToyGame.Spawn, 3)));
        Assert.Throws<ArgumentException>(() => match.Host.Submit(new Command(ToyGame.Spawn, -1)));
        match.Run(seconds: 0.5);
        match.Finish();

        Assert.All(match.Clients, client =>
        {
            var ran = Assert.Single(client.Game.Log);
            Assert.Equal((2, 9), (ran.Player, ran.ContentId));
        });
        Assert.Equal(match.Clients[0].Game.World.ComputeHash(), match.Clients[1].Game.World.ComputeHash());
    }

    [Fact]
    public void AClientCanOnlySubmitItsOwnCommands()
    {
        var match = new TestMatch(clients: 2);
        match.Start();
        match.Step();

        Assert.Throws<ArgumentException>(() => match.Clients[0].Session.Submit(new Command(ToyGame.Spawn, 1)));
    }

    /// <summary>The state hash is reported before every tick the interval divides, starting with the first.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void HashesAreReportedAtTheHashInterval(int hashInterval)
    {
        var match = new TestMatch(clients: 2, options: new HostOptions { CommandDelay = 2, HashInterval = hashInterval, MaxLagTicks = 200 });
        match.Start();
        match.Run(seconds: 3);
        match.Step(TimeSpan.Zero);

        var client = match.Clients[1];
        var reports = client.Sent.OfType<HashReport>().ToList();
        Assert.InRange(client.Tick, 55, 65);
        Assert.Equal(Enumerable.Range(0, reports.Count).Select(index => index * hashInterval), reports.Select(report => report.Tick));
        Assert.Equal(client.Tick / hashInterval * hashInterval, reports[^1].Tick);
        Assert.Equal(reports[^1].Tick, match.Host.GetReportedTick(1));
        Assert.Equal(SessionPhase.Running, match.Host.Phase);

        // Each is the hash of the world as it was before that tick ran.
        var replayed = new ToyGame();
        foreach (var report in reports)
        {
            replayed.Simulation.Run(report.Tick - replayed.World.Tick);
            Assert.Equal(replayed.World.ComputeHash(), report.Hash);
        }
    }

    [Fact]
    public void AMatchCanStartAtALaterTick()
    {
        var games = new[] { new ToyGame(), new ToyGame() };
        foreach (var game in games)
        {
            game.Simulation.Run(33);
        }

        var network = new LoopbackNetwork();
        var host = new HostSession(new HostOptions { FirstTick = 33, CommandDelay = 2, HashInterval = 20 });
        var clients = new List<ClientSession>();
        var bundles = new List<int>();
        foreach (var game in games)
        {
            var link = network.Connect();
            host.Attach(clients.Count, link.HostEnd);
            clients.Add(new ClientSession(link.ClientEnd));
            clients[^1].ReportReady(game.Simulation);
        }

        clients[0].CommandsScheduled += (tick, _) => bundles.Add(tick);
        void Step()
        {
            host.Advance(TestMatch.Frame);
            clients.ForEach(client => client.Advance(TestMatch.Frame));
        }

        Step();
        Step();
        host.Start();
        for (var i = 0; i < 120; i++)
        {
            Step();
        }

        Assert.Equal(SessionPhase.Running, host.Phase);
        Assert.Equal(33, bundles[0]);
        Assert.InRange(games[0].World.Tick, 33 + 35, 33 + 41);
        Assert.Equal(60, host.GetReportedTick(0));
        Assert.Equal(games[0].World.Tick, games[1].World.Tick);
        Assert.Equal(games[0].World.ComputeHash(), games[1].World.ComputeHash());
    }

    /// <summary>The whole match, twice, with the same seeds: the same bundles and the same hash.</summary>
    [Fact]
    public void TheSameSeedsGiveTheSameMatch()
    {
        static (List<string> Bundles, ulong Hash) Play(ulong networkSeed)
        {
            var match = new TestMatch(clients: 2, conditions: new LinkConditions(20 * Millisecond, 60 * Millisecond), networkSeed: networkSeed);
            match.Start();
            for (var frame = 0; frame < 300; frame++)
            {
                match.Clients[frame % 2].Spawn(frame);
                match.Step();
            }

            match.Finish();
            var bundles = match.Clients[0].Bundles
                .Select(bundle => bundle.Tick + ":" + string.Join(",", bundle.Commands.Select(command => command.ContentId)))
                .ToList();
            return (bundles, match.Clients[0].Game.World.ComputeHash());
        }

        var first = Play(networkSeed: 8);
        var second = Play(networkSeed: 8);
        var other = Play(networkSeed: 9);

        Assert.Equal(first.Bundles, second.Bundles);
        Assert.Equal(first.Hash, second.Hash);

        // Other jitter puts commands in other ticks: a different match, and still one all peers agree on.
        Assert.NotEqual(first.Bundles, other.Bundles);
    }

    [Fact]
    public void TheDefaultsAreTheTechnicalPlans()
    {
        var options = new HostOptions();

        Assert.Equal(TimeSpan.FromMilliseconds(1000 / SimInfo.TicksPerSecond), options.TickInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(50), options.TickInterval);
        Assert.Equal(20, options.HashInterval);
        Assert.Null(options.CommandDelay);
        Assert.Equal(1, CommandDelayPolicy.SinglePlayer);
    }
}
