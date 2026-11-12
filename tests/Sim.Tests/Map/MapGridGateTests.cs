using Sim.Map;
using Sim.Movement;
using Sim.Numerics;

namespace Sim.Tests.Map;

/// <summary>
/// The grid of each player (design 2.5): a finished gate is a wall tile to everyone but its
/// owner, so which tiles are passable, the regions and the routes are kept for each player.
/// </summary>
/// <remarks>
/// The map of these tests is 9 by 5 tiles of open ground with a wall down the column x = 4, which
/// cuts it in a west and an east half. The tile (4, 2) of that wall is a gate in most of them.
/// </remarks>
public class MapGridGateTests
{
    private static readonly TileSquare[] Column =
    [
        new(4, 0, 1), new(4, 1, 1), new(4, 2, 1), new(4, 3, 1), new(4, 4, 1),
    ];

    private static int[] GateOf(int player) => [-1, -1, player, -1, -1];

    private static MapGrid Grid() => new(TestMaps.Open(9, 5));

    [Fact]
    public void AGateIsGroundToItsOwnerAndAWallTileToEveryoneElse()
    {
        var grid = Grid();

        Assert.True(grid.BlockOnly(Column, GateOf(1)));

        // The shared grid, which is what a player with no gate walks, has the whole column blocked.
        Assert.Same(grid, grid.For(0));
        Assert.Same(grid, grid.For(2));
        Assert.Same(grid, grid.For(-1));
        Assert.False(grid.IsPassable(4, 2));
        Assert.Equal(2, grid.RegionCount);
        Assert.NotEqual(grid.RegionOf(0, 2), grid.RegionOf(8, 2));
        Assert.Equal(0, grid.View);

        // The owner's grid has the gate open and the rest of the column blocked.
        var own = grid.For(1);
        Assert.NotSame(grid, own);
        Assert.Equal(2, own.View);
        Assert.True(own.IsPassable(4, 2));
        Assert.False(own.IsPassable(4, 1));
        Assert.False(own.IsPassable(4, 3));
        Assert.Equal(1, own.RegionCount);
        Assert.Equal(own.RegionOf(0, 2), own.RegionOf(8, 2));
        Assert.Equal(own.RegionOf(0, 2), own.RegionOf(4, 2));
        Assert.Same(own, own.For(1));
        Assert.Same(grid, own.For(0));
        Assert.Equal((grid.Width, grid.Height), (own.Width, own.Height));
    }

    [Fact]
    public void EachPlayerWalksThroughItsOwnGatesOnly()
    {
        var grid = Grid();

        grid.BlockOnly(Column, [-1, 0, -1, 2, -1]);

        Assert.True(grid.For(0).IsPassable(4, 1));
        Assert.False(grid.For(0).IsPassable(4, 3));
        Assert.True(grid.For(2).IsPassable(4, 3));
        Assert.False(grid.For(2).IsPassable(4, 1));
        Assert.Same(grid, grid.For(1));
        Assert.Equal((1, 3), (grid.For(0).View, grid.For(2).View));
    }

    [Fact]
    public void AGateOnBlockedGroundOpensNothing()
    {
        var grid = new MapGrid(TestMaps.FromRows("....#....", "....#....", "....#....", "....#....", "....#...."));

        grid.BlockOnly([new TileSquare(4, 2, 1)], [1]);

        Assert.False(grid.For(1).IsPassable(4, 2));
        Assert.Equal(2, grid.For(1).RegionCount);
    }

    [Fact]
    public void ASquareWithNoEntryBlocksEveryone()
    {
        var grid = Grid();

        // The first square is the gate of player 0; nothing is said of the other four.
        grid.BlockOnly([Column[2], Column[0], Column[1], Column[3], Column[4]], [0]);

        Assert.True(grid.For(0).IsPassable(4, 2));
        Assert.False(grid.For(0).IsPassable(4, 0));
        Assert.Throws<ArgumentException>(() => grid.BlockOnly([Column[0]], [0, 0]));
        Assert.Throws<InvalidOperationException>(() => grid.For(0).BlockOnly([]));
    }

    [Fact]
    public void WhatChangedIsToldForEachPlayer()
    {
        var grid = Grid();

        // A wall with no gate changes the ground of everyone.
        Assert.True(grid.BlockOnly(Column));
        Assert.True(grid.ChangedFor(0));
        Assert.True(grid.ChangedFor(1));

        // The same again changes nothing.
        Assert.False(grid.BlockOnly(Column));
        Assert.False(grid.ChangedFor(0));
        Assert.False(grid.ChangedFor(1));

        // A tile of it that becomes the gate of player 1 changes the ground of player 1 alone.
        Assert.True(grid.BlockOnly(Column, GateOf(1)));
        Assert.False(grid.ChangedFor(0));
        Assert.True(grid.ChangedFor(1));
        Assert.False(grid.ChangedFor(5));

        // And nothing when it is told again.
        Assert.False(grid.BlockOnly(Column, GateOf(1)));
        Assert.False(grid.ChangedFor(0));
        Assert.False(grid.ChangedFor(1));

        // A wall tile that falls changes the ground of both.
        Assert.True(grid.BlockOnly(Column.AsSpan(1), GateOf(1).AsSpan(1)));
        Assert.True(grid.ChangedFor(0));
        Assert.True(grid.ChangedFor(1));
        Assert.Equal(1, grid.RegionCount);

        // The gate falls: its owner walks the shared grid again.
        Assert.True(grid.BlockOnly([Column[1], Column[3], Column[4]]));
        Assert.Same(grid, grid.For(1));
        Assert.True(grid.ChangedFor(0));
        Assert.True(grid.ChangedFor(1));
    }

