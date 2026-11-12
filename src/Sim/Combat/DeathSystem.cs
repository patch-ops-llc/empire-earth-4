using Sim.Core;
using Sim.Map;

namespace Sim.Combat;

/// <summary>
/// Destroys everything whose hit points are down to zero (design 2.4). It runs after every attack
/// of the tick has been made, so a unit brought to zero in a tick still makes the attack it has due
/// in that tick, and two units that kill each other in the same tick both die. The order of the
/// units' IDs decides nothing.
/// </summary>
/// <remarks>
/// A destroyed entity is gone at once: it is no longer alive for anything that runs after this, and
/// the world drops its values at the end of the tick. Shots it fired fly on. Each is reported as
/// a <see cref="SimEventKind.Death"/>.
/// </remarks>
public sealed class DeathSystem : ISystem
{
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Health> _health;
    private readonly SimEventList _events;

    public DeathSystem(ComponentStore<Body> bodies, ComponentStore<Health> health, SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(events);
        _bodies = bodies;
        _health = health;
        _events = events;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (var i = 0; i < _health.Count; i++)
        {
            if (_health.ValueAt(i).HitPoints > 0)
            {
                continue;
            }

            var entity = _health.EntityAt(i);
            if (!world.Entities.IsAlive(entity))
            {
                continue;
            }

            if (_bodies.TryGet(entity, out var body))
            {
                _events.Add(SimEventKind.Death, body.Owner, entity, position: body.Position);
            }
            else
            {
                _events.Add(SimEventKind.Death, entity: entity);
            }

            world.Entities.Destroy(entity);
        }
    }
}
