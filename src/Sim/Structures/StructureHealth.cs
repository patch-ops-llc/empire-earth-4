using Sim.Combat;
using Sim.Core;
using Sim.Map;
using Sim.Research;
using Sim.Tech;

namespace Sim.Structures;

/// <summary>
/// Design 2.5 and 2.3: a structure's hit points follow its owner's Protection tier, and when the
/// tier rises every structure the player has keeps the same fraction of its new maximum.
/// </summary>
public sealed class StructureHealth : ITechChangeHandler
{
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Health> _health;
    private readonly ComponentStore<PlacedStructure> _structures;

    public StructureHealth(ComponentStore<Body> bodies, ComponentStore<Health> health, ComponentStore<PlacedStructure> structures)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(structures);
        _bodies = bodies;
        _health = health;
        _structures = structures;
    }

    public void TechChanged(World world, int player, PlayerTech before, PlayerTech after)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(after);

        for (var i = 0; i < _structures.Count; i++)
        {
            var entity = _structures.EntityAt(i);
            var healthIndex = _health.IndexOf(entity);
            if (healthIndex < 0
                || !world.Entities.IsAlive(entity)
                || !_bodies.TryGet(entity, out var body)
                || body.Owner != player
                || after.StructureOf(_structures.ValueAt(i).Type) is not { } blueprint)
            {
                continue;
            }

            ref var health = ref _health.ValueAt(healthIndex);
            if (health.Maximum == blueprint.HitPoints)
            {
                continue;
            }

            health.HitPoints = HitPointRules.KeepFraction(health.HitPoints, health.Maximum, blueprint.HitPoints);
            health.Maximum = blueprint.HitPoints;
        }
    }
}
