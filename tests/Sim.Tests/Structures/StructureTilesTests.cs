using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Structures;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Structures.StructureMatches;

namespace Sim.Tests.Structures;

/// <summary>The tiles of the grid follow the structures that stand (plan 15b).</summary>
public class StructureTilesTests
{
    /// <summary>A wall across the map with one gap, two tiles wide, at x 4 and 5.</summary>
    private static MapDef OneGap => TestMaps.FromRows(
        "..........",
        "..........",
        "####..####",
        "####..####",
        "..........",
        "..........");

    [Fact]
    public void TheGridBlocksTheSquaresItIsGivenAndNumbersTheRegionsAgain()
    {
        var grid = new MapGrid(OneGap);
        Assert.Equal(1, grid.RegionCount);

        Assert.True(grid.BlockOnly([new TileSquare(4, 2, 2)]));

        Assert.Equal(2, grid.RegionCount);
        Assert.Equal((1, 2), (grid.RegionOf(0, 0), grid.RegionOf(0, 5)));
        Assert.All(new[] { (4, 2), (5, 2), (4, 3), (5, 3) }, tile =>
        {
            Assert.False(grid.IsPassable(tile.Item1, tile.Item2));
            Assert.Equal(0, grid.RegionOf(tile.Item1, tile.Item2));
            Assert.True(grid.IsOpenGround(tile.Item1, tile.Item2));
        });

        // The same squares again change nothing; none at all gives the ground back as it was.
        Assert.False(grid.BlockOnly([new TileSquare(4, 2, 2)]));
        Assert.True(grid.BlockOnly([]));
        Assert.Equal(1, grid.RegionCount);
        Assert.True(grid.IsPassable(4, 2));
        Assert.Equal(grid.RegionOf(0, 0), grid.RegionOf(0, 5));
    }

    [Fact]
    public void WhatTheGroundBlocksStaysBlockedAndASquareMayReachOffTheMap()
    {
        var grid = new MapGrid(OneGap.WithDeposits((0, 4)), depositSize: 2);
        var before = grid.PassableTiles();

        Assert.True(grid.BlockOnly([new TileSquare(9, 5, 3), new TileSquare(-1, -1, 2)]));
        Assert.Equal(before - 2, grid.PassableTiles());
        Assert.True(grid.BlockOnly([]));

        Assert.Equal(before, grid.PassableTiles());
        Assert.False(grid.IsPassable(0, 2));
        Assert.False(grid.IsPassable(1, 5));
        Assert.False(grid.IsOpenGround(0, 2));
        Assert.False(grid.IsOpenGround(1, 5));
        Assert.False(grid.IsOpenGround(-1, 0));
    }

    [Fact]
    public void AStructureHasABodyAtTheCenterOfItsFootprintItsFullHitPointsAndItsTiles()
    {
        var match = Create(TestMaps.Open(24, 24));
        var generator = GeneratorData.StructureOf("generator");

        var entity = match.PlaceStructure(1, generator, 10, 12);

        Assert.Equal(new PlacedStructure { Type = generator.Index, X = 10, Y = 12, Size = 2 }, match.Structures.Get(entity));
        Assert.Equal((1, TestMatches.Point("11", "13")), (match.Bodies.Get(entity).Owner, match.PositionOf(entity)));
        Assert.Equal((500_000, 500_000), (match.Health.Get(entity).HitPoints, match.Health.Get(entity).Maximum));
        Assert.False(match.Movers.Contains(entity));
        Assert.All(new[] { (10, 12), (11, 12), (10, 13), (11, 13) }, tile => Assert.False(match.Grid.IsPassable(tile.Item1, tile.Item2)));
        Assert.Equal((24 * 24) - 4, match.Grid.PassableTiles());

        var placed = Assert.Single(match.EventsOf(SimEventKind.StructurePlaced));
        Assert.Equal((1, entity, TestMatches.Point("11", "13"), (long)generator.Index), (placed.Player, placed.Entity, placed.Position, placed.Amount));
    }

