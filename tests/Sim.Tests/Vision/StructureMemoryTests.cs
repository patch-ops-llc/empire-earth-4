using Sim.Core;
using Sim.Map;
using Sim.Serialization;
using Sim.Vision;

namespace Sim.Tests.Vision;

/// <summary>
/// The store of what each player remembers of the structures of the others: what it keeps,
/// that it is in the state hash and in snapshots, and what it refuses.
/// </summary>
public class StructureMemoryTests
{
    private static RememberedStructure Seen(int entity, int owner = 1, int x = 4, int y = 6, int size = 3) => new()
    {
        Entity = new EntityId(entity),
        Owner = owner,
        Type = 2,
        X = x,
        Y = y,
        Size = size,
        HitPoints = 900_000,
        MaxHitPoints = 1_200_000,
        IsFinished = true,
        Built = 900,
        LookTier = 3,
        Banner = RememberedStructure.NoTrack,
        Tick = 40,
    };

    private static ulong HashOf(StructureMemory memory)
    {
        var world = new World(1);
        world.AddStore(memory);
        return world.ComputeHash();
    }

    private static StructureMemory With(params RememberedStructure[] seen)
    {
        var memory = new StructureMemory(2, 30, 30);
        foreach (var item in seen)
        {
            memory.Set(0, item);
        }

        return memory;
    }

    [Fact]
    public void AStoreStartsWithNothingRemembered()
    {
        var memory = new StructureMemory(3, 30, 30);

        Assert.Equal(3, memory.PlayerCount);
        for (var player = 0; player < 3; player++)
        {
            Assert.Equal(0, memory.Count(player));
            Assert.False(memory.TryGet(player, new EntityId(1), out _));
        }
    }

    [Fact]
    public void MemoriesAreKeptForEachPlayerInAscendingOrderOfTheirEntities()
    {
        var memory = new StructureMemory(3, 30, 30);

        memory.Set(0, Seen(9));
        memory.Set(0, Seen(3));
        memory.Set(0, Seen(12, owner: 2));
        memory.Set(0, Seen(7));
        memory.Set(2, Seen(7, owner: 0));

        Assert.Equal([3, 7, 9, 12], Enumerable.Range(0, memory.Count(0)).Select(i => memory.At(0, i).Entity.Value));
        Assert.Equal(0, memory.Count(1));
        Assert.Equal(1, memory.Count(2));
        Assert.Equal(1, memory.IndexOf(0, new EntityId(7)));
        Assert.Equal(3, memory.IndexOf(0, new EntityId(12)));

        // A structure it does not remember: the complement of the place it would take.
        Assert.Equal(~0, memory.IndexOf(0, new EntityId(1)));
        Assert.Equal(~2, memory.IndexOf(0, new EntityId(8)));
        Assert.Equal(~4, memory.IndexOf(0, new EntityId(40)));
        Assert.True(memory.IndexOf(5, new EntityId(7)) < 0);
        Assert.False(memory.TryGet(1, new EntityId(7), out _));
        Assert.True(memory.TryGet(2, new EntityId(7), out var seen));
        Assert.Equal(0, seen.Owner);
    }

    [Fact]
    public void AStructureSeenAgainIsWrittenOverWhatWasRemembered()
    {
        var memory = With(Seen(3), Seen(7));
        var later = Seen(7) with { HitPoints = 100_000, Tick = 80 };

        memory.Set(0, later);

        Assert.Equal(2, memory.Count(0));
        Assert.True(memory.TryGet(0, new EntityId(7), out var seen));
        Assert.Equal(later, seen);
        Assert.Equal(Seen(3), memory.At(0, 0));
    }

