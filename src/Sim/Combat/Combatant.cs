using Sim.Core;
using Sim.Numerics;

namespace Sim.Combat;

/// <summary>
/// What makes an entity a unit of a chassis: which chassis it is, the order it is carrying out, the
/// target it has, and the wait until it may attack again. Its hit points are in its
/// <see cref="Health"/>, its position in its <see cref="Map.Body"/>.
/// </summary>
/// <remarks>
/// A worker is not a chassis (design 4.1) and has no combatant: it has no attack, no line that a
/// counter could name, and it never acts on its own.
/// </remarks>
public struct Combatant : IStateValue
{
    /// <summary>The name of the store that holds every combatant.</summary>
    public const string StoreName = "combatants";

    /// <summary>The chassis, as its <see cref="Data.ChassisDef.Index"/>. Its numbers come from the owner's blueprint of it.</summary>
    public int Chassis { get; set; }

    public UnitOrder Order { get; set; }

    /// <summary>
    /// The enemy it is fighting: the one an attack order names, or the one it picked itself while
    /// idle or on an attack-move. <see cref="EntityId.None"/> when it has none.
    /// </summary>
    public EntityId Target { get; set; }

    /// <summary>
    /// The point its order ties it to. While <see cref="UnitOrder.Idle"/>: its post, where it became
    /// idle and where it returns when nothing is left to fight. On <see cref="UnitOrder.AttackMove"/>:
    /// where the order ends, as the move order placed it. Not used by the other orders.
    /// </summary>
    public FixedVector2 Anchor { get; set; }

    /// <summary>
    /// True when combat has taken the unit off its post or off its walk for <see cref="Target"/>, by
    /// stopping it or by sending it after the target. When the target is lost, such a unit is sent
    /// back to <see cref="Anchor"/>.
    /// </summary>
    public bool Engaged { get; set; }

    /// <summary>Where the target stood when the walk toward it was last planned. Meaningful while <see cref="Engaged"/>.</summary>
    public FixedVector2 AimedAt { get; set; }

    /// <summary>
    /// The ticks left until it may attack again, exactly. Zero or less means ready. Each tick takes
    /// off the chassis's attack rate, an attack adds the attack interval, and what is below zero at
    /// that moment carries over, so that a rate of 23/20 gives exactly 23 attacks in the time of 20
    /// (design 2.3). A unit that is ready and does not attack is set to zero and keeps no surplus.
    /// </summary>
    public Fraction Wait { get; set; }

    /// <summary>
    /// Where it stood when the attack system last ran. A unit that stands somewhere else now has
    /// moved in this tick, and a unit attacks only in a tick in which it does not move (design 2.4).
    /// </summary>
    public FixedVector2 Stood { get; set; }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        Chassis = walker.Field(Chassis);
        Order = (UnitOrder)walker.Field((int)Order);
        Target = walker.Field(Target);
        Anchor = new FixedVector2(walker.Field(Anchor.X), walker.Field(Anchor.Y));
        Engaged = walker.Field(Engaged);
        AimedAt = new FixedVector2(walker.Field(AimedAt.X), walker.Field(AimedAt.Y));
        Wait = walker.Field(Wait);
        Stood = new FixedVector2(walker.Field(Stood.X), walker.Field(Stood.Y));
    }
}