    [Fact]
    public void TheGridOfAPlayerFollowsEveryChangeOfTheStructures()
    {
        var grid = Grid();
        grid.BlockOnly(Column, GateOf(1));

        // A structure beside the gate, on the east side, closes it in for its owner too.
        TileSquare[] closed = [.. Column, new(5, 1, 1), new(5, 2, 1), new(5, 3, 1)];
        Assert.True(grid.BlockOnly(closed, GateOf(1)));

        var own = grid.For(1);
        Assert.True(own.IsPassable(4, 2));
        Assert.False(own.IsPassable(5, 2));
        Assert.Equal(2, own.RegionCount);
        Assert.Equal(own.RegionOf(0, 0), own.RegionOf(4, 2));
        Assert.NotEqual(own.RegionOf(0, 0), own.RegionOf(8, 0));
    }

    [Fact]
    public void ARouteLeadsThroughTheGateForItsOwnerAndThereIsNoneForAnyoneElse()
    {
        var grid = Grid();
        grid.BlockOnly(Column, GateOf(1));
        var finder = new RouteFinder(grid);

        var route = finder.Find(grid.For(1), 0, 0, 8, 0, out var expansions);

        Assert.Contains((2 * 9) + 4, route);
        Assert.Equal((0, 8), (route[0], route[^1]));
        Assert.InRange(expansions, 1, 45);
        Assert.Throws<ArgumentException>(() => finder.Find(0, 0, 8, 0, out _));
        Assert.Throws<ArgumentException>(() => finder.Find(grid.For(0), 0, 0, 8, 0, out _));
        Assert.Throws<ArgumentException>(() => finder.Find(new MapGrid(TestMaps.Open(9, 6)), 0, 0, 8, 0, out _));
    }

    [Fact]
    public void TheSecondSearchCrossesTheMarkedTilesOfThePlayersOwnGrid()
    {
        // Player 1 has its gate at (4, 2); the marks are the rest of the column, as if it were an enemy's.
        var grid = Grid();
        grid.BlockOnly(Column, GateOf(1));
        var marks = new bool[45];
        foreach (var y in new[] { 0, 1, 3, 4 })
        {
            marks[(y * 9) + 4] = true;
        }

        var finder = new RouteFinder(grid);

        // Through its own gate, which costs a walk, and not through a marked tile, which costs twenty.
        var own = finder.FindAcross(grid.For(1), 0, 0, 8, 0, marks, 20, out _);
        Assert.NotNull(own);
        Assert.DoesNotContain(own, tile => marks[tile]);
        Assert.Contains((2 * 9) + 4, own);

        // Anyone else crosses the first marked tile on the straight way.
        var other = finder.FindAcross(0, 0, 8, 0, marks, 20, out _);
        Assert.NotNull(other);
        Assert.Equal([4], other.Where(tile => marks[tile]));
    }

    [Fact]
    public void RoutesAreKeptForEachGridAndSharedByThePlayersWithoutAGate()
    {
        // A gap at the bottom of the column, so that everyone has a way, and a gate at the top.
        var grid = Grid();
        grid.BlockOnly(Column.AsSpan(0, 4), [1]);
        var cache = new RouteCache(grid);

        var shared = cache.Get(0, 0, 8);
        Assert.Same(shared, cache.Get(0, 8));
        Assert.Same(shared, cache.Get(2, 0, 8));
        Assert.Equal(1, cache.SearchCount);

        // Player 1 goes straight through its gate at (4, 0).
        var own = cache.Get(1, 0, 8);
        Assert.Equal(2, cache.SearchCount);
        Assert.Equal(9, own.Tiles.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8], own.Tiles);
        Assert.True(shared.Tiles.Count > own.Tiles.Count);
        Assert.Contains((4 * 9) + 4, shared.Tiles);
        Assert.Same(own, cache.Get(1, 0, 8));
        Assert.Equal(2, cache.SearchCount);
    }

    [Fact]
    public void TheLineOfAMeleeHitDoesNotCountTheTilesItsEndsAreIn()
    {
        var grid = Grid();
        grid.BlockOnly(Column, GateOf(1));
        var inGate = MapGrid.TileCenter(4, 2);
        var west = MapGrid.TileCenter(3, 2);
        var east = MapGrid.TileCenter(5, 2);
        var northWest = MapGrid.TileCenter(3, 1);

        // On the shared grid the gate is a wall tile: a walk does not end in it, and a hit on a
        // unit that stands in it is made from the tile beside it.
        Assert.False(grid.IsSegmentClear(west, inGate));
        Assert.True(grid.IsSegmentClearBetween(west, inGate));
        Assert.True(grid.IsSegmentClearBetween(inGate, west));

        // A line across the gate crosses it, and a line to it through a corner needs the two
        // tiles beside the corner, one of which is wall.
        Assert.False(grid.IsSegmentClearBetween(west, east));
        Assert.False(grid.IsSegmentClearBetween(northWest, inGate));

        // To its owner the gate is ground like any other.
        var own = grid.For(1);
        Assert.True(own.IsSegmentClear(west, east));
        Assert.True(own.IsSegmentClearBetween(west, east));
        Assert.False(own.IsSegmentClearBetween(northWest, inGate));

        // Where both ends stand on ground the two tests agree.
        Assert.True(grid.IsSegmentClearBetween(MapGrid.TileCenter(0, 0), MapGrid.TileCenter(3, 4)));
        Assert.False(grid.IsSegmentClearBetween(MapGrid.TileCenter(0, 0), MapGrid.TileCenter(8, 4)));
        Assert.True(grid.IsSegmentClearBetween(west, west));
        Assert.True(grid.IsSegmentClearBetween(inGate, inGate + new FixedVector2(Fixed.Zero, Fixed.Half / 2)));
    }
}