    [Fact]
    public void AMemoryIsForgottenByItsPlace()
    {
        var memory = With(Seen(3), Seen(7), Seen(9));

        memory.RemoveAt(0, 1);

        Assert.Equal([3, 9], Enumerable.Range(0, memory.Count(0)).Select(i => memory.At(0, i).Entity.Value));
        Assert.False(memory.TryGet(0, new EntityId(7), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.RemoveAt(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.At(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.Count(2));

        // The store grows past the room it began with.
        for (var entity = 20; entity < 60; entity++)
        {
            memory.Set(0, Seen(entity));
        }

        Assert.Equal(42, memory.Count(0));
        Assert.Equal(59, memory.At(0, 41).Entity.Value);
    }

    /// <summary>The player does not know that a structure is gone until it looks: the world's removal of entities leaves the memory.</summary>
    [Fact]
    public void AMemoryOutlivesItsStructure()
    {
        var world = new World(1);
        var memory = world.AddStore(new StructureMemory(2, 30, 30));
        var structure = world.Entities.Create();
        memory.Set(0, Seen(structure.Value));
        var before = HashOf(With(Seen(structure.Value)));

        world.Entities.Destroy(structure);
        new Simulation(world).Tick();

        Assert.False(world.Entities.IsAlive(structure));
        Assert.True(memory.TryGet(0, structure, out var seen));
        Assert.Equal(Seen(structure.Value), seen);
        Assert.Equal(before, HashOf(With(Seen(structure.Value))));
    }

    [Fact]
    public void AMemoryThatCannotBeIsRefused()
    {
        var memory = new StructureMemory(2, 30, 30);

        Assert.Throws<ArgumentException>(() => memory.Set(0, Seen(0)));
        Assert.Throws<ArgumentException>(() => memory.Set(0, Seen(3, owner: 0)));
        Assert.Throws<ArgumentException>(() => memory.Set(0, Seen(3, owner: 2)));
        Assert.Throws<ArgumentException>(() => memory.Set(0, Seen(3, owner: -1)));
        Assert.Throws<ArgumentException>(() => memory.Set(0, Seen(3, x: 28)));
        Assert.Throws<ArgumentException>(() => memory.Set(0, Seen(3, y: -1)));
        Assert.Throws<ArgumentException>(() => memory.Set(0, Seen(3, size: 0)));
        Assert.Throws<ArgumentException>(() => memory.Set(0, Seen(3) with { Built = -1 }));
        Assert.Throws<ArgumentException>(() => memory.Set(0, Seen(3) with { Banner = -2 }));
        Assert.Throws<ArgumentException>(() => memory.Set(0, Seen(3) with { MaxHitPoints = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.Set(2, Seen(3)));
        Assert.Equal(0, memory.Count(0));

        // At the edge of the map, and with no hit points left, it can be.
        memory.Set(0, Seen(3, x: 27, y: 27) with { HitPoints = 0 });
        Assert.Equal(new TileSquare(27, 27, 3), memory.At(0, 0).Tiles);
    }

    [Fact]
    public void TheHashCoversEveryFieldOfEveryMemoryOfEveryPlayer()
    {
        var one = Seen(3);
        RememberedStructure[] others =
        [
            one with { Entity = new EntityId(4) },
            one with { Type = 1 },
            one with { X = 5 },
            one with { Y = 5 },
            one with { Size = 2 },
            one with { HitPoints = 900_001 },
            one with { MaxHitPoints = 1_200_001 },
            one with { IsFinished = false },
            one with { Built = 899 },
            one with { LookTier = 4 },
            one with { Banner = 0 },
            one with { Tick = 44 },
        ];
        var hashes = new List<ulong> { HashOf(new StructureMemory(2, 30, 30)), HashOf(With(one)), HashOf(With(one, Seen(7))) };
        hashes.AddRange(others.Select(other => HashOf(With(other))));

        // The same memory of the other player, and of another owner in a match of three.
        var ofTheOther = new StructureMemory(2, 30, 30);
        ofTheOther.Set(1, one with { Owner = 0 });
        hashes.Add(HashOf(ofTheOther));
        var ofThree = new StructureMemory(3, 30, 30);
        ofThree.Set(0, one);
        var ofAnotherOwner = new StructureMemory(3, 30, 30);
        ofAnotherOwner.Set(0, one with { Owner = 2 });
        hashes.Add(HashOf(ofThree));
        hashes.Add(HashOf(ofAnotherOwner));

        Assert.Equal(hashes.Count, hashes.Distinct().Count());
        Assert.Equal(HashOf(With(one)), HashOf(With(one)));
    }

    [Fact]
    public void ASnapshotRestoresWhatEachPlayerRemembered()
    {
        var source = new World(1);
        var memory = source.AddStore(new StructureMemory(2, 30, 30));
        memory.Set(0, Seen(3));
        memory.Set(0, Seen(9) with { IsFinished = false, Built = 12, Banner = 4 });
        memory.Set(1, Seen(5, owner: 0, x: 20, y: 2, size: 2));
        var writer = new ByteWriter();
        Snapshot.Write(source, writer);

        // Into a store that remembers other things, and more of them.
        var target = new World(2);
        var restored = target.AddStore(new StructureMemory(2, 30, 30));
        for (var entity = 1; entity < 30; entity++)
        {
            restored.Set(0, Seen(entity, x: 1, y: 1));
            restored.Set(1, Seen(entity, owner: 0, x: 2, y: 2));
        }

        Snapshot.Restore(new Simulation(target), new ByteReader(writer.ToArray()));

        Assert.Equal(source.ComputeHash(), target.ComputeHash());
        for (var player = 0; player < 2; player++)
        {
            Assert.Equal(memory.Count(player), restored.Count(player));
            for (var i = 0; i < memory.Count(player); i++)
            {
                Assert.Equal(memory.At(player, i), restored.At(player, i));
            }
        }

        // Nothing of what the store held before shows when it grows again.
        restored.Set(0, Seen(4));
        Assert.Equal([3, 4, 9], Enumerable.Range(0, restored.Count(0)).Select(i => restored.At(0, i).Entity.Value));
    }

    /// <summary>A snapshot of a match on another map: its memories have tiles this map has not.</summary>
    [Fact]
    public void ASnapshotWithAMemoryThatCannotBeIsRefused()
    {
        var source = new World(1);
        source.AddStore(new StructureMemory(2, 30, 30)).Set(0, Seen(3, x: 25, y: 25));
        var writer = new ByteWriter();
        Snapshot.Write(source, writer);

        var target = new World(1);
        target.AddStore(new StructureMemory(2, 20, 20));
        var error = Assert.Throws<InvalidDataException>(() => Snapshot.Restore(new Simulation(target), new ByteReader(writer.ToArray())));

        Assert.Contains("tiles that are not on the map", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStoreIsNamedRemembered()
    {
        Assert.Equal("remembered", StructureMemory.StoreName);
        Assert.Equal("remembered", new StructureMemory(1, 1, 1).Name);
    }
}
