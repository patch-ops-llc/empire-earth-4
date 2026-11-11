using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Tests.Map;

/// <summary>What the grid knows beyond single tiles: regions, straight lines, and the nearest place to stand.</summary>
public class MapGridReachTests
{
    private static readonly Fixed Clearance = Fixed.FromRaw(3 * Fixed.RawOne / 8);

    private static FixedVector2 Point(string x, string y) => new(Fraction.Parse(x).ToFixed(), Fraction.Parse(y).ToFixed());

    [Fact]
    public void TilesAUnitCanWalkBetweenShareARegion()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            "..#...",
            "..#.F.",
            "###...",
            "...##.",
            "...#.."));

        // Numbered in the order their first tile is met, row by row.
        Assert.Equal(3, grid.RegionCount);
        Assert.Equal(1, grid.RegionOf(0, 0));
        Assert.Equal(1, grid.RegionOf(1, 1));
        Assert.Equal(2, grid.RegionOf(3, 0));
        Assert.Equal(2, grid.RegionOf(5, 4));
        Assert.Equal(2, grid.RegionOf(4, 4));
        Assert.Equal(3, grid.RegionOf(0, 3));
        Assert.Equal(3, grid.RegionOf(2, 4));

        Assert.Equal(0, grid.RegionOf(2, 0));
        Assert.Equal(0, grid.RegionOf(4, 1));
        Assert.Equal(0, grid.RegionOf(-1, 0));
        Assert.Equal(0, grid.RegionOf(6, 0));
        Assert.Equal(0, grid.RegionOf(0, 5));

        Assert.Equal(2, grid.RegionOf(Point("3.99", "0.01")));
        Assert.Equal(0, grid.RegionOf(Point("2", "0.5")));
    }

    /// <summary>Design 2.5: walls that touch at a corner hold. Tiles that touch only at a corner do not connect.</summary>
    [Fact]
    public void TilesThatTouchOnlyAtACornerAreNotConnected()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            "..#",
            ".#.",
            "#.."));

        Assert.Equal(2, grid.RegionCount);
        Assert.Equal(1, grid.RegionOf(0, 0));
        Assert.Equal(1, grid.RegionOf(1, 0));
        Assert.Equal(1, grid.RegionOf(0, 1));
        Assert.Equal(2, grid.RegionOf(2, 1));
        Assert.Equal(2, grid.RegionOf(1, 2));
        Assert.Equal(2, grid.RegionOf(2, 2));
    }

    [Fact]
    public void AMapWithNoOpenGroundHasNoRegion()
    {
        var grid = new MapGrid(TestMaps.FromRows("#F", "F#"));

        Assert.Equal(0, grid.RegionCount);
        Assert.False(grid.TryFindNearestTile(Point("0.5", "0.5"), 1, Point("0.5", "0.5"), out _, out _));
    }

    [Fact]
    public void AStraightLineIsClearWhenEveryTileItCrossesIsPassable()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            "......",
            "..#...",
            "......",
            "...F..",
            "......"));

        Assert.True(grid.IsSegmentClear(Point("0.5", "0.5"), Point("5.5", "0.5")));
        Assert.True(grid.IsSegmentClear(Point("0.5", "0.5"), Point("0.5", "4.5")));
        Assert.True(grid.IsSegmentClear(Point("0.5", "2.5"), Point("5.5", "2.5")));
        Assert.True(grid.IsSegmentClear(Point("0.5", "4.5"), Point("5.5", "0.5")));
        Assert.True(grid.IsSegmentClear(Point("1.5", "1.5"), Point("1.5", "1.5")));

        Assert.False(grid.IsSegmentClear(Point("0.5", "1.5"), Point("5.5", "1.5")));
        Assert.False(grid.IsSegmentClear(Point("5.5", "1.5"), Point("0.5", "1.5")));
        Assert.False(grid.IsSegmentClear(Point("0.5", "0.5"), Point("4.5", "2.5")));
        Assert.False(grid.IsSegmentClear(Point("3.5", "0.5"), Point("3.5", "4.5")));
        Assert.False(grid.IsSegmentClear(Point("0.5", "0.5"), Point("5.5", "4.5")));
    }

    [Fact]
    public void BothEndsOfALineCount()
    {
        var grid = new MapGrid(TestMaps.FromRows("..#.."));

        Assert.False(grid.IsSegmentClear(Point("0.5", "0.5"), Point("2.5", "0.5")));
        Assert.False(grid.IsSegmentClear(Point("2.5", "0.5"), Point("0.5", "0.5")));
        Assert.False(grid.IsSegmentClear(Point("2.5", "0.5"), Point("2.5", "0.5")));
        Assert.False(grid.IsSegmentClear(Point("0.5", "0.5"), Point("-0.5", "0.5")));
        Assert.False(grid.IsSegmentClear(Point("0.5", "0.5"), Point("0.5", "1")));
        Assert.False(grid.IsSegmentClear(Point("7.5", "0.5"), Point("4.5", "0.5")));

        // A point on a tile's low edge belongs to that tile; a point on its high edge to the next.
        Assert.True(grid.IsSegmentClear(Point("0.5", "0.5"), Point("1.99", "0.5")));
        Assert.False(grid.IsSegmentClear(Point("0.5", "0.5"), Point("2", "0.5")));
        Assert.True(grid.IsSegmentClear(Point("4.5", "0.5"), Point("3", "0.5")));
    }

    /// <summary>
    /// A line through the exact corner where four tiles meet needs all four. Without that rule a
    /// line could slip between two blocked tiles that touch at a corner.
    /// </summary>
    [Fact]
    public void ALineThroughACornerNeedsAllFourTilesAroundIt()
    {
        var crossed = new MapGrid(TestMaps.FromRows(
            ".#",
            "#."));
        Assert.False(crossed.IsSegmentClear(Point("0.5", "0.5"), Point("1.5", "1.5")));
        Assert.False(crossed.IsSegmentClear(Point("1.5", "1.5"), Point("0.5", "0.5")));

        foreach (var rows in (string[][])[["..", "#."], [".#", ".."]])
        {
            var grid = new MapGrid(TestMaps.FromRows(rows));
            Assert.False(grid.IsSegmentClear(Point("0.5", "0.5"), Point("1.5", "1.5")));
            Assert.False(grid.IsSegmentClear(Point("1.5", "1.5"), Point("0.5", "0.5")));
            Assert.False(grid.IsSegmentClear(Point("0.25", "0.25"), Point("1.75", "1.75")));
        }

        var open = new MapGrid(TestMaps.Open(2, 2));
        Assert.True(open.IsSegmentClear(Point("0.5", "0.5"), Point("1.5", "1.5")));
        Assert.True(open.IsSegmentClear(Point("0.5", "1.5"), Point("1.5", "0.5")));
        Assert.True(open.IsSegmentClear(Point("1.5", "0.5"), Point("0.5", "1.5")));

        // A line that only passes near the corner crosses three tiles, not four.
        var oneBlocked = new MapGrid(TestMaps.FromRows("..", "#."));
        Assert.True(oneBlocked.IsSegmentClear(Point("0.5", "0.5"), Point("1.5", "1.25")));
        Assert.False(oneBlocked.IsSegmentClear(Point("0.5", "0.5"), Point("1.25", "1.5")));
    }

    /// <summary>
    /// Checked against a separate, exact test of the line against each blocked tile's square. A line
    /// that goes through the inside of a blocked tile is never clear; a line that touches no blocked
    /// tile, not even its edge, always is. Lines that only touch an edge or a corner may be either:
    /// those are decided by which tile a point on a grid line belongs to and by the corner rule.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void LinesAgreeWithAnExactTestAgainstEveryBlockedTile(ulong seed)
    {
        var random = new DeterministicRandom(seed);
        const int Size = 12;
        var map = RandomMap(random, Size, Size, blockedInTen: 2);
        var grid = new MapGrid(map);
        var touching = 0;
        var clear = 0;

        for (var i = 0; i < 6_000; i++)
        {
            // A third of the lines start and end on quarter tiles, to land on grid lines and corners.
            var coarse = i % 3 == 0;
            var from = RandomPoint(random, Size, coarse);
            var to = RandomPoint(random, Size, coarse);

            var throughBlocked = false;
            var touchesBlocked = false;
            for (var y = -1; y <= Size; y++)
            {
                for (var x = -1; x <= Size; x++)
                {
                    if (grid.IsPassable(x, y))
                    {
                        continue;
                    }

                    throughBlocked |= Crosses(from, to, x, y, inside: true);
                    touchesBlocked |= Crosses(from, to, x, y, inside: false);
                }
            }

            var result = grid.IsSegmentClear(from, to);
            if (throughBlocked)
            {
                Assert.False(result, $"{from} to {to} goes through a blocked tile.");
            }
            else if (!touchesBlocked)
            {
                Assert.True(result, $"{from} to {to} touches no blocked tile.");
                clear++;
            }
            else
            {
                touching++;
            }

            // Walked the other way a line gives the same answer, unless it runs along a blocked
            // tile's edge, which then belongs to the tile on the other side.
            if (throughBlocked || !touchesBlocked)
            {
                Assert.Equal(result, grid.IsSegmentClear(to, from));
            }
        }

        Assert.InRange(clear, 150, 6_000);
        Assert.InRange(touching, 1, 600);
    }

    [Fact]
    public void ACorridorIsClearWhenNothingBlockedReachesIntoIt()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            "........",
            "...#....",
            "........",
            "........",
            "###.####",
            "........"));

        // Past the cliff at (3, 1): the line itself is clear from y = 2 on, the corridor only from 2.375.
        Assert.True(grid.IsSegmentClear(Point("0.5", "2.25"), Point("7.5", "2.25")));
        Assert.False(grid.IsCorridorClear(Point("0.5", "2.25"), Point("7.5", "2.25"), Clearance));
        Assert.True(grid.IsCorridorClear(Point("0.5", "2.375"), Point("7.5", "2.375"), Clearance));
        Assert.True(grid.IsCorridorClear(Point("0.5", "2.5"), Point("7.5", "2.5"), Clearance));

        // Through the gap at (3, 4), one tile wide: only close to its middle.
        Assert.True(grid.IsCorridorClear(Point("3.5", "3.5"), Point("3.5", "5.5"), Clearance));
        Assert.True(grid.IsCorridorClear(Point("3.5", "5.5"), Point("3.5", "3.5"), Clearance));
        Assert.True(grid.IsCorridorClear(Point("3.4", "3.5"), Point("3.4", "5.5"), Clearance));
        Assert.False(grid.IsCorridorClear(Point("3.3", "3.5"), Point("3.3", "5.5"), Clearance));
        Assert.False(grid.IsCorridorClear(Point("3.7", "3.5"), Point("3.7", "5.5"), Clearance));
        Assert.True(grid.IsSegmentClear(Point("3.3", "3.5"), Point("3.3", "5.5")));

        // Slanting through the gap is too tight for a corridor, though a bare line fits.
        Assert.True(grid.IsSegmentClear(Point("3.1", "3.5"), Point("3.9", "5.5")));
        Assert.False(grid.IsCorridorClear(Point("3.1", "3.5"), Point("3.9", "5.5"), Clearance));

        // With no width a corridor is its line, and a point is its own corridor.
        Assert.True(grid.IsCorridorClear(Point("3.1", "3.5"), Point("3.9", "5.5"), Fixed.Zero));
        Assert.True(grid.IsCorridorClear(Point("0.1", "0.1"), Point("0.1", "0.1"), Clearance));

        // However short the line, its corridor is as wide as any: here it fits beside the edge of
        // the map slanting, with 0.3 of a tile to spare for its 0.27, and not straight down.
        var slanting = new FixedVector2(Fixed.Epsilon, Fixed.Epsilon);
        Assert.True(grid.IsCorridorClear(Point("0.3", "0.5"), Point("0.3", "0.5") + slanting, Clearance));
        Assert.True(grid.IsCorridorClear(Point("0.3", "0.5") + slanting, Point("0.3", "0.5"), Clearance));
        Assert.False(grid.IsCorridorClear(Point("0.3", "0.5"), Point("0.3", "0.5") + new FixedVector2(Fixed.Zero, Fixed.Epsilon), Clearance));

        // The corridor ends square. The corner of the cliff reaching into its end closes it, though
        // the line and both side lines pass the cliff by; a cliff straight ahead of its end does not.
        Assert.False(grid.IsCorridorClear(Point("4.1", "1.8"), Point("5.1", "2.8"), Clearance));
        Assert.False(grid.IsCorridorClear(Point("5.1", "2.8"), Point("4.1", "1.8"), Clearance));
        Assert.True(grid.IsCorridorClear(Point("4.2", "1.9"), Point("5.1", "2.8"), Clearance));
        Assert.True(grid.IsCorridorClear(Point("3.5", "3.5"), Point("3.5", "2.01"), Clearance));
        Assert.False(grid.IsCorridorClear(Point("3.5", "1.5"), Point("3.5", "1.5"), Clearance));

        // Along the edge of the map, which is blocked ground like any other.
        Assert.False(grid.IsCorridorClear(Point("0.5", "0.25"), Point("7.5", "0.25"), Clearance));
        Assert.True(grid.IsCorridorClear(Point("0.5", "0.5"), Point("7.5", "0.5"), Clearance));

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.IsCorridorClear(Point("1", "1"), Point("2", "2"), Fixed.Half));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.IsCorridorClear(Point("1", "1"), Point("2", "2"), -Fixed.Epsilon));
    }

    /// <summary>Every point inside a clear corridor is on passable ground, checked point by point.</summary>
    [Theory]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void NothingBlockedLiesInsideAClearCorridor(ulong seed)
    {
        var random = new DeterministicRandom(seed);
        const int Size = 14;
        var grid = new MapGrid(RandomMap(random, Size, Size, blockedInTen: 1));
        var clear = 0;

        for (var i = 0; i < 1_500; i++)
        {
            var from = RandomPoint(random, Size, coarse: false);
            var to = RandomPoint(random, Size, coarse: false);
            if (!grid.IsCorridorClear(from, to, Clearance))
            {
                continue;
            }

            clear++;
            Assert.True(grid.IsCorridorClear(to, from, Clearance));

            var along = to - from;
            var length = along.Length();
            if (length < Fixed.One / 16)
            {
                continue;
            }

            // Points across the corridor, up to fifteen sixteenths of the way to each side.
            var across = new FixedVector2(-along.Y, along.X);
            for (var step = 0; step <= 64; step++)
            {
                var onLine = from + (along * step / 64);
                for (var side = -15; side <= 15; side++)
                {
                    var offset = FixedVector2.MoveTowards(FixedVector2.Zero, across * side, Clearance * Math.Abs(side) / 16);
                    Assert.True(grid.IsPassable(onLine + offset), $"{onLine + offset} is blocked inside the corridor from {from} to {to}.");
                }
            }
        }

        Assert.InRange(clear, 75, 1_500);
    }

    [Fact]
    public void MirroredLinesOnAMirroredMapGiveTheSameAnswer()
    {
        var random = new DeterministicRandom(8);
        const int Size = 12;
        var half = RandomMap(random, Size / 2, Size, blockedInTen: 2);
        var rows = new string[Size];
        for (var y = 0; y < Size; y++)
        {
            var left = string.Concat(Enumerable.Range(0, Size / 2).Select(x => half.TerrainAt(x, y) == TerrainKind.Open ? '.' : '#'));
            rows[y] = left + string.Concat(left.Reverse());
        }

        var grid = new MapGrid(TestMaps.FromRows(rows));
        FixedVector2 Mirror(FixedVector2 point) => new(Fixed.FromInt(Size) - point.X, point.Y);

        for (var i = 0; i < 5_000; i++)
        {
            // Off the grid lines: a point exactly on one belongs to the tile on its high side, and
            // its mirror image then belongs to the wrong one.
            var from = RandomPoint(random, Size, coarse: false);
            var to = RandomPoint(random, Size, coarse: false);
            if (from.X.Raw % Fixed.RawOne == 0 || to.X.Raw % Fixed.RawOne == 0)
            {
                continue;
            }

            Assert.Equal(grid.IsSegmentClear(from, to), grid.IsSegmentClear(Mirror(from), Mirror(to)));
            Assert.Equal(grid.IsCorridorClear(from, to, Clearance), grid.IsCorridorClear(Mirror(from), Mirror(to), Clearance));
            Assert.Equal(Mirror(grid.KeepClear(from, Clearance)), grid.KeepClear(Mirror(from), Clearance));
        }
    }

    [Fact]
    public void APointIsMovedOffTheBlockedGroundItsTileTouches()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            ".....",
            ".....",
            "..#..",
            ".....",
            "....."));
        var margin = Fixed.One / 4;

        // In the open, and at the far side of a tile beside the cliff: left alone.
        Assert.Equal(Point("0.5", "1.5"), grid.KeepClear(Point("0.5", "1.5"), margin));
        Assert.Equal(Point("1.25", "2.9"), grid.KeepClear(Point("1.25", "2.9"), margin));

        // Beside the cliff, on each of its four sides.
        Assert.Equal(Point("1.75", "2.5"), grid.KeepClear(Point("1.99", "2.5"), margin));
        Assert.Equal(Point("3.25", "2.5"), grid.KeepClear(Point("3", "2.5"), margin));
        Assert.Equal(Point("2.5", "1.75"), grid.KeepClear(Point("2.5", "1.9"), margin));
        Assert.Equal(Point("2.5", "3.25"), grid.KeepClear(Point("2.5", "3.1"), margin));

        // Touching it by a corner alone: moved off the corner by the shorter way, x when equal.
        Assert.Equal(Point("1.75", "1.9"), grid.KeepClear(Point("1.9", "1.9"), margin));
        Assert.Equal(Point("3.25", "3.1"), grid.KeepClear(Point("3.1", "3.1"), margin));
        Assert.Equal(Point("1.9", "1.75"), grid.KeepClear(Point("1.9", "1.8"), margin));
        Assert.Equal(Point("1.75", "3.1"), grid.KeepClear(Point("1.8", "3.1"), margin));
        Assert.Equal(Point("3.25", "1.875"), grid.KeepClear(Point("3.2", "1.875"), margin));
        Assert.Equal(Point("3.05", "1.75"), grid.KeepClear(Point("3.05", "1.9"), margin));
        Assert.Equal(Point("1.7", "1.9"), grid.KeepClear(Point("1.7", "1.9"), margin));
        Assert.Equal(Point("1.9", "1.5"), grid.KeepClear(Point("1.9", "1.5"), margin));

        // The edge of the map is blocked ground too.
        Assert.Equal(Point("0.25", "0.25"), grid.KeepClear(Point("0", "0"), margin));
        Assert.Equal(Point("4.75", "4.75"), grid.KeepClear(Point("4.99", "4.99"), margin));
        Assert.Equal(Point("2.5", "0.25"), grid.KeepClear(Point("2.5", "0.1"), margin));

        // Half a tile is the most: it leaves the middle of a passage one tile wide.
        var passage = new MapGrid(TestMaps.FromRows("#.#", "#.#", "#.#"));
        Assert.Equal(Point("1.5", "1.2"), passage.KeepClear(Point("1.1", "1.2"), Fixed.Half));
        Assert.Equal(Point("1.5", "1.2"), passage.KeepClear(Point("1.9", "1.2"), Fixed.Half));
        Assert.Equal(Point("1.5", "0.5"), passage.KeepClear(Point("1.9", "0.2"), Fixed.Half));

        Assert.Equal(Point("1.99", "2.5"), grid.KeepClear(Point("1.99", "2.5"), Fixed.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.KeepClear(Point("1", "1"), Fixed.Half + Fixed.Epsilon));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.KeepClear(Point("1", "1"), -Fixed.Epsilon));
    }

    [Fact]
    public void TheNearestTileOfARegionIsFoundByItsCenter()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            ".....#..",
            ".FFF.#..",
            ".FFF.#..",
            ".FFF.#..",
            ".....#.."));
        var left = grid.RegionOf(0, 0);
        var right = grid.RegionOf(7, 0);
        var far = Point("0.5", "0.5");

        // A point on a tile of the region: that tile.
        Assert.True(grid.TryFindNearestTile(Point("4.9", "2.1"), left, far, out var x, out var y));
        Assert.Equal((4, 2), (x, y));

        // In the forest, nearer its right side.
        Assert.True(grid.TryFindNearestTile(Point("3.5", "2.5"), left, far, out x, out y));
        Assert.Equal((4, 2), (x, y));
        Assert.True(grid.TryFindNearestTile(Point("2.6", "1.4"), left, far, out x, out y));
        Assert.Equal((2, 0), (x, y));
        Assert.True(grid.TryFindNearestTile(Point("3.9", "1.3"), left, far, out x, out y));
        Assert.Equal((4, 1), (x, y));

        // Across the cliff, in the other region: the nearest tile of the region asked for.
        Assert.True(grid.TryFindNearestTile(Point("7.5", "3.5"), left, far, out x, out y));
        Assert.Equal((4, 3), (x, y));
        Assert.True(grid.TryFindNearestTile(Point("0.5", "4.5"), right, far, out x, out y));
        Assert.Equal((6, 4), (x, y));

        Assert.False(grid.TryFindNearestTile(Point("2.5", "2.5"), 0, far, out _, out _));
        Assert.False(grid.TryFindNearestTile(Point("2.5", "2.5"), 3, far, out _, out _));
        Assert.Throws<ArgumentException>(() => grid.TryFindNearestTile(Point("8", "2.5"), left, far, out _, out _));
    }

    [Fact]
    public void AmongTilesEquallyNearTheOneNearestThePreferredPointWins()
    {
        var grid = new MapGrid(TestMaps.FromRows(
            ".....",
            ".FFF.",
            ".FFF.",
            ".FFF.",
            "....."));
        var middle = Point("2.5", "2.5");

        // The four tiles beside the middle of each side are equally near the middle of the forest.
        (string X, string Y, int TileX, int TileY)[] cases =
        [
            ("0.5", "2.5", 0, 2),
            ("4.5", "2.5", 4, 2),
            ("2.5", "0.5", 2, 0),
            ("2.5", "4.5", 2, 4),
            ("3.5", "4.5", 2, 4),
            ("4.5", "3.5", 4, 2),

            // Two of them equally near the preferred point: the lower y.
            ("4.5", "4.5", 4, 2),
            ("0.5", "0.5", 2, 0),
        ];

        foreach (var (preferX, preferY, tileX, tileY) in cases)
        {
            Assert.True(grid.TryFindNearestTile(middle, 1, Point(preferX, preferY), out var x, out var y));
            Assert.Equal((tileX, tileY), (x, y));
        }

        // With nothing to choose by, the lower y and then the lower x.
        Assert.True(grid.TryFindNearestTile(middle, 1, middle, out var firstX, out var firstY));
        Assert.Equal((2, 0), (firstX, firstY));
    }

    /// <summary>Checked against a walk over every tile of the map.</summary>
    [Fact]
    public void TheNearestTileIsTheSameAsASearchOfTheWholeMap()
    {
        var random = new DeterministicRandom(21);
        const int Size = 16;
        var grid = new MapGrid(RandomMap(random, Size, Size, blockedInTen: 6));
        Assert.InRange(grid.RegionCount, 3, 200);

        for (var i = 0; i < 3_000; i++)
        {
            var point = RandomPoint(random, Size, coarse: i % 2 == 0);
            var prefer = RandomPoint(random, Size, coarse: i % 4 == 0);
            var region = 1 + random.NextInt(grid.RegionCount);

            (long Distance, long Preference, int Y, int X) best = (long.MaxValue, 0, 0, 0);
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    if (grid.RegionOf(x, y) != region)
                    {
                        continue;
                    }

                    var center = MapGrid.TileCenter(x, y);
                    var candidate = (FixedVector2.DistanceSquaredRaw(center, point), FixedVector2.DistanceSquaredRaw(center, prefer), y, x);
                    if (candidate.CompareTo(best) < 0)
                    {
                        best = candidate;
                    }
                }
            }

            Assert.True(grid.TryFindNearestTile(point, region, prefer, out var tileX, out var tileY));
            Assert.Equal((best.X, best.Y), (tileX, tileY));
        }
    }

    internal static MapDef RandomMap(DeterministicRandom random, int width, int height, int blockedInTen)
    {
        var rows = new string[height];
        for (var y = 0; y < height; y++)
        {
            rows[y] = string.Concat(Enumerable.Range(0, width).Select(_ => random.NextInt(10) < blockedInTen ? '#' : '.'));
        }

        return TestMaps.FromRows(rows);
    }

    private static FixedVector2 RandomPoint(DeterministicRandom random, int size, bool coarse)
    {
        var grain = coarse ? Fixed.RawOne / 4 : 1;
        return new FixedVector2(
            Fixed.FromRaw(random.NextInt(size * Fixed.RawOne / grain) * grain),
            Fixed.FromRaw(random.NextInt(size * Fixed.RawOne / grain) * grain));
    }

    /// <summary>
    /// True when the line from one point to another meets the square of a tile: its inside alone,
    /// or its edges too. Exact: the stretch of the line inside the square is found as fractions of
    /// the line's length and compared without dividing.
    /// </summary>
    private static bool Crosses(FixedVector2 from, FixedVector2 to, int tileX, int tileY, bool inside)
    {
        // The part of the line inside the square is where low <= t <= high, each a fraction.
        (Int128 Numerator, Int128 Denominator) low = (0, 1);
        (Int128 Numerator, Int128 Denominator) high = (1, 1);

        foreach (var (start, end, tile) in ((long, long, int)[])[(from.X.Raw, to.X.Raw, tileX), (from.Y.Raw, to.Y.Raw, tileY)])
        {
            var near = (long)tile * Fixed.RawOne;
            var far = near + Fixed.RawOne;
            var delta = end - start;
            if (delta == 0)
            {
                if (inside ? start <= near || start >= far : start < near || start > far)
                {
                    return false;
                }

                continue;
            }

            // Entering at (near - start) / delta and leaving at (far - start) / delta, or the other
            // way round when the line runs down the axis. Denominators are kept positive.
            var enter = delta > 0 ? (near - start, delta) : (start - far, -delta);
            var leave = delta > 0 ? (far - start, delta) : (start - near, -delta);
            if ((Int128)enter.Item1 * low.Denominator > low.Numerator * enter.Item2)
            {
                low = (enter.Item1, enter.Item2);
            }

            if ((Int128)leave.Item1 * high.Denominator < high.Numerator * leave.Item2)
            {
                high = (leave.Item1, leave.Item2);
            }
        }

        var order = (low.Numerator * high.Denominator).CompareTo(high.Numerator * low.Denominator);
        return inside ? order < 0 : order <= 0;
    }
}
