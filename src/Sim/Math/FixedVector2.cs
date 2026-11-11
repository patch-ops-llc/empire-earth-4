namespace Sim.Numerics;

/// <summary>
/// A point or an offset on the map: two <see cref="Fixed"/> values, in tiles. x grows to the right
/// and y grows downward, as tiles do.
/// </summary>
/// <remarks>
/// Compare distances by their squares, which are exact: <see cref="DistanceSquaredRaw"/> against
/// <see cref="SquareRaw"/> of a range. <see cref="Length"/> and <see cref="Distance"/> round down.
/// A store feeds a vector to the state hash as its two <see cref="Fixed"/> values, x first.
/// </remarks>
public readonly struct FixedVector2 : IEquatable<FixedVector2>
{
    public FixedVector2(Fixed x, Fixed y)
    {
        X = x;
        Y = y;
    }

    public Fixed X { get; }

    public Fixed Y { get; }

    public static FixedVector2 Zero => default;

    public static FixedVector2 FromInts(int x, int y) => new(Fixed.FromInt(x), Fixed.FromInt(y));

    /// <summary>The vector of length one, within rounding, that points along an angle: (cosine, sine).</summary>
    public static FixedVector2 FromAngle(Angle angle) => new(angle.Cos(), angle.Sin());

    public static FixedVector2 operator +(FixedVector2 a, FixedVector2 b) => new(a.X + b.X, a.Y + b.Y);

    public static FixedVector2 operator -(FixedVector2 a, FixedVector2 b) => new(a.X - b.X, a.Y - b.Y);

    public static FixedVector2 operator -(FixedVector2 value) => new(-value.X, -value.Y);

    public static FixedVector2 operator *(FixedVector2 a, int b) => new(a.X * b, a.Y * b);

    public static FixedVector2 operator *(int a, FixedVector2 b) => b * a;

    public static FixedVector2 operator *(FixedVector2 a, Fixed b) => new(a.X * b, a.Y * b);

    public static FixedVector2 operator *(Fixed a, FixedVector2 b) => b * a;

    public static FixedVector2 operator /(FixedVector2 a, int b) => new(a.X / b, a.Y / b);

    public static bool operator ==(FixedVector2 a, FixedVector2 b) => a.X == b.X && a.Y == b.Y;

    public static bool operator !=(FixedVector2 a, FixedVector2 b) => !(a == b);

    /// <summary>
    /// The square of a length, in the unit of <see cref="DistanceSquaredRaw"/>. A point is within
    /// range r of another when their squared distance is not above <c>SquareRaw(r)</c>.
    /// </summary>
    public static long SquareRaw(Fixed length) => (long)length.Raw * length.Raw;

    /// <summary>
    /// The exact square of the distance between two points, as the sum of the squares of the raw
    /// differences: one unit is 2^-32 of a square tile.
    /// </summary>
    public static long DistanceSquaredRaw(FixedVector2 a, FixedVector2 b)
    {
        var dx = (long)a.X.Raw - b.X.Raw;
        var dy = (long)a.Y.Raw - b.Y.Raw;
        return checked((dx * dx) + (dy * dy));
    }

    /// <summary>The distance between two points, rounded down to the unit.</summary>
    public static Fixed Distance(FixedVector2 a, FixedVector2 b) =>
        Fixed.FromRaw(checked((int)IntegerMath.Sqrt((ulong)DistanceSquaredRaw(a, b))));

    /// <summary>
    /// The dot product of the raw values, exact: one unit is 2^-32 of a square tile. Its sign says
    /// whether two offsets point the same way.
    /// </summary>
    public static long DotRaw(FixedVector2 a, FixedVector2 b) =>
        checked(((long)a.X.Raw * b.X.Raw) + ((long)a.Y.Raw * b.Y.Raw));

    /// <summary>
    /// The cross product of the raw values, exact: one unit is 2^-32 of a square tile. It is positive
    /// when <paramref name="b"/> lies on the side of <paramref name="a"/> that a positive angle turns to.
    /// </summary>
    public static long CrossRaw(FixedVector2 a, FixedVector2 b) =>
        checked(((long)a.X.Raw * b.Y.Raw) - ((long)a.Y.Raw * b.X.Raw));

    /// <summary>
    /// The point reached by going from <paramref name="from"/> straight toward <paramref name="to"/>
    /// for at most <paramref name="maxDistance"/>. It is <paramref name="to"/> itself when that is
    /// within reach, so a mover arrives exactly and never overshoots.
    /// </summary>
    /// <remarks>
    /// Each component of the step is rounded to the nearest unit with a tie going away from zero, so
    /// mirrored input gives a mirrored result. The step can be up to one unit longer or shorter than
    /// <paramref name="maxDistance"/>.
    /// </remarks>
    public static FixedVector2 MoveTowards(FixedVector2 from, FixedVector2 to, Fixed maxDistance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance.Raw, nameof(maxDistance));

        var dx = (long)to.X.Raw - from.X.Raw;
        var dy = (long)to.Y.Raw - from.Y.Raw;
        var distanceSquared = checked((dx * dx) + (dy * dy));
        long reach = maxDistance.Raw;
        if (distanceSquared <= reach * reach)
        {
            return to;
        }

        // Rounded down, and still at least the reach, so the step is never longer than the way left.
        long distance = IntegerMath.Sqrt((ulong)distanceSquared);
        var stepX = DivideRounded(checked(dx * reach), distance);
        var stepY = DivideRounded(checked(dy * reach), distance);
        return new(
            Fixed.FromRaw(checked((int)(from.X.Raw + stepX))),
            Fixed.FromRaw(checked((int)(from.Y.Raw + stepY))));
    }

    /// <summary>The exact square of the length: one unit is 2^-32 of a square tile.</summary>
    public long LengthSquaredRaw() => DistanceSquaredRaw(this, Zero);

    /// <summary>The length, rounded down to the unit.</summary>
    public Fixed Length() => Distance(this, Zero);

    /// <summary>
    /// The angle this offset points along, by <see cref="Angle.Atan2"/>. With y growing downward, a
    /// quarter turn points down the map.
    /// </summary>
    public Angle ToAngle() => Angle.Atan2(Y, X);

    public bool Equals(FixedVector2 other) => this == other;

    public override bool Equals(object? obj) => obj is FixedVector2 other && Equals(other);

    public override int GetHashCode() => unchecked((X.Raw * 397) ^ Y.Raw);

    public override string ToString() => $"({X}, {Y})";

    /// <summary>Divides by a positive number and rounds to the nearest whole number, with a tie going away from zero.</summary>
    private static long DivideRounded(long numerator, long denominator)
    {
        var quotient = checked((2 * Math.Abs(numerator)) + denominator) / (2 * denominator);
        return numerator < 0 ? -quotient : quotient;
    }
}
