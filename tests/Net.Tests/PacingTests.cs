using Sim.Core;

namespace Net.Tests;

/// <summary>
/// When ticks run: steadily when the bundles are there, not at all when they are not, faster when
/// a client is behind, and what the host does about a client that stays behind.
/// </summary>
public class PacingTests
{
    private static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// The head start of the command delay is what absorbs latency and jitter: with enough of it a
    /// client runs one tick per interval however unevenly the bundles arrive.
    /// </summary>
    [Fact]
    public void WithEnoughHeadStartAClientRunsOneTickPerIntervalDespiteJitter()
    {
        var steady = Play(commandDelay: 3);
        var starved = Play(commandDelay: 1);

        // Updates come every 16.7 ms, so a 50 ms tick falls two, three or four updates after the last.
        Assert.All(steady.Gaps, gap => Assert.InRange(gap, 33 * Millisecond, 67 * Millisecond));
        Assert.Equal(1, steady.MostTicksInOneUpdate);
        Assert.Contains(starved.Gaps, gap => gap > 67 * Millisecond);

        static (List<TimeSpan> Gaps, int MostTicksInOneUpdate) Play(int commandDelay)
        {
            var options = new HostOptions { CommandDelay = commandDelay, HashInterval = 1 };
            var match = new TestMatch(clients: 2, options: options, conditions: new LinkConditions(30 * Millisecond, 40 * Millisecond), networkSeed: 3);
            match.Start();
            match.Run(seconds: 20);

            var client = match.Clients[1];
            Assert.InRange(client.Tick, 380, 402);
            var times = client.TickTimes.Select(entry => entry.At).ToList();
            return (times.Skip(1).Zip(times, (later, earlier) => later - earlier).ToList(), client.TicksPerAdvance.Max());
        }
    }

    [Fact]
    public void AClientWaitsForABundleAndNeverRunsAhead()
    {
        var match = new TestMatch(clients: 2);
        match.Start();
        match.Run(seconds: 1);
        var held = match.Clients[1];
        var free = match.Clients[0];

        // Half a second of latency appears from nowhere: nothing reaches the client for that long.
        held.Link.Conditions = new LinkConditions(500 * Millisecond, TimeSpan.Zero);
        var tickBefore = held.Tick;
        var freeBefore = free.Tick;
        for (var frame = 0; frame < 24; frame++)
        {
            match.Step();
            Assert.Equal(held.Session.NextBundleTick, held.Tick + held.Session.BufferedTicks);
            Assert.True(held.Tick <= held.Session.NextBundleTick);
        }

        // It ran the one bundle it had in hand and then stopped. The other client never noticed.
        Assert.Equal(tickBefore + 1, held.Tick);
        Assert.Equal(0, held.Session.BufferedTicks);
        Assert.Equal(held.Session.TickInterval, held.Session.TimeIntoTick);
        Assert.Equal(freeBefore + 8, free.Tick);

        // The latency goes away again. What was held up arrives together, and the client catches up.
        held.Link.Conditions = default;
        match.Run(seconds: 1.5);

        Assert.InRange(free.Tick - held.Tick, 0, 1);
        Assert.Empty(match.HostDepartures);
        match.Finish();
        Assert.Equal(free.Game.World.ComputeHash(), held.Game.World.ComputeHash());
    }

    /// <summary>
    /// A client that had to wait does not rush to make up the wait. Its ticks follow, one per
    /// interval, from when the late bundle came, so a connection that became slower costs one
    /// pause and not a pause and a jump every time a bundle is late again.
    /// </summary>
    [Fact]
    public void AfterAWaitTheClientKeepsAnEvenPaceFromWhereItIs()
    {
        var match = new TestMatch(clients: 2, options: new HostOptions { CommandDelay = 2, HashInterval = 1 });
        match.Start();
        match.Run(seconds: 1);
        var client = match.Clients[1];

        // The connection gets 90 ms slower and stays so: more than the head start of one tick covers.
        client.Link.Conditions = new LinkConditions(90 * Millisecond, TimeSpan.Zero);
        client.TickTimes.Clear();
        client.TicksPerAdvance.Clear();
        match.Run(seconds: 3);

        var times = client.TickTimes.Select(entry => entry.At).ToList();
        var gaps = times.Skip(1).Zip(times, (later, earlier) => later - earlier).ToList();
        Assert.Equal(1, client.TicksPerAdvance.Max());
        Assert.Single(gaps, gap => gap > 67 * Millisecond);
        Assert.All(gaps, gap => Assert.InRange(gap, 33 * Millisecond, 150 * Millisecond));
        Assert.Empty(match.HostDepartures);
    }

