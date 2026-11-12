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
/// A shell, which is a shot with a <see cref="Shot.Blast"/>, lands where its target is when it
/// gets there, and every enemy whose center is within the blast radius of that point takes its
/// full damage, each with its own counter multiplier: the target like the others, and only what
/// the attacker's line can hit. Nothing of its owner's side is touched. A shell whose target is
/// gone is not lost: it flies on to where the target last stood and lands there.
/// </para>
/// <para>
/// A shot at a structure flies to the center of its footprint, and a structure is inside a blast
/// when its center is (design 2.4). What a structure takes is worked out when the shot lands,
/// from the effective Firepower the shot was fired with and the Protection tier the structure's
/// owner has then (design 2.5, the fortification rule).
/// </para>
/// <para>
/// A shot of a unit with splash (design 2.3, Overgunned II) hits its target and then deals a share
/// of its damage to every other enemy round it: see <see cref="SplashDamage"/>. A shot that is
/// lost splashes nobody.
/// </para>
/// <para>
/// A shot fired in one tick flies for the first time in the next, so it is in the air for the
/// distance divided by its speed, rounded up to whole ticks, and for one tick at least.
/// </para>
/// <para>
/// It reports a <see cref="SimEventKind.ShotHit"/> for each shot that lands and a
/// <see cref="SimEventKind.ShotLost"/> for each that is lost. For a shell it reports a
/// <see cref="SimEventKind.Blast"/> and after it a <see cref="SimEventKind.ShotHit"/> for each
/// enemy the blast reached, in the order of their IDs. For a shot that splashes it reports a
/// <see cref="SimEventKind.Splash"/> after the hit and a <see cref="SimEventKind.SplashHit"/> for
/// each other enemy the splash reached.
/// </para>
/// </remarks>
public sealed class ShotSystem : ISystem
{
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Shot> _shots;
    private readonly Targeting _targeting;
    private readonly SimEventList _events;

    // Scratch for one blast or one splash, emptied at its start. Not state.
    private readonly List<EntityId> _reached = [];

    public ShotSystem(ComponentStore<Body> bodies, ComponentStore<Shot> shots, Targeting targeting, SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(shots);
        ArgumentNullException.ThrowIfNull(targeting);
        ArgumentNullException.ThrowIfNull(events);
        _bodies = bodies;
        _shots = shots;
        _targeting = targeting;
        _events = events;
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
            var shell = shot.Blast > Fixed.Zero;
            if (world.Entities.IsAlive(shot.Target) && _bodies.TryGet(shot.Target, out var target))
            {
                shot.Aim = target.Position;
            }
            else if (!shell)
            {
                _events.Add(SimEventKind.ShotLost, shot.Owner, entity, shot.Target, shot.Position);
                world.Entities.Destroy(entity);
                continue;
            }

            shot.Position = FixedVector2.MoveTowards(shot.Position, shot.Aim, shot.Speed);
            if (shot.Position != shot.Aim)
            {
                continue;
            }

            if (shell)
            {
                Explode(world, entity, in shot);
            }
            else
            {
                var damage = _targeting.Hit(shot.Target, shot.Damage, shot.Line, shot.Firepower);
                _events.Add(SimEventKind.ShotHit, shot.Owner, shot.Target, entity, shot.Aim, damage);
                if (shot.SplashRadius > Fixed.Zero)
                {
                    var hit = new SplashingHit(
                        shot.Owner, entity, shot.Target, shot.Aim, shot.SplashRadius, shot.Damage, shot.SplashShare, shot.Line, shot.Firepower);
                    SplashDamage.Land(world, _targeting, _bodies, _events, _reached, in hit);
                }
            }

            world.Entities.Destroy(entity);
        }
    }

    /// <summary>Lands a shell at its <see cref="Shot.Aim"/>: every enemy in the blast takes the full damage.</summary>
    private void Explode(World world, EntityId entity, in Shot shell)
    {
        _events.Add(SimEventKind.Blast, shell.Owner, entity, shell.Target, shell.Aim, shell.Blast.Raw);
        _targeting.ReachedByBlast(world, shell.Owner, shell.Line, shell.Aim, shell.Blast, _reached);
        foreach (var reached in _reached)
        {
            var damage = _targeting.Hit(reached, shell.Damage, shell.Line, shell.Firepower, area: true);
            _events.Add(SimEventKind.ShotHit, shell.Owner, reached, entity, _bodies.Get(reached).Position, damage);
        }
    }
}