    [Fact]
    public void AStructureWithAnOddFootprintHasItsBodyInTheMiddleOfItsMiddleTile()
    {
        Assert.Equal(TestMatches.Point("4.5", "8.5"), StructureSetup.CenterOf(new TileSquare(3, 7, 3)));
        Assert.Equal(TestMatches.Point("5", "9"), StructureSetup.CenterOf(new TileSquare(3, 7, 4)));
    }

    [Fact]
    public void AStructureNeedsOpenGroundUnderNoOtherStructure()
    {
        var match = Create(OneGap.WithDeposits((0, 4)));
        var generator = GeneratorData.StructureOf("generator");
        match.PlaceStructure(0, generator, 6, 0);

        Assert.True(match.HasRoomFor(generator, 4, 2));
        Assert.True(match.HasRoomFor(generator, 8, 0));
        Assert.All(
            new[] { (3, 2), (1, 4), (5, 1), (7, 0), (9, 0), (-1, 0), (8, 5) },
            tile =>
            {
                Assert.False(match.HasRoomFor(generator, tile.Item1, tile.Item2));
                Assert.Throws<ArgumentException>(() => { match.PlaceStructure(0, generator, tile.Item1, tile.Item2); });
            });

        Assert.Throws<ArgumentOutOfRangeException>(() => { match.PlaceStructure(2, generator, 4, 2); });
        Assert.Throws<ArgumentException>(() => { match.PlaceStructure(0, EconomyMatches.DataStartingAtTier(2).StructureOf("generator"), 4, 2); });
        Assert.Throws<ArgumentException>(() => { match.PlaceStructure(0, "tower", 4, 2); });
        Assert.Equal(1, match.Structures.Count);
    }

    /// <summary>Plan 15b: a structure placed by a test blocks routes and frees them when removed.</summary>
    [Fact]
    public void AStructureBlocksRoutesAndFreesThemWhenItIsRemoved()
    {
        var match = Create(OneGap);
        var unit = match.SpawnUnit(0, MapGrid.TileCenter(1, 0), QuarterTile);
        var beyond = MapGrid.TileCenter(1, 5);

        var plug = match.PlaceStructure(1, "generator", 4, 2);
        Assert.Equal(2, match.Grid.RegionCount);
        Assert.NotEqual(match.Grid.RegionOf(1, 0), match.Grid.RegionOf(1, 5));

        // No route leads through: the unit goes to the tile nearest the target on its own side.
        match.OrderMove(0, beyond, unit);
        match.RunUntilIdle(unit);
        Assert.Equal((1, 1), match.TileOf(unit));

        match.Raze(plug);
        match.Simulation.Tick();

        Assert.Single(match.EventsOf(SimEventKind.Death));
        var removed = Assert.Single(match.EventsOf(SimEventKind.StructureRemoved));
        Assert.Equal((1, plug, TestMatches.Point("5", "3")), (removed.Player, removed.Entity, removed.Position));
        Assert.Equal(1, match.Grid.RegionCount);
        Assert.True(match.Grid.IsPassable(4, 2));
        Assert.Equal(0, match.Structures.Count);

        match.OrderMove(0, beyond, unit);
        match.RunUntilIdle(unit);
        Assert.Equal(beyond, match.PositionOf(unit));
    }

    [Fact]
    public void TheTilesAreFreeInTheTickInWhichTheStructureFalls()
    {
        var match = Create(OneGap);
        var plug = match.PlaceStructure(1, "generator", 4, 2);
        match.Simulation.Run(3);
        match.Raze(plug);

        // The store still holds the structure until the tick ends; the grid no longer counts it.
        match.Simulation.Tick();

        Assert.Equal(1, match.Grid.RegionCount);
        Assert.Equal(60 - 16, match.Grid.PassableTiles());
        match.Simulation.Tick();
        Assert.Empty(match.EventsOf(SimEventKind.StructureRemoved));
        Assert.Equal(1, match.Grid.RegionCount);
    }
}
