using Sim.Data;
using Sim.Numerics;

namespace Sim.Map;

/// <summary>
/// The tile grid of a match, built from a <see cref="MapDef"/>: which tiles units can stand on,
/// which of them connect, and whether a straight walk between two points is free.
/// Open ground is passable; forest and cliff are not (design 5.3), and neither is anything outside
/// the map.
/// </summary>
/// <remarks>
/// Tile (x, y) covers the points from (x, y) up to but not including (x + 1, y + 1), with (0, 0) at
/// the top-left corner of the map and y growing downward. The terrain never changes during a match,
/// so the grid is not part of the state hash.
/// </remarks>
public sealed class MapGrid
{
    /// <summary>The most tiles a map can have on one side. Squared distances across it stay far inside 64 bits.</summary>
    public const int MaxSize = 4096;

    private readonly bool[] _passable;
    private readonly int[] _regions;

    public MapGrid(MapDef map)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentOutOfRangeException.ThrowIfLessThan(map.Width, 1, nameof(map));
        ArgumentOutOfRangeException.ThrowIfLessThan(map.Height, 1, nameof(map));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(map.Width, MaxSize, nameof(map));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(map.Height, MaxSize, nameof(map));
        if (map.Tiles.Count != map.Width * map.Height)
        {
            throw new ArgumentException(
                $"Map '{map.Id}' is {map.Width} by {map.Height} but has {map.Tiles.Count} tiles.", nameof(map));
        }

        Width = map.Width;
        Height = map.Height;
        _passable = new bool[Width * Height];
        for (var i = 0; i < _passable.Length; i++)
        {
            _passable[i] = map.Tiles[i] == TerrainKind.Open;
        }

