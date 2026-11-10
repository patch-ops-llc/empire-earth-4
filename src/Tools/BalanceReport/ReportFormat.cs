using System.Globalization;
using Sim.Numerics;

namespace Tools.BalanceReport;

/// <summary>
/// How the balance report writes its values. Every number is written from exact integers and
/// fractions, with a point as the decimal mark, so the report is the same on every machine.
/// </summary>
public static class ReportFormat
{
    /// <summary>A value kept in thousandths, such as hit points: 317383 is written as 317.383 and 75000 as 75.</summary>
    public static string Thousandths(long thousandths) => Scaled(thousandths, 3);

    /// <summary>A value rounded half up to <paramref name="places"/> decimal places, without trailing zeros.</summary>
    public static string Rounded(Fraction value, int places)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(places);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(places, 9);

        long scale = 1;
        for (var place = 0; place < places; place++)
        {
            scale *= 10;
        }

        return Scaled(value.RoundHalfUp(scale), places);
    }

    /// <summary>A distance in tiles, to thousandths.</summary>
    public static string Tiles(Fixed? distance) => distance is { } value ? Rounded(value.ToFraction(), 3) : "";

    /// <summary>A time in ticks, written in seconds: 154 ticks is 7.7.</summary>
    public static string Seconds(int ticks) => Rounded(new Fraction(ticks, Sim.SimInfo.TicksPerSecond), 2);

    /// <summary>One line of CSV. A field with a comma, a quote or a line break is quoted.</summary>
    public static string Row(IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return string.Join(",", fields.Select(Field));
    }

    private static string Field(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") < 0 ? field : $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Scaled(long unscaled, int places)
    {
        var digits = Int128.Abs(unscaled).ToString(CultureInfo.InvariantCulture).PadLeft(places + 1, '0');
        var whole = digits[..^places];
        var fraction = digits[^places..].TrimEnd('0');
        var sign = unscaled < 0 ? "-" : "";
        return fraction.Length == 0 ? $"{sign}{whole}" : $"{sign}{whole}.{fraction}";
    }
}
