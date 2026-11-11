namespace Sim.Research;

/// <summary>
/// Whether a player may start researching the next tier of a track right now, and if not, the
/// first reason why. These are the checks of design 1.3, made once, when a research starts.
/// </summary>
public enum ResearchStartCheck
{
    Allowed,

    /// <summary>There is no such player in the match.</summary>
    UnknownPlayer,

    /// <summary>There is no such track.</summary>
    UnknownTrack,

    /// <summary>One of the player's slots is already researching this track.</summary>
    InProgress,

    /// <summary>The track is at its last tier.</summary>
    AtTopTier,

    /// <summary>The tier is above the match's epoch cap.</summary>
    AboveEpochCap,

    /// <summary>The tier is further above the player's epoch than the reach rule allows.</summary>
    BeyondReach,

    /// <summary>Every slot the player has is busy, or the player has none.</summary>
    NoFreeSlot,

    /// <summary>The player's stockpile does not cover the price.</summary>
    TooExpensive,
}
