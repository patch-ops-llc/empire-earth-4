using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;

namespace Sim.Combat;

/// <summary>
/// Makes every unit's attack that is due in this tick, and has each unit pick, follow and give up
/// targets as its order tells it to (design 2.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Attacks.</b> A unit attacks when its wait is over, its target is in reach from where it
/// stands, and it has not walked in this tick (<see cref="WalkMarks"/>): a step under an order
/// or toward a target is a walk, and a step that its own side gives it to keep their spacing is
/// not. Then it waits its attack interval. A melee hit lands
/// in the tick it is made; a ranged attack fires a <see cref="Shot"/>. A unit that comes into reach
/// while walking is stopped, and so attacks in the next tick at the earliest. Turning takes no
/// time, and a unit may walk again in the tick after an attack. A melee hit is reported as a
/// <see cref="SimEventKind.MeleeHit"/> and a shot as a <see cref="SimEventKind.ShotFired"/>. A melee
/// unit that stands closer than their spacing to a unit of its own side that is fighting has no
/// room in the rank and does not attack (<see cref="Fighters.HasNoRoom"/>): only the front rank
/// of a melee block fights.
/// </para>
/// <para>
/// <b>Nothing between enemies depends on the order of the units.</b> A unit decides from positions,
/// which no attack changes, and never from hit points; deaths wait for <see cref="DeathSystem"/>.
/// So a unit brought to zero earlier in the loop still attacks, a unit still attacks a target that
/// another has just brought to zero, and the lower ID gets no first strike. Among one side the
/// order of the store decides one thing: of two melee units that stand closer than their spacing
/// and would both make their first attack from there in the same tick, the one asked first
/// makes it, and the other then has no room.
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
/// <b>How far a unit goes.</b> An idle unit walks straight toward its target, and never further
/// from its post than its acquisition range; when a look finds nothing to pick it walks back to
/// its post. A unit on an attack-move follows its target while it is within acquisition range,
/// then goes on to where its order ends. A unit on an attack order follows its target anywhere,
/// and is idle where it stands once the target is destroyed.
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
    private readonly WalkMarks _walks;
    private readonly SimEventList _events;

    public AttackSystem(
        ComponentStore<Body> bodies,
        ComponentStore<Mover> movers,
        ComponentStore<Combatant> combatants,
        ComponentStore<Shot> shots,
        Targeting targeting,
        Approach approach,
        Fighters fighters,
        WalkMarks walks,
        SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(shots);
        ArgumentNullException.ThrowIfNull(targeting);
        ArgumentNullException.ThrowIfNull(approach);
        ArgumentNullException.ThrowIfNull(fighters);
        ArgumentNullException.ThrowIfNull(walks);
        ArgumentNullException.ThrowIfNull(events);
        _bodies = bodies;
        _movers = movers;
        _combatants = combatants;
        _shots = shots;
        _targeting = targeting;
        _approach = approach;
        _fighters = fighters;
        _walks = walks;
        _events = events;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

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
                Walked: _walks.Walked(entity),
                TurnToLook: (world.Tick + entity.Value) % CombatRules.LookInterval == 0);

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
                if (!IsWalking(turn.Mover))
                {
                    BecomeIdle(ref unit, position);
                }

                return false;

            case UnitOrder.Attack:
                if (!_targeting.IsEnemy(turn.World, owner, unit.Target, out var named))
                {
                    if (turn.Mover >= 0)
                    {
                        _movers.ValueAt(turn.Mover).Stop();
                    }

                    BecomeIdle(ref unit, position);
                    return false;
                }

                return Fight(ref unit, in turn, in named, retargeted: false);
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
            if (!_targeting.Reaches(turn.Profile, position, target.Position, _targeting.LineOf(unit.Target)))
            {
                if (unit.Wait <= Fraction.Zero)
                {
                    retargeted = Look(ref unit, in turn);
                }
                else if (unit.Order == UnitOrder.AttackMove
                    && FixedVector2.DistanceSquaredRaw(position, target.Position) > turn.Profile.AcquisitionSquared)
                {
                    unit.Target = EntityId.None;
                    Resume(ref unit, in turn);
                }
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

            return Fight(ref unit, in turn, in target, retargeted);
        }

        if (unit.Order == UnitOrder.AttackMove && !unit.Engaged && !IsWalking(turn.Mover))
        {
            BecomeIdle(ref unit, position);
        }

        return false;
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
        if (turn.Mover < 0)
        {
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
    /// Attacks the unit's target if it can, and otherwise stops within reach of it or walks after
    /// it. True when it attacked.
    /// </summary>
    private bool Fight(ref Combatant unit, in Turn turn, in Body target, bool retargeted)
    {
        var profile = turn.Profile;
        var position = turn.Body.Position;
        var targetLine = _targeting.LineOf(unit.Target);
        var reaches = _targeting.Reaches(profile, position, target.Position, targetLine);

        // A unit does not step back from a target inside its minimum range. It stands and waits.
        if (reaches || Targeting.IsTooClose(profile, position, target.Position))
        {
            unit.Engaged = true;
            unit.AimedAt = target.Position;
            if (turn.Mover >= 0)
            {
                _movers.ValueAt(turn.Mover).Stop();
            }

            if (!reaches || turn.Walked || unit.Wait > Fraction.Zero)
            {
                return false;
            }

            if (profile.Melee && _fighters.HasNoRoom(turn.Entity, turn.Body))
            {
                return false;
            }

            if (turn.Mover >= 0 && target.Position != position)
            {
                _movers.ValueAt(turn.Mover).Facing = (target.Position - position).ToAngle();
            }

            if (profile.Melee)
            {
                var damage = _targeting.Hit(unit.Target, profile.Damage, profile.Line);
                _events.Add(SimEventKind.MeleeHit, turn.Body.Owner, unit.Target, turn.Entity, target.Position, damage);
            }
            else
            {
                var shot = turn.World.Entities.Create();
                _shots.Add(shot, new Shot
                {
                    Position = position,
                    Target = unit.Target,
                    Speed = profile.ShotSpeed,
                    Damage = profile.Damage,
                    Owner = turn.Body.Owner,
                    Line = profile.Line,
                    Firepower = profile.Firepower,
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

        // The walk is planned when the unit takes up the target, and again when the target has
        // moved on: at once if the unit has nowhere left to go, else on its turn to look. An idle
        // unit that has come to the end of its walk without the target in reach thinks again too.
        // So does any other unit that stands out of reach, on its turn to look: its own side has
        // stepped it away from a target it had reached (design 2.4, units that stop settle), and
        // it walks up again, a little further round the target each time, until it has a place.
        ref var mover = ref _movers.ValueAt(turn.Mover);
        var arrived = mover.State == MoveState.Idle;
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
            if (!_targeting.TryFindStand(profile, position, unit.Anchor, target.Position, targetLine, out var stand))
            {
                unit.Target = EntityId.None;
                return false;
            }

            mover.WalkStraightTo(stand);
        }
        else
        {
            _approach.WalkTo(ref mover, position, target.Position);
        }

        unit.Engaged = true;
        unit.AimedAt = target.Position;
        return false;
    }

    private bool IsWalking(int mover) => mover >= 0 && _movers.ValueAt(mover).State != MoveState.Idle;

    internal static void BecomeIdle(ref Combatant unit, FixedVector2 position)
    {
        unit.Order = UnitOrder.Idle;
        unit.Target = EntityId.None;
        unit.Engaged = false;
        unit.Anchor = position;
    }

    /// <summary>What one unit's turn reads and does not change.</summary>
    private readonly record struct Turn(
        World World, EntityId Entity, Body Body, AttackProfile Profile, int Mover, bool Walked, bool TurnToLook);
}
