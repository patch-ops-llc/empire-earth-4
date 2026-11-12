using Sim.Core;
using Sim.Map;
using Sim.Structures;

namespace Sim.Combat;

/// <summary>
/// Makes the attack of every finished Tower that is due in this tick, and has each pick and give
/// up targets (design 2.5): a Tower picks like an idle unit that cannot move.
/// </summary>
/// <remarks>
/// <para>
/// <b>Picking.</b> The nearest enemy unit or Tower within its range that it can hit, measured from
/// the nearest edge of its footprint, ties to the lowest ID. A Tower without a target looks every
/// <see cref="CombatRules.LookInterval"/> ticks, taking turns by ID like the units. A Tower whose
/// wait is over and whose target is out of range picks again in that tick. It picks no structure
/// but a Tower, and nobody can order it.
/// </para>
/// <para>
/// <b>Attacks.</b> A Tower attacks when its wait is over and its target is in range, and then
/// waits its attack interval. It fires a <see cref="Shot"/> from the center of its footprint,
/// reported as a <see cref="SimEventKind.ShotFired"/> with the Tower as the attacker. The shot
/// carries the damage of the owner's Firepower tier at that moment, that tier as its effective
/// Firepower, and no line: a Tower has no counter multipliers.
/// </para>
/// <para>
/// <b>Where it runs.</b> Right after <see cref="AttackSystem"/> and before
/// <see cref="DeathSystem"/>: a Tower's attack is one of the attacks of the tick, so a Tower
/// brought to zero in this tick still fires in it. Like a unit, it decides from positions alone,
/// so nothing depends on whether units or Towers go first.
/// </para>
/// </remarks>
public sealed class TurretSystem : ISystem
{
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<PlacedStructure> _structures;
    private readonly ComponentStore<Turret> _turrets;
    private readonly ComponentStore<Shot> _shots;
    private readonly Targeting _targeting;
    private readonly SimEventList _events;

    public TurretSystem(
        ComponentStore<Body> bodies,
        ComponentStore<PlacedStructure> structures,
        ComponentStore<Turret> turrets,
        ComponentStore<Shot> shots,
        Targeting targeting,
        SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(turrets);
        ArgumentNullException.ThrowIfNull(shots);
        ArgumentNullException.ThrowIfNull(targeting);
        ArgumentNullException.ThrowIfNull(events);
        _bodies = bodies;
        _structures = structures;
        _turrets = turrets;
        _shots = shots;
        _targeting = targeting;
        _events = events;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var rules = _targeting.Rules;
        for (var i = 0; i < _turrets.Count; i++)
        {
            var entity = _turrets.EntityAt(i);
            if (!world.Entities.IsAlive(entity)
                || !_bodies.TryGet(entity, out var body)
                || !_structures.TryGet(entity, out var structure)
                || rules.TurretOf(body.Owner, structure.Type) is not { } profile)
            {
                continue;
            }

            ref var turret = ref _turrets.ValueAt(i);
            if (turret.Wait > 0)
            {
                turret.Wait--;
            }

            var tiles = structure.Tiles;
            var target = default(Body);
            if (turret.Target != EntityId.None && !_targeting.IsEnemy(world, body.Owner, turret.Target, out target))
            {
                turret.Target = EntityId.None;
            }

            var inRange = turret.Target != EntityId.None && IsInRange(profile, tiles, turret.Target, in target);
            var look = turret.Target == EntityId.None
                ? (world.Tick + entity.Value) % rules.LookInterval == 0
                : !inRange && turret.Wait <= 0;
            if (look)
            {
                turret.Target = _targeting.PickForTurret(world, entity, body.Owner, tiles, profile);
                inRange = turret.Target != EntityId.None && _targeting.IsEnemy(world, body.Owner, turret.Target, out target);
            }

            if (!inRange || turret.Wait > 0)
            {
                continue;
            }

            var shot = world.Entities.Create();
            _shots.Add(shot, new Shot
            {
                Position = body.Position,
                Target = turret.Target,
                Aim = target.Position,
                Speed = profile.ShotSpeed,
                Damage = profile.Damage,
                Owner = body.Owner,
                Line = CombatRules.NoLine,
                Firepower = profile.Firepower,
            });
            _events.Add(SimEventKind.ShotFired, body.Owner, shot, entity, body.Position);
            turret.Wait = profile.IntervalTicks;
        }
    }

    private bool IsInRange(TurretProfile profile, TileSquare tiles, EntityId target, in Body body) =>
        _targeting.MarkOf(target, in body).DistanceSquaredRawTo(tiles) <= profile.RangeSquared;
}