    /// <summary>
    /// A short hold-up on the network delivers a few bundles late and together. The client pauses
    /// once and then runs on at one tick per interval, the bundles that came together waiting
    /// their turn: within the slack it does not jump ahead to use them up.
    /// </summary>
    [Fact]
    public void AShortHoldUpCostsOnePauseAndNoJump()
    {
        var match = new TestMatch(clients: 2, options: new HostOptions { CommandDelay = 2, HashInterval = 1 });
        match.Start();
        match.Run(seconds: 1);
        var client = match.Clients[1];
        client.TickTimes.Clear();
        client.TicksPerAdvance.Clear();

        client.Link.Conditions = new LinkConditions(110 * Millisecond, TimeSpan.Zero);
        match.Run(seconds: 0.08);
        client.Link.Conditions = default;
        match.Run(seconds: 2);

        var times = client.TickTimes.Select(entry => entry.At).ToList();
        var gaps = times.Skip(1).Zip(times, (later, earlier) => later - earlier).ToList();
        Assert.Equal(1, client.TicksPerAdvance.Max());
        Assert.Single(gaps, gap => gap > 67 * Millisecond);
        Assert.All(gaps, gap => Assert.InRange(gap, 33 * Millisecond, 150 * Millisecond));

        // It is a little further behind the host than before, by no more than the slack.
        var level = client.Session.CommandDelay - 1;
        Assert.InRange(client.Session.BufferedTicks, level + 1, level + client.Session.Options.CatchUpSlack);
        Assert.InRange(match.Clients[0].Tick - client.Tick, 1, client.Session.Options.CatchUpSlack);
    }

    /// <summary>
    /// The match ends at one tick for everyone. A client that is far behind when the host ends it
    /// still runs every tick up to there, a limited number per update, and ends in the same state.
    /// </summary>
    [Fact]
    public void AClientThatIsBehindWhenTheMatchEndsStillRunsToTheLastTick()
    {
        var match = new TestMatch(clients: 2, options: new HostOptions { CommandDelay = 2, HashInterval = 1 });
        match.Start();
        match.Run(seconds: 1);
        var late = match.Clients[1];
        late.Frozen = true;
        for (var frame = 0; frame < 150; frame++)
        {
            match.Clients[0].Spawn(frame);
            match.Step();
        }

        match.Host.EndSession();
        late.Frozen = false;
        late.TicksPerAdvance.Clear();
        var behind = match.Host.NextTick - late.Tick;
        var finalTick = match.Finish();

        Assert.True(behind > 40);
        Assert.Equal(finalTick, late.Tick);
        Assert.Equal(new SessionEndInfo(SessionEndReason.HostEnded, finalTick), late.Session.EndInfo);
        Assert.Equal([10, 10, 10, 10], late.TicksPerAdvance.Take(4));
        Assert.Equal(behind, late.TicksPerAdvance.Sum());
        Assert.Equal(match.Clients[0].Game.World.ComputeHash(), late.Game.World.ComputeHash());
        Assert.Equal(match.Clients[0].Game.Log, late.Game.Log);
    }

