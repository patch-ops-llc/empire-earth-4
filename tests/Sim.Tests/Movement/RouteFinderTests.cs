using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Tests.Map;

namespace Sim.Tests.Movement;

/// <summary>The search for the cheapest walk between two tiles.</summary>
public class RouteFinderTests
{
    [Fact]
    public void OverOpenGroundARouteIsAsShortAsTheGroundAllows()
    {
        var grid = new MapGrid(TestMaps.Open(12, 9));
        var finder = new RouteFinder(grid);

        // Straight along a row, straight down a column, and a pure diagonal.
        AssertRoute(grid, finder.Find(1, 4, 9, 4, out _), (1, 4), (9, 4), cost: 8 * 70);
        AssertRoute(grid, finder.Find(3, 8, 3, 0, out _), (3, 8), (3, 0), cost: 8 * 70);
        AssertRoute(grid, finder.Find(0, 0, 8, 8, out _), (0, 0), (8, 8), cost: 8 * 99);

        // 10 across and 3 down: three diagonal steps and seven straight ones.
        var route = finder.Find(1, 2, 11, 5, out var expansions);
        AssertRoute(grid, route, (1, 2), (11, 5), cost: (3 * 99) + (7 * 70));
        Assert.Equal(11, route.Length);

        // Over open ground the search goes almost straight for the goal.
        Assert.InRange(expansions, route.Length, 3 * route.Length);
    }

    [Fact]
    public void ARouteToTheTileItStartsOnIsThatTile()
    {
        var grid = new MapGrid(TestMaps.Open(4, 4));

        var route = new RouteFinder(grid).Find(2, 3, 2, 3, out var expansions);

        Assert.Equal([(3 * 4) + 2], route);
        Assert.Equal(1, expansions);
    }

