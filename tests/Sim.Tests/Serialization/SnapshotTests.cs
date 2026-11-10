using Sim.Core;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Tests.Core;
using Xunit.Sdk;

namespace Sim.Tests.Serialization;

public class SnapshotTests
{
    private const ulong Seed = DeterminismTests.Seed;
    private const int Ticks = DeterminismTests.Ticks;

    public static TheoryData<int> SnapshotTicks => [0, 1, 2, 57, 299, 300, 599, 600];

    /// <summary>
    /// The test the plan asks of every system, here for the kernel and the toy match: snapshot,
    /// restore and continue matches the run that was never interrupted.
    /// </summary>
    [Theory]
    [MemberData(nameof(SnapshotTicks))]
    public void TheToyMatchRestoresAndContinuesFromAnyTick(int snapshotTick)
    {
        SnapshotAssert.RestoresAndContinues(() => new ToyMatch(Seed).Simulation, ToyMatch.Script(Ticks), snapshotTick, Ticks);
    }

    [Fact]
    public void ARestoredToyMatchEndsOnTheRecordedHash()
    {
        var source = new ToyMatch(Seed);
        var script = ToyMatch.Script(Ticks);
        source.Play(250, script);
        var snapshot = SnapshotOf(source.World);

        var restored = new ToyMatch(Seed + 99);
        Snapshot.Restore(restored.Simulation, new ByteReader(snapshot));
        var hashes = restored.Play(Ticks - 250, script.Where(entry => entry.Tick >= 250));

        Assert.Equal(DeterminismTests.ExpectedFinalHash, hashes[^1]);
        Assert.Equal(Ticks, restored.World.Tick);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(40)]
    public void AWorldWithStoresOfChangingSizeRestoresAndContinues(int snapshotTick)
    {
        SnapshotAssert.RestoresAndContinues(() => new TinyGame().Simulation, snapshotTick, lastTick: 90);
    }

    /// <summary>
    /// The layout of a snapshot, byte by byte, for a world small enough to write out. The last
    /// eight bytes are the state hash of that world.
    /// </summary>
    [Fact]
    public void ASnapshotOfASmallWorldHasItsRecordedBytes()
    {
        var world = new World(0x0102030405060708);
        var health = world.AddStore(new ComponentStore<Health>("hp"));
        world.Entities.Create();
        health.Add(world.Entities.Create(), new Health { Points = 77 });
        new Simulation(world).Run(3);

        const string expected =
            "534E4150" + // SNAP
            "0100" + // layout version
            "01000000" + "02000000" + "6870" + // one store, named hp
            "03000000" + // tick
            "0807060504030201" + // random generator
            "03000000" + // next entity ID
            "02000000" + "01000000" + "00" + "02000000" + "00" + // two entities, neither destroyed
            "01000000" + // one store
            "01000000" + "02000000" + "4D000000" + // hp: one value, entity 2, 77 points
            "738173BEDC52E49B"; // state hash
        Assert.Equal(expected, Convert.ToHexString(SnapshotOf(world)));
        Assert.Equal(0x9BE452DCBE738173, world.ComputeHash());
        Assert.Equal(1, Snapshot.Version);
    }

    [Fact]
    public void AnEmptyWorldRestores()
    {
        var snapshot = SnapshotOf(new World(9));

        var simulation = new Simulation(new World(1));
        Snapshot.Restore(simulation, new ByteReader(snapshot));

        Assert.Equal(new World(9).ComputeHash(), simulation.World.ComputeHash());
        Assert.Equal(9UL, simulation.World.Random.State);
    }

    [Fact]
    public void ASnapshotCanSitBetweenOtherValues()
    {
        var match = new ToyMatch(Seed);
        match.Simulation.Run(30);
        var writer = new ByteWriter();
        writer.WriteInt32(123);
        var start = writer.Length;
        Snapshot.Write(match.World, writer);
        var end = writer.Length;
        writer.WriteInt32(456);

        var reader = new ByteReader(writer.ToArray());
        Assert.Equal(123, reader.ReadInt32());
        var restored = new ToyMatch(Seed);
        Snapshot.Restore(restored.Simulation, reader);

        Assert.Equal(end, reader.Position);
        Assert.True(end - start > 100);
        Assert.Equal(456, reader.ReadInt32());
        Assert.Equal(match.World.ComputeHash(), restored.World.ComputeHash());
    }