    /// <summary>
    /// A slow client falls behind and catches up by running several ticks per update; it does not
    /// stall the others (technical plan, section 2).
    /// </summary>
    [Fact]
    public void ASlowClientCatchesUpSeveralTicksPerUpdateWithoutHoldingUpTheOthers()
    {
        var match = new TestMatch(clients: 3, options: new HostOptions { PlayerCount = 3, CommandDelay = 2, HashInterval = 1 });
        match.Start();
        match.Run(seconds: 1);
        var slow = match.Clients[2];
        var other = match.Clients[0];

        slow.Frozen = true;
        var slowTick = slow.Tick;
        var otherTick = other.Tick;
        var hostTick = match.Host.NextTick;
        for (var frame = 0; frame < 180; frame++)
        {
            other.Spawn(frame);
            match.Step();
        }

        Assert.Equal(slowTick, slow.Tick);
        Assert.Equal(hostTick + 60, match.Host.NextTick);
        Assert.Equal(otherTick + 60, other.Tick);
        var gaps = other.TickTimes.Skip(1).Zip(other.TickTimes, (later, earlier) => later.At - earlier.At);
        Assert.All(gaps, gap => Assert.InRange(gap, 33 * Millisecond, 67 * Millisecond));

        // Back again, 60 ticks behind: ten ticks an update, the most a client runs in one.
        slow.Frozen = false;
        slow.TicksPerAdvance.Clear();
        match.Run(seconds: 0.2);

        Assert.Equal(10, slow.Session.Options.MaxTicksPerAdvance);
        Assert.Equal([10, 10, 10, 10, 10, 10], slow.TicksPerAdvance.Take(6));
        Assert.All(slow.TicksPerAdvance, ticks => Assert.InRange(ticks, 0, 10));
        Assert.InRange(other.Tick - slow.Tick, 0, 1);
        Assert.InRange(slow.Session.BufferedTicks, slow.Session.CommandDelay - 1, slow.Session.CommandDelay);

        Assert.Empty(match.HostDepartures);
        Assert.Equal(SessionPhase.Running, match.Host.Phase);
        match.Finish();
        Assert.Equal(other.Game.World.ComputeHash(), slow.Game.World.ComputeHash());
        Assert.Equal(other.Game.Log, slow.Game.Log);
    }

    /// <summary>A client that cannot keep up is dropped (technical plan, section 2), and the match goes on.</summary>
    [Fact]
    public void AClientThatStopsRespondingIsDroppedAndTheOthersPlayOn()
    {
        var options = new HostOptions { PlayerCount = 3, CommandDelay = 2, HashInterval = 1, MaxLagTicks = 40 };
        var match = new TestMatch(clients: 3, options: options);
        match.Start();
        match.Run(seconds: 1);
        var hung = match.Clients[1];
        var other = match.Clients[0];

        hung.Frozen = true;
        match.Run(seconds: 1.9);
        Assert.Empty(match.HostDepartures);
        Assert.True(match.Host.IsAttached(1));

        match.Run(seconds: 0.4);
        Assert.Equal([(1, SessionEndReason.FellBehind)], match.HostDepartures);
        Assert.False(match.Host.IsAttached(1));
        Assert.Equal(-1, match.Host.GetReportedTick(1));
        Assert.Equal([(1, SessionEndReason.FellBehind)], other.Departures);
        Assert.Equal([(1, SessionEndReason.FellBehind)], match.Clients[2].Departures);

        // The seat is the host's now, so the host can give an order in the dropped player's name.
        var tickAtDrop = other.Tick;
        Assert.True(match.Host.Submit(new Command(ToyGame.Spawn, 1, contentId: 5)));
        match.Run(seconds: 1);
        Assert.Equal(SessionPhase.Running, match.Host.Phase);
        Assert.InRange(other.Tick, tickAtDrop + 19, tickAtDrop + 21);
        Assert.Equal((1, 5), (other.Game.Log[^1].Player, other.Game.Log[^1].ContentId));

        // When the client comes back to life it learns that it is out, and stops where it is.
        hung.Frozen = false;
        var hungTick = hung.Tick;
        match.Step();
        Assert.Equal(new SessionEndInfo(SessionEndReason.FellBehind, -1), hung.Session.EndInfo);
        Assert.Equal(hungTick, hung.Tick);

        match.Finish();
        Assert.Equal(other.Game.World.ComputeHash(), match.Clients[2].Game.World.ComputeHash());
    }

