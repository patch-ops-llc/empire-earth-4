using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Combat;

/// <summary>
/// Flies every shot one tick toward its target and lands the ones that arrive (design 2.4).
/// </summary>
/// <remarks>
/// <para>
/// A shot follows its target: it heads for where the target is now, and it hits when it gets
/// there. Nothing stops it. A shot whose target is gone is lost. A shot does not need its attacker,
/// whose side of the hit it carries.
/// </para>
/// <para>
/// A shot fired in one tick flies for the first time in the next, so it is in the air for the
/// distance divided by its speed, rounded up to whole ticks, and for one tick at least.
/// </para>
/// </remarks>
public sealed class ShotSystem : ISystem
{
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Shot> _shots;
    private readonly Targeting _targeting;

    public ShotSystem(ComponentStore<Body> bodies, ComponentStore<Shot> shots, Targeting targeting)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(shots);
        ArgumentNullException.ThrowIfNull(targeting);
        _bodies = bodies;
        _shots = shots;
        _targeting = targeting;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (var i = 0; i < _shots.Count; i++)
        {
            var entity = _shots.EntityAt(i);
            if (!world.Entities.IsAlive(entity))
            {
                continue;
            }

            ref var shot = ref _shots.ValueAt(i);
            if (!world.Entities.IsAlive(shot.Target) || !_bodies.TryGet(shot.Target, out var target))
            {
                world.Entities.Destroy(entity);
                continue;
            }

            shot.Position = FixedVector2.MoveTowards(shot.Position, target.Position, shot.Speed);
            if (shot.Position == target.Position)
            {
                _targeting.Hit(shot.Target, shot.Damage, shot.Line);
                world.Entities.Destroy(entity);
            }
        }
    }
}
