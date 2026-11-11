namespace Sim.Research;

public enum ResearchEventKind
{
    /// <summary>A research was started and paid for.</summary>
    Started = 1,

    /// <summary>A research finished and its track went up a tier.</summary>
    Finished = 2,

    /// <summary>The player cancelled a research and got its price back.</summary>
    Cancelled = 3,

    /// <summary>A research was cancelled, with its price given back, because its slot was lost.</summary>
    Lost = 4,

    /// <summary>The player's epoch went up. Design 1.4: it is announced to all players.</summary>
    EpochChanged = 5,
}

/// <summary>
/// Something that happened to a player's research, for whoever shows the match: the epoch
/// announcement, sounds, the research panel.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Tick">The tick it happened in.</param>
/// <param name="Player">The player whose research it is.</param>
/// <param name="Track">The track of the research. For <see cref="ResearchEventKind.EpochChanged"/>, the track whose research raised the epoch.</param>
/// <param name="Tier">The tier the research reaches or has reached.</param>
/// <param name="Epoch">The player's epoch after the event.</param>
public readonly record struct ResearchEvent(ResearchEventKind Kind, int Tick, int Player, int Track, int Tier, int Epoch);
