using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.SiegeMatches;
using static Sim.Tests.Walls.WalledMatches;

namespace Sim.Tests.Walls;

/// <summary>
/// The second search of the wall rule (design 2.5): which ground lies past enemy walls, when a
/// wall is what keeps a unit from a place, and which tile is the first on the way through.
/// </summary>
public class BreachingTests
{
    private static readonly (int X, int Y) Low = (14, 8);
    private static readonly (int X, int Y) High = (22, 16);

    [Fact]
    public void TheGroundInsideEnemyWallsIsOneWithTheGroundOutsideAndTheWallsAreNoGround()
    {
        var match = Field();
        match.Ring(1, Low, High);

        // For the other player the walls could be crossed. For their owner they could not.
        Assert.Equal(match.WallMap.PastWallsOf(0, 5, 12), match.WallMap.PastWallsOf(0, 18, 12));
        Assert.NotEqual(0, match.WallMap.PastWallsOf(0, 18, 12));
        Assert.NotEqual(match.WallMap.PastWallsOf(1, 5, 12), match.WallMap.PastWallsOf(1, 18, 12));
        Assert.Equal(0, match.WallMap.PastWallsOf(0, 14, 12));
        Assert.Equal(0, match.WallMap.PastWallsOf(1, 14, 12));
        Assert.Equal(0, match.WallMap.PastWallsOf(0, -1, 12));

        Assert.True(match.Breaching.IsCutOff(0, Center(5, 12), Center(18, 12)));
        Assert.True(match.Breaching.IsCutOff(0, Center(18, 12), Center(5, 12)));
        Assert.False(match.Breaching.IsCutOff(1, Center(5, 12), Center(18, 12)));
        Assert.False(match.Breaching.IsCutOff(0, Center(5, 12), Center(6, 20)));
        Assert.False(match.Breaching.IsCutOff(0, Center(5, 12), Center(14, 12)));
    }

    [Fact]
    public void TheWallMapFollowsTheWallsThatStand()
    {
        var match = Field();
        Assert.Equal(EntityId.None, match.WallMap.WallAt(14, 12));
        match.Ring(1, Low, High);
        var tile = match.WallOn(14, 12);
        Assert.True(match.Breaching.IsCutOff(0, Center(5, 12), Center(18, 12)));

        // A foundation of a wall is a wall. Another structure is not.
        var foundation = match.Lay(1, Wall, 5, 5);
        match.PlaceStructure(1, Barracks, 2, 2);
        Assert.Equal(foundation, match.WallMap.WallAt(5, 5));
        Assert.Equal(EntityId.None, match.WallMap.WallAt(3, 3));

        match.SetHitPoints(tile, 0);
        match.Run(1);
        Assert.Equal(EntityId.None, match.WallMap.WallAt(14, 12));
        Assert.False(match.Breaching.IsCutOff(0, Center(5, 12), Center(18, 12)));
    }

    [Fact]
    public void TheTileIsTheFirstEnemyWallOnTheWayAndNoSearchRunsWithoutOne()
    {
        var match = Field();
        match.Ring(1, Low, High);

        Assert.Equal(match.WallOn(14, 12), match.Breaching.FindWall(0, Center(5, 12), Center(18, 12), out var expansions));
        Assert.True(expansions > 0);
        Assert.Equal(match.WallOn(22, 12), match.Breaching.FindWall(0, Center(29, 12), Center(18, 12), out _));
        Assert.Equal(match.WallOn(18, 8), match.Breaching.FindWall(0, Center(18, 2), Center(18, 12), out _));
        Assert.Equal(match.WallOn(14, 12), match.Breaching.FindWall(0, Center(18, 12), Center(5, 12), out _));
        Assert.Equal(4, match.Breaching.SearchCount);

        Assert.Equal(EntityId.None, match.Breaching.FindWall(1, Center(5, 12), Center(18, 12), out expansions));
        Assert.Equal(EntityId.None, match.Breaching.FindWall(0, Center(5, 12), Center(6, 20), out _));
        Assert.Equal(0, expansions);
        Assert.Equal(4, match.Breaching.SearchCount);
    }

    [Fact]
    public void AWayRoundASecondWallIsTakenWhenItCostsLessThanCrossingIt()
    {
        // The west side of the camp is two tiles thick from y = 9 to 15. One tile costs as much
        // as 20 tiles of walking, so the way leads round to the north side, which is one thick.
        var match = Field();
        match.Ring(1, Low, High);
        for (var y = 9; y <= 15; y++)
        {
            match.WallTile(1, 15, y);
        }

        var wall = match.Breaching.FindWall(0, Center(5, 12), Center(18, 12), out _);

        var tile = match.Structures.Get(wall);
        Assert.Equal(8, tile.Y);
        Assert.InRange(tile.X, 16, 18);
    }