        _regions = new int[_passable.Length];
        RegionCount = LabelRegions();
    }

    /// <summary>The number of tiles along x.</summary>
    public int Width { get; }

    /// <summary>The number of tiles along y.</summary>
    public int Height { get; }

    /// <summary>The number of regions. Regions are numbered from 1.</summary>
    public int RegionCount { get; }

    /// <summary>The center of a tile, where a unit standing on it is placed.</summary>
    public static FixedVector2 TileCenter(int x, int y) =>
        new(Fixed.FromInt(x) + Fixed.Half, Fixed.FromInt(y) + Fixed.Half);

    /// <summary>True when the tile is on the map.</summary>
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>True when the point is on the map: inside one of its tiles.</summary>
    public bool Contains(FixedVector2 point) => Contains(point.X.FloorToInt(), point.Y.FloorToInt());

    /// <summary>True when units can stand on the tile. False for every tile outside the map.</summary>
    public bool IsPassable(int x, int y) => Contains(x, y) && _passable[(y * Width) + x];

    /// <summary>True when units can stand on the tile that holds the point.</summary>
    public bool IsPassable(FixedVector2 point) => IsPassable(point.X.FloorToInt(), point.Y.FloorToInt());

    /// <summary>
    /// The region of a tile: a number from 1 that two passable tiles share exactly when a unit can
    /// walk from one to the other. Zero for a tile no unit can stand on. "Cannot reach" is this
    /// lookup, with no search.
    /// </summary>
    /// <remarks>
    /// A walk goes from a tile to one beside it, or diagonally when both tiles beside that step are
    /// passable too, so tiles that touch only at a corner do not connect.
    /// </remarks>
    public int RegionOf(int x, int y) => Contains(x, y) ? _regions[(y * Width) + x] : 0;

    /// <summary>The region of the tile that holds the point.</summary>
    public int RegionOf(FixedVector2 point) => RegionOf(point.X.FloorToInt(), point.Y.FloorToInt());

    /// <summary>
    /// True when a point can go straight from one place to the other without leaving passable
    /// tiles. Both ends count.
    /// </summary>
    /// <remarks>
    /// Exact, in whole numbers. A line that passes exactly through the corner where four tiles meet
    /// needs all four to be passable, so it never slips between two blocked tiles that touch at a
    /// corner.
    /// </remarks>
    public bool IsSegmentClear(FixedVector2 from, FixedVector2 to)
    {
        long fromX = from.X.Raw;
        long fromY = from.Y.Raw;
        var tileX = from.X.FloorToInt();
        var tileY = from.Y.FloorToInt();
        var endX = to.X.FloorToInt();
        var endY = to.Y.FloorToInt();
        if (!IsPassable(tileX, tileY))
        {
            return false;
        }

        var stepX = Math.Sign(to.X.Raw - fromX);
        var stepY = Math.Sign(to.Y.Raw - fromY);
        var lengthX = Math.Abs(to.X.Raw - fromX);
        var lengthY = Math.Abs(to.Y.Raw - fromY);

        while (tileX != endX || tileY != endY)
        {
            // How far along each axis the line still has to go to leave the tile. Going up an
            // axis the next tile begins on the grid line; going down it begins just past it.
            var leftX = stepX > 0 ? ((long)(tileX + 1) << Fixed.FractionalBits) - fromX : fromX - ((long)tileX << Fixed.FractionalBits);
            var leftY = stepY > 0 ? ((long)(tileY + 1) << Fixed.FractionalBits) - fromY : fromY - ((long)tileY << Fixed.FractionalBits);

            // The line leaves through the side it reaches first: leftX / lengthX against leftY / lengthY.
            var order = tileY == endY ? -1
                : tileX == endX ? 1
                : (leftX * lengthY).CompareTo(leftY * lengthX);
            if (order < 0)
            {
                tileX += stepX;
            }
            else if (order > 0)
            {
                tileY += stepY;
            }
            else
            {
                if (!IsPassable(tileX + stepX, tileY) || !IsPassable(tileX, tileY + stepY))
                {
                    return false;
                }

                tileX += stepX;
                tileY += stepY;
            }

            if (!IsPassable(tileX, tileY))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when something as wide as twice <paramref name="halfWidth"/> can go straight from one
    /// place to the other: no blocked tile reaches into the rectangle that has the line as its
    /// middle and extends half a width to each side of it. A unit walking such a line keeps that
    /// distance, sideways, from every blocked tile.
    /// </summary>
    /// <remarks>
    /// The half width must be below half a tile. The rectangle is then too narrow to hold a tile,
    /// so it is free when its four edges are, and those are what is tested, after the line itself.
    /// The rectangle ends square at both ends: a blocked tile straight ahead of the far end, or
    /// behind the near end, may be closer than the half width. Mirrored input gives the same answer.
    /// </remarks>
    public bool IsCorridorClear(FixedVector2 from, FixedVector2 to, Fixed halfWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(halfWidth.Raw, nameof(halfWidth));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(halfWidth.Raw, Fixed.RawOne / 2, nameof(halfWidth));

        if (!IsSegmentClear(from, to))
        {
            return false;
        }

        if (from == to || halfWidth == Fixed.Zero)
        {
            return true;
        }

        // The offset to a side line: the line's direction turned a quarter turn, at the half width.
        // A short line has its length measured 65,536 times finer, so that rounding the length
        // down does not stretch the offset.
        var alongX = (long)to.X.Raw - from.X.Raw;
        var alongY = (long)to.Y.Raw - from.Y.Raw;
        var lengthSquared = checked((alongX * alongX) + (alongY * alongY));
        long scale = lengthSquared < 1L << 30 ? Fixed.RawOne : 1;
        long length = IntegerMath.Sqrt((ulong)(lengthSquared * scale * scale));
        var side = new FixedVector2(
            Fixed.FromRaw((int)DivideRounded(-alongY * halfWidth.Raw * scale, length)),
            Fixed.FromRaw((int)DivideRounded(alongX * halfWidth.Raw * scale, length)));

        return IsSegmentClear(from + side, to + side)
            && IsSegmentClear(from - side, to - side)
            && IsSegmentClear(from + side, from - side)
            && IsSegmentClear(to + side, to - side);
    }

    /// <summary>
    /// A point of the same tile, at or near the given one, that keeps a distance from blocked
    /// ground: at least <paramref name="margin"/> from every blocked tile around its own. A point
    /// that already does is returned as it is.
    /// </summary>
    /// <remarks>
    /// The point is moved straight off each side of its tile that has a blocked tile behind it.
    /// Where only a corner of its tile touches a blocked tile, it is moved off that corner along
    /// the axis that takes the shorter move, x when they are equal. The margin must be at most half
    /// a tile, so that a tile with blocked ground on both sides still has a place to stand.
    /// Mirrored input gives the mirrored point.
    /// </remarks>
    public FixedVector2 KeepClear(FixedVector2 point, Fixed margin)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(margin.Raw, nameof(margin));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(margin.Raw, Fixed.RawOne / 2, nameof(margin));

        var tileX = point.X.FloorToInt();
        var tileY = point.Y.FloorToInt();
        var lowX = Fixed.FromInt(tileX);
        var lowY = Fixed.FromInt(tileY);
        var highX = lowX + Fixed.One;
        var highY = lowY + Fixed.One;
        var x = point.X;
        var y = point.Y;

        var left = !IsPassable(tileX - 1, tileY);
        var right = !IsPassable(tileX + 1, tileY);
        var above = !IsPassable(tileX, tileY - 1);
        var below = !IsPassable(tileX, tileY + 1);
        x = left ? Fixed.Max(x, lowX + margin) : x;
        x = right ? Fixed.Min(x, highX - margin) : x;
        y = above ? Fixed.Max(y, lowY + margin) : y;
        y = below ? Fixed.Min(y, highY - margin) : y;

        // The corners that alone touch a blocked tile. A corner between two blocked sides, or
        // beside one, is already far enough.
        OffCorner(-1, -1, left || above, lowX, lowY);
        OffCorner(1, -1, right || above, highX, lowY);
        OffCorner(-1, 1, left || below, lowX, highY);
        OffCorner(1, 1, right || below, highX, highY);
        return new FixedVector2(x, y);

        void OffCorner(int stepX, int stepY, bool sideBlocked, Fixed cornerX, Fixed cornerY)
        {
            if (sideBlocked || IsPassable(tileX + stepX, tileY + stepY))
            {
                return;
            }

            var shortX = margin - Fixed.Abs(x - cornerX);
            var shortY = margin - Fixed.Abs(y - cornerY);
            if (shortX <= Fixed.Zero || shortY <= Fixed.Zero)
            {
                return;
            }

            if (shortX <= shortY)
            {
                x = cornerX - (margin * stepX);
            }
            else
            {
                y = cornerY - (margin * stepY);
            }
        }
    }

    /// <summary>
    /// Finds the tile of a region whose center is nearest a point. Among tiles equally near, the
    /// one nearest <paramref name="preferNear"/> wins, then the one with the lower y, then the
    /// lower x. The point must be on the map. Returns false when there is no such region.
    /// </summary>
    public bool TryFindNearestTile(FixedVector2 point, int region, FixedVector2 preferNear, out int tileX, out int tileY)
    {
        tileX = -1;
        tileY = -1;
        var centerX = point.X.FloorToInt();
        var centerY = point.Y.FloorToInt();
        if (!Contains(centerX, centerY))
        {
            throw new ArgumentException($"The point {point} is not on the map.", nameof(point));
        }

        if (region < 1 || region > RegionCount)
        {
            return false;
        }

        var best = long.MaxValue;
        var bestPreference = long.MaxValue;
        var rings = Math.Max(Width, Height);
        for (var ring = 0; ring <= rings; ring++)
        {
            // Every tile of this ring and beyond is further than the ring before it is wide.
            long sure = (long)(ring - 1) << Fixed.FractionalBits;
            if (ring > 0 && best <= sure * sure)
            {
                break;
            }

            for (var y = centerY - ring; y <= centerY + ring; y++)
            {
                var onEdgeRow = y == centerY - ring || y == centerY + ring;
                for (var x = centerX - ring; x <= centerX + ring; x += onEdgeRow ? 1 : Math.Max(2 * ring, 1))
                {
                    if (RegionOf(x, y) != region)
                    {
                        continue;
                    }

                    var center = TileCenter(x, y);
                    var distance = FixedVector2.DistanceSquaredRaw(center, point);
                    var preference = FixedVector2.DistanceSquaredRaw(center, preferNear);
                    var better = distance != best ? distance < best
                        : preference != bestPreference ? preference < bestPreference
                        : y != tileY ? y < tileY
                        : x < tileX;
                    if (better)
                    {
                        best = distance;
                        bestPreference = preference;
                        tileX = x;
                        tileY = y;
                    }
                }
            }
        }

        return tileX >= 0;
    }

    /// <summary>Divides by a positive number and rounds to the nearest whole number, with a tie going away from zero.</summary>
    private static long DivideRounded(long numerator, long denominator)
    {
        var quotient = ((2 * Math.Abs(numerator)) + denominator) / (2 * denominator);
        return numerator < 0 ? -quotient : quotient;
    }

    /// <summary>
    /// Numbers the regions by flooding from each passable tile not yet numbered, row by row, so the
    /// numbers depend only on the terrain. Returns how many there are.
    /// </summary>
    private int LabelRegions()
    {
        var count = 0;
        var pending = new int[_passable.Length];
        for (var first = 0; first < _passable.Length; first++)
        {
            if (!_passable[first] || _regions[first] != 0)
            {
                continue;
            }

            count++;
            _regions[first] = count;
            pending[0] = first;
            var pendingCount = 1;
            while (pendingCount > 0)
            {
                var tile = pending[--pendingCount];
                var x = tile % Width;
                var y = tile / Width;
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            // A diagonal step needs both tiles beside it, so tiles joined by one are already
            // joined by two straight steps: flooding along the four sides finds every region.
            void Visit(int x, int y)
            {
                if (IsPassable(x, y) && _regions[(y * Width) + x] == 0)
                {
                    _regions[(y * Width) + x] = count;
                    pending[pendingCount++] = (y * Width) + x;
                }
            }
        }

        return count;
    }
}
