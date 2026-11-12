namespace Net.Tests;

/// <summary>
/// The pace of a match, which is the host's: paused, no tick runs anywhere; and the speed is the
/// time between two ticks, on every machine alike. Neither changes what a tick computes.
/// </summary>
public class PaceTests
{
    [Fact]
    public void WhileTheHostHoldsTheMatchNoTickRunsAnywhereAndThenItGoesOn()
    {
        var match = new TestMatch(clients: 2);
        match.Start();
        match.Run(seconds: 1);

        Assert.True(match.Host.SetPaused(true));
        match.Run(seconds: 0.5);
        var (held, issued) = (match.Clients.Select(client => client.Tick).ToArray(), match.Host.NextTick);
        match.Run(seconds: 3);

        Assert.True(match.Host.IsPaused);
        Assert.All(match.Clients, client => Assert.True(client.Session.IsPaused));
        Assert.Equal(held, match.Clients.Select(client => client.Tick));
        Assert.Equal(issued, match.Host.NextTick);

        // Every client ran every bundle there was, so all of them rest in the same tick.
        Assert.All(match.Clients, client => Assert.Equal(issued, client.Tick));
        Assert.All(match.Clients, client => Assert.Equal(client.Session.TickInterval, client.Session.TimeIntoTick));
        Assert.Equal(match.Clients[0].Game.World.ComputeHash(), match.Clients[1].Game.World.ComputeHash());

        Assert.True(match.Host.SetPaused(false));
        match.Run(seconds: 1);

        Assert.False(match.Host.IsPaused);
        Assert.All(match.Clients, client => Assert.False(client.Session.IsPaused));
        Assert.All(match.Clients, client => Assert.InRange(client.Tick, issued + 17, issued + 21));
        Assert.Empty(match.HostDepartures);
    }

    /// <summary>Nobody is dropped for the ticks that did not run, however long the pause is.</summary>
    [Fact]
    public void ALongPauseDropsNobodyAndOwesNoTicks()
    {
        var match = new TestMatch(clients: 2, options: new HostOptions { CommandDelay = 2, HashInterval = 1, MaxLagTicks = 20 });
        match.Start();
        match.Run(seconds: 1);
        match.Host.SetPaused(true);
        match.Run(seconds: 60);
        var issued = match.Host.NextTick;

        match.Host.SetPaused(false);
        match.Step();

        Assert.InRange(match.Host.NextTick, issued, issued + 1);
        match.Run(seconds: 1);
        Assert.Empty(match.HostDepartures);
        Assert.All(match.Clients, client => Assert.Equal(1, client.TicksPerAdvance.Max()));
    }

    [Fact]
    public void OrdersGivenInAPauseWaitAndRunInTheFirstTickAfterIt()
    {
        var match = new TestMatch(clients: 2);
        match.Start();
        match.Run(seconds: 0.5);
        match.Host.SetPaused(true);
        match.Run(seconds: 0.5);
        var issued = match.Host.NextTick;

        Assert.True(match.Clients[1].Spawn(41));
        Assert.True(match.Clients[0].Spawn(40));
        match.Run(seconds: 1);
        Assert.All(match.Clients, client => Assert.Empty(client.Game.Log));

        match.Host.SetPaused(false);
        match.Run(seconds: 0.5);

        Assert.All(match.Clients, client =>
        {
            Assert.Equal([issued, issued], client.Game.Log.Select(entry => entry.Tick));
            Assert.Equal([40, 41], client.Game.Log.Select(entry => entry.ContentId).Order());
        });
    }

    [Theory]
    [InlineData(25, 40)]
    [InlineData(12.5, 80)]
    [InlineData(100, 10)]
    public void TheSpeedIsTheTimeBetweenTwoTicksOnEveryMachine(double milliseconds, int ticksPerSecond)
    {
        var interval = TimeSpan.FromMilliseconds(milliseconds);
        var match = new TestMatch(clients: 2);
        match.Start();
        match.Run(seconds: 1);

        Assert.True(match.Host.SetTickInterval(interval));
        match.Run(seconds: 0.5);
        var before = match.Clients.Select(client => client.Tick).ToArray();
        match.Run(seconds: 2);

        Assert.Equal(interval, match.Host.TickInterval);
        Assert.All(match.Clients, client => Assert.Equal(interval, client.Session.TickInterval));
        Assert.All(match.Clients, client => Assert.InRange(client.Tick - before[client.Seat], (2 * ticksPerSecond) - 3, (2 * ticksPerSecond) + 3));
        Assert.Empty(match.HostDepartures);

        // Back at the speed the match began with.
        match.Host.SetTickInterval(TestMatch.TickInterval);
        match.Run(seconds: 0.5);
        before = [.. match.Clients.Select(client => client.Tick)];
        match.Run(seconds: 2);
        Assert.All(match.Clients, client => Assert.InRange(client.Tick - before[client.Seat], 37, 43));

        match.Finish();
        Assert.Equal(match.Clients[0].Game.World.ComputeHash(), match.Clients[1].Game.World.ComputeHash());
    }