    [Fact]
    public void RestoringDiscardsWaitingCommandsAndMovesTheQueueToTheSnapshotsTick()
    {
        var source = new ToyMatch(Seed);
        source.Simulation.Commands.Enqueue(45, new Command(ToyMatch.Spawn, 0, targetX: Fixed.One, targetY: Fixed.One));
        source.Simulation.Run(40);
        var snapshot = SnapshotOf(source.World);

        var target = new ToyMatch(Seed);
        target.Simulation.Commands.Enqueue(3, new Command(ToyMatch.Spawn, 1, targetX: Fixed.One, targetY: Fixed.One));
        target.Simulation.Commands.Enqueue(70, new Command(ToyMatch.Spawn, 1, targetX: Fixed.One, targetY: Fixed.One));
        target.Simulation.Run(2);
        Snapshot.Restore(target.Simulation, new ByteReader(snapshot));

        // The command the source was still waiting for is not in the snapshot either.
        Assert.Equal(0, target.Simulation.Commands.Count);
        Assert.Equal(40, target.Simulation.Commands.NextTick);
        Assert.Throws<ArgumentOutOfRangeException>(() => target.Simulation.Commands.Enqueue(39, default));
        target.Simulation.Commands.Enqueue(40, new Command(ToyMatch.Spawn, 0, targetX: Fixed.One, targetY: Fixed.One));
        target.Simulation.Tick();
        Assert.Equal(41, target.World.Tick);
    }

    [Fact]
    public void EntitiesDestroyedButNotYetRemovedStaySoThroughASnapshot()
    {
        var source = new TinyGame();
        source.Simulation.Run(5);
        var doomed = source.World.Entities[1];
        source.World.Entities.Destroy(doomed);
        var snapshot = SnapshotOf(source.World);

        var restored = new TinyGame();
        Snapshot.Restore(restored.Simulation, new ByteReader(snapshot));

        Assert.False(restored.World.Entities.IsAlive(doomed));
        Assert.Equal(source.World.Entities.Count, restored.World.Entities.Count);
        Assert.Equal(source.World.Entities.AliveCount, restored.World.Entities.AliveCount);
        Assert.True(restored.Health.Contains(doomed));

        source.World.FlushDestroyedEntities();
        restored.World.FlushDestroyedEntities();
        Assert.False(restored.Health.Contains(doomed));
        Assert.Equal(source.World.ComputeHash(), restored.World.ComputeHash());
    }

    [Fact]
    public void RestoringForgetsEntitiesTheWorldWasWaitingToRemove()
    {
        var source = new TinyGame();
        source.Simulation.Run(5);
        var snapshot = SnapshotOf(source.World);

        var target = new TinyGame();
        target.Simulation.Run(5);
        target.World.Entities.Destroy(target.World.Entities[0]);
        target.World.Entities.Destroy(target.World.Entities[2]);
        Snapshot.Restore(target.Simulation, new ByteReader(snapshot));

        Assert.Equal(target.World.Entities.Count, target.World.Entities.AliveCount);
        target.Simulation.Run(20);
        source.Simulation.Run(20);
        Assert.Equal(source.World.ComputeHash(), target.World.ComputeHash());
    }

    [Fact]
    public void AWorldLargerThanANewWorldsArraysRestores()
    {
        var source = new World(3);
        var sourceHealth = source.AddStore(new ComponentStore<Health>("hp"));
        for (var i = 0; i < 500; i++)
        {
            var entity = source.Entities.Create();
            if (i % 2 == 0)
            {
                sourceHealth.Add(entity, new Health { Points = i });
            }
        }

        var target = new World(3);
        var targetHealth = target.AddStore(new ComponentStore<Health>("hp"));
        Snapshot.Restore(new Simulation(target), new ByteReader(SnapshotOf(source)));

        Assert.Equal(500, target.Entities.Count);
        Assert.Equal(250, targetHealth.Count);
        Assert.Equal(498, targetHealth.Get(new EntityId(499)).Points);
        Assert.True(target.Entities.IsAlive(new EntityId(500)));
        Assert.Equal(source.ComputeHash(), target.ComputeHash());
        Assert.Equal(new EntityId(501), target.Entities.Create());
    }

