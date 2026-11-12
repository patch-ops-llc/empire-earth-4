namespace Sim.Walls;

/// <summary>
/// The tiles of a wall order (design 2.5): a straight run named by its first and its last tile,
/// with one tile for each step along the longer axis. The other coordinate comes from the
/// straight line between the centers of the two tiles, rounded half up.
/// </summary>
/// <remarks>
/// Half up is toward the higher coordinate, whichever way the run goes, so the run from one tile
/// to another and the run back have the same tiles. A run along a diagonal has tiles that touch
/// only at their corners, which still close a line (design 2.5).
/// </remarks>
public static class WallRun
{
    /// <summary>The number of tiles in the run from one tile to another, both included.</summary>
    public static int LengthOf(int firstX, int firstY, int lastX, int lastY) =>
        Math.Max(Math.Abs(lastX - firstX), Math.Abs(lastY - firstY)) + 1;

    /// <summary>The tile of a run at a step, where step 0 is the first tile and the last step is the last tile.</summary>
    public static (int X, int Y) TileAt(int firstX, int firstY, int lastX, int lastY, int step)
    {
        var dx = lastX - firstX;
        var dy = lastY - firstY;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        ArgumentOutOfRangeException.ThrowIfNegative(step);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(step, steps);
        if (steps == 0)
        {
            return (firstX, firstY);
        }

        return Math.Abs(dx) >= Math.Abs(dy)
            ? (firstX + (step * Math.Sign(dx)), firstY + RoundHalfUp((long)step * dy, steps))
            : (firstX + RoundHalfUp((long)step * dx, steps), firstY + (step * Math.Sign(dy)));
    }

    /// <summary>A quotient with a positive divisor, rounded to the nearest whole number, a half going up.</summary>
    private static int RoundHalfUp(long numerator, long divisor)
    {
        var doubled = (2 * numerator) + divisor;
        var quotient = doubled / (2 * divisor);
        if (doubled % (2 * divisor) < 0)
        {
            quotient--;
        }

        return (int)quotient;
    }
}
