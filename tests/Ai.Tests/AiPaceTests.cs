using Sim;
using Sim.Core;
using Sim.Setup;

namespace Ai.Tests;

/// <summary>
/// How often it thinks when its orders take long (<see cref="AiPace"/>). On the host an order is
/// carried out a few ticks after it is given, and a round every half second reads a match in
/// which the orders of the round before have run. From another machine an order takes the way
/// to the host as well, and a round that came too early would give the same orders twice.
/// </summary>
public class AiPaceTests
{
    /// <summary>The command delays a host chooses are 2 to 5 ticks. Nothing is different then.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(8)]
    public void WithTheDelayOfAHostItThinksEveryIntervalAsItAlwaysDid(int delay)
    {
        var run = new Run(delay, "elite");

        run.Play(60 * SimInfo.TicksPerSecond);

        Assert.Equal(delay, run.Ai.Pace.OrderTicks);
        Assert.Equal(AiPlayer.ThinkInterval, run.Ai.Pace.Interval);
        Assert.True(run.Rounds.Count >= 8, $"{run.Rounds.Count} rounds gave orders");
        Assert.All(run.Rounds, tick => Assert.Equal(1, tick % AiPlayer.ThinkInterval));
        Assert.Equal(0, run.Match.Simulation.DroppedCommandCount);
        Assert.Equal(0, run.Refusals);
    }

    /// <summary>
    /// With a round trip of 280 ms and the longest delay a host chooses, an order from the other
    /// machine runs about twelve ticks after it is given; the protocol allows a delay of twenty.
    /// </summary>
    [Theory]
    [InlineData(9, "swarm")]
    [InlineData(10, "elite")]
    [InlineData(12, "swarm")]
    [InlineData(20, "elite")]
    [InlineData(30, "swarm")]
    public void WhereItsOrdersTakeLongerItWaitsForThemAndThinksAsMuchLater(int delay, string personality)
    {
        var run = new Run(delay, personality);

        run.Play(5 * 60 * SimInfo.TicksPerSecond);

        // The second round is the tick the first one's orders were carried out in, and no sooner.
        Assert.Equal(1, run.Rounds[0]);
        Assert.Equal(1 + Math.Max(delay, AiPlayer.ThinkInterval), run.Rounds[1]);
        Assert.Equal(delay, run.Ai.Pace.OrderTicks);
        Assert.Equal(Math.Max(AiPlayer.ThinkInterval, delay + AiPace.Margin), run.Ai.Pace.Interval);
        for (var i = 2; i < run.Rounds.Count; i++)
        {
            Assert.True(run.Rounds[i] - run.Rounds[i - 1] >= delay, $"rounds at ticks {run.Rounds[i - 1]} and {run.Rounds[i]}");
        }

        // So nothing it orders is ordered twice: no command is dropped and none is refused.
        Assert.True(run.Rounds.Count >= 30, $"{run.Rounds.Count} rounds gave orders");
        Assert.Equal(0, run.Match.Simulation.DroppedCommandCount);
        Assert.Equal(0, run.Refusals);
    }

    /// <summary>An order can come to nothing. It waits no longer than <see cref="AiPace.LongestWait"/>, and takes no measure from it.</summary>
    [Fact]
    public void ForOrdersThatAreNeverCarriedOutItWaitsOnlySoLong()
    {
        var run = new Run(delay: -1, "swarm");

        run.Play(AiPace.LongestWait);
        Assert.Equal([1], run.Rounds);
        Assert.True(run.Ai.Pace.IsWaiting);

        run.Play(1);
        Assert.Equal([1, 1 + AiPace.LongestWait], run.Rounds);
        Assert.Equal(0, run.Ai.Pace.OrderTicks);
        Assert.Equal(AiPlayer.ThinkInterval, run.Ai.Pace.Interval);
    }

    /// <summary>A worker it watches that dies is not waited for.</summary>
    [Fact]
    public void ItDoesNotWaitForAWorkerThatIsGone()
    {
        var run = new Run(delay: -1, "swarm");
        run.Play(1);
        var workers = run.Given.Where(command => command.Type == CommandType.Gather).Select(command => command.Entities[0]).ToList();
        Assert.NotEmpty(workers);
        Assert.True(run.Ai.Pace.IsWaiting);

        ref var health = ref run.Match.Health.ValueAt(run.Match.Health.IndexOf(workers[0]));
        health.HitPoints = 0;
        run.Play(2);

        Assert.False(run.Ai.Pace.IsWaiting);
        Assert.Equal(0, run.Ai.Pace.OrderTicks);
    }

    /// <summary>
    /// It can only tell the tick its orders ran in when it is called after every tick. Called
    /// once a round, as some tests of this project do, it takes no measure and thinks as before.
    /// </summary>
    [Fact]
    public void CalledLessOftenThanEveryTickItTakesNoMeasure()
    {
        var data = GameFiles.Data;
        var match = MatchSetup.Create(data, data.Maps.Single(map => map.Id == "ridge"), seed: 1, [new PlayerSetup(0)]);
        var ai = new AiPlayer(match, 0, data.PersonalityOf("swarm")!);
        var commands = new List<Command>();
        var rounds = 0;

        for (var round = 0; round < 6; round++)
        {
            for (var tick = 0; tick < AiPlayer.ThinkInterval + 3; tick++)
            {
                match.Simulation.Tick();
            }

            commands.Clear();
            ai.Think(commands);
            rounds += commands.Count > 0 ? 1 : 0;
            foreach (var command in commands)
            {
                match.Simulation.Commands.Enqueue(match.World.Tick, command);
            }
        }

        Assert.Equal(0, ai.Pace.OrderTicks);
        Assert.Equal(AiPlayer.ThinkInterval, ai.Pace.Interval);
        Assert.True(rounds >= 2);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    /// <summary>
    /// One computer player alone on Ridge, called after every tick, with its orders carried out
    /// a number of ticks after the round that gave them, or never.
    /// </summary>
    private sealed class Run
    {
        private readonly int _delay;
        private readonly List<Command> _commands = [];

        public Run(int delay, string personality)
        {
            var data = GameFiles.Data;
            _delay = delay;
            Match = MatchSetup.Create(data, data.Maps.Single(map => map.Id == "ridge"), seed: 1, [new PlayerSetup(0)]);
            Ai = new AiPlayer(Match, 0, data.PersonalityOf(personality)!);
        }

        public Match Match { get; }

        public AiPlayer Ai { get; }

        /// <summary>The ticks after which it gave orders.</summary>
        public List<int> Rounds { get; } = [];

        public List<Command> Given { get; } = [];

        /// <summary>The orders the simulation refused with an event, which need not be dropped commands.</summary>
        public int Refusals { get; private set; }

        public void Play(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                Match.Simulation.Tick();
                foreach (var happened in Match.Simulation.Events)
                {
                    if (happened.Kind is SimEventKind.BuildRefused or SimEventKind.OrderRefused or SimEventKind.TrainRefused)
                    {
                        Refusals++;
                    }
                }

                _commands.Clear();
                Ai.Think(_commands);
                if (_commands.Count > 0)
                {
                    Rounds.Add(Match.World.Tick);
                    Given.AddRange(_commands);
                }

                foreach (var command in _commands)
                {
                    if (_delay > 0)
                    {
                        Match.Simulation.Commands.Enqueue(Match.World.Tick + _delay - 1, command);
                    }
                }
            }
        }
    }
}
