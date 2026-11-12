using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;

namespace Sim.Combat;

/// <summary>
/// The questions combat asks about a target (design 2.4): whether it is an enemy that can be
/// damaged, whether it is in range, which one a unit picks, and what a hit takes off it. The attack
/// system and the order handler share one of these, so that both judge alike.
/// </summary>
/// <remarks>
/// Distances are between centers and are compared as squares. A target at exactly the range is in
/// range; a target at exactly the minimum range can be fired at.
/// </remarks>
public sealed class Targeting
{
    /// <summary>
    /// How far inside its reach an idle unit aims to stand when it walks up to a target: a
    /// sixteenth of a tile. It only keeps the rounding of the walk from leaving the unit a hair
    /// outside its reach; the unit stops as soon as the target is in reach, before it gets there.
    /// </summary>
    public static Fixed StandMargin => Fixed.FromRaw(Fixed.RawOne / 16);

    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<Health> _health;
    private readonly SpatialIndex _spatialIndex;
    private readonly CombatRules _rules;

    // Scratch for one search, emptied at its start. Not state.
    private readonly List<EntityId> _found = [];

    public Targeting(
        MapGrid grid,
        ComponentStore<Body> bodies,
        ComponentStore<Combatant> combatants,
        ComponentStore<Health> health,
        SpatialIndex spatialIndex,
        CombatRules rules)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(spatialIndex);
        ArgumentNullException.ThrowIfNull(rules);
        _grid = grid;
        _bodies = bodies;
        _combatants = combatants;
        _health = health;
        _spatialIndex = spatialIndex;
        _rules = rules;
    }

    public CombatRules Rules => _rules;

    /// <summary>
    /// True when the entity can be attacked by a player: it is alive, on the map, has hit points, and
    /// belongs to someone else. There is no friendly fire.
    /// </summary>
    /// <remarks>
    /// Fog of war adds its test here and in <see cref="Pick"/>: until it exists, a player sees
    /// every enemy.
    /// </remarks>
    public bool IsEnemy(World world, int player, EntityId entity, out Body body)
    {
        ArgumentNullException.ThrowIfNull(world);
        body = default;
        return world.Entities.IsAlive(entity)
            && _bodies.TryGet(entity, out body)
            && body.Owner != player
            && _health.Contains(entity);
    }

    /// <summary>The roster index of an entity's line, or <see cref="CombatRules.NoLine"/> when it is not a unit of a chassis.</summary>
    public int LineOf(EntityId entity) =>
        _combatants.TryGet(entity, out var combatant) ? _rules.LineOf(combatant.Chassis) : CombatRules.NoLine;

    /// <summary>
    /// True when an attack made from <paramref name="from"/> reaches a target standing at
    /// <paramref name="target"/>: within range and not inside the minimum range, or within melee
    /// reach with a clear straight line between the two centers.
    /// </summary>
    /// <remarks>
    /// The line of a melee hit must cross no tile a unit cannot walk on, and a line through the
    /// exact corner where four tiles meet needs all four, which is what
    /// <see cref="MapGrid.IsSegmentClear"/> tests. The tiles the two stand in are passable, so
    /// counting them changes nothing. Shots are not blocked by anything.
    /// </remarks>
    public bool Reaches(AttackProfile profile, FixedVector2 from, FixedVector2 target, int targetLine)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var distance = FixedVector2.DistanceSquaredRaw(from, target);
        if (profile.Melee)
        {
            return distance <= _rules.ReachSquared(profile.Line, targetLine) && _grid.IsSegmentClear(from, target);
        }

        return distance <= profile.RangeSquared && distance >= profile.MinRangeSquared;
    }

    /// <summary>
    /// True when a target is inside the minimum range of the attack. The unit cannot fire at it, and
    /// it does not step back to make room (design 2.4): it stands where it is.
    /// </summary>
    public static bool IsTooClose(AttackProfile profile, FixedVector2 from, FixedVector2 target)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return FixedVector2.DistanceSquaredRaw(from, target) < profile.MinRangeSquared;
    }

    /// <summary>
    /// Where an idle unit walks to for a target it cannot reach from where it stands (design 2.4:
    /// it moves only as far as it must, and never further from its post than its acquisition
    /// range). False when there is no such place: the target cannot be brought into reach within
    /// that limit, and an idle unit leaves it alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The place is on the straight line to the target, just inside reach of it, by
    /// <see cref="StandMargin"/>; the unit is stopped on the way there, in the first tick the
    /// target is in reach. Should that place be further from the post than the limit, the place is
    /// the point of the limit nearest the target instead, if the target is in reach from there.
    /// </para>
    /// <para>
    /// The unit must be able to walk straight to the place, and a melee unit must have a clear line
    /// to the target from it. Both ends of the walk are inside the circle round the post, or the
    /// unit is on its way back into it, so the walk never leaves the circle. A way round blocked
    /// ground could, which is why an idle unit takes none.
    /// </para>
    /// </remarks>
    public bool TryFindStand(
        AttackProfile profile, FixedVector2 position, FixedVector2 post, FixedVector2 target, int targetLine, out FixedVector2 stand)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var reach = profile.Melee ? _rules.Reach(profile.Line, targetLine) : profile.Range;
        stand = FixedVector2.MoveTowards(target, position, reach - StandMargin);
        if (FixedVector2.DistanceSquaredRaw(post, stand) > profile.AcquisitionSquared)
        {
            stand = FixedVector2.MoveTowards(post, target, profile.Acquisition);
            if (FixedVector2.DistanceSquaredRaw(stand, target) > FixedVector2.SquareRaw(reach))
            {
                return false;
            }
        }

        return stand != position
            && _grid.IsCorridorClear(position, stand, Mover.Clearance)
            && (!profile.Melee || _grid.IsSegmentClear(stand, target));
    }

    /// <summary>
    /// The target a unit picks for itself (design 2.4): the nearest enemy within its acquisition
    /// range that its line can hit, the lowest ID among equally near ones, or
    /// <see cref="EntityId.None"/>.
    /// </summary>
    /// <remarks>
    /// Not picked: a target inside the minimum range; for a melee unit, a target on ground it cannot
    /// walk to; and for an idle unit, a target it can neither reach from where it stands nor bring
    /// into reach without going further from its post than its acquisition range. The search reads
    /// positions and nothing that an attack of this tick changes, so the order in which units pick
    /// does not matter.
    /// </remarks>
    public EntityId Pick(World world, EntityId self, in Body body, AttackProfile profile, UnitOrder order, FixedVector2 post) =>
        Nearest(world, self, in body, profile, order, post, inReachOnly: false);

    /// <summary>
    /// The target of a unit that enemy units have stopped on its way (design 2.4, "a blocked unit
    /// keeps its order"): the nearest enemy it can hit from where it stands, the lowest ID among
    /// equally near ones, or <see cref="EntityId.None"/> when none is in reach.
    /// </summary>
    /// <remarks>
    /// The nearest enemy is not always one the unit can get to: the spacing of a unit beside it
    /// may hold the attacker a hair outside its reach. The unit in its way is within reach, so a
    /// stopped unit fights that one and does not stand idle before it.
    /// </remarks>
    public EntityId PickInReach(World world, EntityId self, in Body body, AttackProfile profile) =>
        Nearest(world, self, in body, profile, UnitOrder.Idle, body.Position, inReachOnly: true);

    private EntityId Nearest(
        World world, EntityId self, in Body body, AttackProfile profile, UnitOrder order, FixedVector2 post, bool inReachOnly)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);

        var position = body.Position;
        var region = _grid.RegionOf(position);
        var best = EntityId.None;
        var bestDistance = long.MaxValue;
        _spatialIndex.QueryCircle(position, profile.Acquisition, _found);
        foreach (var candidate in _found)
        {
            if (candidate == self || !IsEnemy(world, body.Owner, candidate, out var other))
            {
                continue;
            }

            // The candidates come in ascending ID order, so the first of several equally near wins.
            var distance = FixedVector2.DistanceSquaredRaw(position, other.Position);
            if (distance >= bestDistance || distance < profile.MinRangeSquared)
            {
                continue;
            }

            var line = LineOf(candidate);
            if (!_rules.CanHit(profile.Line, line)
                || (profile.Melee && _grid.RegionOf(other.Position) != region))
            {
                continue;
            }

            if ((inReachOnly || order == UnitOrder.Idle)
                && !Reaches(profile, position, other.Position, line)
                && (inReachOnly || !TryFindStand(profile, position, post, other.Position, line, out _)))
            {
                continue;
            }

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// Lands a hit: takes the damage off the target's hit points, with the target's side multiplied
    /// in and the result rounded once. Hit points stop at zero, and the target stays until the end
    /// of the tick even there.
    /// </summary>
    /// <returns>
    /// The damage of the hit in thousandths of a hit point, which may be more than the target had
    /// left. Zero for a target that cannot be damaged.
    /// </returns>
    public long Hit(EntityId target, Fraction attackerSide, int attackerLine)
    {
        var index = _health.IndexOf(target);
        if (index < 0)
        {
            return 0;
        }

        ref var health = ref _health.ValueAt(index);
        var damage = _rules.DamageOf(attackerSide, attackerLine, LineOf(target));
        health.HitPoints = Math.Max(0, checked(health.HitPoints - damage));
        return damage;
    }
}