    [Fact]
    public void TwoWallsAreCrossedWhereTheWayRoundWouldCostMore()
    {
        // Two lines of wall from edge to edge, the second with a gap far to the north. Round by
        // the gap is more than 20 tiles further than straight through both.
        var match = Create(TestMaps.Open(40, 60));
        for (var y = 0; y < 60; y++)
        {
            match.WallTile(1, 14, y);
            if (y >= 2)
            {
                match.WallTile(1, 16, y);
            }
        }

        match.WallTile(1, 20, 0);
        match.WallTile(1, 20, 1);
        match.WallTile(1, 19, 2);
        match.WallTile(1, 18, 2);
        match.WallTile(1, 17, 2);

        Assert.Equal(match.WallOn(14, 50), match.Breaching.FindWall(0, Center(5, 50), Center(25, 50), out _));
    }

    [Fact]
    public void OnlyEnemyWallsAreCrossed()
    {
        // A camp of player 0's own wall with one tile of player 1's in its east side.
        var match = Field();
        match.Ring(0, Low, High);
        match.SetHitPoints(match.WallOn(22, 12), 0);
        match.Run(1);
        var enemy = match.WallTile(1, 22, 12);

        Assert.True(match.Breaching.IsCutOff(0, Center(5, 12), Center(18, 12)));
        Assert.Equal(enemy, match.Breaching.FindWall(0, Center(5, 12), Center(18, 12), out _));
    }

    [Fact]
    public void AWayAcrossNeverSlipsBetweenTwoTilesThatTouchAtACorner()
    {
        var grid = new MapGrid(TestMaps.Open(12, 12));
        var marks = new bool[144];
        var blocked = new List<TileSquare>();
        for (var i = 0; i < 12; i++)
        {
            marks[(i * 12) + i] = true;
            blocked.Add(new TileSquare(i, i, 1));
        }

        grid.BlockOnly([.. blocked]);
        var finder = new RouteFinder(grid);

        var way = finder.FindAcross(2, 8, 8, 2, marks, 20, out _);

        Assert.NotNull(way);
        Assert.Equal(1, way.Count(tile => marks[tile]));
        Assert.Equal((8 * 12) + 2, way[0]);
        Assert.Equal((2 * 12) + 8, way[^1]);
    }

    [Fact]
    public void TheCostOfATileDecidesBetweenCrossingAndGoingRound()
    {
        // A marked line of 9 tiles in the middle of open ground. Round its end is 5 tiles further.
        var grid = new MapGrid(TestMaps.Open(15, 15));
        var marks = new bool[225];
        var blocked = new List<TileSquare>();
        for (var y = 3; y <= 11; y++)
        {
            marks[(y * 15) + 7] = true;
            blocked.Add(new TileSquare(7, y, 1));
        }

        grid.BlockOnly([.. blocked]);
        var finder = new RouteFinder(grid);

        Assert.Equal(1, finder.FindAcross(5, 7, 9, 7, marks, 2, out _)!.Count(tile => marks[tile]));
        Assert.Equal(0, finder.FindAcross(5, 7, 9, 7, marks, 20, out _)!.Count(tile => marks[tile]));
    }

    [Fact]
    public void ThereIsNoWayAcrossGroundThatIsBlockedAndNotMarked()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            "......",
            ".###..",
            ".#.#..",
            ".###..",
            "......"));
        var finder = new RouteFinder(grid);

        Assert.Null(finder.FindAcross(0, 0, 2, 2, new bool[30], 20, out var expansions));
        Assert.True(expansions > 0);
        Assert.Throws<ArgumentException>(() => finder.FindAcross(0, 0, 1, 1, new bool[30], 20, out _));
        Assert.Throws<ArgumentException>(() => finder.FindAcross(0, 0, 2, 2, new bool[29], 20, out _));
    }

    [Fact]
    public void TheNearestTileCanBeLookedForByAnyLabels()
    {
        var grid = new MapGrid(TestMaps.Open(6, 4));
        var labels = new int[24];
        labels[(1 * 6) + 4] = 7;
        labels[(3 * 6) + 0] = 7;
        labels[(2 * 6) + 2] = 3;

        Assert.True(grid.TryFindNearestTile(labels, TestMatches.Point("2.5", "1.5"), 7, default, out var x, out var y));
        Assert.Equal((4, 1), (x, y));
        Assert.True(grid.TryFindNearestTile(labels, TestMatches.Point("2.5", "1.5"), 3, default, out x, out y));
        Assert.Equal((2, 2), (x, y));
        Assert.False(grid.TryFindNearestTile(labels, TestMatches.Point("2.5", "1.5"), 5, default, out _, out _));
        Assert.False(grid.TryFindNearestTile(labels, TestMatches.Point("2.5", "1.5"), 0, default, out _, out _));
        Assert.Throws<ArgumentException>(() => grid.TryFindNearestTile(new int[23], TestMatches.Point("2.5", "1.5"), 7, default, out _, out _));
    }
}
