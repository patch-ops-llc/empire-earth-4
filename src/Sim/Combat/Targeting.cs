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
    /// Where an idle unit would stand to fight a target it cannot reach from where it is: the
    /// target's place, or the point nearest to it that is no further from the unit's post than its
    /// acquisition range. False when the target is out of reach from there too, when the unit
    /// already stands there, or when it cannot walk straight to that point: such a target cannot
    /// be brought into range within the limit, and an idle unit leaves it alone.
    /// </summary>
    /// <remarks>
    /// Both the unit and the point are inside the circle round the post, or the unit is on its way
    /// back into it, so the straight walk between them never leaves the circle. A way round blocked
    /// ground could, which is why an idle unit takes none.
    /// </remarks>
    public bool TryFindStand(
        AttackProfile profile, FixedVector2 position, FixedVector2 post, FixedVector2 target, int targetLine, out FixedVector2 stand)
    {
        ArgumentNullException.ThrowIfNull(profile);
        stand = FixedVector2.MoveTowards(post, target, profile.Acquisition);
        var distance = FixedVector2.DistanceSquaredRaw(stand, target);
        var limit = profile.Melee ? _rules.ReachSquared(profile.Line, targetLine) : profile.RangeSquared;
        return distance <= limit
            && stand != position
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
    public EntityId Pick(World world, EntityId self, in Body body, AttackProfile profile, UnitOrder order, FixedVector2 post)
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

            if (order == UnitOrder.Idle
                && !Reaches(profile, position, other.Position, line)
                && !TryFindStand(profile, position, post, other.Position, line, out _))
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
    /// in and the result rounded once. The target stays until the end of the tick even at zero.
    /// </summary>
    public void Hit(EntityId target, Fraction attackerSide, int attackerLine)
    {
        var index = _health.IndexOf(target);
        if (index < 0)
        {
            return;
        }

        ref var health = ref _health.ValueAt(index);
        health.HitPoints = checked(health.HitPoints - _rules.DamageOf(attackerSide, attackerLine, LineOf(target)));
    }
}
