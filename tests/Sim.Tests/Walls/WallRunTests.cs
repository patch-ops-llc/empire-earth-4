using Sim.Walls;

namespace Sim.Tests.Walls;

/// <summary>
/// The tiles of a wall order (design 2.5): one tile for each step along the longer axis, the
/// other coordinate from the straight line between the two tile centers, rounded half up.
/// </summary>
public class WallRunTests
{
    public static TheoryData<int, int, int, int, (int X, int Y)[]> Runs() => new()
    {
        { 3, 4, 3, 4, [(3, 4)] },
        { 3, 4, 6, 4, [(3, 4), (4, 4), (5, 4), (6, 4)] },
        { 6, 4, 3, 4, [(6, 4), (5, 4), (4, 4), (3, 4)] },
        { 3, 4, 3, 2, [(3, 4), (3, 3), (3, 2)] },
        // A diagonal: the tiles touch at their corners only.
        { 3, 4, 6, 7, [(3, 4), (4, 5), (5, 6), (6, 7)] },
        { 3, 4, 1, 6, [(3, 4), (2, 5), (1, 6)] },
        // One up over four along: the line is at 1/4, 1/2 and 3/4 of a tile, and the half goes up.
        { 0, 0, 4, 1, [(0, 0), (1, 0), (2, 1), (3, 1), (4, 1)] },
        // One down over four along: at -1/4, -1/2 and -3/4, and the half goes up again, to 0.
        { 0, 1, 4, 0, [(0, 1), (1, 1), (2, 1), (3, 0), (4, 0)] },
        // The longer axis is y.
        { 3, 2, 4, 6, [(3, 2), (3, 3), (4, 4), (4, 5), (4, 6)] },
        { 5, 9, 3, 4, [(5, 9), (5, 8), (4, 7), (4, 6), (3, 5), (3, 4)] },
    };

    [Theory]
    [MemberData(nameof(Runs))]
    public void ARunHasOneTileForEachStepAlongTheLongerAxis(int firstX, int firstY, int lastX, int lastY, (int X, int Y)[] expected)
    {
        Assert.Equal(expected.Length, WallRun.LengthOf(firstX, firstY, lastX, lastY));
        Assert.Equal(expected, TilesOf(firstX, firstY, lastX, lastY));
    }

    /// <summary>Half up is toward the higher coordinate whichever way the run goes, so both ways give the same wall.</summary>
    [Fact]
    public void TheRunBackHasTheSameTiles()
    {
        for (var lastX = -7; lastX <= 7; lastX++)
        {
            for (var lastY = -7; lastY <= 7; lastY++)
            {
                var there = TilesOf(0, 0, lastX, lastY);
                var back = TilesOf(lastX, lastY, 0, 0);
                Array.Reverse(back);
                Assert.Equal(there, back);
            }
        }
    }

    /// <summary>Each tile touches the one before it, at a side or at a corner: the run has no gap a unit could walk through.</summary>
    [Fact]
    public void EveryTileOfARunTouchesTheOneBeforeIt()
    {
        for (var lastX = -9; lastX <= 9; lastX++)
        {
            for (var lastY = -9; lastY <= 9; lastY++)
            {
                var tiles = TilesOf(2, 3, 2 + lastX, 3 + lastY);
                Assert.Equal((2, 3), tiles[0]);
                Assert.Equal((2 + lastX, 3 + lastY), tiles[^1]);
                for (var i = 1; i < tiles.Length; i++)
                {
                    Assert.InRange(Math.Abs(tiles[i].X - tiles[i - 1].X), 0, 1);
                    Assert.InRange(Math.Abs(tiles[i].Y - tiles[i - 1].Y), 0, 1);
                    Assert.NotEqual(tiles[i - 1], tiles[i]);
                }
            }
        }
    }

    private static (int X, int Y)[] TilesOf(int firstX, int firstY, int lastX, int lastY)
    {
        var tiles = new (int X, int Y)[WallRun.LengthOf(firstX, firstY, lastX, lastY)];
        for (var step = 0; step < tiles.Length; step++)
        {
            tiles[step] = WallRun.TileAt(firstX, firstY, lastX, lastY, step);
        }

        return tiles;
    }
}
