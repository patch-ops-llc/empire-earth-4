namespace Sim.Data;

/// <summary>
/// Conventions of the data files and the sanity limits the loader enforces. The limits are far outside
/// anything the design uses; they exist to catch slips such as a missing decimal point.
/// </summary>
internal static class DataRules
{
    public const string FileExtension = ".jsonc";
    public const string TracksFile = "tracks.jsonc";
    public const string RulesFile = "rules.jsonc";
    public const string LinesFolder = "lines/";
    public const string MapsFolder = "maps/";

    /// <summary>The key editors use to find a file's schema. It is not data and is left out of the hash.</summary>
    public const string SchemaKey = "$schema";

    /// <summary>The counter target that means every structure and not a line.</summary>
    public const string StructuresTarget = "structures";

    public const int MaxIdLength = 40;
    public const int MaxTracks = 16;
    public const int MaxTiers = 20;
    public const int MaxMapSize = 256;
    public const int MinMapSize = 16;
    public const int MaxPrice = 1_000_000;
    public const int MaxSeconds = 3_600;

    public static readonly DataNumber Zero = DataNumber.FromInteger(0);
    public static readonly DataNumber MaxMultiplier = DataNumber.FromInteger(100);
    public static readonly DataNumber MaxHitPoints = DataNumber.FromInteger(1_000_000);
    public static readonly DataNumber MaxDamagePerSecond = DataNumber.FromInteger(100_000);
    public static readonly DataNumber MaxSpeed = DataNumber.FromInteger(100);
    public static readonly DataNumber MaxDistance = DataNumber.FromInteger(64);
    public static readonly DataNumber MaxProjectileSpeed = DataNumber.FromInteger(1_000);

    public static bool IsId(string text)
    {
        if (text.Length == 0 || text.Length > MaxIdLength || !char.IsAsciiLetterLower(text[0]))
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A multiplier such as "1.25": above zero and at most <see cref="MaxMultiplier"/>.</summary>
    public static DataNumber Multiplier(this JsonValue value) => value.Number(Zero, MaxMultiplier, aboveMin: true);

    /// <summary>
    /// A time written in seconds, returned in ticks. It must be above zero and a whole number of ticks.
    /// </summary>
    public static int SecondsAsTicks(this JsonValue value)
    {
        var seconds = value.Number(Zero, DataNumber.FromInteger(MaxSeconds), aboveMin: true);
        if (!seconds.IsWholeWhenMultipliedBy(SimInfo.TicksPerSecond))
        {
            value.Error($"{seconds} s is not a whole number of ticks; there are {SimInfo.TicksPerSecond} ticks per second");
            return 1;
        }

        return (int)seconds.MultipliedBy(SimInfo.TicksPerSecond);
    }

    /// <summary>Finds <paramref name="id"/> in a list of IDs and reports an error if it is not there.</summary>
    public static int IndexIn(this JsonValue value, string id, IReadOnlyList<string> ids, string what, string definedIn)
    {
        if (id.Length == 0)
        {
            return 0;
        }

        for (var i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        value.Error($"unknown {what} '{id}'; {definedIn} has: {string.Join(", ", ids)}");
        return 0;
    }
}
