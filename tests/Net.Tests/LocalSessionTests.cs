using Sim.Core;

namespace Net.Tests;

/// <summary>Single-player: the host and the one client in the same process, over the loopback.</summary>
public class LocalSessionTests
{
    private static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    [Fact]
    public void ALocalSessionIsRunningAsSoonAsItIsMade()
    {
        var game = new ToyGame();

        var session = new LocalSession(game.Simulation);

        Assert.Equal(SessionPhase.Running, session.Host.Phase);
        Assert.Equal(SessionPhase.Running, session.Client.Phase);
        Assert.Equal(0, session.Client.Player);
        Assert.Equal(2, session.Client.PlayerCount);
        Assert.Same(game.Simulation, session.Client.Simulation);
        Assert.Equal(1, session.Host.CommandDelay);
        Assert.Equal(1, session.Client.CommandDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(50), session.Client.TickInterval);
        Assert.Equal(0, game.World.Tick);
    }

    /// <summary>The single-player command delay is 1 tick (technical plan, section 2).</summary>
    [Fact]
    public void ACommandRunsInTheNextTick()
    {
        var game = new ToyGame();
        var session = new LocalSession(game.Simulation);
        Assert.Equal(3, session.Advance(150 * Millisecond));

        Assert.True(session.Client.Submit(new Command(ToyGame.Spawn, 0, contentId: 9)));
        Assert.Empty(game.Log);
        Assert.Equal(1, session.Advance(50 * Millisecond));

        Assert.Equal([new ToyGame.Executed(3, 0, ToyGame.Spawn, 9)], game.Log);
    }

    [Fact]
    public void TicksRunTwentyTimesASecondWhateverTheFrameRate()
    {
        foreach (var (frame, frames, expected) in new[] { (10 * Millisecond, 100, 20), (TestMatch.Frame, 600, 199), (125 * Millisecond, 80, 200), (Millisecond, 1000, 20) })
        {
            var game = new ToyGame();
            var session = new LocalSession(game.Simulation);
            var ran = 0;
            for (var i = 0; i < frames; i++)
            {
                var ticks = session.Advance(frame);
                Assert.InRange(ticks, 0, frame < 50 * Millisecond ? 1 : 3);
                ran += ticks;

                // Between two updates no bundle waits, so the simulation's command queue is empty
                // and a snapshot taken here is the whole state of the game.
                Assert.Equal(0, session.Client.BufferedTicks);
                Assert.Equal(game.World.Tick, session.Host.NextTick);
            }

            Assert.Equal(expected, ran);
            Assert.Equal(expected, game.World.Tick);
            Assert.Equal(ToyGame.Replay([], expected).World.ComputeHash(), game.World.ComputeHash());
        }
    }

    /// <summary>An AI player sits in a seat the host runs, and gives its commands through the host.</summary>
    [Fact]
    public void TheOtherSeatsAreTheHosts()
    {
        var game = new ToyGame();
        var session = new LocalSession(game.Simulation, playerCount: 4);
        var events = new List<(int Tick, int Commands)>();
        session.Client.CommandsScheduled += (tick, commands) => events.Add((tick, commands.Length));

        Assert.True(session.Host.Submit(new Command(ToyGame.Spawn, 3, contentId: 1)));
        Assert.True(session.Client.Submit(new Command(ToyGame.Spawn, 0, contentId: 2)));
        Assert.True(session.Host.Submit(new Command(ToyGame.Spawn, 1, contentId: 3)));
        session.Advance(100 * Millisecond);

        // The host's own commands are in its hands already. The client's reach it in the next update.
        Assert.Equal([(0, 3, 1), (0, 1, 3), (0, 0, 2)], game.Log.Select(entry => (entry.Tick, entry.Player, entry.ContentId)));
        Assert.Equal([(0, 3), (1, 0)], events);
        Assert.Throws<ArgumentException>(() => session.Host.Submit(new Command(ToyGame.Spawn, 0)));
        Assert.Throws<ArgumentException>(() => session.Host.Submit(new Command(ToyGame.Spawn, 4)));
        Assert.Throws<ArgumentException>(() => session.Client.Submit(new Command(ToyGame.Spawn, 1)));
    }

    /// <summary>
    /// A computer player's round is a list whose order matters: its gather orders come before
    /// its build orders. The host runs a seat's commands in the order they were handed over.
    /// </summary>
    [Fact]
    public void ARoundOfOrdersForOneSeatRunsInTheOrderItWasHandedOver()
    {
        var game = new ToyGame();
        var session = new LocalSession(game.Simulation);
        int[] round = [7, 3, 9, 1, 8, 2];

        foreach (var contentId in round)
        {
            Assert.True(session.Host.Submit(new Command(ToyGame.Spawn, 1, contentId: contentId)));
        }

        session.Advance(100 * Millisecond);

        Assert.Equal(round.Select(contentId => (0, 1, contentId)), game.Log.Select(entry => (entry.Tick, entry.Player, entry.ContentId)));
    }

