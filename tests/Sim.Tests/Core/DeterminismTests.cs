using Sim.Core;
using Sim.Numerics;

namespace Sim.Tests.Core;

/// <summary>Same input, same hash: the property the lockstep model stands on.</summary>
public class DeterminismTests
{
    internal const int Ticks = 600;
    internal const ulong Seed = 20261110;

    /// <summary>
    /// The state hash of the toy match after its last tick. It pins the behaviour of the whole
    /// kernel: fixed-point rounding, the sine table, fractions, the random generator, the order of
    /// a tick and the hash itself. If a change to any of them is intended, update this value in the
    /// same pull request; if not, the change has broken determinism with earlier builds.
    /// </summary>
    internal const ulong ExpectedFinalHash = 0x38B6B6E5A9D9C5B7;

    [Fact]
    public void TheSameSeedAndCommandsGiveTheSameHashAfterEveryTick()
    {
        var first = new ToyMatch(Seed).Play(Ticks, ToyMatch.Script(Ticks));
        var second = new ToyMatch(Seed).Play(Ticks, ToyMatch.Script(Ticks));

        Assert.Equal(first, second);
        Assert.Equal(Ticks, first.Distinct().Count());
    }

    [Fact]
    public void TheFinalHashIsTheRecordedOne()
    {
        var hashes = new ToyMatch(Seed).Play(Ticks, ToyMatch.Script(Ticks));

        Assert.Equal(ExpectedFinalHash, hashes[^1]);
    }

    [Fact]
    public void TheToyMatchExercisesTheKernel()
    {
        var match = new ToyMatch(Seed);
        var script = ToyMatch.Script(Ticks);
        match.Play(Ticks, script);

        // Without these the tests above would prove little.
        Assert.Equal(Ticks, match.World.Tick);
        Assert.InRange(match.Movers.Count, 20, 200);
        Assert.Equal(match.Movers.Count, match.World.Entities.Count);
        Assert.True(match.World.Entities[^1].Value > 150, "Entities were created throughout the match.");
        Assert.True(match.World.Entities[^1].Value - match.World.Entities.Count > 50, "Entities were destroyed.");
        Assert.True(match.Scores[0] > 0 && match.Scores[1] > 0, "Both players scored.");
        Assert.InRange(match.Simulation.DroppedCommandCount, 10, script.Count - 10);
        Assert.Equal(0, match.Simulation.Commands.Count);
    }

    [Fact]
    public void ADifferentSeedGivesADifferentHash()
    {
        var first = new ToyMatch(Seed).Play(Ticks, ToyMatch.Script(Ticks));
        var second = new ToyMatch(Seed + 1).Play(Ticks, ToyMatch.Script(Ticks));

        Assert.NotEqual(first[0], second[0]);
        Assert.NotEqual(first[^1], second[^1]);
    }

    [Fact]
    public void OneCommandMovedByOneTickChangesTheHashFromThatTickOn()
    {
        var script = ToyMatch.Script(Ticks);
        var moved = script.FindIndex(entry => entry.Tick >= 300 && entry.Command.Type == ToyMatch.Spawn
            && entry.Command.TargetX < Fixed.FromInt(ToyMatch.MapSize));
        var movedTick = script[moved].Tick;
        var changed = new List<(int Tick, Command Command)>(script);
        changed[moved] = (movedTick + 1, script[moved].Command);

        var first = new ToyMatch(Seed).Play(Ticks, script);
        var second = new ToyMatch(Seed).Play(Ticks, changed);

        Assert.Equal(first[..movedTick], second[..movedTick]);
        Assert.NotEqual(first[movedTick], second[movedTick]);
        Assert.NotEqual(first[^1], second[^1]);
    }

    [Fact]
    public void TheOrderOfTwoCommandsInOneTickMatters()
    {
        // Two spawns in the same tick draw from the random generator in the order they run.
        var a = new Command(ToyMatch.Spawn, 0, targetX: Fixed.FromInt(10), targetY: Fixed.FromInt(10));
        var b = new Command(ToyMatch.Spawn, 1, targetX: Fixed.FromInt(50), targetY: Fixed.FromInt(50));

        var first = new ToyMatch(Seed).Play(8, [(2, a), (2, b)]);
        var second = new ToyMatch(Seed).Play(8, [(2, b), (2, a)]);

        Assert.Equal(first[..2], second[..2]);
        Assert.NotEqual(first[2], second[2]);
    }

    [Fact]
    public void CommandsForDifferentTicksCanBeQueuedInAnyOrder()
    {
        // Latest tick first. The sort is stable, so commands for the same tick keep their order.
        var script = ToyMatch.Script(Ticks);
        var latestFirst = script.OrderByDescending(entry => entry.Tick).ToList();
        Assert.True(latestFirst[0].Tick > latestFirst[^1].Tick);
        Assert.Contains(script.GroupBy(entry => entry.Tick), group => group.Count() > 1);

        var first = new ToyMatch(Seed).Play(Ticks, script);
        var second = new ToyMatch(Seed).Play(Ticks, latestFirst);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheOrderInWhichStoresWereRegisteredDoesNotMatter()
    {
        var first = new ToyMatch(Seed).Play(Ticks, ToyMatch.Script(Ticks));
        var second = new ToyMatch(Seed, registerStoresInReverse: true).Play(Ticks, ToyMatch.Script(Ticks));

        Assert.Equal(first, second);
    }

    [Fact]
    public void RunningTicksOneByOneOrInABatchIsTheSame()
    {
        var stepped = new ToyMatch(Seed);
        var batched = new ToyMatch(Seed);
        foreach (var (tick, command) in ToyMatch.Script(Ticks))
        {
            stepped.Simulation.Commands.Enqueue(tick, command);
            batched.Simulation.Commands.Enqueue(tick, command);
        }

        for (var i = 0; i < Ticks; i++)
        {
            stepped.Simulation.Tick();
        }

        batched.Simulation.Run(Ticks);

        Assert.Equal(stepped.World.ComputeHash(), batched.World.ComputeHash());
        Assert.Equal(Ticks, batched.World.Tick);
    }
}
