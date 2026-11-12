using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Structures;

namespace Sim.Combat;

/// <summary>
/// The questions combat asks about a target (design 2.4): whether it is an enemy that can be
/// damaged, whether it is in range, which one a unit picks, and what a hit takes off it. The attack
/// system and the order handler share one of these, so that both judge alike.
/// </summary>
/// <remarks>
/// Distances are between centers and are compared as squares. A target at exactly the range is in
/// range; a target at exactly the minimum range can be fired at. To a structure, every distance
/// is measured to the nearest edge of its footprint (design 2.5), which a <see cref="Mark"/> does.
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
    /// The structures of the match, when it has any. A structure is an enemy like a unit: it can
    /// be named by an attack order, picked as design 2.4 says, and damaged as design 2.5 says.
    /// </summary>
    public ComponentStore<PlacedStructure>? Structures { get; init; }

    /// <summary>
    /// True when the entity can be attacked by a player: it is alive, on the map, has hit points, and
    /// belongs to someone else. There is no friendly fire. A unit and a structure both can.
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
    /// What an attacker measures to when it aims at an entity: the center and the line of a unit,
    /// or the footprint of a structure.
    /// </summary>
    /// <param name="entity">The target.</param>
    /// <param name="body">Its body, as <see cref="IsEnemy"/> gave it.</param>
    public Mark MarkOf(EntityId entity, in Body body) =>
        Structures is { } structures && structures.TryGet(entity, out var structure)
            ? new Mark(body.Position, CombatRules.NoLine, structure.Type, structure.Tiles)
            : new Mark(body.Position, LineOf(entity), Mark.NoStructure, default);

    /// <summary>
    /// True for a structure that units pick only on an attack-move and only for want of a unit or
    /// a Tower: any structure that does not attack and is no wall or gate.
    /// </summary>
    public bool IsPickedLast(EntityId entity) =>
        Structures is { } structures
        && structures.TryGet(entity, out var structure)
        && _rules.PickOf(structure.Type) == StructurePick.Last;

    /// <summary>
    /// True when an attack made from <paramref name="from"/> reaches a target (design 2.4). A unit
    /// is reached as the other overload says. A structure is reached when the nearest edge of its
    /// footprint is within range and not inside the minimum range, or within
    /// <see cref="CombatRules.StructureReach"/> of a melee attacker. A melee hit on a structure
    /// needs no clear line.
    /// </summary>
    public bool Reaches(AttackProfile profile, FixedVector2 from, in Mark mark)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!mark.IsStructure)
        {
            return Reaches(profile, from, mark.Center, mark.Line);
        }

        var distance = mark.DistanceSquaredRawTo(from);
        return profile.Melee
            ? distance <= _rules.StructureReachSquared
            : distance <= profile.RangeSquared && distance >= profile.MinRangeSquared;
    }

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
    public bool TryFindStand(AttackProfile profile, FixedVector2 position, FixedVector2 post, in Mark mark, out FixedVector2 stand)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!mark.IsStructure)
        {
            return TryFindStand(profile, position, post, mark.Center, mark.Line, out stand);
        }

        // To a structure the place is on the straight line to the nearest point of its footprint.
        // The corridor of the walk keeps the unit off the footprint itself.
        var target = mark.NearestTo(position);
        var reach = profile.Melee ? _rules.StructureReach : profile.Range;
        stand = FixedVector2.MoveTowards(target, position, reach - StandMargin);
        if (FixedVector2.DistanceSquaredRaw(post, stand) > profile.AcquisitionSquared)
        {
            stand = FixedVector2.MoveTowards(post, target, profile.Acquisition);
            if (mark.DistanceSquaredRawTo(stand) > FixedVector2.SquareRaw(reach))
            {
                return false;
            }
        }

        return stand != position && _grid.IsCorridorClear(position, stand, Mover.Clearance);
    }

    /// <summary><see cref="TryFindStand(AttackProfile, FixedVector2, FixedVector2, in Mark, out FixedVector2)"/> for a unit as the target, at a point.</summary>
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
    /// The target a unit picks for itself (design 2.4): the nearest enemy unit or Tower within its
    /// acquisition range that its line can hit, the lowest ID among equally near ones. A unit on an
    /// attack-move that can pick neither picks the nearest other enemy structure by the same rule.
    /// <see cref="EntityId.None"/> when there is nothing to pick.
    /// </summary>
    /// <remarks>
    /// Not picked: a target inside the minimum range; for a melee unit, a target on ground it cannot
    /// walk to; for an idle unit, a target it can neither reach from where it stands nor bring
    /// into reach without going further from its post than its acquisition range; and a wall or a
    /// gate, ever. The search reads positions and nothing that an attack of this tick changes, so
    /// the order in which units pick does not matter.
    /// </remarks>
    public EntityId Pick(World world, EntityId self, in Body body, AttackProfile profile, UnitOrder order, FixedVector2 post)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);

        var position = body.Position;
        var region = _grid.RegionOf(position);
        var best = EntityId.None;
        var bestDistance = long.MaxValue;
        var bestStructure = EntityId.None;
        var bestStructureDistance = long.MaxValue;

        // The index files a structure under its center, which is further off than its nearest edge.
        _spatialIndex.QueryCircle(position, profile.Acquisition + _rules.FurthestEdge, _found);
        foreach (var candidate in _found)
        {
            if (candidate == self || !IsEnemy(world, body.Owner, candidate, out var other))
            {
                continue;
            }

            var mark = MarkOf(candidate, in other);
            var last = false;
            if (mark.IsStructure)
            {
                var pick = _rules.PickOf(mark.Structure);
                last = pick == StructurePick.Last;
                if (pick == StructurePick.Never || (last && order != UnitOrder.AttackMove))
                {
                    continue;
                }
            }

            // The candidates come in ascending ID order, so the first of several equally near wins.
            var distance = mark.DistanceSquaredRawTo(position);
            if (distance > profile.AcquisitionSquared
                || distance >= (last ? bestStructureDistance : bestDistance)
                || distance < profile.MinRangeSquared)
            {
                continue;
            }

            if (!_rules.CanHit(profile.Line, mark.Line) || (profile.Melee && !IsBesideRegion(in mark, region)))
            {
                continue;
            }

            if (order == UnitOrder.Idle
                && !Reaches(profile, position, in mark)
                && !TryFindStand(profile, position, post, in mark, out _))
            {
                continue;
            }

            if (last)
            {
                bestStructure = candidate;
                bestStructureDistance = distance;
            }
            else
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best != EntityId.None ? best : bestStructure;
    }

    /// <summary>
    /// The target a finished Tower picks (design 2.5): the nearest enemy unit or Tower within its
    /// range that it can hit, measured from the nearest edge of its footprint, the lowest ID among
    /// equally near ones, or <see cref="EntityId.None"/>. It picks like an idle unit that cannot
    /// move, so its range is as far as it looks.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="self">The Tower.</param>
    /// <param name="owner">Its owner.</param>
    /// <param name="tiles">Its footprint.</param>
    /// <param name="profile">What it attacks with.</param>
    public EntityId PickForTurret(World world, EntityId self, int owner, TileSquare tiles, TurretProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);

        var best = EntityId.None;
        var bestDistance = long.MaxValue;
        var furthest = profile.Range + (2 * _rules.FurthestEdge);
        _spatialIndex.QueryCircle(tiles.Center, furthest, _found);
        foreach (var candidate in _found)
        {
            if (candidate == self || !IsEnemy(world, owner, candidate, out var other))
            {
                continue;
            }

            var mark = MarkOf(candidate, in other);
            if ((mark.IsStructure && _rules.PickOf(mark.Structure) != StructurePick.LikeAUnit) || !profile.CanHit(mark.Line))
            {
                continue;
            }

            var distance = mark.DistanceSquaredRawTo(tiles);
            if (distance > profile.RangeSquared || distance >= bestDistance)
            {
                continue;
            }

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// True when a melee unit standing in a region can get to a target: a unit stands on ground of
    /// that region, or one of the tiles round a structure's footprint is ground of that region.
    /// From each of those tiles the footprint is within melee reach.
    /// </summary>
    private bool IsBesideRegion(in Mark mark, int region)
    {
        if (!mark.IsStructure)
        {
            return _grid.RegionOf(mark.Center) == region;
        }

        var tiles = mark.Tiles;
        for (var y = tiles.Y - 1; y <= tiles.Y + tiles.Size; y++)
        {
            for (var x = tiles.X - 1; x <= tiles.X + tiles.Size; x++)
            {
                if (!tiles.Covers(x, y) && _grid.RegionOf(x, y) == region)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Lists what a blast reaches (design 2.4): every entity of another player than
    /// <paramref name="owner"/> that can be damaged, that the attacker's line can hit, and whose
    /// center is within <paramref name="radius"/> of <paramref name="center"/>, the radius itself
    /// included. A structure counts by its center too, walls among them. In ascending ID order.
    /// The list is emptied first.
    /// </summary>
    /// <remarks>
    /// Nothing blocks a blast, and it never reaches its owner's side. What its owner sees does not
    /// matter either: fog of war decides what a unit may fire at, not what a shell that has landed
    /// damages, so no test for it belongs here.
    /// </remarks>
    public void ReachedByBlast(World world, int owner, int attackerLine, FixedVector2 center, Fixed radius, List<EntityId> reached)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(reached);
        _spatialIndex.QueryCircle(center, radius, reached);
        var kept = 0;
        for (var i = 0; i < reached.Count; i++)
        {
            var entity = reached[i];
            if (world.Entities.IsAlive(entity)
                && _bodies.TryGet(entity, out var body)
                && body.Owner != owner
                && _health.Contains(entity)
                && _rules.CanHit(attackerLine, LineOf(entity)))
            {
                reached[kept++] = entity;
            }
        }

        reached.RemoveRange(kept, reached.Count - kept);
    }

    /// <summary>
    /// Lands a hit: takes the damage off the target's hit points, with the target's side multiplied
    /// in and the result rounded once. Hit points stop at zero, and the target stays until the end
    /// of the tick even there.
    /// </summary>
    /// <remarks>
    /// The target's side of a unit is the counter multiplier of the two lines. That of a structure
    /// is the attacking line's multiplier against structures and the fortification rule, under
    /// the Protection tier its owner has at this moment (design 2.5). A foundation is a structure
    /// like any other here.
    /// </remarks>
    /// <param name="target">What is hit.</param>
    /// <param name="attackerSide">The attacker's damage of one hit, fixed when it attacked.</param>
    /// <param name="attackerLine">The attacker's line, or <see cref="CombatRules.NoLine"/> for a Tower.</param>
    /// <param name="attackerFirepower">The attacker's effective Firepower, fixed when it attacked.</param>
    /// <returns>
    /// The damage of the hit in thousandths of a hit point, which may be more than the target had
    /// left. Zero for a target that cannot be damaged.
    /// </returns>
    public long Hit(EntityId target, Fraction attackerSide, int attackerLine, int attackerFirepower)
    {
        var index = _health.IndexOf(target);
        if (index < 0)
        {
            return 0;
        }

        ref var health = ref _health.ValueAt(index);
        var damage = Structures is { } structures && structures.Contains(target) && _bodies.TryGet(target, out var body)
            ? _rules.DamageToStructure(attackerSide, attackerLine, attackerFirepower, body.Owner)
            : _rules.DamageOf(attackerSide, attackerLine, LineOf(target));
        health.HitPoints = Math.Max(0, checked(health.HitPoints - damage));
        return damage;
    }
}

/// <summary>
/// What an attacker measures to when it aims at a target (design 2.4 and 2.5): the center of a
/// unit, or the nearest edge of the footprint of a structure.
/// </summary>
/// <param name="Center">The target's center: where a unit stands, and the middle of a structure's footprint, which is where a shot at it flies.</param>
/// <param name="Line">The roster index of a unit's line, or <see cref="CombatRules.NoLine"/> for a worker and for a structure.</param>
/// <param name="Structure">A structure's place in the roster, or <see cref="NoStructure"/> for a unit.</param>
/// <param name="Tiles">A structure's footprint. Not used for a unit.</param>
public readonly record struct Mark(FixedVector2 Center, int Line, int Structure, TileSquare Tiles)
{
    /// <summary>The <see cref="Structure"/> of a target that is a unit.</summary>
    public const int NoStructure = -1;

    public bool IsStructure => Structure != NoStructure;

    /// <summary>The point of the target that is nearest to a point: a unit's center, or the nearest point of a structure's footprint.</summary>
    public FixedVector2 NearestTo(FixedVector2 from) => IsStructure ? Tiles.NearestPointTo(from) : Center;

    /// <summary>The square of the distance from a point to the target, in the unit of <see cref="FixedVector2.DistanceSquaredRaw"/>.</summary>
    public long DistanceSquaredRawTo(FixedVector2 from) => FixedVector2.DistanceSquaredRaw(from, NearestTo(from));

    /// <summary>
    /// The square of the distance from the nearest edge of a footprint to the target, in the same
    /// unit: what a Tower measures, so that the distance between a Tower and a unit is the same in
    /// both directions (design 2.5). Between two footprints it is the gap between them.
    /// </summary>
    public long DistanceSquaredRawTo(TileSquare from)
    {
        if (!IsStructure)
        {
            return from.DistanceSquaredRawTo(Center);
        }

        long gapX = Math.Max(0, Math.Max(Tiles.X - (from.X + from.Size), from.X - (Tiles.X + Tiles.Size)));
        long gapY = Math.Max(0, Math.Max(Tiles.Y - (from.Y + from.Size), from.Y - (Tiles.Y + Tiles.Size)));
        return ((gapX * gapX) + (gapY * gapY)) * Fixed.RawOne * Fixed.RawOne;
    }
}
