using Sim.Core;
using Sim.Map;
using Sim.Movement;

namespace Sim.Combat;

/// <summary>
/// EXPERIMENT, variant B, not for merging. A standing unit that shoots and has its target in
/// reach holds its ground: its own side does not step it away, and a unit too close to it takes
/// the whole step and goes round. Melee units keep their own rule (<see cref="Fighters"/>).
/// </summary>
public sealed class StandFirm(Targeting targeting, ComponentStore<Combatant> combatants, ComponentStore<Body> bodies) : IGroundHolders
{
    public bool HoldsGround(EntityId unit)
    {
        if (!combatants.TryGet(unit, out var combatant)
            || combatant.Target == EntityId.None
            || !bodies.TryGet(unit, out var body)
            || !bodies.TryGet(combatant.Target, out var target))
        {
            return false;
        }

        var profile = targeting.Rules.ProfileOf(body.Owner, combatant.Chassis);
        return !profile.Melee && targeting.Reaches(profile, body.Position, targeting.MarkOf(combatant.Target, in target));
    }
}
