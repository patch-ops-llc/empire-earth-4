using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Perks;
using Sim.Walls;

namespace Sim.Combat;

/// <summary>
/// Makes every unit's attack that is due in this tick, and has each unit pick, follow and give up
/// targets as its order tells it to (design 2.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Attacks.</b> A unit attacks when its wait is over, its target is in reach from where it
/// stands, and it has not moved in this tick. Then it waits its attack interval. A melee hit lands
/// in the tick it is made; a ranged attack fires a <see cref="Shot"/>. A unit that comes into reach
/// while walking is stopped, and so attacks in the next tick at the earliest. Turning takes no
/// time, and a unit may walk again in the tick after an attack. A melee hit is reported as a
/// <see cref="SimEventKind.MeleeHit"/> and a shot as a <see cref="SimEventKind.ShotFired"/>. A melee
/// unit that stands closer than their spacing to a melee unit of its own side that is fighting,
/// or that stands on its place, has no room in the rank and does not attack
/// (<see cref="Fighters.HasNoRoom"/>): only the front rank of a melee block fights.
/// </para>
/// <para>
/// <b>Places.</b> A melee unit that its own side leaves no room looks for a free place by its
/// target and walks there (<see cref="MeleePlaces"/>). It looks when it stands in reach with no
/// room in the rank, when its own side has stepped it out of reach, and, on its way to a target
/// that stands still, when it is within its reach, its spacing and one step of it: then it walks
/// onto its place, the point that faces it or the nearest free one, and not in among the units
/// that are there. A unit on its way to its place walks on while the target is in reach. A melee
/// unit that stands in reach with room takes where it stands as its place. A unit on its place
/// holds its ground like one that is fighting. The place is given up when the target moves, when
/// the unit gets another target or order, when it is no longer on the place or on its way there,
/// when enemy units stop it on its way (on its turn to look; the next place it looks for must
/// be one it can walk straight to), and when it has no room on it after all. A unit that finds
/// no place because its own side has them all stops where it is and looks again on each turn to
/// look. A structure has places like a unit: they lie round its footprint, at the unit's reach
/// from its nearest edge.
/// </para>
/// <para>
/// <b>Charge</b> (design 2.3). "The distance is what the unit has actually moved since its last
/// attack. The bonus applies to all damage of one attack, then the count restarts." A unit whose
/// chassis carries Charge in this tick and that has walked the perk's distance or more since its
/// last attack, or since it was made when it has not attacked, makes this attack with the
/// attacker's side multiplied: the melee hit, or the shot, which keeps it until it lands. Every
/// attack, with the bonus or without, sets the count of <see cref="PerkCounts.Walked"/> back to
/// zero. Such an attack is reported as a <see cref="SimEventKind.ChargedAttack"/> first.
/// </para>
/// <para>
/// <b>Nothing between enemies depends on the order of the units.</b> A unit decides from positions,
/// which no attack changes, and never from hit points; deaths wait for <see cref="DeathSystem"/>.
/// So a unit brought to zero earlier in the loop still attacks, a unit still attacks a target that
/// another has just brought to zero, and the lower ID gets no first strike. Among one side the
/// order of the store decides who gets a place: units look in that order, a place taken is in
/// the state at once, and of two melee units that stand closer than their spacing and could both
/// take where they stand, the one asked first does, and the other then has no room.
/// </para>
/// <para>
/// <b>Picking.</b> Idle units and units on an attack-move pick their own target: the nearest enemy
/// in acquisition range that they can hit. A unit without a target looks every
/// <see cref="CombatRules.LookInterval"/> ticks, taking turns by ID. A unit whose wait is over and
/// whose picked target is not in reach picks again in that tick. A unit that enemy units have
/// stopped on its way (<see cref="Mover.Waiting"/>) picks the nearest enemy it can hit from where
/// it stands, if there is one: the units in its way are in reach, so it fights them. A unit on a
/// move order never picks, and the target of an attack order is never swapped.
/// </para>
/// <para>
/// <b>Structures</b> (design 2.5). A structure is a target like a unit, measured to the nearest
/// edge of its footprint, and a shot at it flies to its center. Units pick a Tower like a unit. A
/// unit on an attack-move picks another structure only when it can pick no unit and no Tower, and
/// while it has such a structure it looks again on each of its turns to look: it turns on a unit
/// or a Tower as soon as one can be picked, and otherwise keeps the structure it has. A wall or a
/// gate is attacked only on an attack order. A unit sent at a structure walks up to the tile
/// beside its footprint that is nearest to itself, and a melee unit takes its place by the
/// footprint when it is nearly there. Towers themselves attack in
/// <see cref="TurretSystem"/>.
/// </para>
/// <para>
/// <b>How far a unit goes.</b> An idle unit walks straight toward its target, and never further
/// from its post than its acquisition range; when a look finds nothing to pick it walks back to
/// its post. A unit on an attack-move follows its target while it is within acquisition range,
/// then goes on to where its order ends. A unit on an attack order follows its target anywhere,
/// and is idle where it stands once the target is destroyed.
/// </para>
/// <para>
/// <b>Fog.</b> A unit fights only what its owner sees (design 5.3). A target that an idle unit or a
/// unit on an attack-move picked is given up in the tick its owner stops seeing it, like one out
/// of range. A unit on an attack order then walks to where the target was last seen and is idle
/// there. A shot already in the air still lands. A structure is seen by any tile of its
/// footprint, and an attack order on one holds while it is out of sight, since it stands where
/// it was seen. One that is destroyed out of its attackers' owner's sight is still remembered by
/// that player, so the units go on to where it stood, and are idle there.
/// </para>
/// <para>
/// <b>Accents</b> (design 2.3). The rate bonus of a melee unit makes each tick of waiting count as
/// more than one (<see cref="AttackProfile.Rate"/>). A unit with splash also deals a share of
/// each hit to the other enemies round its target: a melee hit at once, a shot when it lands
/// (<see cref="SplashDamage"/>). What a target's accents take off a hit is the target's side,
/// read when the hit lands (<see cref="CombatRules.DamageToUnit"/>).
/// </para>
/// <para>
/// <b>Enemy walls in the way</b> (design 2.5, <see cref="WallRule"/>). A unit on a move or an
/// attack-move order whose place enemy walls keep it from attacks the wall tile in its way,
/// <see cref="Combatant.WallInWay"/>, and keeps its order meanwhile; a move order fights nothing
/// else. So does a unit on an attack order whose target enemy walls keep it from. Once the tile
/// is destroyed the unit goes on with its order: to its place, after its target, or to the next
/// wall in its way. It does the same when the way has opened elsewhere, which it sees on its
/// turn to look. A unit on an attack-move still turns on whatever it picks and can get to, and
/// returns to the wall afterwards. A target it has picked that enemy walls keep out of its
/// reach does not hold it: it goes on with its order, and attacks that target once it is in
/// reach. A unit that was not given its wall with its order finds it here, on its turn to look
/// or when its walk has ended, while the tick has some of
/// <see cref="Breaching.ExpansionsPerTick"/> left. An idle unit never attacks a wall.
/// </para>
/// </remarks>
public sealed class AttackSystem : ISystem
{
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<Shot> _shots;
    private readonly Targeting _targeting;
    private readonly Approach _approach;
    private readonly Fighters _fighters;
    private readonly MeleePlaces _places;
    private readonly PerkCounters _counters;
    private readonly SimEventList _events;
    private readonly WallRule _walls;

