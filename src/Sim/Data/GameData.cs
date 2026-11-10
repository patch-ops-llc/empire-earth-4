namespace Sim.Data;

/// <summary>
/// Everything loaded from the data files, already checked. A position in one of these lists is that
/// item's dense integer ID: tracks keep the order of <c>tracks.jsonc</c>, which the design's
/// tie-breaks rely on, and everything else is sorted by ID.
/// </summary>
public sealed record GameData(
    IReadOnlyList<string> EpochNames,
    IReadOnlyList<TrackDef> Tracks,
    RuleSet Rules,
    IReadOnlyList<LineDef> Lines,
    IReadOnlyList<MapDef> Maps,
    string ContentHash)
{
    /// <summary>Tiers per track, which is also the number of epochs.</summary>
    public int TierCount => EpochNames.Count;
}

public enum TrackGroup
{
    Better,
    More,
    Richer,
}

/// <summary>One track (design 1.1 and 1.2). <see cref="TierNames"/> has one name per tier, tier 1 first.</summary>
public sealed record TrackDef(string Id, string Name, string ShortName, TrackGroup Group, IReadOnlyList<string> TierNames);
