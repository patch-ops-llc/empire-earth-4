using Sim.Core;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;

namespace Sim.Tests.Map;

/// <summary>
/// The spatial index is not state: a match that runs on keeps the index its ticks made, and a
/// match restored from a snapshot makes one new from the state. Both must answer alike in every
/// tick, or a rule that reads the index would send the two matches apart. The tick that could
/// tell them apart is the one after an entity was destroyed: the match that ran on still has it
/// filed until the index is next made, and the restored match never filed it.
/// </summary>
/// <remarks>
/// The match here has rules that read the index and write what they read into the state, so that
/// a different answer is a different state hash: a command that reads before any system of its
/// tick, which is the index the tick before left, and a system that reads last in its tick,
/// right after it has destroyed what was due.
/// </remarks>
public class SpatialIndexAfterDeathTests
{
    private const int LastTick = 60;

    /// <summary>The tick in which one unit is destroyed.</summary>
    private const int FirstDeath = 20;

    /// <summary>The tick in which two more are, one of them on its way.</summary>
    private const int SecondDeath = 31;

    /// <summary>A command type no rule uses, for the reader that runs with the commands.</summary>
    private const CommandType ReadTheIndex = (CommandType)60_001;

    private static readonly Fixed QuarterTile = Fixed.One / 4;

    private static readonly FixedVector2 Middle = TestMatches.Point("10.5", "10.5");

    /// <summary>
    /// Seven units of two players round the middle of an open map, one of them sent across it,
    /// with the readers and the deaths described at the class.
    /// </summary>
    private static Match Build() => Build(out _);

    private static Match Build(out Readings readings)
    {
        var match = TestMatches.Create(TestMaps.Open(24, 24));
        var units = new EntityId[7];
        for (var i = 0; i < units.Length; i++)
        {
            units[i] = match.SpawnUnit(i % 2, new FixedVector2(Middle.X + Fixed.FromInt((i % 3) - 1), Middle.Y + Fixed.FromInt((i / 3) - 1)), QuarterTile);
        }

        match.RefreshDerivedState();
        var read = match.World.AddStore(new Readings());
        match.Simulation.SetCommandHandler(ReadTheIndex, new ReadingCommand(match, read));
        match.Simulation.AddSystem(new DeathsAndReading(
            match,
            read,
            [(FirstDeath, units[4]), (SecondDeath, units[0]), (SecondDeath, units[5])]));
        readings = read;
        return match;
    }

    /// <summary>The walker's order, and a reading with the commands of every tick.</summary>
    private static (int Tick, Command Command)[] Script()
    {
        var match = Build();
        var walker = match.Bodies.EntityAt(0);
        var far = TestMatches.Point("20.5", "3.5");
        return
        [
            (0, new Command(CommandType.Move, 0, [walker], far.X, far.Y)),
            .. Enumerable.Range(0, LastTick).Select(tick => (tick, new Command(ReadTheIndex, 0))),
        ];
    }

    private static Match Run(int ticks, out Readings readings)
    {
        var match = Build(out readings);
        foreach (var (tick, command) in Script())
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        match.Simulation.Run(ticks);
        return match;
    }

