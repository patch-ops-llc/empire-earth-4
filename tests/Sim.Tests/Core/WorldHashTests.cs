using Sim.Core;
using Sim.Numerics;

namespace Sim.Tests.Core;

public class WorldHashTests
{
    private static ToyMatch MatchAfter(int ticks)
    {
        var match = new ToyMatch(seed: 3);
        match.Simulation.Run(ticks);
        return match;
    }

    [Fact]
    public void ReadingTheWorldDoesNotChangeItsHash()
    {
        var match = MatchAfter(40);
        var hash = match.World.ComputeHash();

        _ = match.World.Entities.IsAlive(new EntityId(1));
        _ = match.Movers.TryGet(new EntityId(1), out _);
        _ = match.World.Random.State;
        _ = match.World.Stores.Count;

        Assert.Equal(hash, match.World.ComputeHash());
        Assert.Equal(hash, MatchAfter(40).World.ComputeHash());
    }

    [Fact]
    public void EveryPartOfTheStateIsInTheHash()
    {
        var hashes = new List<ulong> { MatchAfter(40).World.ComputeHash() };

        void Changed(Action<ToyMatch> change)
        {
            var match = MatchAfter(40);
            change(match);
            hashes.Add(match.World.ComputeHash());
        }

        Changed(match => match.Simulation.Tick());
        Changed(match => match.World.Random.NextUInt64());
        Changed(match => match.World.Entities.Create());
        Changed(match => match.World.Entities.Destroy(match.Movers.EntityAt(0)));
        Changed(match =>
        {
            match.World.Entities.Destroy(match.Movers.EntityAt(0));
            match.World.FlushDestroyedEntities();
        });
        Changed(match => match.Movers.Remove(match.Movers.EntityAt(0)));
        Changed(match => match.Movers.ValueAt(0).X += Fixed.Epsilon);
        Changed(match => match.Movers.ValueAt(match.Movers.Count - 1).Y -= Fixed.Epsilon);
        Changed(match => match.Movers.ValueAt(3).Heading += Angle.FromUnits(1));
        Changed(match => match.Movers.ValueAt(3).Owner ^= 1);
        Changed(match => match.Movers.ValueAt(3).Age++);
        Changed(match => match.Scores.Add(0, 1));
        Changed(match => match.Scores.Add(1, 1));
        Changed(match => match.World.AddStore(new ComponentStore<ToyMatch.Mover>("extra")));

        Assert.Equal(hashes.Count, hashes.Distinct().Count());
    }

    [Fact]
    public void WaitingCommandsAreInputAndNotPartOfTheHash()
    {
        var match = MatchAfter(40);
        var hash = match.World.ComputeHash();

        match.Simulation.Commands.Enqueue(45, new Command(ToyMatch.Spawn, 0, targetX: Fixed.One, targetY: Fixed.One));

        Assert.Equal(hash, match.World.ComputeHash());
    }

    [Fact]
    public void SwappingTheValuesOfTwoEntitiesChangesTheHash()
    {
        var match = MatchAfter(40);
        var hash = match.World.ComputeHash();

        (match.Movers.ValueAt(0), match.Movers.ValueAt(1)) = (match.Movers.ValueAt(1), match.Movers.ValueAt(0));

        Assert.NotEqual(hash, match.World.ComputeHash());
    }

    [Fact]
    public void TheSameValueOnADifferentEntityChangesTheHash()
    {
        static ulong HashWithValueOn(int holder)
        {
            var world = new World(0);
            var movers = world.AddStore(new ComponentStore<ToyMatch.Mover>("movers"));
            world.Entities.Create();
            world.Entities.Create();
            movers.Add(new EntityId(holder), new ToyMatch.Mover { Owner = 1 });
            return world.ComputeHash();
        }

        Assert.NotEqual(HashWithValueOn(1), HashWithValueOn(2));
    }

    [Fact]
    public void AFreshWorldHashesTheSameOnEveryRunAndDependsOnTheSeed()
    {
        Assert.Equal(new World(1).ComputeHash(), new World(1).ComputeHash());
        Assert.NotEqual(new World(1).ComputeHash(), new World(2).ComputeHash());
    }

    [Fact]
    public void TheTickNumberAloneChangesTheHash()
    {
        var world = new World(1);
        var before = world.ComputeHash();

        // Nothing is registered, so the tick number is all that changes.
        new Simulation(world).Tick();

        Assert.Equal(1, world.Tick);
        Assert.NotEqual(before, world.ComputeHash());
    }
}
