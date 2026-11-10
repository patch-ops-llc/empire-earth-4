using System.Globalization;

namespace Sim.Numerics;

/// <summary>
/// A signed Q16.16 fixed-point number: a 32-bit integer counting units of 1/65,536. Positions, speeds
/// and ranges use it, with one tile as 1. The range is -32,768 to just under 32,768.
/// </summary>
/// <remarks>
/// Every operation is integer arithmetic and gives the same bits on every machine. Addition,
/// subtraction and multiplication by a whole number are exact. Multiplication and division of two
/// fixed-point numbers round to the nearest unit, with a tie going away from zero, so that
/// <c>(-a) * b</c> equals <c>-(a * b)</c>. A result outside the range throws
/// <see cref="OverflowException"/> in every build configuration.
/// </remarks>
public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
{
    public const int FractionalBits = 16;

    /// <summary>The raw value of 1.</summary>
    public const int RawOne = 1 << FractionalBits;

    private const long RawHalf = RawOne / 2;

    /// <summary>10^16 / 65,536. One raw unit is exactly this many units of 10^-16.</summary>
    private const ulong DecimalUnitsPerRawUnit = 152_587_890_625;

    private Fixed(int raw) => Raw = raw;

    /// <summary>The number of 1/65,536 units.</summary>
    public int Raw { get; }

    public static Fixed Zero => default;

    public static Fixed One => new(RawOne);

    public static Fixed Half => new(RawOne / 2);

    /// <summary>The smallest positive value, 1/65,536.</summary>
    public static Fixed Epsilon => new(1);

    public static Fixed MinValue => new(int.MinValue);

    public static Fixed MaxValue => new(int.MaxValue);

    public static Fixed FromRaw(int raw) => new(raw);

    public static Fixed FromInt(int value) => new(checked(value * RawOne));

    /// <summary>The exact value as a fraction.</summary>
    public Fraction ToFraction() => new(Raw, RawOne);

    public static Fixed operator +(Fixed a, Fixed b) => new(checked(a.Raw + b.Raw));

    public static Fixed operator -(Fixed a, Fixed b) => new(checked(a.Raw - b.Raw));

    public static Fixed operator -(Fixed value) => new(checked(-value.Raw));

    public static Fixed operator *(Fixed a, Fixed b)
    {
        var product = (long)a.Raw * b.Raw;
        var rounded = product >= 0
            ? (product + RawHalf) >> FractionalBits
            : -((-product + RawHalf) >> FractionalBits);
        return new(checked((int)rounded));
    }

    public static Fixed operator *(Fixed a, int b) => new(checked(a.Raw * b));

    public static Fixed operator *(int a, Fixed b) => b * a;

    public static Fixed operator /(Fixed a, Fixed b) => new(checked((int)DivideRounded((long)a.Raw << FractionalBits, b.Raw)));

    public static Fixed operator /(Fixed a, int b) => new(checked((int)DivideRounded(a.Raw, b)));

    public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;

    public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;

    public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;

    public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;

    public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;

    public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

    public static Fixed Abs(Fixed value) => value.Raw < 0 ? -value : value;

    public static Fixed Min(Fixed a, Fixed b) => a.Raw <= b.Raw ? a : b;

    public static Fixed Max(Fixed a, Fixed b) => a.Raw >= b.Raw ? a : b;

    public static Fixed Clamp(Fixed value, Fixed min, Fixed max)
    {
        if (min > max)
        {
            throw new ArgumentException("The lower bound is above the upper bound.", nameof(min));
        }

        return value < min ? min : value > max ? max : value;
    }

    /// <summary>The square root, rounded down to the unit. Negative input throws.</summary>
    public static Fixed Sqrt(Fixed value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value.Raw, nameof(value));
        return new((int)IntegerMath.Sqrt((ulong)value.Raw << FractionalBits));
    }

    /// <summary>The largest whole number not above this value. This is the tile index of a coordinate.</summary>
    public int FloorToInt() => Raw >> FractionalBits;

    /// <summary>The smallest whole number not below this value.</summary>
    public int CeilingToInt() => (int)-(-(long)Raw >> FractionalBits);

    /// <summary>The nearest whole number, with a tie going up.</summary>
    public int RoundToInt() => (int)((Raw + RawHalf) >> FractionalBits);

    public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);

    public bool Equals(Fixed other) => Raw == other.Raw;

    public override bool Equals(object? obj) => obj is Fixed other && Equals(other);

    public override int GetHashCode() => Raw;

    /// <summary>The exact decimal value, for example "1.5" or "-0.0000152587890625". Never depends on culture.</summary>
    public override string ToString()
    {
        long raw = Raw;
        var magnitude = (ulong)Math.Abs(raw);
        var text = (magnitude >> FractionalBits).ToString(CultureInfo.InvariantCulture);
        var fraction = (magnitude & (RawOne - 1)) * DecimalUnitsPerRawUnit;
        if (fraction != 0)
        {
            text += "." + fraction.ToString("D16", CultureInfo.InvariantCulture).TrimEnd('0');
        }

        return raw < 0 ? "-" + text : text;
    }

    /// <summary>Divides and rounds to the nearest whole number, with a tie going away from zero.</summary>
    private static long DivideRounded(long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException();
        }

        // Both magnitudes are below 2^48, so doubling them cannot overflow.
        var n = Math.Abs(numerator);
        var d = Math.Abs(denominator);
        var quotient = ((2 * n) + d) / (2 * d);
        return (numerator < 0) != (denominator < 0) ? -quotient : quotient;
    }
}
