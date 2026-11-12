using Sim.Numerics;

namespace Sim.Map;

/// <summary>A square of tiles: <see cref="Size"/> tiles a side, with its lowest tile at (X, Y).</summary>
public readonly record struct TileSquare(int X, int Y, int Size)
{
    /// <summary>The middle of the square.</summary>
    public FixedVector2 Center => new(
        Fixed.FromRaw(((2 * X) + Size) * (Fixed.RawOne / 2)),
        Fixed.FromRaw(((2 * Y) + Size) * (Fixed.RawOne / 2)));

    /// <summary>True when the tile is one of the square's.</summary>
    public bool Covers(int x, int y) => x >= X && y >= Y && x < X + Size && y < Y + Size;

    /// <summary>True when the point is on one of the square's tiles.</summary>
    public bool Covers(FixedVector2 point) => Covers(point.X.FloorToInt(), point.Y.FloorToInt());

    /// <summary>The point of the square, its edge included, that is nearest to a point: the point itself when it is inside.</summary>
    public FixedVector2 NearestPointTo(FixedVector2 point) => new(
        Fixed.Clamp(point.X, Fixed.FromInt(X), Fixed.FromInt(X + Size)),
        Fixed.Clamp(point.Y, Fixed.FromInt(Y), Fixed.FromInt(Y + Size)));

    /// <summary>
    /// The square of the distance from a point to the nearest edge of the square, in raw units as
    /// <see cref="FixedVector2.DistanceSquaredRaw"/> gives them. Zero for a point inside.
    /// </summary>
    public long DistanceSquaredRawTo(FixedVector2 point) => FixedVector2.DistanceSquaredRaw(point, NearestPointTo(point));
}
