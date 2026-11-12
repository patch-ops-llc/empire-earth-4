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
    /// A shell landed: a shot with a blast radius. <see cref="SimEvent.Player"/> is the owner of
    /// the unit that fired it, <see cref="SimEvent.Entity"/> the shell, which is gone after this
    /// tick, <see cref="SimEvent.Other"/> the target it was fired at, which may be gone by now,
    /// <see cref="SimEvent.Position"/> the center of the blast, and <see cref="SimEvent.Amount"/>
    /// its radius in units of 1/65,536 of a tile, the raw value of a
    /// <see cref="Numerics.Fixed"/>. A <see cref="ShotHit"/> follows for each enemy the blast
    /// reached, with the shell as its <see cref="SimEvent.Other"/>; none follows when it reached
    /// nobody.
    /// </summary>
    Blast,

    /// <summary>
    /// A build order ended before its foundation was laid, and its whole price was given back.
    /// <see cref="SimEvent.Player"/> is the owner, for whom alone the event is meant,
    /// <see cref="SimEvent.Entity"/> the worker that had the order,
    /// <see cref="SimEvent.Position"/> the center of the site, and <see cref="SimEvent.Amount"/>
    /// why it ended, as a <see cref="Construction.BuildProblem"/>. A wall order that ends
    /// before its last tile is reported the same way, once: every tile not yet laid was given
    /// back, and the position is the center of the tile that was next.
    /// </summary>
    BuildOrderEnded,

    /// <summary>
    /// A build order was refused when it was given: nothing was paid and no worker went.
    /// <see cref="SimEvent.Player"/> is the player who gave it, for whom alone the event is
    /// meant, <see cref="SimEvent.Entity"/> the first entity the command lists, if any,
    /// <see cref="SimEvent.Position"/> the center of the site, or the command's point when the
    /// site is no place on the map, and <see cref="SimEvent.Amount"/> why it was refused, as a
    /// <see cref="Construction.BuildProblem"/>.
    /// </summary>
    BuildRefused,

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
    /// One of the orders a unit takes, the development spawn, or an order to cancel a build was
    /// not carried out and changed nothing. <see cref="SimEvent.Player"/> is the player the command names,
    /// <see cref="SimEvent.Amount"/> the number of its <see cref="CommandType"/>,
    /// <see cref="SimEvent.Entity"/> the first entity it lists, if any,
    /// <see cref="SimEvent.Other"/> its target entity, if any, and
    /// <see cref="SimEvent.Position"/> its target point.
    /// </summary>
    OrderRefused,

    /// <summary>
    /// A ranged attack was made. <see cref="SimEvent.Player"/> is the attacker's owner,
    /// <see cref="SimEvent.Entity"/> the shot, which is in the store of shots from this tick
    /// until it lands or is lost, <see cref="SimEvent.Other"/> the attacker, and
    /// <see cref="SimEvent.Position"/> where it was fired from.
    /// </summary>
    ShotFired,

    /// <summary>
    /// A shot reached its target, or the blast of a shell reached an enemy: one event for each
    /// enemy a blast reaches, after its <see cref="Blast"/>. <see cref="SimEvent.Player"/> is the
    /// owner of the unit that fired the shot, <see cref="SimEvent.Entity"/> what was hit,
    /// <see cref="SimEvent.Other"/> the shot, which is gone after this tick,
    /// <see cref="SimEvent.Position"/> where what was hit stood, and
    /// <see cref="SimEvent.Amount"/> the damage of the hit in thousandths of a hit point, which
    /// may be more than it had left.
    /// </summary>
    ShotHit,

    /// <summary>
    /// A shot's target was gone before the shot arrived, and the shot with it. Never reported for
    /// a shell, which lands where its target last stood and is reported as a <see cref="Blast"/>.
    /// <see cref="SimEvent.Player"/> is the owner of the unit that fired it,
    /// <see cref="SimEvent.Entity"/> the shot, <see cref="SimEvent.Other"/> the target it had,
    /// and <see cref="SimEvent.Position"/> where the shot was.
    /// </summary>
    ShotLost,

    /// <summary>
    /// A hit landed that splashes (design 2.3, Overgunned II): besides its target, it damages
    /// every other enemy within a distance of the target's center. Reported right after the
    /// <see cref="ShotHit"/> or <see cref="MeleeHit"/> of the hit itself, whether or not it
    /// reached anyone else. <see cref="SimEvent.Player"/> is the attacker's owner,
    /// <see cref="SimEvent.Entity"/> the shot, which is gone after this tick, or the attacker
    /// for a melee hit, <see cref="SimEvent.Other"/> the target that was hit,
    /// <see cref="SimEvent.Position"/> the target's center, from which the distance is measured,
    /// and <see cref="SimEvent.Amount"/> that distance in units of 1/65,536 of a tile, the raw
    /// value of a <see cref="Numerics.Fixed"/>. A <see cref="SplashHit"/> follows for each
    /// other enemy it reached.
    /// </summary>
    Splash,

    /// <summary>
    /// The splash of a hit reached an enemy other than its target: one event for each, after
    /// their <see cref="Splash"/>, in the order of their IDs. <see cref="SimEvent.Player"/> is
    /// the attacker's owner, <see cref="SimEvent.Entity"/> what was hit,
    /// <see cref="SimEvent.Other"/> the shot or, for a melee hit, the attacker, as in the
    /// <see cref="Splash"/>, <see cref="SimEvent.Position"/> where what was hit stood, and
    /// <see cref="SimEvent.Amount"/> the damage in thousandths of a hit point, which may be more
    /// than it had left.
    /// </summary>
    SplashHit,

    /// <summary>
    /// A structure has built itself and gives what its file says from now on.
    /// <see cref="SimEvent.Player"/> is its owner, <see cref="SimEvent.Entity"/> the structure,
    /// <see cref="SimEvent.Position"/> the center of its footprint, and
    /// <see cref="SimEvent.Amount"/> its place in the roster of structures, or for a gate the
    /// number after the roster's last. A gate is open to its owner's units from this tick.
    /// </summary>
    StructureFinished,

    /// <summary>
    /// A structure was laid on the map and blocks its tiles from now on.
    /// <see cref="SimEvent.Player"/> is its owner, <see cref="SimEvent.Entity"/> the structure,
    /// <see cref="SimEvent.Position"/> the center of its footprint, and
    /// <see cref="SimEvent.Amount"/> its place in the roster of structures, or for a gate the
    /// number after the roster's last. One laid by a worker
    /// is not finished yet: it is in the store of foundations until it has built itself.
    /// </summary>
    StructurePlaced,

    /// <summary>
    /// A structure is gone and its tiles are free again. <see cref="SimEvent.Player"/> is its
    /// owner, <see cref="SimEvent.Entity"/> the structure, <see cref="SimEvent.Position"/> the
    /// center of its footprint, and <see cref="SimEvent.Amount"/> its place in the roster of
    /// structures. One whose hit points ran out is reported as a <see cref="Death"/> as well;
    /// one that its owner cancelled while it was building itself is not.
    /// </summary>
    StructureRemoved,

    /// <summary>
    /// A tile of a wall order could not be laid when its worker came to it, and its price was
    /// given back. <see cref="SimEvent.Player"/> is the owner, for whom alone the event is meant,
    /// <see cref="SimEvent.Entity"/> the worker that has the order,
    /// <see cref="SimEvent.Position"/> the center of the tile, and <see cref="SimEvent.Amount"/>
    /// why, as a <see cref="Construction.BuildProblem"/>. The order goes on with its next tile.
    /// </summary>
    WallTileSkipped,
}
