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
    /// Something was destroyed because its hit points were at zero when the attacks of the tick
    /// had been made. <see cref="SimEvent.Player"/> is its owner, <see cref="SimEvent.Entity"/>
    /// is what died, and <see cref="SimEvent.Position"/> is where it stood. Nothing says who
    /// killed it: the rules do not keep that.
    /// </summary>
    Death,

    /// <summary>
    /// A player's epoch went up (design 1.4: it is announced to all players). It follows the
    /// <see cref="ResearchFinished"/> that raised it. Fields as for <see cref="ResearchStarted"/>,
    /// with no entity.
    /// </summary>
    EpochChanged,

    /// <summary>
    /// A melee attack landed. <see cref="SimEvent.Player"/> is the attacker's owner,
    /// <see cref="SimEvent.Entity"/> the target, <see cref="SimEvent.Other"/> the attacker,
    /// <see cref="SimEvent.Position"/> where the target stood, and <see cref="SimEvent.Amount"/>
    /// the damage of the hit in thousandths of a hit point, which may be more than the target
    /// had left.
    /// </summary>
    MeleeHit,

    /// <summary>
    /// One of the orders a unit takes, or the development spawn, was not carried out and changed
    /// nothing. <see cref="SimEvent.Player"/> is the player the command names,
    /// <see cref="SimEvent.Amount"/> the number of its <see cref="CommandType"/>,
    /// <see cref="SimEvent.Entity"/> the first entity it lists, if any,
    /// <see cref="SimEvent.Other"/> its target entity, if any, and
    /// <see cref="SimEvent.Position"/> its target point.
    /// </summary>
    OrderRefused,

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

    /// <summary>
    /// A ranged attack was made. <see cref="SimEvent.Player"/> is the attacker's owner,
    /// <see cref="SimEvent.Entity"/> the shot, which is in the store of shots from this tick
    /// until it lands or is lost, <see cref="SimEvent.Other"/> the attacker, and
    /// <see cref="SimEvent.Position"/> where it was fired from.
    /// </summary>
    ShotFired,

    /// <summary>
    /// A shot reached its target. <see cref="SimEvent.Player"/> is the owner of the unit that
    /// fired it, <see cref="SimEvent.Entity"/> the target, <see cref="SimEvent.Other"/> the
    /// shot, which is gone after this tick, <see cref="SimEvent.Position"/> where the target
    /// stood, and <see cref="SimEvent.Amount"/> the damage of the hit in thousandths of a hit
    /// point, which may be more than the target had left.
    /// </summary>
    ShotHit,

    /// <summary>
    /// A shot's target was gone before the shot arrived, and the shot with it.
    /// <see cref="SimEvent.Player"/> is the owner of the unit that fired it,
    /// <see cref="SimEvent.Entity"/> the shot, <see cref="SimEvent.Other"/> the target it had,
    /// and <see cref="SimEvent.Position"/> where the shot was.
    /// </summary>
    ShotLost,
}
