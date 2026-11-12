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
    public const string ResearchFile = "research.jsonc";
    public const string ResourcesFile = "resources.jsonc";
    public const string AccentsFile = "accents.jsonc";
    public const string AiFolder = "ai/";
    public const string LinesFolder = "lines/";
    public const string MapsFolder = "maps/";
    public const string StructuresFolder = "structures/";

    /// <summary>The key editors use to find a file's schema. It is not data and is left out of the hash.</summary>
    public const string SchemaKey = "$schema";

    /// <summary>The counter target that means every structure and not a line.</summary>
    public const string StructuresTarget = "structures";

    /// <summary>What a structure's list of what it trains calls the worker, which is not a line.</summary>
    public const string WorkerUnit = "worker";

    /// <summary>The resource workers earn at a structure that is a work site.</summary>
    public const string EnergyWorkSite = "energy";

    /// <summary>
    /// The structure every player starts with (design 5.2). Its file is required: the match setup
    /// places one for every player, and the map checks read its footprint and territory radius.
    /// </summary>
    public const string StartStructure = "capitol";

    /// <summary>
    /// The tracks the rules name. The formulas of design 2.3 and section 4 say which track scales
    /// what, so <c>tracks.jsonc</c> must define each of these IDs. Their order there is free.
    /// </summary>
    public const string FirepowerTrack = "firepower";
    public const string ProtectionTrack = "protection";
    public const string MobilityTrack = "mobility";
    public const string ManufacturingTrack = "manufacturing";
    public const string LogisticsTrack = "logistics";
    public const string ExtractionTrack = "extraction";

    public const int MaxIdLength = 40;
    public const int MaxTracks = 16;
    public const int MaxTiers = 20;
    public const int MaxMapSize = 256;
    public const int MinMapSize = 16;
    public const int MaxPrice = 1_000_000;
    public const int MaxSeconds = 3_600;
    public const int MaxWorkerPlaces = 64;
    public const int MaxFootprint = 8;
    public const int MaxSight = 64;
    public const int MaxTerritoryRadius = 64;
    public const int MaxResearchSlots = 8;
    public const int MaxStructuresPerPlayer = 1_000;
    public const int MaxFullRateAmount = 100_000_000;
    public const int MaxAiWorkers = 200;
    public const int MaxAiWeight = 1_000;
    public const int MaxAiRatio = 100;
    public const int MaxAiUnits = 200;

    public static readonly DataNumber Zero = DataNumber.FromInteger(0);
    public static readonly DataNumber One = DataNumber.FromInteger(1);
    public static readonly DataNumber MaxMultiplier = DataNumber.FromInteger(100);
    public static readonly DataNumber MaxHitPoints = DataNumber.FromInteger(1_000_000);
    public static readonly DataNumber MaxDamagePerSecond = DataNumber.FromInteger(100_000);
    public static readonly DataNumber MaxSpeed = DataNumber.FromInteger(100);
    public static readonly DataNumber MaxDistance = DataNumber.FromInteger(64);
    public static readonly DataNumber MaxProjectileSpeed = DataNumber.FromInteger(1_000);
    public static readonly DataNumber MaxIncomeRate = DataNumber.FromInteger(100_000);

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

    /// <summary>A multiplier that raises a value, such as "1.15": at least 1 and at most <see cref="MaxMultiplier"/>.</summary>
    public static DataNumber RaisingMultiplier(this JsonValue value) => value.Number(One, MaxMultiplier);

    /// <summary>A share of a whole or a multiplier that lowers a value, such as "0.25": above zero and at most 1.</summary>
    public static DataNumber Share(this JsonValue value) => value.Number(Zero, One, aboveMin: true);

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
