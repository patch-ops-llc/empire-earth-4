namespace Sim.Tech;

/// <summary>
/// One accent that a research would add, remove or change on one chassis. A level of 0 means the
/// unit does not carry the accent. <see cref="Chassis"/> is an index into
/// <see cref="Sim.Data.GameData.Chassis"/> and <see cref="Track"/> into <see cref="Sim.Data.GameData.Tracks"/>.
/// </summary>
public readonly record struct AccentChange(int Chassis, int Track, int LevelBefore, int LevelAfter);

/// <summary>
/// What researching the next tier of one track would change, for the research grid to show before
/// the player pays (design 2.3, "Accents can be displaced").
/// </summary>
public sealed class ResearchPreview
{
    internal ResearchPreview(
        ResearchOffer offer,
        PlayerTech before,
        PlayerTech after,
        IReadOnlyList<int> unlockedChassis,
        IReadOnlyList<AccentChange> accentChanges)
    {
        Offer = offer;
        Before = before;
        After = after;
        UnlockedChassis = unlockedChassis;
        AccentChanges = accentChanges;
    }

    /// <summary>The research itself: whether it may start now, and its price.</summary>
    public ResearchOffer Offer { get; }

    /// <summary>The player as they are.</summary>
    public PlayerTech Before { get; }

    /// <summary>The player with the tier researched.</summary>
    public PlayerTech After { get; }

    /// <summary>Indexes of the chassis the research would unlock, in rising order.</summary>
    public IReadOnlyList<int> UnlockedChassis { get; }

    /// <summary>
    /// Every accent the research would add, remove or change on a chassis the player already has
    /// unlocked, by chassis and then by track. A chassis the research unlocks is not listed: it
    /// has no units yet, so nothing on the field changes.
    /// </summary>
    public IReadOnlyList<AccentChange> AccentChanges { get; }
}
