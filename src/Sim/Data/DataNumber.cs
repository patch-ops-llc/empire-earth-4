using System.Globalization;

namespace Sim.Data;

/// <summary>
/// A number from a data file, kept exactly: the value is <see cref="Unscaled"/> divided by ten to the
/// power <see cref="Scale"/>. It is read from the text of the JSON token with integer arithmetic, so no
/// floating-point type is ever involved. Trailing zeros are dropped, so "0.90" and "0.9" are equal.
/// </summary>
public readonly record struct DataNumber : IComparable<DataNumber>
{
    /// <summary>Most digits after the decimal point that a data file may use.</summary>
    public const int MaxScale = 9;

    /// <summary>Most digits in total, so that the value always fits in 64 bits.</summary>
    public const int MaxDigits = 18;

    private DataNumber(long unscaled, int scale)
    {
        Unscaled = unscaled;
        Scale = scale;
    }

    public long Unscaled { get; }

    public int Scale { get; }

    public bool IsInteger => Scale == 0;

    public static DataNumber FromInteger(long value) => new(value, 0);

    /// <summary>
    /// Reads plain decimal notation: an optional minus sign, digits, and optionally a point followed by
    /// digits. Exponents, a leading plus sign, spaces and leading zeros are not accepted.
    /// </summary>
    public static bool TryParse(string? text, out DataNumber number)
    {
        number = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var position = 0;
        var negative = text[0] == '-';
        if (negative)
        {
            position++;
        }

        var integerStart = position;
        while (position < text.Length && char.IsAsciiDigit(text[position]))
        {
            position++;
        }

        var integerDigits = position - integerStart;
        if (integerDigits == 0 || (integerDigits > 1 && text[integerStart] == '0'))
        {
            return false;
        }

        var fractionDigits = 0;
        if (position < text.Length)
        {
            if (text[position] != '.')
            {
                return false;
            }

            position++;
            var fractionStart = position;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }

            fractionDigits = position - fractionStart;
            if (fractionDigits == 0 || position != text.Length)
            {
                return false;
            }
        }

        if (fractionDigits > MaxScale || integerDigits + fractionDigits > MaxDigits)
        {
            return false;
        }

        long unscaled = 0;
        foreach (var character in text)
        {
            if (char.IsAsciiDigit(character))
            {
                unscaled = (unscaled * 10) + (character - '0');
            }
        }

        var scale = fractionDigits;
        while (scale > 0 && unscaled % 10 == 0)
        {
            unscaled /= 10;
            scale--;
        }

        number = new DataNumber(negative ? -unscaled : unscaled, scale);
        return true;
    }

    /// <summary>True when the value times <paramref name="factor"/> is a whole number.</summary>
    public bool IsWholeWhenMultipliedBy(long factor) => (Unscaled * (Int128)factor) % PowerOfTen(Scale) == 0;

    /// <summary>The value times <paramref name="factor"/>, which must be a whole number.</summary>
    public long MultipliedBy(long factor)
    {
        var product = Unscaled * (Int128)factor;
        var divisor = PowerOfTen(Scale);
        if (product % divisor != 0)
        {
            throw new InvalidOperationException($"{this} times {factor} is not a whole number.");
        }

        return checked((long)(product / divisor));
    }

    public int CompareTo(DataNumber other)
    {
        var left = Unscaled * (Int128)PowerOfTen(other.Scale);
        var right = other.Unscaled * (Int128)PowerOfTen(Scale);
        return left.CompareTo(right);
    }

    public static bool operator <(DataNumber left, DataNumber right) => left.CompareTo(right) < 0;

    public static bool operator <=(DataNumber left, DataNumber right) => left.CompareTo(right) <= 0;

    public static bool operator >(DataNumber left, DataNumber right) => left.CompareTo(right) > 0;

    public static bool operator >=(DataNumber left, DataNumber right) => left.CompareTo(right) >= 0;

    public override string ToString()
    {
        var digits = (Unscaled < 0 ? -(Int128)Unscaled : Unscaled).ToString(CultureInfo.InvariantCulture);
        if (Scale > 0)
        {
            digits = digits.PadLeft(Scale + 1, '0');
            digits = string.Concat(digits.AsSpan(0, digits.Length - Scale), ".", digits.AsSpan(digits.Length - Scale));
        }

        return Unscaled < 0 ? "-" + digits : digits;
    }

    private static long PowerOfTen(int exponent)
    {
        long result = 1;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10;
        }

        return result;
    }
}
