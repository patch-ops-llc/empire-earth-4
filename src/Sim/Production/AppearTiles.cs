using Sim.Map;
using Sim.Numerics;

namespace Sim.Production;

/// <summary>
/// Where the units of an order appear (design 5.4, "Where a unit appears"): on the centers of
/// tiles beside the structure.
/// </summary>
/// <remarks>
/// <para>
/// The candidates are the tiles outside the footprint that share a side with it and that a unit
/// can stand on. They are ranked by the distance from their center to a point, which is the rally
/// point, or the structure's own center while none is set; ties go to the lower y, then the lower
/// x. The first unit takes the first tile, and the second unit of a pair the next. When there is
/// no next tile, it takes the same tile as the first.
/// </para>
/// <para>
/// When no tile beside the structure can be stood on, every unit of the order appears on the tile
/// nearest the structure's center that a unit can stand on, with the same ties.
/// </para>
/// </remarks>
internal static class AppearTiles
{
    /// <summary>
    /// Fills <paramref name="tiles"/> with one tile for each unit of an order. Returns false,
    /// with nothing filled, only when no unit can stand anywhere on the map.
    /// </summary>
    /// <param name="grid">The map grid.</param>
    /// <param name="footprint">The tiles of the structure.</param>
    /// <param name="center">The center of the structure.</param>
    /// <param name="toward">The point the tiles are ranked by.</param>
    /// <param name="tiles">One entry for each unit.</param>
    public static bool Find(MapGrid grid, TileSquare footprint, FixedVector2 center, FixedVector2 toward, Span<(int X, int Y)> tiles)
    {
        var last = default(Rank);
        var found = 0;
        while (found < tiles.Length && TryNextBeside(grid, footprint, toward, found == 0 ? null : last, out var next))
        {
            last = next;
            tiles[found++] = (next.X, next.Y);
        }

        if (found == 0)
        {
            if (!TryNearest(grid, footprint, center, out var nearest))
            {
                return false;
            }

            tiles[found++] = nearest;
        }

        for (var i = found; i < tiles.Length; i++)
        {
            tiles[i] = tiles[found - 1];
        }

        return true;
    }

    /// <summary>The best tile beside the footprint that ranks after <paramref name="after"/>, or the best of all when that is null.</summary>
    private static bool TryNextBeside(MapGrid grid, TileSquare footprint, FixedVector2 toward, Rank? after, out Rank best)
    {
        best = default;
        var any = false;
        for (var along = 0; along < footprint.Size; along++)
        {
            Consider(footprint.X + along, footprint.Y - 1, ref best);
            Consider(footprint.X + along, footprint.Y + footprint.Size, ref best);
            Consider(footprint.X - 1, footprint.Y + along, ref best);
            Consider(footprint.X + footprint.Size, footprint.Y + along, ref best);
        }

        return any;

        void Consider(int x, int y, ref Rank best)
        {
            if (!grid.IsPassable(x, y))
            {
                return;
            }

            var rank = new Rank(FixedVector2.DistanceSquaredRaw(MapGrid.TileCenter(x, y), toward), y, x);
            if ((after is not { } previous || rank.CompareTo(previous) > 0) && (!any || rank.CompareTo(best) < 0))
            {
                best = rank;
                any = true;
            }
        }
    }

    /// <summary>The tile nearest the structure's center that a unit can stand on, looked for ring by ring around the footprint.</summary>
    private static bool TryNearest(MapGrid grid, TileSquare footprint, FixedVector2 center, out (int X, int Y) tile)
    {
        var best = default(Rank);
        var any = false;
        var rings = Math.Max(grid.Width, grid.Height);
        for (var ring = 1; ring <= rings; ring++)
        {
            // No tile of this ring or a later one is nearer the center than the middle of a side of this ring.
            long sure = (long)(footprint.Size + (2 * ring) - 1) * (Fixed.RawOne / 2);
            if (any && best.DistanceSquared < sure * sure)
            {
                break;
            }

            var low = -ring;
            var high = footprint.Size - 1 + ring;
            for (var along = low; along <= high; along++)
            {
                Consider(footprint.X + along, footprint.Y + low);
                Consider(footprint.X + along, footprint.Y + high);
                Consider(footprint.X + low, footprint.Y + along);
                Consider(footprint.X + high, footprint.Y + along);
            }
        }

        tile = (best.X, best.Y);
        return any;

        void Consider(int x, int y)
        {
            if (!grid.IsPassable(x, y))
            {
                return;
            }

            var rank = new Rank(FixedVector2.DistanceSquaredRaw(MapGrid.TileCenter(x, y), center), y, x);
            if (!any || rank.CompareTo(best) < 0)
            {
                best = rank;
                any = true;
            }
        }
    }

    /// <summary>A tile with what it is ranked by: nearer first, then the lower y, then the lower x.</summary>
    private readonly record struct Rank(long DistanceSquared, int Y, int X) : IComparable<Rank>
    {
        public int CompareTo(Rank other)
        {
            var order = DistanceSquared.CompareTo(other.DistanceSquared);
            if (order == 0)
            {
                order = Y.CompareTo(other.Y);
            }

            return order != 0 ? order : X.CompareTo(other.X);
        }
    }
}
