using Sim.Combat;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Tech;

namespace Sim.Research;

/// <summary>
/// The live refresh of design 2.3: "when a research finishes, every existing unit updates". Most
/// of what a unit is, it reads from its owner's blueprint whenever it needs it, and so has the new
/// values at once. Two values are kept in the state of each unit and are brought up to date here:
/// its hit points and its speed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hit points.</b> Every unit of a chassis gets the hit points of its owner's new blueprint as
/// its maximum, and every worker those of its owner's new Protection tier (design 4.1). Each keeps
/// the fraction of its maximum it had (<see cref="Health.ChangeMaximum"/>). Anything else that has
/// hit points is left as it is: what places it brings its own handler.
/// </para>
/// <para>
/// <b>Speed.</b> Every unit of a chassis gets the speed of its owner's new blueprint. A worker's
/// speed never changes (design 4.1).
/// </para>
/// <para>
/// It runs inside the research system, the last of the tick. So the new hit points are there for
/// the attacks of the next tick, and a unit takes its first step at the new speed in the next
/// tick. The values come from the table it is handed, in the order of the stores, so every
/// machine does the same.
/// </para>
/// </remarks>
public sealed class UnitRefresh(
    CombatRules combat,
    ComponentStore<Body> bodies,
    ComponentStore<Combatant> combatants,
    ComponentStore<Health> health,
    ComponentStore<Mover> movers,
    ComponentStore<Worker> workers) : ITechChangeHandler
{
    public void TechChanged(World world, int player, PlayerTech before, PlayerTech after)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(after);

        var workerHitPoints = combat.WorkerHitPoints(after);
        for (var i = 0; i < health.Count; i++)
        {
            var entity = health.EntityAt(i);
            if (!IsOf(world, entity, player))
            {
                continue;
            }

            if (combatants.TryGet(entity, out var combatant))
            {
                health.ValueAt(i).ChangeMaximum(after.Blueprints[combatant.Chassis].HitPoints);
            }
            else if (workers.Contains(entity))
            {
                health.ValueAt(i).ChangeMaximum(workerHitPoints);
            }
        }

        for (var i = 0; i < combatants.Count; i++)
        {
            var entity = combatants.EntityAt(i);
            if (IsOf(world, entity, player) && movers.Contains(entity))
            {
                movers.Get(entity).Speed = after.Blueprints[combatants.ValueAt(i).Chassis].SpeedPerTick;
            }
        }
    }

    /// <summary>Whether an entity is alive and the player's. One destroyed in this tick is still in the stores, and is left alone.</summary>
    private bool IsOf(World world, EntityId entity, int player) =>
        world.Entities.IsAlive(entity) && bodies.TryGet(entity, out var body) && body.Owner == player;
}