    /// <summary>
    /// Whatever a store held before a restore is gone after it, even in a field its walk leaves
    /// out. The restored world then behaves the same whatever it was restored into, and the missing
    /// field shows up in the round-trip test and not as a difference between two machines.
    /// </summary>
    [Fact]
    public void ARestoredValueKeepsNothingOfWhatItsPlaceHeldBefore()
    {
        var source = new World(1);
        source.AddStore(new ComponentStore<Forgetful>("forgetful")).Add(source.Entities.Create(), new Forgetful { Total = 7, Step = 3 });

        var target = new World(1);
        var store = target.AddStore(new ComponentStore<Forgetful>("forgetful"));
        store.Add(target.Entities.Create(), new Forgetful { Total = 100, Step = 9 });
        store.Add(target.Entities.Create(), new Forgetful { Total = 200, Step = 9 });
        Snapshot.Restore(new Simulation(target), new ByteReader(SnapshotOf(source)));

        Assert.Equal(1, store.Count);
        Assert.Equal(7, store.ValueAt(0).Total);
        Assert.Equal(0, store.ValueAt(0).Step);
    }

    /// <summary>
    /// A count is checked against the bytes that are left using the smallest size an item can
    /// have, so a snapshot cannot make the kernel set aside more memory than its own size.
    /// </summary>
    [Fact]
    public void ACountLargerThanItsItemsCouldFillIsRejectedBeforeMemoryIsSetAside()
    {
        const int Claimed = 4_000_000;
        var writer = new ByteWriter();
        writer.WriteBytes("SNAP"u8);
        writer.WriteUInt16(Snapshot.Version);
        writer.WriteCount(1);
        writer.WriteString("hp");
        writer.WriteInt32(0);
        writer.WriteUInt64(1);
        writer.WriteInt32(Claimed + 1);
        writer.WriteCount(Claimed);
        writer.WriteBytes(new byte[Claimed + 100]);
        var snapshot = writer.ToArray();
        var world = new World(1);
        world.AddStore(new ComponentStore<Health>("hp"));
        var simulation = new Simulation(world);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => Snapshot.Restore(simulation, new ByteReader(snapshot)));
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 256 * 1024);
    }

    [Fact]
    public void ASnapshotCutShortAnywhereIsRejected()
    {
        var source = new TinyGame();
        source.Simulation.Run(7);
        var snapshot = SnapshotOf(source.World);

        for (var length = 0; length < snapshot.Length; length++)
        {
            var reader = new ByteReader(snapshot.AsMemory(0, length));
            Assert.Throws<InvalidDataException>(() => Snapshot.Restore(new TinyGame().Simulation, reader));
        }
    }

    /// <summary>
    /// The hash at the end of a snapshot covers every field, so damage anywhere is noticed and a
    /// damaged snapshot is never restored as if it were good.
    /// </summary>
    [Fact]
    public void ASnapshotWithAnyOneByteChangedIsRejected()
    {
        var source = new ToyMatch(Seed);
        source.Play(40, ToyMatch.Script(40));
        var snapshot = SnapshotOf(source.World);
        Assert.InRange(snapshot.Length, 200, 2000);

        foreach (var mask in (byte[])[0x01, 0x80, 0xFF])
        {
            for (var i = 0; i < snapshot.Length; i++)
            {
                var damaged = (byte[])snapshot.Clone();
                damaged[i] ^= mask;
                var target = new ToyMatch(Seed);
                Assert.Throws<InvalidDataException>(() => Snapshot.Restore(target.Simulation, new ByteReader(damaged)));
            }
        }
    }

    [Fact]
    public void ASnapshotOnlyRestoresIntoAWorldWithTheSameStores()
    {
        var source = new World(1);
        source.AddStore(new ComponentStore<Health>("armor"));
        source.AddStore(new ComponentStore<Health>("health"));
        var snapshot = SnapshotOf(source);

        void Rejects(params string[] storeNames)
        {
            var world = new World(1);
            foreach (var name in storeNames)
            {
                world.AddStore(new ComponentStore<Health>(name));
            }

            Assert.Throws<InvalidDataException>(() => Snapshot.Restore(new Simulation(world), new ByteReader(snapshot)));
        }

        Rejects();
        Rejects("health");
        Rejects("armor", "health", "shield");
        Rejects("armor", "stamina");
        Rejects("Armor", "health");

        var same = new World(1);
        same.AddStore(new ComponentStore<Health>("health"));
        same.AddStore(new ComponentStore<Health>("armor"));
        Snapshot.Restore(new Simulation(same), new ByteReader(snapshot));
    }

    [Fact]
    public void DataThatIsNotASnapshotOfThisLayoutIsRejected()
    {
        var snapshot = SnapshotOf(new World(1));

        var otherLetters = (byte[])snapshot.Clone();
        otherLetters[3] = (byte)'Q';
        var otherVersion = (byte[])snapshot.Clone();
        otherVersion[4] = 2;

        Assert.Throws<InvalidDataException>(() => Snapshot.Restore(new Simulation(new World(1)), new ByteReader(otherLetters)));
        Assert.Throws<InvalidDataException>(() => Snapshot.Restore(new Simulation(new World(1)), new ByteReader(otherVersion)));
        Assert.Throws<InvalidDataException>(() => Snapshot.Restore(new Simulation(new World(1)), new ByteReader(new byte[64])));
    }

    public static TheoryData<string, int, int, int[], bool[], int[]> StatesNoWorldCanBeIn => new()
    {
        { "a negative tick", -1, 3, [1, 2], [false, false], [2] },
        { "entity IDs that descend", 0, 3, [2, 1], [false, false], [2] },
        { "an entity ID twice", 0, 3, [2, 2], [false, false], [2] },
        { "an entity ID of zero", 0, 3, [0, 2], [false, false], [2] },
        { "a negative entity ID", 0, 3, [-5, 2], [false, false], [2] },
        { "a next ID that an entity already has", 0, 2, [1, 2], [false, false], [2] },
        { "a next ID of zero in an empty world", 0, 0, [], [], [] },
        { "store IDs that descend", 0, 3, [1, 2], [false, false], [2, 1] },
        { "a store ID twice", 0, 3, [1, 2], [false, false], [1, 1] },
        { "a store ID of zero", 0, 3, [1, 2], [false, false], [0] },
    };

    /// <summary>
    /// These pass every check of the byte format and carry the right hash for what they say, so
    /// only the kernel's own checks stand between them and a world whose lookups no longer work.
    /// </summary>
    [Theory]
    [MemberData(nameof(StatesNoWorldCanBeIn))]
    public void AStateNoWorldCanBeInIsRejected(string what, int tick, int nextId, int[] ids, bool[] destroyed, int[] storeIds)
    {
        var snapshot = HandWritten(tick, nextId, ids, destroyed, storeIds);
        var world = new World(1);
        world.AddStore(new ComponentStore<Health>("hp"));

        var exception = Assert.Throws<InvalidDataException>(() => Snapshot.Restore(new Simulation(world), new ByteReader(snapshot)));
        Assert.False(exception.Message.Contains("hashes to", StringComparison.Ordinal), $"{what} was only caught by the hash: {exception.Message}");
    }

    [Fact]
    public void TheHandWrittenSnapshotIsGoodWhenItsStateIs()
    {
        var snapshot = HandWritten(tick: 4, nextId: 3, ids: [1, 2], destroyed: [false, true], storeIds: [1, 2]);
        var world = new World(1);
        var health = world.AddStore(new ComponentStore<Health>("hp"));

        Snapshot.Restore(new Simulation(world), new ByteReader(snapshot));

        Assert.Equal(4, world.Tick);
        Assert.Equal(2, world.Entities.Count);
        Assert.Equal(1, world.Entities.AliveCount);
        Assert.Equal(11, health.Get(new EntityId(2)).Points);
        Assert.Equal(new EntityId(3), world.Entities.Create());
    }

    /// <summary>
    /// A walk that hands a field over without assigning the result back hashes and writes
    /// correctly and restores nothing. The hash at the end of the snapshot catches it at once.
    /// </summary>
    [Fact]
    public void AWalkThatDoesNotAssignTheResultBackCannotBeRestored()
    {
        var source = new World(1);
        source.AddStore(new ComponentStore<Careless>("careless")).Add(source.Entities.Create(), new Careless { Points = 5 });
        var snapshot = SnapshotOf(source);

        var target = new World(1);
        target.AddStore(new ComponentStore<Careless>("careless"));
        var exception = Assert.Throws<InvalidDataException>(() => Snapshot.Restore(new Simulation(target), new ByteReader(snapshot)));

        Assert.Contains("Walk", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A field left out of the walk is in neither the hash nor the snapshot, so nothing notices
    /// until the restored run goes its own way. That is what the round-trip test is for.
    /// </summary>
    [Fact]
    public void AFieldLeftOutOfTheWalkIsCaughtByTheRoundTripTest()
    {
        static Simulation Build()
        {
            var world = new World(1);
            var store = world.AddStore(new ComponentStore<Forgetful>("forgetful"));
            store.Add(world.Entities.Create(), default);
            var simulation = new Simulation(world);
            simulation.AddSystem(new ForgetfulSystem(store));
            return simulation;
        }

        var exception = Assert.ThrowsAny<XunitException>(() => SnapshotAssert.RestoresAndContinues(Build, snapshotTick: 10, lastTick: 20));

        Assert.Contains("missing from a store's Walk", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWalkTellsAWalkerTheNameOfEveryField()
    {
        var match = new ToyMatch(Seed);
        match.Simulation.Run(8);
        var recorder = new RecordingWalker();

        match.World.Walk(recorder);

        Assert.DoesNotContain(recorder.Names, string.IsNullOrWhiteSpace);
        Assert.Equal("Tick", recorder.Names[0]);
        Assert.Equal(["Owner", "X", "Y", "Heading", "Speed", "Age"], recorder.Names.Where(name => !name.Contains('_', StringComparison.Ordinal)).Skip(2).Take(6));
        Assert.Equal(match.World.ComputeHash(), recorder.Hash);
    }

    [Fact]
    public void WritingAndRestoringNeedTheirArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Snapshot.Write(null!, new ByteWriter()));
        Assert.Throws<ArgumentNullException>(() => Snapshot.Write(new World(1), null!));
        Assert.Throws<ArgumentNullException>(() => Snapshot.Restore(null!, new ByteReader(ReadOnlyMemory<byte>.Empty)));
        Assert.Throws<ArgumentNullException>(() => Snapshot.Restore(new Simulation(new World(1)), null!));
        Assert.Throws<ArgumentNullException>(() => new World(1).Walk(null!));
    }

    private static byte[] SnapshotOf(World world)
    {
        var writer = new ByteWriter();
        Snapshot.Write(world, writer);
        return writer.ToArray();
    }

    /// <summary>
    /// A snapshot of a world with one store, "hp", written field by field, with the hash its
    /// fields have. Entity <c>n</c> in the store has <c>10 + position</c> points.
    /// </summary>
    private static byte[] HandWritten(int tick, int nextId, int[] ids, bool[] destroyed, int[] storeIds)
    {
        var writer = new ByteWriter();
        var hasher = default(StateHasher);

        void Number(int value)
        {
            writer.WriteInt32(value);
            hasher.Add(value);
        }

        writer.WriteBytes("SNAP"u8);
        writer.WriteUInt16(Snapshot.Version);
        writer.WriteCount(1);
        writer.WriteString("hp");

        Number(tick);
        writer.WriteUInt64(1);
        hasher.Add(1UL);
        Number(nextId);
        Number(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            Number(ids[i]);
            writer.WriteBool(destroyed[i]);
            hasher.Add(destroyed[i]);
        }

        Number(1);
        Number(storeIds.Length);
        for (var i = 0; i < storeIds.Length; i++)
        {
            Number(storeIds[i]);
            Number(10 + i);
        }

        writer.WriteUInt64(hasher.Finish());
        return writer.ToArray();
    }

    private struct Health : IStateValue
    {
        public int Points;

        public void Walk(IStateWalker walker) => Points = walker.Field(Points);
    }

    private struct Careless : IStateValue
    {
        public int Points;

        public readonly void Walk(IStateWalker walker) => _ = walker.Field(Points);
    }

    private struct Forgetful : IStateValue
    {
        public int Total;
        public int Step;

        public void Walk(IStateWalker walker) => Total = walker.Field(Total);
    }

    private sealed class ForgetfulSystem(ComponentStore<Forgetful> store) : ISystem
    {
        public void Run(World world)
        {
            ref var value = ref store.ValueAt(0);
            value.Step++;
            value.Total += value.Step;
        }
    }

    /// <summary>
    /// A world whose stores grow and shrink: a health value per entity, and a ledger that belongs to
    /// no entity and holds a number of entries that changes.
    /// </summary>
    private sealed class TinyGame
    {
        public TinyGame()
        {
            World = new World(11);
            Health = World.AddStore(new ComponentStore<Health>("health"));
            Ledger = World.AddStore(new LedgerStore());
            Simulation = new Simulation(World);
            Simulation.AddSystem(new TinySystem(this));
        }

        public World World { get; }

        public Simulation Simulation { get; }

        public ComponentStore<Health> Health { get; }

        public LedgerStore Ledger { get; }

        private sealed class TinySystem(TinyGame game) : ISystem
        {
            public void Run(World world)
            {
                var born = world.Random.NextInt(1, 4);
                for (var i = 0; i < born; i++)
                {
                    game.Health.Add(world.Entities.Create(), new Health { Points = world.Random.NextInt(50) + game.Ledger.Total });
                }

                if (world.Tick % 3 == 2)
                {
                    game.Ledger.Record(game.Health.ValueAt(0).Points);
                    world.Entities.Destroy(game.Health.EntityAt(0));
                }

                if (world.Tick % 20 == 19)
                {
                    // Nearly everything goes at once, so the stores shrink as well as grow.
                    for (var i = 2; i < game.Health.Count; i++)
                    {
                        world.Entities.Destroy(game.Health.EntityAt(i));
                    }

                    game.Ledger.Forget(keep: 2);
                }
            }
        }
    }

    /// <summary>A store that belongs to no entity, with a count that changes: the pattern for such stores.</summary>
    private sealed class LedgerStore : IStateStore
    {
        private readonly List<long> _entries = [];

        public string Name => "ledger";

        public int Total { get; private set; }

        public void Record(int points)
        {
            _entries.Add(points);
            Total = checked(Total + points) % 1000;
        }

        public void Forget(int keep)
        {
            if (_entries.Count > keep)
            {
                _entries.RemoveRange(keep, _entries.Count - keep);
            }
        }

        public void RemoveEntities(ReadOnlySpan<EntityId> destroyed)
        {
        }

        public void Walk(IStateWalker walker)
        {
            Total = walker.Field(Total);

            var count = walker.Count(_entries.Count, minimumBytesPerItem: sizeof(long));
            if (walker.IsRestoring)
            {
                _entries.Clear();
                _entries.AddRange(new long[count]);
            }

            for (var i = 0; i < count; i++)
            {
                _entries[i] = walker.Field(_entries[i]);
            }
        }
    }

    /// <summary>A walker written outside the kernel, as a tool that shows or compares states would write one.</summary>
    private sealed class RecordingWalker : IStateWalker
    {
        private StateHasher _hasher;

        public List<string> Names { get; } = [];

        public ulong Hash => _hasher.Finish();

        public bool IsRestoring => false;

        public bool Field(bool value, string name = "")
        {
            Names.Add(name);
            _hasher.Add(value);
            return value;
        }

        public int Field(int value, string name = "")
        {
            Names.Add(name);
            _hasher.Add(value);
            return value;
        }

        public uint Field(uint value, string name = "")
        {
            Names.Add(name);
            _hasher.Add(value);
            return value;
        }

        public long Field(long value, string name = "")
        {
            Names.Add(name);
            _hasher.Add(value);
            return value;
        }

        public ulong Field(ulong value, string name = "")
        {
            Names.Add(name);
            _hasher.Add(value);
            return value;
        }

        public Int128 Field(Int128 value, string name = "")
        {
            Names.Add(name);
            _hasher.Add(value);
            return value;
        }

        public EntityId Field(EntityId value, string name = "")
        {
            Names.Add(name);
            _hasher.Add(value);
            return value;
        }

        public Fixed Field(Fixed value, string name = "")
        {
            Names.Add(name);
            _hasher.Add(value);
            return value;
        }

        public Angle Field(Angle value, string name = "")
        {
            Names.Add(name);
            _hasher.Add(value);
            return value;
        }

        public Fraction Field(Fraction value, string name = "")
        {
            Names.Add(name);
            _hasher.Add(value);
            return value;
        }

        public int Count(int count, int minimumBytesPerItem = 1, string name = "")
        {
            Names.Add(name);
            _hasher.Add(count);
            return count;
        }
    }
}
