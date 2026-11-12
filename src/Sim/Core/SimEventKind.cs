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
    /// A melee attack landed. <see cref="SimEvent.Player"/> is the attacker's owner,
    /// <see cref="SimEvent.Entity"/> the target, <see cref="SimEvent.Other"/> the attacker,
    /// <see cref="SimEvent.Position"/> where the target stood, and <see cref="SimEvent.Amount"/>
    /// the damage of the hit in thousandths of a hit point, which may be more than the target
    /// had left.
    /// </summary>
    MeleeHit,

    /// <summary>
    /// One of the orders a unit takes, the development spawn, an order to cancel training or one
    /// to set a rally point was not carried out and changed nothing. <see cref="SimEvent.Player"/> is the player the command names,
    /// <see cref="SimEvent.Amount"/> the number of its <see cref="CommandType"/>,
    /// <see cref="SimEvent.Entity"/> the first entity it lists, if any,
    /// <see cref="SimEvent.Other"/> its target entity, if any, and
    /// <see cref="SimEvent.Position"/> its target point.
    /// </summary>
    OrderRefused,

    /// <summary>
    /// A production structure got its rally point. <see cref="SimEvent.Player"/> is its owner,
    /// <see cref="SimEvent.Entity"/> the structure and <see cref="SimEvent.Position"/> the point.
    /// </summary>
    RallyPointSet,

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

    /// <summary>
    /// A structure was laid on the map and blocks its tiles from now on.
    /// <see cref="SimEvent.Player"/> is its owner, <see cref="SimEvent.Entity"/> the structure,
    /// <see cref="SimEvent.Position"/> the center of its footprint, and
    /// <see cref="SimEvent.Amount"/> its place in the roster of structures.
    /// </summary>
    StructurePlaced,

    /// <summary>
    /// A structure is gone and its tiles are free again. <see cref="SimEvent.Player"/> is its
    /// owner, <see cref="SimEvent.Entity"/> the structure, <see cref="SimEvent.Position"/> the
    /// center of its footprint, and <see cref="SimEvent.Amount"/> its place in the roster of
    /// structures. One whose hit points ran out is reported as a <see cref="Death"/> as well.
    /// </summary>
    StructureRemoved,

    /// <summary>
    /// The player took an order out of a structure's queue and got back what was paid for it.
    /// <see cref="SimEvent.Player"/> is the owner, <see cref="SimEvent.Entity"/> the structure,
    /// <see cref="SimEvent.Position"/> its center, and <see cref="SimEvent.Amount"/> holds what
    /// the order trained and how many, which <c>Production.TrainEvent.TryRead</c> takes apart.
    /// </summary>
    TrainCancelled,

    /// <summary>
    /// An order was placed in a structure's queue and paid for. The fields are those of
    /// <see cref="TrainCancelled"/>.
    /// </summary>
    TrainOrdered,

    /// <summary>
    /// A structure was destroyed and one order of its queue was given back in full: one event
    /// for each order, in the order of the queue. The fields are those of
    /// <see cref="TrainCancelled"/>.
    /// </summary>
    TrainRefunded,

    /// <summary>
    /// An order to train was not placed and changed nothing. <see cref="SimEvent.Player"/> is
    /// the player the command names, <see cref="SimEvent.Entity"/> the first entity it lists, if
    /// any, and <see cref="SimEvent.Amount"/> holds what it asked for and why it was refused,
    /// which <c>Production.TrainEvent.TryRead</c> takes apart.
    /// </summary>
    TrainRefused,

    /// <summary>
    /// A unit came out of a production structure: one event for each unit, so two for a pair.
    /// <see cref="SimEvent.Player"/> is its owner, <see cref="SimEvent.Entity"/> the unit,
    /// <see cref="SimEvent.Other"/> the structure, <see cref="SimEvent.Position"/> where the
    /// unit appeared, and <see cref="SimEvent.Amount"/> holds what it is, which
    /// <c>Production.TrainEvent.TryRead</c> takes apart.
    /// </summary>
    UnitTrained,
}
