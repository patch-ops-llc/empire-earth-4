using System.Globalization;
using Sim;

namespace Game;

/// <summary>
/// The speeds a host can set (design: pause and speed are the host's). A speed is the time
/// between two ticks and nothing else: a tick is the same tick at any speed.
/// </summary>
public static class MatchSpeed
{
    /// <summary>The time between two ticks at normal speed: 20 ticks a second.</summary>
    public static readonly TimeSpan Normal = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / SimInfo.TicksPerSecond);

    /// <summary>The speeds on offer, slowest first: how many times normal speed, and its name on a button.</summary>
    public static IReadOnlyList<(double Factor, string Name)> All { get; } =
    [
        (0.5, "Half"),
        (1.0, "Normal"),
        (2.0, "Double"),
        (4.0, "Four times"),
    ];

    /// <summary>The time between two ticks at a number of times normal speed.</summary>
    public static TimeSpan IntervalOf(double factor) => TimeSpan.FromTicks((long)Math.Round(Normal.Ticks / factor));

    /// <summary>How many times normal speed a time between two ticks is.</summary>
    public static double FactorOf(TimeSpan interval) => interval > TimeSpan.Zero ? (double)Normal.Ticks / interval.Ticks : 1.0;

    /// <summary>True when the time between two ticks is that of this speed.</summary>
    public static bool Is(TimeSpan interval, double factor) => interval == IntervalOf(factor);

    /// <summary>A speed in a few characters, for a button: "0.5x", "2x".</summary>
    public static string Mark(double factor) => string.Create(CultureInfo.InvariantCulture, $"{factor:0.##}x");

    /// <summary>A speed as a sentence names it: "normal speed", "double speed", or the number when it is none on offer.</summary>
    public static string NameOf(TimeSpan interval)
    {
        foreach (var (factor, name) in All)
        {
            if (Is(interval, factor))
            {
                return $"{name.ToLowerInvariant()} speed";
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{FactorOf(interval):0.##} times normal speed");
    }
}