    /// <summary>A match is the same match at any pace: the commands and their ticks decide it, not when the ticks ran.</summary>
    [Fact]
    public void PauseAndSpeedChangeNothingATickComputes()
    {
        var match = new TestMatch(clients: 2);
        match.Start();
        match.Run(seconds: 0.2);
        for (var round = 0; round < 12; round++)
        {
            Assert.True(match.Clients[round % 2].Spawn(round));
            match.Run(seconds: 0.3);
            switch (round % 4)
            {
                case 0:
                    match.Host.SetTickInterval(TimeSpan.FromMilliseconds(20));
                    break;
                case 1:
                    match.Host.SetPaused(true);
                    break;
                case 2:
                    match.Host.SetPaused(false);
                    break;
                default:
                    match.Host.SetTickInterval(TimeSpan.FromMilliseconds(80));
                    break;
            }
        }

        var finalTick = match.Finish();

        var replayed = ToyGame.Replay(match.Clients[0].Bundles, finalTick);
        Assert.All(match.Clients, client => Assert.Equal(replayed.World.ComputeHash(), client.Game.World.ComputeHash()));
        Assert.All(match.Clients, client => Assert.Equal(finalTick, client.Tick));
        Assert.Equal(12, match.Clients[0].Game.Log.Count);
    }

    [Fact]
    public void EveryClientIsToldOnceOfEachChange()
    {
        var match = new TestMatch(clients: 2);
        match.Start();
        match.Run(seconds: 0.2);
        var changes = new List<(bool Paused, double Milliseconds)>();
        var guest = match.Clients[1].Session;
        guest.PaceChanged += () => changes.Add((guest.IsPaused, guest.TickInterval.TotalMilliseconds));

        match.Host.SetPaused(true);
        match.Host.SetPaused(true);
        match.Host.SetTickInterval(TimeSpan.FromMilliseconds(25));
        match.Host.SetTickInterval(TimeSpan.FromMilliseconds(25));
        match.Host.SetPaused(false);
        match.Run(seconds: 0.2);

        Assert.Equal([(true, 50), (true, 25), (false, 25)], changes);
    }

    [Fact]
    public void TheSpeedSetBeforeAPauseIsTheSpeedAfterIt()
    {
        var match = new TestMatch(clients: 1);
        match.Start();
        match.Host.SetTickInterval(TimeSpan.FromMilliseconds(25));
        match.Host.SetPaused(true);
        match.Run(seconds: 1);
        var before = match.Clients[0].Tick;

        match.Host.SetPaused(false);
        match.Run(seconds: 1);

        Assert.InRange(match.Clients[0].Tick - before, 38, 41);
    }

    [Fact]
    public void ThePaceCanOnlyBeSetWhileTheMatchRuns()
    {
        var match = new TestMatch(clients: 1);
        Assert.False(match.Host.SetPaused(true));
        Assert.False(match.Host.SetTickInterval(TimeSpan.FromMilliseconds(25)));
        Assert.False(match.Host.IsPaused);
        Assert.Equal(TestMatch.TickInterval, match.Host.TickInterval);

        match.Start();
        Assert.Throws<ArgumentOutOfRangeException>(() => match.Host.SetTickInterval(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => match.Host.SetTickInterval(TimeSpan.FromMilliseconds(-50)));
        match.Finish();

        Assert.False(match.Host.SetPaused(true));
        Assert.False(match.Host.SetTickInterval(TimeSpan.FromMilliseconds(25)));
    }

    [Fact]
    public void AMatchThatEndsInAPauseEndsWhereItRests()
    {
        var match = new TestMatch(clients: 2);
        match.Start();
        match.Run(seconds: 1);
        match.Host.SetPaused(true);
        match.Run(seconds: 0.5);
        var issued = match.Host.NextTick;

        var finalTick = match.Finish();

        Assert.Equal(issued, finalTick);
        Assert.All(match.Clients, client => Assert.Equal(finalTick, client.Tick));
    }

    /// <summary>Whatever reached the host before the end gets a tick, so a player who concedes as it leaves has conceded.</summary>
    [Fact]
    public void OrdersTheHostHoldsWhenTheMatchEndsRunInItsLastTick()
    {
        var match = new TestMatch(clients: 2);
        match.Start();
        match.Run(seconds: 1);
        match.Host.SetPaused(true);
        match.Run(seconds: 0.5);
        var issued = match.Host.NextTick;
        match.Clients[1].Spawn(9);
        match.Run(seconds: 0.2);

        var finalTick = match.Finish();

        Assert.Equal(issued + 1, finalTick);
        Assert.All(match.Clients, client =>
        {
            Assert.Equal(finalTick, client.Tick);
            Assert.Equal((issued, 9), (client.Game.Log.Single().Tick, client.Game.Log.Single().ContentId));
        });
        Assert.Equal(match.Clients[0].Game.World.ComputeHash(), match.Clients[1].Game.World.ComputeHash());
    }

    /// <summary>A match played alone is paused and sped up through the same host.</summary>
    [Fact]
    public void AMatchPlayedAloneIsPausedAndSpedUpThroughItsHost()
    {
        var game = new ToyGame();
        var session = new LocalSession(game.Simulation);
        Run(session, seconds: 1);
        Assert.InRange(game.World.Tick, 19, 21);

        session.Host.SetPaused(true);
        Run(session, seconds: 1);
        var held = game.World.Tick;
        Run(session, seconds: 1);
        Assert.Equal(held, game.World.Tick);
        Assert.True(session.Client.IsPaused);

        session.Host.SetPaused(false);
        session.Host.SetTickInterval(TimeSpan.FromMilliseconds(12.5));
        Run(session, seconds: 1);
        Assert.InRange(game.World.Tick - held, 78, 81);

        static void Run(LocalSession session, double seconds)
        {
            for (var passed = TimeSpan.Zero; passed < TimeSpan.FromSeconds(seconds); passed += TestMatch.Frame)
            {
                session.Advance(TestMatch.Frame);
            }
        }
    }
}
