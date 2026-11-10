using System.Globalization;

namespace Sim.Numerics;

/// <summary>
/// An exact fraction of two whole numbers, always in lowest terms with a positive denominator.
/// The rules multiply and divide by factors such as 1.25 and 0.90 without loss and round once at the
/// end. A fraction is how that is done.
/// </summary>
/// <remarks>
/// Numerator and denominator are 128-bit integers, wide enough for the largest value the rules
/// produce (1.15 to the 14th power does not fit in 64 bits). An intermediate value that does not
/// fit in 128 bits throws <see cref="OverflowException"/> in every build configuration.
/// </remarks>
public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
{
    /// <summary>The longest digit string <see cref="TryParse"/> accepts. 10^36 fits in 128 bits with room to spare.</summary>
    private const int MaxDigits = 36;

    private readonly Int128 _numerator;

    // Stored less one, so that the default value of the struct is 0/1 and not the invalid 0/0.
    private readonly Int128 _denominatorMinusOne;

    public Fraction(Int128 numerator, Int128 denominator)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException("A fraction cannot have a denominator of zero.");
        }

        checked
        {
            if (denominator < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            var divisor = GreatestCommonDivisor(Int128.Abs(numerator), denominator);
            _numerator = numerator / divisor;
            _denominatorMinusOne = (denominator / divisor) - 1;
        }
    }

    public static Fraction Zero => default;

    public static Fraction One => new(1, 1);

    public Int128 Numerator => _numerator;

    /// <summary>Always positive.</summary>
    public Int128 Denominator => _denominatorMinusOne + 1;

    public static Fraction FromInteger(Int128 value) => new(value, 1);

    /// <summary>
    /// A decimal number given as its digits and the number of them after the decimal point:
    /// 125 with a scale of 2 is 1.25.
    /// </summary>
    public static Fraction FromDecimalDigits(Int128 unscaledValue, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, MaxDigits);
        return new(unscaledValue, PowerOfTen(scale));
    }

    /// <summary>
    /// Reads a plain decimal number such as "1.25", "-0.5" or "40": an optional minus sign, digits,
    /// and optionally a point followed by digits. No exponent, spaces, plus sign or digit grouping,
    /// and the same result under every culture. Throws <see cref="FormatException"/> for anything else.
    /// </summary>
    public static Fraction Parse(ReadOnlySpan<char> text) =>
        TryParse(text, out var result)
            ? result
            : throw new FormatException($"'{text}' is not a plain decimal number.");

    /// <summary>As <see cref="Parse"/>, but returns false where it would throw.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out Fraction result)
    {
        result = default;
        var negative = text.Length > 0 && text[0] == '-';
        var digits = negative ? text[1..] : text;
        var point = digits.IndexOf('.');
        var wholeDigits = point < 0 ? digits : digits[..point];
        var fractionDigits = point < 0 ? [] : digits[(point + 1)..];

        if (wholeDigits.IsEmpty || (point >= 0 && fractionDigits.IsEmpty)
            || wholeDigits.Length + fractionDigits.Length > MaxDigits)
        {
            return false;
        }

        Int128 unscaled = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            if (i == point)
            {
                continue;
            }

            var character = digits[i];
            if (character is < '0' or > '9')
            {
                return false;
            }

            unscaled = (unscaled * 10) + (character - '0');
        }

        result = new(negative ? -unscaled : unscaled, PowerOfTen(fractionDigits.Length));
        return true;
    }

    public static implicit operator Fraction(long value) => new(value, 1);

    public static Fraction operator +(Fraction a, Fraction b)
    {
        checked
        {
            var divisor = GreatestCommonDivisor(a.Denominator, b.Denominator);
            var scaleA = b.Denominator / divisor;
            var scaleB = a.Denominator / divisor;
            return new((a.Numerator * scaleA) + (b.Numerator * scaleB), a.Denominator * scaleA);
        }
    }

    public static Fraction operator -(Fraction a, Fraction b) => a + -b;

    public static Fraction operator -(Fraction value) => new(checked(-value.Numerator), value.Denominator, alreadyReduced: true);

    public static Fraction operator *(Fraction a, Fraction b)
    {
        checked
        {
            // Dividing out the common factors crosswise first keeps the products small, and leaves
            // the result in lowest terms.
            var divisorA = GreatestCommonDivisor(Int128.Abs(a.Numerator), b.Denominator);
            var divisorB = GreatestCommonDivisor(Int128.Abs(b.Numerator), a.Denominator);
            return new(
                (a.Numerator / divisorA) * (b.Numerator / divisorB),
                (a.Denominator / divisorB) * (b.Denominator / divisorA),
                alreadyReduced: true);
        }
    }

    public static Fraction operator /(Fraction a, Fraction b) => a * b.Reciprocal();

    public static bool operator ==(Fraction a, Fraction b) => a.Equals(b);

    public static bool operator !=(Fraction a, Fraction b) => !a.Equals(b);

    public static bool operator <(Fraction a, Fraction b) => a.CompareTo(b) < 0;

    public static bool operator >(Fraction a, Fraction b) => a.CompareTo(b) > 0;

    public static bool operator <=(Fraction a, Fraction b) => a.CompareTo(b) <= 0;

    public static bool operator >=(Fraction a, Fraction b) => a.CompareTo(b) >= 0;

    /// <summary>One divided by this value. Zero throws <see cref="DivideByZeroException"/>.</summary>
    public Fraction Reciprocal()
    {
        if (_numerator == 0)
        {
            throw new DivideByZeroException("Zero has no reciprocal.");
        }

        return _numerator > 0
            ? new(Denominator, _numerator, alreadyReduced: true)
            : new(checked(-Denominator), checked(-_numerator), alreadyReduced: true);
    }

    /// <summary>
    /// This value raised to a whole power, which may be zero or negative. Zero to a negative power
    /// throws <see cref="DivideByZeroException"/>.
    /// </summary>
    public Fraction Pow(int exponent)
    {
        var factor = exponent < 0 ? Reciprocal() : this;
        var remaining = Math.Abs((long)exponent);
        var result = One;
        while (remaining > 0)
        {
            if ((remaining & 1) != 0)
            {
                result *= factor;
            }

            remaining >>= 1;
            if (remaining > 0)
            {
                factor *= factor;
            }
        }

        return result;
    }

    /// <summary>
    /// This value times <paramref name="scale"/>, rounded to the nearest whole number with a tie
    /// going up (toward positive infinity). This is the one rounding the design allows per value:
    /// a scale of 1 for a price, 1,000 for hit points and damage in thousandths, and the ticks per
    /// second for a time given in seconds.
    /// </summary>
    public long RoundHalfUp(long scale = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        checked
        {
            // floor(x * scale + 1/2), written as one floored division.
            var numerator = (2 * _numerator * scale) + Denominator;
            var denominator = 2 * Denominator;
            var quotient = numerator / denominator;
            if (numerator < 0 && numerator % denominator != 0)
            {
                quotient--;
            }

            return (long)quotient;
        }
    }

    /// <summary>
    /// Rounds to the simulation's distance unit of 1/65,536, with a tie going up. This is the one
    /// rounding of a speed, range or radius.
    /// </summary>
    public Fixed ToFixed() => Fixed.FromRaw(checked((int)RoundHalfUp(Fixed.RawOne)));

    public int CompareTo(Fraction other)
    {
        checked
        {
            return (_numerator * other.Denominator).CompareTo(other._numerator * Denominator);
        }
    }

    public bool Equals(Fraction other) =>
        _numerator == other._numerator && _denominatorMinusOne == other._denominatorMinusOne;

    public override bool Equals(object? obj) => obj is Fraction other && Equals(other);

    // Not HashCode.Combine and not Int128.GetHashCode: both are seeded per process.
    public override int GetHashCode() => unchecked(((int)(ulong)_numerator * 397) ^ (int)(ulong)_denominatorMinusOne);

    /// <summary>"5/4", or "3" for a whole number. Never depends on culture.</summary>
    public override string ToString()
    {
        var numerator = _numerator.ToString(CultureInfo.InvariantCulture);
        return _denominatorMinusOne == 0
            ? numerator
            : numerator + "/" + Denominator.ToString(CultureInfo.InvariantCulture);
    }

    private Fraction(Int128 numerator, Int128 denominator, bool alreadyReduced)
    {
        _ = alreadyReduced;
        _numerator = numerator;
        _denominatorMinusOne = denominator - 1;
    }

    private static Int128 PowerOfTen(int exponent)
    {
        Int128 result = 1;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10;
        }

        return result;
    }

    /// <summary>Euclid's algorithm. Both arguments are non-negative and not both zero.</summary>
    private static Int128 GreatestCommonDivisor(Int128 a, Int128 b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
