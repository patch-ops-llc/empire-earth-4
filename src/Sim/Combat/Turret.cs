using Sim.Core;

namespace Sim.Combat;

/// <summary>
/// What makes a finished structure attack (design 2.5, the Tower): the target it has and the wait
/// until it may attack again. A structure gets one when it is finished, if its file gives it an
/// attack; a foundation has none, and so does not attack. What it attacks with comes from its
/// owner's blueprint of the structure, as <see cref="CombatRules.TurretOf"/> gives it.
/// </summary>
/// <remarks>
/// It is not a <see cref="Combatant"/>: a structure is of no chassis, takes no orders, never
/// moves, and uses no supply.
/// </remarks>
public struct Turret : IStateValue
{
    /// <summary>The name of the store that holds the turret of every finished structure that attacks.</summary>
    public const string StoreName = "turrets";

    /// <summary>The enemy it picked, or <see cref="EntityId.None"/>.</summary>
    public EntityId Target { get; set; }

    /// <summary>The ticks left until it may attack again. Zero means ready.</summary>
    public int Wait { get; set; }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        Target = walker.Field(Target);
        Wait = walker.Field(Wait);
    }
}