    /// <summary>
    /// A machine too slow to run the simulation in real time falls further behind with every
    /// second, however it tries to catch up, until the host drops it.
    /// </summary>
    [Fact]
    public void AClientTooSlowForRealTimeIsDroppedInTheEnd()
    {
        var options = new HostOptions { CommandDelay = 2, HashInterval = 1, MaxLagTicks = 40 };
        var match = new TestMatch(clients: 2, options: options, clientOptions: new ClientOptions { MaxTicksPerAdvance = 1 });
        match.Start();
        var slow = match.Clients[1];

        // One update in six, and one tick an update at most: ten ticks a second against the host's twenty.
        for (var frame = 0; frame < 600 && match.HostDepartures.Count == 0; frame++)
        {
            slow.Frozen = frame % 6 != 0;
            match.Step();
            Assert.True(match.Host.NextTick - slow.Tick <= 40 + 3, "The host let the client fall further behind than it allows.");
        }

        Assert.Equal([(1, SessionEndReason.FellBehind)], match.HostDepartures);
        Assert.InRange(match.Host.NextTick, 70, 95);
        Assert.Equal(SessionPhase.Running, match.Host.Phase);
        Assert.Equal(SessionPhase.Running, match.Clients[0].Session.Phase);
    }

    [Fact]
    public void AClientThatLeavesIsReportedAndTheOthersPlayOn()
    {
        var match = new TestMatch(clients: 3, options: new HostOptions { PlayerCount = 3, CommandDelay = 2, HashInterval = 1 });
        match.Start();
        match.Run(seconds: 1);
        SessionEndInfo? end = null;
        match.Clients[2].Session.Stopped += info => end = info;

        match.Clients[2].Session.Leave();
        var leftAt = match.Clients[2].Tick;
        match.Run(seconds: 1);

        Assert.Equal(new SessionEndInfo(SessionEndReason.Left, -1), end);
        Assert.Equal(SessionPhase.Stopped, match.Clients[2].Session.Phase);
        Assert.Equal(leftAt, match.Clients[2].Tick);
        Assert.False(match.Clients[2].Spawn(1));
        Assert.Equal([(2, SessionEndReason.ConnectionLost)], match.HostDepartures);
        Assert.Equal([(2, SessionEndReason.ConnectionLost)], match.Clients[0].Departures);
        Assert.Equal(SessionPhase.Running, match.Host.Phase);
        Assert.InRange(match.Clients[0].Tick, leftAt + 19, leftAt + 21);

        match.Finish();
        Assert.Equal(match.Clients[0].Game.World.ComputeHash(), match.Clients[1].Game.World.ComputeHash());
    }

    [Fact]
    public void WhenTheHostIsGoneEveryClientStops()
    {
        var match = new TestMatch(clients: 2, conditions: new LinkConditions(20 * Millisecond, TimeSpan.Zero));
        match.Start();
        match.Run(seconds: 1);

        foreach (var client in match.Clients)
        {
            client.Link.HostEnd.Close();
        }

        match.Run(seconds: 0.5);

        Assert.All(match.Clients, client =>
        {
            Assert.Equal(new SessionEndInfo(SessionEndReason.ConnectionLost, -1), client.Session.EndInfo);
            Assert.False(client.Link.ClientEnd.IsOpen);
            Assert.Equal(0, client.Session.Advance(TimeSpan.FromSeconds(1)));
        });
    }

    /// <summary>The picture between two ticks: how far the client is into the tick interval.</summary>
    [Fact]
    public void TimeIntoTickSaysHowFarTheNextTickIs()
    {
        var match = new TestMatch(clients: 1, options: new HostOptions { CommandDelay = 3, HashInterval = 1 });
        match.Start();
        var session = match.Clients[0].Session;
        match.Step(TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, session.TimeIntoTick);

        match.Step(30 * Millisecond);
        Assert.Equal(30 * Millisecond, session.TimeIntoTick);
        Assert.Equal(0, match.Clients[0].Tick);

        match.Step(30 * Millisecond);
        Assert.Equal(10 * Millisecond, session.TimeIntoTick);
        Assert.Equal(1, match.Clients[0].Tick);

        match.Step(140 * Millisecond);
        Assert.Equal(TimeSpan.Zero, session.TimeIntoTick);
        Assert.Equal(4, match.Clients[0].Tick);
    }

    [Fact]
    public void TimeDoesNotRunBackwards()
    {
        var match = new TestMatch(clients: 1);
        match.Start();

        Assert.Throws<ArgumentOutOfRangeException>(() => match.Host.Advance(-Millisecond));
        Assert.Throws<ArgumentOutOfRangeException>(() => match.Clients[0].Session.Advance(-Millisecond));
    }
}
