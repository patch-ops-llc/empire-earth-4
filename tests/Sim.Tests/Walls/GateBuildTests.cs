using Sim.Combat;
using Sim.Construction;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Data;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Tests.Structures;
using static Sim.Tests.Construction.ConstructionMatches;

namespace Sim.Tests.Walls;

/// <summary>
/// Building a gate (design 2.5; design 8.4, rows 11, 22 and 30): a gate is placed on a free tile
/// with the build order, like any structure, for 40 Materials, and builds itself in 10 seconds.
/// While it does, it is a wall tile to everyone.
/// </summary>
/// <remarks>
/// The matches are those of the tests of construction: player 0 has a finished Capitol on the
/// tiles (6, 10) to (7, 11) and 1,000 of each resource, and its territory is the tiles within 6
/// of the point (7, 11).
/// </remarks>
public class GateBuildTests
{
    private const int GateTicks = 200;

    private static int Gate(Match match) => match.Data.Gate!.Index;

    [Fact]
    public void AGateIsBuiltByABuildOrderForItsOwnPriceInItsOwnTime()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 8);
        match.OrderBuild(0, "gate", 10, 10, worker);

        match.Simulation.Tick();

        // Charged when the order is given, as for every structure.
        Assert.Equal((960, 1000), match.StockOf(0));
        Assert.Equal(
            new BuildOrder { Type = Gate(match), X = 10, Y = 10, Size = 1, Materials = 40, Energy = 0 },
            match.BuildOrders.Get(worker));
        Assert.Empty(match.EventsOf(SimEventKind.BuildRefused));

        var laidIn = match.RunUntilOrderEnds(worker);
        var gate = match.StructureAt(10, 10);
        var placed = Assert.Single(match.EventsOf(SimEventKind.StructurePlaced));
        Assert.Equal((gate, (long)Gate(match), 0), (placed.Entity, placed.Amount, placed.Player));
        Assert.Equal(new Foundation { Materials = 40 }, match.Foundations.Get(gate));
        Assert.Equal(match.Research.Tech(0).Gate!.HitPoints, match.Health.Get(gate).HitPoints);
        Assert.Equal(match.Research.Tech(0).StructureOf(match.Def("wall").Index)!.HitPoints, match.Health.Get(gate).HitPoints);
        Assert.Equal(gate, match.WallMap.WallAt(10, 10));

        // The foundation blocks its tile for everyone, its owner included.
        Assert.False(match.Grid.IsPassable(10, 10));
        Assert.Same(match.Grid, match.Grid.For(0));

        match.RunThrough(laidIn + GateTicks - 1);
        Assert.False(match.Construction.IsFinished(gate));
        Assert.Same(match.Grid, match.Grid.For(0));

        match.Simulation.Tick();
        Assert.True(match.Construction.IsFinished(gate));
        Assert.Equal(gate, Assert.Single(match.EventsOf(SimEventKind.StructureFinished)).Entity);
        Assert.True(match.Grid.For(0).IsPassable(10, 10));
        Assert.False(match.Grid.For(1).IsPassable(10, 10));
        Assert.False(match.Grid.IsPassable(10, 10));
        Assert.Equal((960, 1000), match.StockOf(0));
    }

    public static TheoryData<int, int, BuildProblem> SitesThatAreRefused() => new()
    {
        // On the Capitol, on a wall tile, on a gate that stands and on one that builds itself.
        { 7, 11, BuildProblem.SiteNotFree },
        { 9, 9, BuildProblem.SiteNotFree },
        { 9, 10, BuildProblem.SiteNotFree },
        { 9, 11, BuildProblem.SiteNotFree },
        // Off the map, and outside the territory, which a gate needs as a wall tile does.
        { 40, 10, BuildProblem.SiteNotFree },
        { 13, 10, BuildProblem.OutsideTerritory },
    };

    [Theory]
    [MemberData(nameof(SitesThatAreRefused))]
    public void AGateIsRefusedWhereAWallTileWouldBe(int x, int y, BuildProblem why)
    {
        var match = WithACapitol();
        match.PlaceStructure(0, "wall", 9, 9);
        match.PlaceStructure(0, "gate", 9, 10);
        match.StructureFactory.Lay(0, match.Data.Gate!, 9, 11, new Foundation { Materials = 40 });
        var worker = match.WorkerAt(0, 10, 8);
        match.OrderBuild(0, "gate", x, y, worker);

        match.Simulation.Tick();

        Assert.Equal([(worker, why)], match.Problems(SimEventKind.BuildRefused));
        Assert.Equal((1000, 1000), match.StockOf(0));
        Assert.Equal(why, match.Construction.CheckSite(0, match.Data.Gate!, x, y));
        Assert.Equal(BuildProblem.None, match.Construction.CheckSite(0, match.Data.Gate!, 10, 10));
        Assert.True(match.Construction.CanBuildOnTile(0, match.Data.Gate!, 10, 10));
        Assert.False(match.Construction.CanBuildOnTile(0, match.Data.Gate!, 9, 10));
    }

    [Fact]
    public void TheWallItselfIsStillPlacedByAWallOrderAlone()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 8);
        match.OrderBuild(0, "wall", 10, 10, worker);

        match.Simulation.Tick();

        Assert.Equal([(worker, BuildProblem.NoStructure)], match.Problems(SimEventKind.BuildRefused));
        Assert.Null(match.Construction.Buildable(match.Def("wall").Index));
        Assert.Same(match.Data.Gate, match.Construction.Buildable(Gate(match)));
        Assert.Null(match.Construction.Buildable(Gate(match) + 1));
    }

    [Fact]
    public void AGateIsLockedWhileTheWallIs()
    {
        // The wall of this data needs Protection 2, and so does the gate made from it.
        var files = Files().Edit("structures/wall.jsonc", "\"track\": \"protection\",", "\"track\": \"protection\", \"unlock_tier\": 2,");
        var match = WithACapitol(DataAssert.Valid(files));
        var worker = match.WorkerAt(0, 10, 8);
        match.OrderBuild(0, "gate", 10, 10, worker);

        match.Simulation.Tick();

        Assert.Equal([(worker, BuildProblem.Locked)], match.Problems(SimEventKind.BuildRefused));
        Assert.Equal(BuildProblem.Locked, match.Construction.CheckStructure(0, Gate(match)));
    }

    [Fact]
    public void CancellingAGateThatBuildsItselfGivesThreeQuartersOfItsPriceBack()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 9);
        match.OrderBuild(0, "gate", 10, 10, worker);
        match.RunUntilOrderEnds(worker);
        var gate = match.StructureAt(10, 10);
        match.Simulation.Run(50);
        Assert.Equal((960, 1000), match.StockOf(0));

        match.OrderCancelBuild(0, gate);
        match.Simulation.Tick();

        Assert.False(match.World.Entities.IsAlive(gate));
        Assert.Equal((990, 1000), match.StockOf(0));
        Assert.True(match.Grid.IsPassable(10, 10));
        Assert.Equal(EntityId.None, match.WallMap.WallAt(10, 10));
    }

    /// <summary>Design 8.4, row 30.</summary>
    [Fact]
    public void TheOwnersUnitsOnTheSiteAreMovedOffAndAnEnemyUnitStopsTheFoundation()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 9);
        var standing = match.SpawnChassis(0, 0, TestMatches.Point("10.4", "10.6"));
        match.OrderBuild(0, "gate", 10, 10, worker);

        match.Simulation.Tick();

        // The tile outside whose center is nearest: the one to the left.
        Assert.NotEqual(EntityId.None, match.StructureAt(10, 10));
        Assert.Equal(MapGrid.TileCenter(9, 10), match.PositionOf(standing));

        var enemy = match.SpawnUnit(1, MapGrid.TileCenter(11, 10), StructureMatches.QuarterTile);
        match.OrderBuild(0, "gate", 11, 10, worker);
        match.Simulation.Tick();

        Assert.Equal([(worker, BuildProblem.EnemyOnSite)], match.Problems(SimEventKind.BuildOrderEnded));
        Assert.Equal(EntityId.None, match.StructureAt(11, 10));
        Assert.Equal((960, 1000), match.StockOf(0));
        Assert.Equal(MapGrid.TileCenter(11, 10), match.PositionOf(enemy));
    }

    [Fact]
    public void AUnitMovedOffASiteMayBePutOnItsOwnersGate()
    {
        // The tile below the site is a finished gate of the owner and the others round it are
        // wall, so the gate is the nearest tile a unit of the owner can stand on.
        var match = WithACapitol();
        foreach (var (x, y) in new[] { (9, 9), (10, 9), (11, 9), (9, 10), (11, 10), (9, 11), (11, 11) })
        {
            match.PlaceStructure(0, "wall", x, y);
        }

        match.PlaceStructure(0, "gate", 10, 11);
        var worker = match.WorkerAt(0, 10, 12);
        var standing = match.SpawnChassis(0, 0, MapGrid.TileCenter(10, 10));
        match.OrderMove(0, MapGrid.TileCenter(10, 11), worker);
        match.Simulation.Run(40);
        Assert.Equal(MapGrid.TileCenter(10, 11), match.PositionOf(worker));

        match.OrderBuild(0, "gate", 10, 10, worker);
        match.Simulation.Tick();

        Assert.NotEqual(EntityId.None, match.StructureAt(10, 10));
        Assert.Equal(MapGrid.TileCenter(10, 11), match.PositionOf(standing));
    }

    /// <summary>The player may also build the gate first and draw the wall across it.</summary>
    [Fact]
    public void AWallRunDrawnAcrossAGateLeavesItsTileOutAndCostsWhatItAdds()
    {
        var match = WithACapitol();
        var gate = match.PlaceStructure(0, "gate", 10, 8);
        var worker = match.WorkerAt(0, 8, 7);
        List<(int X, int Y)> tiles = [];
        Assert.Equal(BuildProblem.None, match.Walls.TilesToLay(0, 9, 8, 11, 8, tiles));
        Assert.Equal([(9, 8), (11, 8)], tiles);
        match.OrderWall(0, (9, 8), (11, 8), worker);

        match.Simulation.Tick();
        Assert.Equal((960, 1000), match.StockOf(0));
        match.RunUntilWallOrderEnds(worker);

        Assert.Equal([(9, 8), (11, 8)], match.WallOf(0).Order());
        Assert.Equal(gate, match.WallMap.WallAt(10, 8));
        Assert.Equal((960, 1000), match.StockOf(0));
        Assert.True(match.Grid.For(0).IsPassable(10, 8));
    }

    [Fact]
    public void AGateInTheGapOfAWallClosesItToEveryoneButItsOwner()
    {
        // A wall from the top of the map down the column x = 12 to the cliff at the bottom, with
        // a gap at (12, 10), which is in player 0's territory.
        var rows = Enumerable.Range(0, 24).Select(row => row < 20 ? new string('.', 40) : new string('.', 12) + "#" + new string('.', 27));
        var match = Create(TestMaps.FromRows([.. rows]));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 1000, 1000);
        for (var y = 0; y < 20; y++)
        {
            if (y != 10)
            {
                match.PlaceStructure(0, "wall", 12, y);
            }
        }

        var builder = match.WorkerAt(0, 10, 9);
        var own = match.WorkerAt(0, 9, 12);
        var other = match.WorkerAt(1, 9, 14);
        Assert.Equal(1, match.Grid.RegionCount);

        match.OrderBuild(0, "gate", 12, 10, builder);
        var laidIn = match.RunUntilOrderEnds(builder);
        Assert.Equal(2, match.Grid.RegionCount);
        match.RunThrough(laidIn + GateTicks);
        Assert.Equal((2, 1, 2), (match.Grid.RegionCount, match.Grid.For(0).RegionCount, match.Grid.For(1).RegionCount));

        // Each is sent to the same tile on the far side. The owner's worker walks through; the
        // other one has no way and goes to the nearest tile it can reach.
        match.OrderMove(0, MapGrid.TileCenter(20, 14), own);
        match.OrderMove(1, MapGrid.TileCenter(20, 14), other);
        var through = false;
        for (var tick = 0; tick < 400; tick++)
        {
            match.Simulation.Tick();
            through |= match.StandsOn(own) == (12, 10);
            Assert.True(match.StandsOn(other).X < 12);
        }

        Assert.True(through);
        Assert.Equal(MapGrid.TileCenter(20, 14), match.PositionOf(own));
        Assert.Equal(MapGrid.TileCenter(11, 14), match.PositionOf(other));
        Assert.Equal(MoveState.Idle, match.MoveStateOf(other));
    }

    [Fact]
    public void AWorkerWalksToItsWorkThroughItsOwnersGate()
    {
        // The deposit on the tiles (20, 10) to (21, 11) lies behind a wall with one gate in it.
        var rows = Enumerable.Range(0, 24).Select(row => new string('.', 40));
        var match = Create(TestMaps.FromRows([.. rows]).WithDeposits((20, 10)));
        match.PlaceTerritory(0, 10, 10, 2, 40);
        for (var y = 0; y < 24; y++)
        {
            match.PlaceStructure(0, y == 4 ? "gate" : "wall", 14, y);
        }

        var deposit = match.SiteAt(20, 10);
        var own = match.WorkerAt(0, 5, 10);
        var other = match.WorkerAt(1, 5, 12);
        match.OrderGather(0, deposit, own);
        match.OrderGather(1, deposit, other);

        match.Simulation.Tick();

        // The other player's worker has no way to a place at the deposit, so its order is dropped.
        Assert.Equal(deposit, match.WorkerOf(own).Site);
        Assert.Equal(EntityId.None, match.WorkerOf(other).Site);
        Assert.Equal(1, match.Simulation.DroppedCommandCount);

        var through = false;
        for (var tick = 0; tick < 600 && match.StateOf(own) != WorkerState.Working; tick++)
        {
            match.Simulation.Tick();
            through |= match.StandsOn(own) == (14, 4);
        }

        Assert.True(through);
        Assert.Equal(WorkerState.Working, match.StateOf(own));
    }
}