    /// <summary>
    /// The snapshot is taken before the deaths, in the tick each death is due, in the tick right
    /// after each, which is the one the two matches could differ in, and later.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(FirstDeath)]
    [InlineData(FirstDeath + 1)]
    [InlineData(FirstDeath + 2)]
    [InlineData(SecondDeath)]
    [InlineData(SecondDeath + 1)]
    [InlineData(SecondDeath + 2)]
    [InlineData(45)]
    [InlineData(LastTick)]
    public void AMatchWhoseRulesReadTheIndexRestoresAndContinuesFromAnyTick(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(Build, Script(), snapshotTick, LastTick);

    /// <summary>The run does what the test above says it does, so that the test means something.</summary>
    [Fact]
    public void InThatMatchTheReadersReadInEveryTickAndTheDeathsComeWhenTheyAreDue()
    {
        var match = Run(LastTick, out var readings);

        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal(4, match.Bodies.Count);
        Assert.Equal(2 * LastTick, readings.Seen.Count);

        // With the commands of a tick, the index is as the tick before left it: the unit that
        // was destroyed in that tick is counted no more and is in no answer.
        Assert.Equal(7, readings.WithTheCommandsOf(FirstDeath).Count);
        Assert.Equal(6, readings.WithTheCommandsOf(FirstDeath + 1).Count);
        Assert.Equal(6, readings.WithTheCommandsOf(FirstDeath + 1).Listed);
        Assert.Equal(6, readings.WithTheCommandsOf(SecondDeath).Count);
        Assert.Equal(4, readings.WithTheCommandsOf(SecondDeath + 1).Count);
        Assert.Equal(4, readings.WithTheCommandsOf(SecondDeath + 1).Listed);

        // What is destroyed is out of the count and of every answer from that moment, in the
        // same tick, although its values stay in the stores until the tick ends.
        Assert.Equal((7, 7), (readings.AtTheEndOf(FirstDeath - 1).Count, readings.AtTheEndOf(FirstDeath - 1).Listed));
        Assert.Equal((6, 6), (readings.AtTheEndOf(FirstDeath).Count, readings.AtTheEndOf(FirstDeath).Listed));
        Assert.Equal((4, 4), (readings.AtTheEndOf(SecondDeath).Count, readings.AtTheEndOf(SecondDeath).Listed));

        // The walker moves, so what is read near the middle changes while it leaves.
        Assert.True(readings.AtTheEndOf(0).Near > readings.AtTheEndOf(FirstDeath - 1).Near);
    }

    /// <summary>
    /// The same without a snapshot test between: after a tick with a death, the index of the
    /// match that ran on and the index of a match restored from it give the same count and the
    /// same answer for every part of the map.
    /// </summary>
    [Theory]
    [InlineData(FirstDeath + 1)]
    [InlineData(SecondDeath + 1)]
    public void InTheTickAfterADeathTheIndexAnswersAsTheIndexOfARestoredMatch(int tick)
    {
        var live = Run(tick, out _);
        var writer = new Sim.Serialization.ByteWriter();
        Sim.Serialization.Snapshot.Write(live.World, writer);
        var restored = Build();
        Sim.Serialization.Snapshot.Restore(restored, new Sim.Serialization.ByteReader(writer.ToArray()));

        Assert.Equal(live.Bodies.Count, live.SpatialIndex.Count);
        Assert.Equal(restored.SpatialIndex.Count, live.SpatialIndex.Count);
        var here = new List<EntityId>();
        var there = new List<EntityId>();
        for (var y = 0; y < live.Grid.Height; y += 2)
        {
            for (var x = 0; x < live.Grid.Width; x += 2)
            {
                var min = new FixedVector2(Fixed.FromInt(x), Fixed.FromInt(y));
                var max = new FixedVector2(Fixed.FromInt(x + 2), Fixed.FromInt(y + 2));
                live.SpatialIndex.QueryBox(min, max, here);
                restored.SpatialIndex.QueryBox(min, max, there);
                Assert.Equal(here, there);
            }
        }
    }

    /// <summary>What a reader got from the index. It is kept for the tests and is not state.</summary>
    private readonly record struct Reading(int Tick, bool WithTheCommands, int Count, int Listed, int Near);

    /// <summary>
    /// What the readers have read, folded into one number that is state: a reader that got a
    /// different answer leaves a different number, and so a different state hash.
    /// </summary>
    private sealed class Readings : IStateStore
    {
        private long _folded;

        public string Name => "indexreadings";

        public List<Reading> Seen { get; } = [];

        public Reading WithTheCommandsOf(int tick) => Seen.Single(reading => reading.Tick == tick && reading.WithTheCommands);

        public Reading AtTheEndOf(int tick) => Seen.Single(reading => reading.Tick == tick && !reading.WithTheCommands);

        /// <summary>Reads the count, the whole map and a circle round the middle, and folds every answer in.</summary>
        public void Read(Match match, bool withTheCommands, List<EntityId> found)
        {
            var index = match.SpatialIndex;
            Fold(index.Count);
            index.QueryBox(default, new FixedVector2(Fixed.FromInt(match.Grid.Width), Fixed.FromInt(match.Grid.Height)), found);
            var listed = found.Count;
            Fold(found);
            index.QueryCircle(Middle, Fixed.FromInt(2), found);
            Fold(found);
            Seen.Add(new Reading(match.World.Tick, withTheCommands, index.Count, listed, found.Count));
        }

        public void RemoveEntities(ReadOnlySpan<EntityId> destroyed)
        {
        }

        public void Walk(IStateWalker walker) => _folded = walker.Field(_folded);

        private void Fold(List<EntityId> found)
        {
            Fold(found.Count);
            foreach (var entity in found)
            {
                Fold(entity.Value);
            }
        }

        private void Fold(int value) => _folded = unchecked((_folded * 1_000_003) + value);
    }

    /// <summary>The reader that runs with the commands of a tick, before any system of it.</summary>
    private sealed class ReadingCommand(Match match, Readings readings) : ICommandHandler
    {
        private readonly List<EntityId> _found = [];

        public bool Execute(World world, in Command command)
        {
            readings.Read(match, withTheCommands: true, _found);
            return true;
        }
    }

    /// <summary>
    /// The last system of the tick: it destroys what is due, as the system that settles deaths
    /// does once the index of the tick has been made, and then reads the index.
    /// </summary>
    private sealed class DeathsAndReading(Match match, Readings readings, (int Tick, EntityId Unit)[] deaths) : ISystem
    {
        private readonly List<EntityId> _found = [];

        public void Run(World world)
        {
            foreach (var (tick, unit) in deaths)
            {
                if (tick == world.Tick)
                {
                    world.Entities.Destroy(unit);
                }
            }

            readings.Read(match, withTheCommands: false, _found);
        }
    }
}