    // What the searches for walls may still expand in this tick. Set at the start of each run,
    // so nothing of it lasts from one tick to the next. Not state.
    private int _wallBudget;

    // Scratch for one splash, emptied at its start. Not state.
    private readonly List<EntityId> _splashed = [];

    public AttackSystem(
        ComponentStore<Body> bodies,
        ComponentStore<Mover> movers,
        ComponentStore<Combatant> combatants,
        ComponentStore<Shot> shots,
        Targeting targeting,
        Approach approach,
        Fighters fighters,
        MeleePlaces places,
        PerkCounters counters,
        SimEventList events,
        WallRule walls)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(shots);
        ArgumentNullException.ThrowIfNull(targeting);
        ArgumentNullException.ThrowIfNull(approach);
        ArgumentNullException.ThrowIfNull(fighters);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(walls);
        _walls = walls;
        _bodies = bodies;
        _movers = movers;
        _combatants = combatants;
        _shots = shots;
        _targeting = targeting;
        _approach = approach;
        _fighters = fighters;
        _places = places;
        _counters = counters;
        _events = events;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        _wallBudget = Breaching.ExpansionsPerTick;
        for (var i = 0; i < _combatants.Count; i++)
        {
            var entity = _combatants.EntityAt(i);
            if (!world.Entities.IsAlive(entity) || !_bodies.TryGet(entity, out var body))
            {
                continue;
            }

            ref var unit = ref _combatants.ValueAt(i);
            var profile = _targeting.Rules.ProfileOf(body.Owner, unit.Chassis);
            var turn = new Turn(
                world,
                entity,
                body,
                profile,
                _movers.IndexOf(entity),
                Moved: body.Position != unit.Stood,
                TurnToLook: (world.Tick + entity.Value) % _targeting.Rules.LookInterval == 0);

            unit.Stood = body.Position;
            if (unit.Wait > Fraction.Zero)
            {
                unit.Wait -= profile.Rate;
            }

            var attacked = Act(ref unit, in turn);
            if (!attacked && unit.Wait < Fraction.Zero)
            {
                unit.Wait = Fraction.Zero;
            }

            // A place is by one target. Whatever took the target away took the place with it.
            if (!unit.HasPlace)
            {
                unit.PlaceFor = EntityId.None;
            }
        }
    }

    /// <summary>One unit's turn. True when it attacked.</summary>
    private bool Act(ref Combatant unit, in Turn turn)
    {
        var owner = turn.Body.Owner;
        var position = turn.Body.Position;
        switch (unit.Order)
        {
            case UnitOrder.Move:
                if (BreakThrough(ref unit, in turn, out var hitWall))
                {
                    return hitWall;
                }

                if (!IsWalking(turn.Mover))
                {
                    BecomeIdle(ref unit, position);
                }

                return false;

            case UnitOrder.Attack:
                if (!_targeting.IsEnemyThere(turn.World, owner, unit.Target, out var named))
                {
                    // A structure its owner remembers and has not seen gone: the owner cannot
                    // know, so the unit goes on to where the structure stood (design 5.3).
                    if (_targeting.IsRememberedGone(turn.World, owner, unit.Target, out _))
                    {
                        GoWhereLastSeen(ref unit, in turn);
                        return false;
                    }

                    if (turn.Mover >= 0)
                    {
                        _movers.ValueAt(turn.Mover).Stop();
                    }

                    BecomeIdle(ref unit, position);
                    return false;
                }

                // Losing sight ends the order for a unit, which may have walked on. A structure
                // stands where it was seen, so the order on one holds while it is out of sight.
                if (!_targeting.IsStructure(unit.Target) && !_targeting.Sees(owner, unit.Target, in named))
                {
                    GoWhereLastSeen(ref unit, in turn);
                    return false;
                }

                // While its owner sees the target, the unit notes where: the point the order is tied to.
                unit.Anchor = named.Position;
                return Pursue(ref unit, in turn, in named, retargeted: false);
        }

        // Idle or on an attack-move: the unit picks for itself.
        var target = default(Body);
        var retargeted = false;
        if (unit.Target != EntityId.None && !_targeting.IsEnemy(turn.World, owner, unit.Target, out target))
        {
            unit.Target = EntityId.None;
        }

        if (unit.Target != EntityId.None)
        {
            // A unit that has a structure only for want of a unit or a Tower keeps looking for one.
            var mark = _targeting.MarkOf(unit.Target, in target);
            var lookPast = turn.TurnToLook && mark.IsStructure && _targeting.Rules.PickOf(mark.Structure) == StructurePick.Last;
            if (_targeting.Reaches(turn.Profile, position, in mark))
            {
                retargeted = lookPast && LookPastStructure(ref unit, in turn);
            }
            else if (unit.Wait <= Fraction.Zero)
            {
                retargeted = Look(ref unit, in turn);
            }
            else if (lookPast)
            {
                retargeted = LookPastStructure(ref unit, in turn);
            }
            else if (unit.Order == UnitOrder.AttackMove
                && mark.DistanceSquaredRawTo(position) > turn.Profile.AcquisitionSquared)
            {
                unit.Target = EntityId.None;
                Resume(ref unit, in turn);
            }
        }
        else if (turn.TurnToLook)
        {
            retargeted = Look(ref unit, in turn);
        }

        if (unit.Target != EntityId.None)
        {
            if (retargeted)
            {
                _targeting.IsEnemy(turn.World, owner, unit.Target, out target);
            }

            return unit.Order == UnitOrder.Idle
                ? Fight(ref unit, in turn, unit.Target, in target, retargeted)
                : Pursue(ref unit, in turn, in target, retargeted);
        }

        if (unit.Order == UnitOrder.AttackMove)
        {
            if (BreakThrough(ref unit, in turn, out var hitWall))
            {
                return hitWall;
            }

            if (!unit.Engaged && !IsWalking(turn.Mover))
            {
                BecomeIdle(ref unit, position);
            }
        }

        return false;
    }

    /// <summary>
    /// The wall rule for a unit on a move or an attack-move order that has no target: attacks the
    /// wall in its way, goes on once that is destroyed or the way is open, and looks for a wall
    /// when enemy walls keep it from its place. False when no enemy wall is in its way, and the
    /// order goes on as it would without walls. <paramref name="attacked"/> is true when it
    /// attacked.
    /// </summary>
    private bool BreakThrough(ref Combatant unit, in Turn turn, out bool attacked)
    {
        attacked = false;
        if (turn.Mover < 0)
        {
            return false;
        }

        var owner = turn.Body.Owner;
        var position = turn.Body.Position;
        if (unit.WallInWay != EntityId.None)
        {
            // The way may have opened elsewhere: another unit broke through, or the wall's owner
            // took a tile down. Then this tile is no longer in the way.
            if (_targeting.IsEnemyThere(turn.World, owner, unit.WallInWay, out var wall)
                && !(turn.TurnToLook && !_walls.IsCutOff(owner, position, unit.Goal)))
            {
                attacked = Fight(ref unit, in turn, unit.WallInWay, in wall, retargeted: false);
                return true;
            }

            unit.WallInWay = EntityId.None;
            GoOn(ref unit, in turn);
            return true;
        }

        if (!_walls.IsCutOff(owner, position, unit.Goal))
        {
            return false;
        }

        // Enemy walls keep it from its place and it has not found the tile in its way yet. It
        // keeps its order, also when it stands at the nearest tile it could reach.
        if (turn.TurnToLook || !IsWalking(turn.Mover))
        {
            unit.WallInWay = _walls.Find(owner, position, unit.Goal, ref _wallBudget);
            _walls.WalkToWall(turn.World, ref unit, ref _movers.ValueAt(turn.Mover), turn.Body, turn.Profile);
        }

        return true;
    }

    /// <summary>
    /// Sends a unit on a move or an attack-move order on with it after the wall in its way: to
    /// the place of its order, or to the next wall that keeps it from there.
    /// </summary>
    private void GoOn(ref Combatant unit, in Turn turn)
    {
        var position = turn.Body.Position;
        ref var mover = ref _movers.ValueAt(turn.Mover);
        unit.Engaged = false;
        if (_walls.IsCutOff(turn.Body.Owner, position, unit.Goal))
        {
            unit.WallInWay = _walls.Find(turn.Body.Owner, position, unit.Goal, ref _wallBudget);
            if (unit.WallInWay != EntityId.None)
            {
                _walls.WalkToWall(turn.World, ref unit, ref mover, turn.Body, turn.Profile);
                return;
            }
        }

        _approach.WalkTo(ref mover, position, unit.Goal);
        unit.Anchor = mover.State == MoveState.Idle ? position : mover.Destination;
    }

    /// <summary>
    /// Goes after the target of a unit on an attack or an attack-move order: attacks it or walks
    /// after it, unless enemy walls keep the unit from it. Then a unit on an attack order
    /// attacks the wall tile in its way to the target. A unit on an attack-move does not follow
    /// such a target: it goes on with its order, which takes it through the wall if its place
    /// lies behind it, and attacks the target once it is in reach. True when it attacked.
    /// </summary>
    private bool Pursue(ref Combatant unit, in Turn turn, in Body target, bool retargeted)
    {
        var owner = turn.Body.Owner;
        var position = turn.Body.Position;
        var mark = _targeting.MarkOf(unit.Target, in target);
        var named = unit.Order == UnitOrder.Attack;
        if (turn.Mover < 0
            || _targeting.Reaches(turn.Profile, position, in mark)
            || Targeting.IsTooClose(turn.Profile, position, mark.NearestTo(position)))
        {
            // It has its target before it. The wall of an attack order was in the way to this
            // target; that of an attack-move is in the way to its place, and stays.
            if (named)
            {
                unit.WallInWay = EntityId.None;
            }

            return Fight(ref unit, in turn, unit.Target, in target, retargeted);
        }

        if (!named)
        {
            if (!_walls.TryFindStand(owner, turn.Profile, position, in mark, out _))
            {
                return Fight(ref unit, in turn, unit.Target, in target, retargeted);
            }

            Resume(ref unit, in turn);
            if (BreakThrough(ref unit, in turn, out var hitWall))
            {
                return hitWall;
            }

            if (!unit.Engaged && !IsWalking(turn.Mover))
            {
                BecomeIdle(ref unit, position);
            }

            return false;
        }

        ref var mover = ref _movers.ValueAt(turn.Mover);
        var think = turn.TurnToLook || mover.State == MoveState.Idle;
        if (unit.WallInWay != EntityId.None)
        {
            if (_targeting.IsEnemyThere(turn.World, owner, unit.WallInWay, out var wall)
                && !(think && !_walls.TryFindStand(owner, turn.Profile, position, in mark, out _)))
            {
                return Fight(ref unit, in turn, unit.WallInWay, in wall, retargeted: false);
            }

            // The tile is destroyed, or the unit can get to its target without that. It walks
            // after the target again, or turns to the next wall.
            unit.WallInWay = EntityId.None;
            retargeted = true;
            think = true;
        }

        if (think && _walls.TryFindStand(owner, turn.Profile, position, in mark, out var stand))
        {
            unit.WallInWay = _walls.Find(owner, position, stand, ref _wallBudget);
            if (unit.WallInWay != EntityId.None)
            {
                _walls.WalkToWall(turn.World, ref unit, ref mover, turn.Body, turn.Profile);
                return false;
            }
        }

        return Fight(ref unit, in turn, unit.Target, in target, retargeted);
    }

    /// <summary>
    /// Picks a target. When there is none to pick, the unit goes back to what it was doing. True
    /// when its target is another one than before.
    /// </summary>
    private bool Look(ref Combatant unit, in Turn turn)
    {
        var picked = _targeting.Pick(turn.World, turn.Entity, turn.Body, turn.Profile, unit.Order, unit.Anchor);

        // Enemy units have stopped it on its way. The nearest enemy may be one it cannot get to,
        // so it takes the nearest it can hit from where it stands, if there is one.
        if (turn.Mover >= 0 && _movers.ValueAt(turn.Mover).Waiting)
        {
            var inReach = _targeting.PickInReach(turn.World, turn.Entity, turn.Body, turn.Profile);
            if (inReach != EntityId.None)
            {
                picked = inReach;
            }
        }

        var changed = picked != unit.Target;
        unit.Target = picked;
        if (picked == EntityId.None)
        {
            Resume(ref unit, in turn);
        }

        return changed;
    }

    /// <summary>
    /// For a unit whose target is a structure it picked for want of anything else: takes up a unit
    /// or a Tower if one can be picked now, and otherwise keeps the structure it has. True when it
    /// changed its target.
    /// </summary>
    private bool LookPastStructure(ref Combatant unit, in Turn turn)
    {
        var picked = _targeting.Pick(turn.World, turn.Entity, turn.Body, turn.Profile, unit.Order, unit.Anchor);
        if (picked == EntityId.None || picked == unit.Target || _targeting.IsPickedLast(picked))
        {
            return false;
        }

        unit.Target = picked;
        return true;
    }

    /// <summary>
    /// Sends a unit that combat took off its post or its walk back to it: an idle unit to its post,
    /// a unit on an attack-move on to where its order ends.
    /// </summary>
    private void Resume(ref Combatant unit, in Turn turn)
    {
        if (!unit.Engaged)
        {
            return;
        }

        unit.Engaged = false;
        if (turn.Mover < 0 || unit.WallInWay != EntityId.None)
        {
            // A unit with a wall in its way walks back to that; its turn sends it.
            return;
        }

        ref var mover = ref _movers.ValueAt(turn.Mover);
        if (turn.Body.Position == unit.Anchor)
        {
            mover.Stop();
        }
        else
        {
            _approach.WalkTo(ref mover, turn.Body.Position, unit.Anchor);
        }
    }

    /// <summary>
    /// Ends an attack order whose target its owner no longer sees (design 5.3): the unit walks to
    /// where the target was last seen and is idle there. The same for a remembered structure that
    /// is gone without its owner's enemy having seen it go.
    /// </summary>
    /// <remarks>
    /// The walk is a move order, so the unit does not fight on the way, and does not take the
    /// attack up again should the target come back into sight. Once idle it picks for itself.
    /// </remarks>
    private void GoWhereLastSeen(ref Combatant unit, in Turn turn)
    {
        var position = turn.Body.Position;
        var lastSeen = unit.Anchor;
        BecomeIdle(ref unit, position);
        if (turn.Mover < 0 || lastSeen == position)
        {
            return;
        }

        ref var mover = ref _movers.ValueAt(turn.Mover);
        _approach.WalkTo(ref mover, position, lastSeen);
        if (mover.State != MoveState.Idle)
        {
            // As a move order: the place may lie behind enemy walls, and then the unit breaks in.
            unit.Order = UnitOrder.Move;
            unit.Anchor = mover.Destination;
            unit.Goal = _walls.GoalFor(turn.Body.Owner, position, lastSeen);
        }
    }

    /// <summary>
    /// Attacks an enemy if the unit can, and otherwise stops within reach of it, walks after it,
    /// or goes to a free place by it. The enemy is the unit's target or the wall in its way.
    /// True when it attacked.
    /// </summary>
    private bool Fight(ref Combatant unit, in Turn turn, EntityId enemy, in Body target, bool retargeted)
    {
        var profile = turn.Profile;
        var position = turn.Body.Position;
        var mark = _targeting.MarkOf(enemy, in target);
        var nearest = mark.NearestTo(position);
        var reaches = _targeting.Reaches(profile, position, in mark);

        // Enemy units have stopped it on its way: a place it looks for now must be one it can
        // walk straight to.
        var blocked = turn.Mover >= 0 && _movers.ValueAt(turn.Mover).Waiting;

        if (unit.PlaceFor != EntityId.None)
        {
            if (!KeepsPlace(in unit, in turn, enemy, in target, retargeted))
            {
                unit.PlaceFor = EntityId.None;
            }
            else if (position != unit.Place)
            {
                // On its way to its place. It walks on even where the target is already in
                // reach: it has no room to attack from before it gets there.
                return false;
            }
        }

        // A unit does not step back from a target inside its minimum range. It stands and waits.
        if (reaches || Targeting.IsTooClose(profile, position, nearest))
        {
            unit.Engaged = true;
            unit.AimedAt = target.Position;
            var stopped = false;
            if (turn.Mover >= 0)
            {
                ref var walker = ref _movers.ValueAt(turn.Mover);
                stopped = walker.State != MoveState.Idle;
                walker.Stop();
            }

            if (!reaches)
            {
                return false;
            }

            // A melee unit looks at its own side when it has just stopped, when it is ready to
            // attack, and on its turn to look; one that is fighting from where it stands has its
            // room. With no room in the rank it does not attack, and looks for a free place by
            // its target. With room, where it stands becomes its place, so that the units behind
            // it leave it there.
            var ready = !turn.Moved && unit.Wait <= Fraction.Zero;
            if (profile.Melee
                && turn.Mover >= 0
                && (ready || stopped || turn.TurnToLook)
                && !(unit.Wait > Fraction.Zero && unit.Fought == position))
            {
                if (_fighters.HasNoRoom(turn.Entity, turn.Body))
                {
                    unit.PlaceFor = EntityId.None;
                    if (stopped || turn.TurnToLook)
                    {
                        TakePlace(ref unit, in turn, enemy, in target, blocked, out _);
                    }

                    return false;
                }

                if (unit.PlaceFor == EntityId.None
                    && _places.IsFreeWhereItStands(turn.Entity, turn.Body, profile, enemy, target.Position))
                {
                    unit.PlaceFor = enemy;
                    unit.Place = position;
                }
            }

            if (!ready)
            {
                return false;
            }

            if (turn.Mover >= 0 && target.Position != position)
            {
                _movers.ValueAt(turn.Mover).Facing = (target.Position - position).ToAngle();
            }

            // The attacker's side is settled now, Charge included, also for a shot that lands later,
            // and the splash of the hit is a share of the same side.
            var side = profile.Damage;
            var walked = _counters.TakeWalked(turn.Entity);
            if (profile.Charge is { } charge && walked >= charge.Distance)
            {
                side *= charge.Multiplier;
                _events.Add(SimEventKind.ChargedAttack, turn.Body.Owner, turn.Entity, unit.Target, position, charge.Multiplier.RoundHalfUp(1000));
            }

            if (profile.Melee)
            {
                var damage = _targeting.Hit(enemy, side, profile.Line, profile.Firepower);
                _events.Add(SimEventKind.MeleeHit, turn.Body.Owner, enemy, turn.Entity, nearest, damage);
                if (profile.SplashRadius > Fixed.Zero)
                {
                    var hit = new SplashingHit(
                        turn.Body.Owner, turn.Entity, enemy, target.Position, profile.SplashRadius, side, profile.SplashShare,
                        profile.Line, profile.Firepower);
                    SplashDamage.Land(turn.World, _targeting, _bodies, _events, _splashed, in hit);
                }
            }
            else
            {
                var shot = turn.World.Entities.Create();
                _shots.Add(shot, new Shot
                {
                    Position = position,
                    Target = enemy,
                    Aim = target.Position,
                    Speed = profile.ShotSpeed,
                    Damage = side,
                    Owner = turn.Body.Owner,
                    Line = profile.Line,
                    Firepower = profile.Firepower,
                    Blast = profile.Blast,
                    SplashShare = profile.SplashShare,
                    SplashRadius = profile.SplashRadius,
                });
                _events.Add(SimEventKind.ShotFired, turn.Body.Owner, shot, turn.Entity, position);
            }

            unit.Wait += profile.IntervalTicks;
            unit.Fought = position;
            return true;
        }

        if (turn.Mover < 0)
        {
            // It cannot walk, so it can only wait for the target to come into reach.
            return false;
        }

        ref var mover = ref _movers.ValueAt(turn.Mover);
        var arrived = mover.State == MoveState.Idle;

        // A melee unit that has come to a stop by a target that has not moved since, and stands
        // out of reach of it: its own side has stepped it away from a target it had reached
        // (design 2.4, units that stop settle). On its turn to look it looks for a free place by
        // the target. When there is none because its own side has them all, it waits where it
        // stands: walking up again would only get it stepped away again.
        var settled = profile.Melee && !retargeted && unit.Engaged && unit.AimedAt == target.Position;
        if (settled && arrived)
        {
            if (!turn.TurnToLook)
            {
                return false;
            }

            if (TakePlace(ref unit, in turn, enemy, in target, blocked, out var crowded) || crowded)
            {
                return false;
            }
        }
        else if (settled && IsNear(in turn, in mover, in mark))
        {
            // On its way to a target that stands still, and nearly there. It looks for its place
            // now and walks onto it: the point that faces it, where it would have stopped anyway,
            // or the nearest free one when its own side is in the way there. When its own side
            // has every place it stops here, behind them, and does not walk in among them.
            if (TakePlace(ref unit, in turn, enemy, in target, blocked, out var crowded))
            {
                return false;
            }

            if (crowded)
            {
                mover.Stop();
                return false;
            }
        }

        // The walk is planned when the unit takes up the target, and again when the target has
        // moved on: at once if the unit has nowhere left to go, else on its turn to look. An idle
        // unit that has come to the end of its walk without the target in reach thinks again too,
        // and so does any other unit that stands out of reach, on its turn to look.
        var replan = retargeted
            || !unit.Engaged
            || (arrived && (unit.Order == UnitOrder.Idle || turn.TurnToLook))
            || (unit.AimedAt != target.Position && (turn.TurnToLook || arrived));
        if (!replan)
        {
            return false;
        }

        if (unit.Order == UnitOrder.Idle)
        {
            if (!_targeting.TryFindStand(profile, position, unit.Anchor, in mark, out var stand))
            {
                unit.Target = EntityId.None;
                return false;
            }

            mover.WalkStraightTo(stand);
        }
        else if (mark.IsStructure)
        {
            _approach.WalkToStructure(ref mover, position, mark.Tiles);
        }
        else
        {
            _approach.WalkTo(ref mover, position, target.Position);
        }

        unit.Engaged = true;
        unit.AimedAt = target.Position;
        return false;
    }

    /// <summary>
    /// True when a walking unit is near enough to its target to look for its place: within its
    /// reach, its own spacing and one step. A unit that finds no place stops there, and from
    /// further out than its spacing behind the units that have the places.
    /// </summary>
    private bool IsNear(in Turn turn, in Mover mover, in Mark mark)
    {
        var reach = mark.IsStructure ? _targeting.Rules.StructureReach : _targeting.Rules.Reach(turn.Profile.Line, mark.Line);
        return mark.DistanceSquaredRawTo(turn.Body.Position) <= FixedVector2.SquareRaw(reach + mover.Spacing + mover.Speed);
    }

    /// <summary>
    /// True while the place a unit has is still its place: the target stands where it stood, and
    /// the unit stands on the place or is on its way there. A unit that enemy units have stopped
    /// on the way gives the place up on its turn to look.
    /// </summary>
    private bool KeepsPlace(in Combatant unit, in Turn turn, EntityId enemy, in Body target, bool retargeted)
    {
        if (retargeted || unit.PlaceFor != enemy || unit.AimedAt != target.Position || turn.Mover < 0)
        {
            return false;
        }

        ref var mover = ref _movers.ValueAt(turn.Mover);
        if (mover.State == MoveState.Idle)
        {
            return turn.Body.Position == unit.Place;
        }

        return mover.Destination == unit.Place && !(mover.Waiting && turn.TurnToLook);
    }

    /// <summary>
    /// Finds a free place by the unit's target and sends the unit there. False when there is
    /// none; <paramref name="crowded"/> then says whether its own side has taken the room.
    /// </summary>
    private bool TakePlace(ref Combatant unit, in Turn turn, EntityId enemy, in Body target, bool blocked, out bool crowded)
    {
        var position = turn.Body.Position;
        crowded = false;

        // A unit that cannot walk stands where it is and waits for room there.
        if (_movers.ValueAt(turn.Mover).Speed <= Fixed.Zero)
        {
            return false;
        }

        if (!_places.TryFind(
            turn.Entity, turn.Body, turn.Profile, unit.Order, unit.Anchor, enemy, target.Position, blocked, out var place, out crowded))
        {
            return false;
        }

        unit.PlaceFor = enemy;
        unit.Place = place;
        unit.Engaged = true;
        unit.AimedAt = target.Position;
        ref var mover = ref _movers.ValueAt(turn.Mover);
        if (place == position)
        {
            mover.Stop();
        }
        else if (unit.Order == UnitOrder.Idle)
        {
            mover.WalkStraightTo(place);
        }
        else
        {
            _approach.WalkTo(ref mover, position, place);
        }

        return true;
    }

    private bool IsWalking(int mover) => mover >= 0 && _movers.ValueAt(mover).State != MoveState.Idle;

    internal static void BecomeIdle(ref Combatant unit, FixedVector2 position)
    {
        unit.Order = UnitOrder.Idle;
        unit.Target = EntityId.None;
        unit.WallInWay = EntityId.None;
        unit.Engaged = false;
        unit.Anchor = position;
    }

    /// <summary>What one unit's turn reads and does not change.</summary>
    private readonly record struct Turn(
        World World, EntityId Entity, Body Body, AttackProfile Profile, int Mover, bool Moved, bool TurnToLook);
}