    [Fact]
    public void ThePersonCanSitInAnySeat()
    {
        var game = new ToyGame();
        var session = new LocalSession(game.Simulation, playerCount: 3, localPlayer: 2);

        Assert.Equal(2, session.Client.Player);
        Assert.True(session.Client.Submit(new Command(ToyGame.Spawn, 2)));
        Assert.True(session.Host.Submit(new Command(ToyGame.Spawn, 0)));
        session.Advance(50 * Millisecond);

        Assert.Equal([0, 2], game.Log.Select(entry => entry.Player));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalSession(new ToyGame().Simulation, playerCount: 3, localPlayer: 3));
        Assert.Equal(0, new LocalSession(new ToyGame().Simulation, playerCount: 1).Client.Player);
    }

    /// <summary>
    /// One long update runs no more ticks than the limit and the rest of the time is dropped: a
    /// machine that stalls, or cannot keep up, plays slower and never has a backlog to work off.
    /// </summary>
    [Fact]
    public void ALongUpdateRunsAtMostTheLimitAndDropsTheRestOfTheTime()
    {
        var game = new ToyGame();
        var session = new LocalSession(game.Simulation);

        Assert.Equal(10, session.Advance(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, session.Client.BufferedTicks);
        Assert.Equal(0, session.Advance(TimeSpan.Zero));
        Assert.Equal(10, session.Advance(TimeSpan.FromHours(3)));
        Assert.Equal(1, session.Advance(TimeSpan.FromMilliseconds(50)));
        Assert.Equal(21, game.World.Tick);
        Assert.Equal(SessionPhase.Running, session.Client.Phase);
        Assert.True(session.Host.IsAttached(0));
    }

    /// <summary>Running faster than real time is a matter of saying more time has passed.</summary>
    [Fact]
    public void WithAHigherLimitTheGameCanBeRunFast()
    {
        var game = new ToyGame();
        var session = new LocalSession(game.Simulation, clientOptions: new ClientOptions { MaxTicksPerAdvance = 1000 });

        Assert.Equal(600, session.Advance(TimeSpan.FromSeconds(30)));

        Assert.Equal(ToyGame.Replay([], 600).World.ComputeHash(), game.World.ComputeHash());
        Assert.Equal(0, session.Advance(TimeSpan.Zero));
        Assert.Equal(600, session.Host.GetReportedTick(0));
        Assert.True(session.Host.IsAttached(0));
    }

    [Fact]
    public void TimeIntoTickIsTheTimeSinceTheLastTick()
    {
        var session = new LocalSession(new ToyGame().Simulation);

        session.Advance(30 * Millisecond);
        Assert.Equal(30 * Millisecond, session.Client.TimeIntoTick);
        session.Advance(30 * Millisecond);
        Assert.Equal(10 * Millisecond, session.Client.TimeIntoTick);
        session.Advance(40 * Millisecond);
        Assert.Equal(TimeSpan.Zero, session.Client.TimeIntoTick);
    }

    [Fact]
    public void ASimulationThatHasAlreadyRunStartsWhereItIs()
    {
        var game = new ToyGame();
        game.Simulation.Run(7);

        var session = new LocalSession(game.Simulation);
        session.Client.Submit(new Command(ToyGame.Spawn, 0));
        session.Advance(100 * Millisecond);

        Assert.Equal(7, session.Host.Options.FirstTick);
        Assert.Equal(9, game.World.Tick);
        Assert.Equal(7, Assert.Single(game.Log).Tick);
    }

    /// <summary>The same path as a network match, hash reports included.</summary>
    [Fact]
    public void TheClientReportsItsHashToTheHost()
    {
        var session = new LocalSession(new ToyGame().Simulation, hostOptions: new HostOptions { HashInterval = 5 });

        session.Advance(TimeSpan.FromMilliseconds(600));
        session.Advance(TimeSpan.Zero);

        Assert.Equal(10, session.Host.GetReportedTick(0));
        Assert.Equal(1, session.Host.CommandDelay);
    }

    [Fact]
    public void TheCommandDelayCanBeSetToTryANetworkDelayAlone()
    {
        var game = new ToyGame();
        var session = new LocalSession(game.Simulation, hostOptions: new HostOptions { CommandDelay = 4 });
        session.Advance(100 * Millisecond);

        session.Client.Submit(new Command(ToyGame.Spawn, 0));
        session.Advance(400 * Millisecond);

        Assert.Equal(2 + 4 - 1, Assert.Single(game.Log).Tick);
    }

    [Fact]
    public void EndingTheSessionStopsTheClientAtTheLastTick()
    {
        var game = new ToyGame();
        var session = new LocalSession(game.Simulation);
        session.Advance(TimeSpan.FromMilliseconds(250));

        session.Host.EndSession();
        Assert.Equal(0, session.Advance(TimeSpan.FromSeconds(1)));

        Assert.Equal(new SessionEndInfo(SessionEndReason.HostEnded, 5), session.Client.EndInfo);
        Assert.Equal(5, game.World.Tick);
        Assert.False(session.Client.Submit(new Command(ToyGame.Spawn, 0)));
        Assert.False(session.Host.Submit(new Command(ToyGame.Spawn, 1)));
    }
}
