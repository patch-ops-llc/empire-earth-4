namespace Sim.Core;

/// <summary>
/// What a <see cref="SimEvent"/> reports. Each piece of the simulation that reports something adds
/// its kinds here, and says in the comment of each which fields of the event it fills.
/// </summary>
/// <remarks>
/// Keep the lines in alphabetical order after <see cref="None"/>, so that two pull requests rarely
/// touch the same line, and give them no numbers. Unlike a <see cref="CommandType"/>, the number
/// of a kind is recorded nowhere: events are not in the state hash, a snapshot, a replay or a
/// network message. So a number may change from one build to the next, and nothing may store one.
/// </remarks>
public enum SimEventKind
{
    /// <summary>Not an event. The default value, which cannot be added to the list.</summary>
    None = 0,

    /// <summary>
    /// A player's epoch went up (design 1.4: it is announced to all players). It follows the
    /// <see cref="ResearchFinished"/> that raised it. Fields as for <see cref="ResearchStarted"/>,
    /// with no entity.
    /// </summary>
    EpochChanged,

    /// <summary>The player cancelled a research and got its price back. Fields as for <see cref="ResearchStarted"/>.</summary>
    ResearchCancelled,

    /// <summary>
    /// A research finished and its track went up a tier. What stands on the map already has its
    /// new values. Fields as for <see cref="ResearchStarted"/>.
    /// </summary>
    ResearchFinished,

    /// <summary>
    /// A research was cancelled, with its price given back, because its slot was lost. Fields as
    /// for <see cref="ResearchStarted"/>; the entity is the holder that fell.
    /// </summary>
    ResearchLost,

    /// <summary>
    /// A research was started and paid for. Player: whose research it is. Entity: the holder of
    /// the research's slot, or none for the slot a player has of their own. Amount: the track, the
    /// tier and the player's epoch after the event, which <c>ResearchEvent.TryRead</c> takes apart.
    /// No position.
    /// </summary>
    ResearchStarted,
}
