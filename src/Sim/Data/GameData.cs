namespace Sim.Data;

/// <summary>
/// Everything loaded from the data files, already checked. A position in one of these lists is that
/// item's dense integer ID: tracks keep the order of <c>tracks.jsonc</c>, which the design's
/// tie-breaks rely on, research goes by tier, accents by track, and lines, chassis and maps are
/// sorted by ID. <see cref="Lines"/> and <see cref="Structures"/> hold only those of the roster
/// that have a data file, in the order of the roster.
/// </summary>
public sealed record GameData(
    IReadOnlyList<string> EpochNames,
    IReadOnlyList<TrackDef> Tracks,
    TrackRoles TrackRoles,
    RuleSet Rules,
    IReadOnlyList<ResearchTier> Research,
    ResourceRules Resources,
    IReadOnlyList<AccentDef> Accents,
    IReadOnlyList<LineDef> Lines,
    IReadOnlyList<ChassisDef> Chassis,
    IReadOnlyList<StructureDef> Structures,
    IReadOnlyList<MapDef> Maps,
    string ContentHash)
{
    /// <summary>
    /// The gate (design 2.5), or null when the data has no wall. It is the wall with the gate's
    /// price and build time, under the ID <see cref="DataRules.Gate"/> and the number that follows
    /// the last of the roster: the number a command names it by, and the type a placed gate has.
    /// </summary>
    /// <remarks>
    /// It is no entry of <see cref="Structures"/>, which holds what the roster lists and the files
    /// describe. Both <c>StructureOf</c> lookups find it.
    /// </remarks>
    public StructureDef? Gate { get; } = GateOf(Structures, Rules);

    /// <summary>
    /// How many numbers name a structure: those of the roster and the gate's. A table with one
    /// entry for each kind of structure, indexed by <see cref="StructureDef.Index"/>, has this size.
    /// </summary>
    public int StructureKinds => Rules.Structures.Count + 1;

    /// <summary>Tiers per track, which is also the number of epochs.</summary>
    public int TierCount => EpochNames.Count;

    /// <summary>The research that raises a track to <paramref name="tier"/>, from 2 to <see cref="TierCount"/>.</summary>
    public ResearchTier ResearchOf(int tier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tier, TierCount);
        return Research[tier - 2];
    }

    /// <summary>
    /// The line with this roster index, as in <see cref="LineDef.Index"/> and <see cref="ChassisDef.Line"/>,
    /// or null for a line of the roster that has no data file yet.
    /// </summary>
    public LineDef? LineOf(int line)
    {
        foreach (var candidate in Lines)
        {
            if (candidate.Index == line)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The structure with this roster index, as in <see cref="StructureDef.Index"/> and
    /// <see cref="LineDef.BuiltAt"/>, or null for a structure of the roster that has no data file yet.
    /// The number after the roster's last is the <see cref="Gate"/>'s.
    /// </summary>
    public StructureDef? StructureOf(int structure)
    {
        foreach (var candidate in Structures)
        {
            if (candidate.Index == structure)
            {
                return candidate;
            }
        }

        return Gate is { } gate && gate.Index == structure ? gate : null;
    }

    /// <summary>
    /// The structure every player starts with (design 5.2): the Capitol. The loader requires its
    /// file, with a territory radius, so the map checks and the match setup can rely on it.
    /// </summary>
    public StructureDef StartStructure => StructureOf(DataRules.StartStructure);

    /// <summary>The structure with this ID. Throws when the data has no file for it.</summary>
    public StructureDef StructureOf(string id)
    {
        foreach (var candidate in Structures)
        {
            if (candidate.Id == id)
            {
                return candidate;
            }
        }

        if (Gate is { } gate && gate.Id == id)
        {
            return gate;
        }

        throw new ArgumentException($"The data has no structure '{id}'.", nameof(id));
    }

    private static StructureDef? GateOf(IReadOnlyList<StructureDef> structures, RuleSet rules)
    {
        foreach (var structure in structures)
        {
            if (structure.Wall is { } wall)
            {
                return structure with
                {
                    Id = DataRules.Gate,
                    Index = rules.Structures.Count,
                    Materials = wall.Gate.Materials,
                    Energy = wall.Gate.Energy,
                    BuildTicks = wall.Gate.BuildTicks,
                    IsGate = true,
                };
            }
        }

        return null;
    }

    /// <summary>The accent a track gives, or null for a track that gives none.</summary>
    public AccentDef? AccentOf(int track)
    {
        foreach (var accent in Accents)
        {
            if (accent.Track == track)
            {
                return accent;
            }
        }

        return null;
    }
}

public enum TrackGroup
{
    Better,
    More,
    Richer,
}

/// <summary>One track (design 1.1 and 1.2). <see cref="TierNames"/> has one name per tier, tier 1 first.</summary>
public sealed record TrackDef(string Id, string Name, string ShortName, TrackGroup Group, IReadOnlyList<string> TierNames);

/// <summary>
/// Where each track the rules name sits in <see cref="GameData.Tracks"/>. The rules say which track
/// scales what (design 2.3 and section 4): Firepower damage, Protection hit points, Mobility speed,
/// Manufacturing price and build time, Logistics the supply cap and Extraction income.
/// </summary>
public sealed record TrackRoles(int Firepower, int Protection, int Mobility, int Manufacturing, int Logistics, int Extraction);