    [Fact]
    public void ARouteGoesAroundWhatIsInTheWay()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            "........",
            "....#...",
            "....#...",
            "....#...",
            "....F...",
            "........"));
        var finder = new RouteFinder(grid);

        // From (1, 2) to (7, 2), around the top of the wall: two diagonal steps up, two straight
        // ones past the wall, two diagonal steps down.
        var route = finder.Find(1, 2, 7, 2, out _);
        AssertRoute(grid, route, (1, 2), (7, 2), cost: (4 * 99) + (2 * 70));
        Assert.Contains((0 * 8) + 4, route);
    }

    /// <summary>
    /// A diagonal step needs both tiles beside it, so a route never passes between two blocked
    /// tiles that touch at a corner, and never clips the corner of one.
    /// </summary>
    [Fact]
    public void ARouteNeverCutsACorner()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            "...#....",
            "..#.....",
            "........"));
        var finder = new RouteFinder(grid);

        // The gap between the two cliffs is a corner, not a way through. The route goes around
        // the lower cliff in six straight steps, because every diagonal one would clip a cliff.
        var route = finder.Find(2, 0, 3, 1, out _);
        AssertRoute(grid, route, (2, 0), (3, 1), cost: 6 * 70);

        // Around the single cliff at (2, 1): two straight steps, not one diagonal.
        var one = new MapGrid(TestMaps.FromRows(
            "...",
            ".#.",
            "..."));
        AssertRoute(one, new RouteFinder(one).Find(0, 1, 1, 0, out _), (0, 1), (1, 0), cost: 2 * 70);
        AssertRoute(one, new RouteFinder(one).Find(0, 1, 2, 1, out _), (0, 1), (2, 1), cost: 4 * 70);
    }

    [Fact]
    public void TilesInDifferentRegionsHaveNoRoute()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            "..#..",
            "..#..",
            "..#.."));
        var finder = new RouteFinder(grid);

        Assert.Throws<ArgumentException>(() => finder.Find(0, 0, 4, 0, out _));
        Assert.Throws<ArgumentException>(() => finder.Find(0, 0, 2, 1, out _));
        Assert.Throws<ArgumentException>(() => finder.Find(2, 1, 0, 0, out _));
        Assert.Throws<ArgumentException>(() => finder.Find(-1, 0, 0, 0, out _));
        Assert.Throws<ArgumentException>(() => finder.Find(0, 0, 0, 3, out _));
        Assert.Throws<ArgumentNullException>(() => new RouteFinder(null!));
    }

    /// <summary>
    /// Checked against a plain search with no estimate and no tie-break, which shares no code with
    /// the finder: every route is a walk the rules allow and costs the least any walk can.
    /// </summary>
    [Theory]
    [InlineData(1UL, 2)]
    [InlineData(2UL, 3)]
    [InlineData(3UL, 4)]
    public void EveryRouteIsAValidWalkOfTheLowestCost(ulong seed, int blockedInTen)
    {
        var random = new DeterministicRandom(seed);
        const int Size = 24;
        var grid = new MapGrid(MapGridReachTests.RandomMap(random, Size, Size, blockedInTen));
        var finder = new RouteFinder(grid);
        var found = 0;

        for (var i = 0; i < 400; i++)
        {
            var (startX, startY) = (random.NextInt(Size), random.NextInt(Size));
            var (goalX, goalY) = (random.NextInt(Size), random.NextInt(Size));
            var region = grid.RegionOf(startX, startY);
            if (region == 0 || region != grid.RegionOf(goalX, goalY))
            {
                continue;
            }

            var route = finder.Find(startX, startY, goalX, goalY, out var expansions);
            AssertRoute(grid, route, (startX, startY), (goalX, goalY), LowestCost(grid, startX, startY, goalX, goalY));
            Assert.InRange(expansions, 1, Size * Size);
            found++;
        }

        Assert.InRange(found, 20, 400);
    }

    /// <summary>
    /// A route is a function of the grid and its two ends alone. That is what lets the routes be
    /// kept outside the state: one that is missing can be found again and is the same.
    /// </summary>
    [Fact]
    public void TheSameSearchGivesTheSameRouteWhateverWasSearchedBefore()
    {
        var random = new DeterministicRandom(17);
        const int Size = 20;
        var grid = new MapGrid(MapGridReachTests.RandomMap(random, Size, Size, blockedInTen: 2));
        var used = new RouteFinder(grid);

        for (var i = 0; i < 300; i++)
        {
            var (startX, startY) = (random.NextInt(Size), random.NextInt(Size));
            var (goalX, goalY) = (random.NextInt(Size), random.NextInt(Size));
            var region = grid.RegionOf(startX, startY);
            if (region == 0 || region != grid.RegionOf(goalX, goalY))
            {
                continue;
            }

            var route = used.Find(startX, startY, goalX, goalY, out var expansions);
            var fresh = new RouteFinder(grid).Find(startX, startY, goalX, goalY, out var freshExpansions);

            Assert.Equal(fresh, route);
            Assert.Equal(freshExpansions, expansions);
            Assert.Equal(route, used.Find(startX, startY, goalX, goalY, out _));
        }
    }

    [Fact]
    public void TheCacheFindsEachRouteOnceAndCanBeEmptiedAtAnyTime()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            "......",
            "..##..",
            "......"));
        var cache = new RouteCache(grid);

        var first = cache.Get(6, 11);
        Assert.Equal(1, cache.SearchCount);
        Assert.Same(first, cache.Get(6, 11));
        Assert.Equal(1, cache.SearchCount);
        Assert.Equal(new RouteFinder(grid).Find(0, 1, 5, 1, out var expansions), first.Tiles);
        Assert.Equal(expansions, first.Expansions);

        // The way back is another route.
        var back = cache.Get(11, 6);
        Assert.Equal(2, cache.SearchCount);
        Assert.Equal(first.Tiles.Count, back.Tiles.Count);

        cache.Clear();
        var again = cache.Get(6, 11);
        Assert.Equal(3, cache.SearchCount);
        Assert.Equal(first.Tiles, again.Tiles);
        Assert.Equal(first.Expansions, again.Expansions);

        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Get(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Get(0, 18));
        Assert.Throws<ArgumentException>(() => cache.Get(0, 8));
    }

    /// <summary>A route that is still asked for is never searched for twice, however many others come and go.</summary>
    [Fact]
    public void ARouteInUseStaysInTheCacheWhileOthersAreDropped()
    {
        const int Size = 64;
        var cache = new RouteCache(new MapGrid(TestMaps.Open(Size, Size)));
        var inUse = cache.Get(0, 5);
        var searches = 1;

        for (var goal = 1; goal < 3 * RouteCache.Capacity; goal++)
        {
            cache.Get(Size, goal);
            searches++;
            Assert.Same(inUse, cache.Get(0, 5));
        }

        Assert.Equal(searches, cache.SearchCount);

        // One asked for long ago has been dropped, and is found again as it was.
        var early = cache.Get(Size, 1);
        Assert.Equal(searches + 1, cache.SearchCount);
        Assert.Equal([Size, 1], early.Tiles);
    }

    /// <summary>Asserts that a route is a walk from start to goal that the rules allow, and that it has the cost given.</summary>
    internal static void AssertRoute(MapGrid grid, IReadOnlyList<int> route, (int X, int Y) start, (int X, int Y) goal, int cost)
    {
        Assert.Equal((start.Y * grid.Width) + start.X, route[0]);
        Assert.Equal((goal.Y * grid.Width) + goal.X, route[^1]);

        var total = 0;
        for (var i = 0; i < route.Count; i++)
        {
            var (x, y) = (route[i] % grid.Width, route[i] / grid.Width);
            Assert.True(grid.IsPassable(x, y), $"The route crosses the blocked tile ({x}, {y}).");
            if (i == 0)
            {
                continue;
            }

            var (fromX, fromY) = (route[i - 1] % grid.Width, route[i - 1] / grid.Width);
            var (stepX, stepY) = (Math.Abs(x - fromX), Math.Abs(y - fromY));
            Assert.True(stepX <= 1 && stepY <= 1 && stepX + stepY > 0, $"The route jumps from ({fromX}, {fromY}) to ({x}, {y}).");
            if (stepX + stepY == 2)
            {
                Assert.True(
                    grid.IsPassable(x, fromY) && grid.IsPassable(fromX, y),
                    $"The route cuts a corner from ({fromX}, {fromY}) to ({x}, {y}).");
                total += RouteFinder.DiagonalCost;
            }
            else
            {
                total += RouteFinder.StraightCost;
            }
        }

        Assert.Equal(cost, total);
        Assert.Equal(route.Count, route.Distinct().Count());
    }

    /// <summary>The lowest cost of any walk between two tiles, by trying every tile until nothing gets cheaper.</summary>
    private static int LowestCost(MapGrid grid, int startX, int startY, int goalX, int goalY)
    {
        var cost = new int[grid.Width * grid.Height];
        Array.Fill(cost, int.MaxValue);
        cost[(startY * grid.Width) + startX] = 0;

        for (var changed = true; changed;)
        {
            changed = false;
            for (var y = 0; y < grid.Height; y++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var here = cost[(y * grid.Width) + x];
                    if (here == int.MaxValue)
                    {
                        continue;
                    }

                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if ((dx == 0 && dy == 0) || !grid.IsPassable(x + dx, y + dy))
                            {
                                continue;
                            }

                            var diagonal = dx != 0 && dy != 0;
                            if (diagonal && !(grid.IsPassable(x + dx, y) && grid.IsPassable(x, y + dy)))
                            {
                                continue;
                            }

                            var there = here + (diagonal ? 99 : 70);
                            if (there < cost[((y + dy) * grid.Width) + x + dx])
                            {
                                cost[((y + dy) * grid.Width) + x + dx] = there;
                                changed = true;
                            }
                        }
                    }
                }
            }
        }

        return cost[(goalY * grid.Width) + goalX];
    }
}
