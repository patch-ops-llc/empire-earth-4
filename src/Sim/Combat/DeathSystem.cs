using Sim.Core;

namespace Sim.Combat;

/// <summary>
/// Destroys everything whose hit points are zero or less (design 2.4). It runs after every attack
/// of the tick has been made, so a unit brought to zero in a tick still makes the attack it has due
/// in that tick, and two units that kill each other in the same tick both die. The order of the
/// units' IDs decides nothing.
/// </summary>
/// <remarks>
/// A destroyed entity is gone at once: it is no longer alive for anything that runs after this, and
/// the world drops its values at the end of the tick. Shots it fired fly on.
/// </remarks>
public sealed class DeathSystem : ISystem
{
    private readonly ComponentStore<Health> _health;

    public DeathSystem(ComponentStore<Health> health)
    {
        ArgumentNullException.ThrowIfNull(health);
        _health = health;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (var i = 0; i < _health.Count; i++)
        {
            if (_health.ValueAt(i).HitPoints <= 0)
            {
                world.Entities.Destroy(_health.EntityAt(i));
            }
        }
    }
}
