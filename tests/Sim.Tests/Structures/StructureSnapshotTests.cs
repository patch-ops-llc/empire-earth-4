using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Serialization;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using static Sim.Tests.Structures.StructureMatches;

namespace Sim.Tests.Structures;

/// <summary>
/// The tiles under structures are worked out from the state, so a match restored from a snapshot
/// has them without their being in the snapshot (plan 15b: a restore builds the same grid).
/// </summary>
public class StructureSnapshotTests
{
    private static MapDef TwoGaps => TestMaps.FromRows(
        "....................",
        "....................",
        "....................",
        "####..########..####",
        "####..########..####",
        "....................",
        "....................",
        "....................");

    private static Match Build()
    {
        var match = Create(TwoGaps);
        match.SpawnUnit(0, MapGrid.TileCenter(1, 0), QuarterTile);
        match.SpawnUnit(1, MapGrid.TileCenter(18, 7), QuarterTile);
        match.PlaceStructure(1, "generator", 4, 3);
        match.PlaceStructure(0, "capitol", 8, 5);
        match.RefreshDerivedState();
        return match;
    }

    [Fact]
    public void AMatchWithStructuresRestoresAndContinues()
    {
        var first = new EntityId(1);
        var second = new EntityId(2);
        (int Tick, Command Command)[] script =
        [
            (0, new Command(CommandType.Move, 0, [first], MapGrid.TileCenter(1, 7).X, MapGrid.TileCenter(1, 7).Y)),
            (3, new Command(CommandType.Move, 1, [second], MapGrid.TileCenter(2, 0).X, MapGrid.TileCenter(2, 0).Y)),
            (60, new Command(CommandType.Move, 0, [first], MapGrid.TileCenter(19, 0).X, MapGrid.TileCenter(19, 0).Y)),
        ];

        foreach (var snapshotTick in new[] { 0, 1, 20, 61, 100 })
        {
            SnapshotAssert.RestoresAndContinues(Build, script, snapshotTick, lastTick: 160);
        }
    }

    /// <summary>
    /// The match the snapshot goes into has other structures, so other tiles and regions, than the
    /// one it was taken from. After the restore it has that one's, and walks on as that one does.
    /// </summary>
    [Fact]
    public void ARestoreBuildsTheGridOfTheStructuresInTheSnapshot()
    {
        var source = Build();
        var unit = new EntityId(1);
        source.OrderMove(0, MapGrid.TileCenter(1, 7), unit);
        source.Simulation.Run(10);
        source.PlaceStructure(0, "generator", 14, 3);
        source.Raze(source.StructuresOf(0)[0]);
        source.Simulation.Run(10);
        Assert.Equal(2, source.Grid.RegionCount);
        var snapshot = new ByteWriter();
        Snapshot.Write(source.World, snapshot);

        var target = Build();
        target.PlaceStructure(0, "generator", 0, 5);
        target.Simulation.Run(3);
        Assert.Equal(1, target.Grid.RegionCount);
        target.Routes.Get(0, 1);

        Snapshot.Restore(target, new ByteReader(snapshot.WrittenSpan.ToArray()));

        SnapshotAssert.AssertSameDerivedState(source, target, "The restored match");
        Assert.Equal(source.World.ComputeHash(), target.World.ComputeHash());
        Assert.True(target.Grid.IsPassable(0, 5));
        Assert.False(target.Grid.IsPassable(14, 3));
        Assert.True(target.Grid.IsPassable(8, 5));
        for (var tick = 0; tick < 120; tick++)
        {
            source.Simulation.Tick();
            target.Simulation.Tick();
            Assert.Equal(source.World.ComputeHash(), target.World.ComputeHash());
        }
    }

    [Fact]
    public void TheStructuresAreInTheStateHash()
    {
        var with = Build();
        var without = Build();
        without.Raze(without.StructuresOf(1)[0]);

        Assert.Equal(with.World.ComputeHash(), Build().World.ComputeHash());
        with.Simulation.Run(2);
        without.Simulation.Run(2);
        Assert.NotEqual(with.World.ComputeHash(), without.World.ComputeHash());
        Assert.Contains(with.World.Stores, store => ReferenceEquals(store, with.Structures) && store.Name == "structures");
    }
}
