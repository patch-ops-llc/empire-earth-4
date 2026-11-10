namespace Sim.Tech;

/// <summary>Whether the next tier of a track may be researched (design 1.3 and 1.4).</summary>
public enum ResearchStatus
{
    Available,

    /// <summary>The track is at its last tier.</summary>
    AtTopTier,

    /// <summary>The tier is above the match's epoch cap.</summary>
    AboveEpochCap,

    /// <summary>The tier is further above the player's epoch than the reach rule allows.</summary>
    BeyondReach,
}

/// <summary>The price band of a tier, by how far it is from the player's epoch (design 1.3).</summary>
public enum ResearchBand
{
    /// <summary>At or below the epoch.</summary>
    CatchUp,

    /// <summary>One tier above the epoch.</summary>
    Frontier,

    /// <summary>Further above the epoch, as far as the reach rule allows.</summary>
    Ahead,
}

/// <summary>What a research costs after its band's multipliers, each value rounded once, half up.</summary>
public readonly record struct ResearchPrice(ResearchBand Band, int Materials, int Energy, int Ticks);

/// <summary>
/// The next tier of one track: <see cref="Tier"/> is the tier the research would reach, and
/// <see cref="Price"/> is set exactly when <see cref="Status"/> is <see cref="ResearchStatus.Available"/>.
/// A tier that may not be researched is not offered and has no price.
/// </summary>
public sealed record ResearchOffer(int Track, int Tier, ResearchStatus Status, ResearchPrice? Price)
{
    public bool IsAvailable => Status == ResearchStatus.Available;
}
