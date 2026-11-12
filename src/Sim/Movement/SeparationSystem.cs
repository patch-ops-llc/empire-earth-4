using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Movement;

/// <summary>
/// Keeps the units of one side apart: the soft half of the spacing rule (design 2.4). Units that
/// stand closer to each other than their spacing step apart until none is, and then stand still.
/// Units that walk give each other half that distance where they have the room. Nothing here
/// stops a unit: a walking unit goes through any crowd of its own side, and the crowd closes
/// behind it. A unit that waits for enemy units to clear its way stands still.
/// </summary>
/// <remarks>
/// <para>
/// Which two units keep which distance is <see cref="SpacingRule.DistanceOwnUnitsKeep"/>. Enemy
/// units never push each other: a unit steps away from its own side only, and like every movement
/// its step is taken only if the enemies near it allow it (<see cref="SpacingRule.AllowsStep"/>).
/// A standing unit whose step an enemy does not allow takes the part of it that leads along that
/// enemy's side, so units that stop before an enemy line spread out along it.
/// </para>
/// <para>
/// Every tick, each unit adds up what it is short of with each neighbor that is too close, half
/// of it for each of the two, in the direction away from that neighbor, and takes one step of that
/// sum, at most <see cref="Mover.Speed"/> long for a standing unit and half of it for a walking
/// one. All steps are worked out from where the units stand after movement, before any is taken,
/// so the order of the units decides nothing but ties: two units on the very same point part
/// along a direction made from their two IDs.
/// </para>
/// <para>
/// A standing unit steps only onto ground it can stand on, never across the corner between two
/// blocked tiles, and ends <see cref="Mover.StandOff"/> away from blocked ground like a unit that
/// has arrived. Where the ground lets it take none of its step, it tries the same step turned a
/// quarter turn. A walking unit steps only to where it keeps its distance from blocked ground and
/// its way to the point it is heading for stays clear, so the step never costs it its route. A step that cannot be taken is not taken: units
/// pressed into a place too small for them stay closer than their spacing.
/// </para>
/// <para>
/// A standing unit that holds its ground (<see cref="IGroundHolders"/>), a worker at its place
/// or a melee unit where it last attacked from, takes no step, and a unit too close to it takes
/// the whole step, as beside a unit that cannot move. Whole steps away from several such units
/// count as their mean, so that a unit between two of them comes to rest in the middle and does
/// not step past it each tick.
/// </para>
/// <para>
/// Standing units aim for <see cref="Slack"/> more than their spacing, so that two units end apart
/// by at least the spacing whatever the rounding, and a block that stands at exactly its spacing
/// is left alone.
/// </para>
/// </remarks>
public sealed class SeparationSystem : ISystem
{
    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly RouteCache _routes;
    private readonly EnemyBlocking _blocking;
    private readonly IGroundHolders[] _holders;

    // Scratch for one tick. Not state.
    private readonly List<int> _near = [];
    private Step[] _steps = new Step[64];

    /// <param name="grid">The map grid.</param>
    /// <param name="bodies">The store of bodies.</param>
    /// <param name="movers">The store of movers.</param>
    /// <param name="routes">The routes found so far.</param>
    /// <param name="holders">Each names standing units that hold their ground.</param>
    public SeparationSystem(
        MapGrid grid, ComponentStore<Body> bodies, ComponentStore<Mover> movers, RouteCache routes, params IGroundHolders[] holders)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(routes);
        _grid = grid;
        _bodies = bodies;
        _movers = movers;
        _routes = routes;
        _blocking = new EnemyBlocking(grid, bodies, movers);
        ArgumentNullException.ThrowIfNull(holders);
        _holders = [.. holders];
    }

    /// <summary>What standing units step apart by beyond their spacing: 1/256 of a tile.</summary>
    public static Fixed Slack => Fixed.FromRaw(Fixed.RawOne / 256);

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        // Movement has just run, so the units are filed again under where they stand now. A step
        // here is at most a unit's speed, and a standing unit is then moved off blocked ground,
        // by less than a tile.
        _blocking.Begin(world, beyondSpeed: Fixed.One);
        var widest = _blocking.WidestSpacing;
        if (widest == Fixed.Zero)
        {
            return;
        }

        if (_steps.Length < _movers.Count)
        {
            _steps = new Step[Math.Max(_movers.Count, _steps.Length * 2)];
        }

        var anyStep = false;
        for (var i = 0; i < _movers.Count; i++)
        {
            _steps[i] = StepOf(i, widest);
            anyStep |= _steps[i] != default;
        }

        if (!anyStep)
        {
            return;
        }

        for (var i = 0; i < _movers.Count; i++)
        {
            var step = _steps[i];
            if (step == default)
            {
                continue;
            }

            ref var mover = ref _movers.ValueAt(i);
            ref var body = ref _bodies.ValueAt(_blocking.BodyIndexOf(i));
            var standing = mover.State == MoveState.Idle;
            var next = standing ? StepStanding(i, widest, body.Position, step) : StepWalking(mover, body.Position, step);
            if (!_blocking.Allows(i, mover.Spacing, body.Position, next, out var blocker))
            {
                // An enemy is in the way of the step. A standing unit takes the part of the step
                // that leads along that enemy's side, if the enemies allow that.
                if (!standing)
                {
                    continue;
                }

                next = StepStanding(i, widest, body.Position, Beside(step, body.Position, blocker.Position));
                if (!_blocking.Allows(i, mover.Spacing, body.Position, next, out _))
                {
                    continue;
                }
            }

            body.Position = next;
        }
    }

    /// <summary>
    /// A step without its part toward an enemy: what is left leads along the enemy's side, and
    /// by rounding a hair away from it.
    /// </summary>
    private static Step Beside(Step step, FixedVector2 position, FixedVector2 enemy)
    {
        var towardX = (long)enemy.X.Raw - position.X.Raw;
        var towardY = (long)enemy.Y.Raw - position.Y.Raw;
        var squared = (towardX * towardX) + (towardY * towardY);
        var along = (step.X * towardX) + (step.Y * towardY);
        if (squared == 0 || along <= 0)
        {
            return default;
        }

        return new Step(
            step.X - (int)AwayFromZero(towardX * along, squared),
            step.Y - (int)AwayFromZero(towardY * along, squared));

        static long AwayFromZero(long value, long divisor) =>
            value >= 0 ? (value + divisor - 1) / divisor : -((-value + divisor - 1) / divisor);
    }

    /// <summary>The step a unit would take away from the neighbors that are too close, before the ground has its say.</summary>
    private Step StepOf(int unit, Fixed widest)
    {
        ref var mover = ref _movers.ValueAt(unit);
        if (mover.Speed <= Fixed.Zero || mover.State == MoveState.Planning || mover.Waiting || !_blocking.IsFiled(unit))
        {
            return default;
        }

        var position = _blocking.StoodAt(unit);
        var owner = _blocking.OwnerOf(unit);
        var standing = mover.State == MoveState.Idle;
        if (standing && HoldsGround(unit))
        {
            return default;
        }

        long pushX = 0;
        long pushY = 0;
        long heldX = 0;
        long heldY = 0;
        var holding = 0;
        _blocking.Near(position, widest, _near);
        foreach (var other in _near)
        {
            if (other == unit || SpacingRule.AreEnemyGroundUnits(owner, _blocking.OwnerOf(other)))
            {
                continue;
            }

            ref var otherMover = ref _movers.ValueAt(other);
            var otherPosition = _blocking.StoodAt(other);
            var keep = SpacingRule.DistanceOwnUnitsKeep(mover, otherMover);
            var squared = FixedVector2.DistanceSquaredRaw(position, otherPosition);
            if (squared >= FixedVector2.SquareRaw(keep))
            {
                continue;
            }

            // What the two are short of, and this unit's part of it: half, rounded up, or all of
            // it when the other cannot move or holds its ground.
            long distance = IntegerMath.Sqrt((ulong)squared);
            var missing = keep.Raw - distance + (standing ? Slack.Raw : 0);
            var otherStepsToo = otherMover.Speed > Fixed.Zero && !(otherMover.State == MoveState.Idle && HoldsGround(other));
            var share = otherStepsToo ? (missing + 1) / 2 : missing;
            long partX;
            long partY;
            if (distance > 0)
            {
                partX = ((long)position.X.Raw - otherPosition.X.Raw) * share / distance;
                partY = ((long)position.Y.Raw - otherPosition.Y.Raw) * share / distance;
            }
            else
            {
                var away = DirectionBetween(_movers.EntityAt(unit), _movers.EntityAt(other));
                partX = away.X.Raw * share / Fixed.RawOne;
                partY = away.Y.Raw * share / Fixed.RawOne;
            }

            if (otherStepsToo)
            {
                pushX += partX;
                pushY += partY;
            }
            else
            {
                heldX += partX;
                heldY += partY;
                holding++;
            }
        }

        // The whole steps away from units that stay where they are count once between them, as
        // their mean: a unit between two such units would otherwise step past the middle each
        // tick, back and forth, and never rest.
        if (holding > 0)
        {
            pushX += heldX / holding;
            pushY += heldY / holding;
        }

        if (pushX == 0 && pushY == 0)
        {
            return default;
        }

        // No longer than the unit's step of one tick, or half of it when it is walking.
        long reach = standing ? mover.Speed.Raw : mover.Speed.Raw / 2;
        var lengthSquared = (pushX * pushX) + (pushY * pushY);
        if (lengthSquared > reach * reach)
        {
            long length = IntegerMath.Sqrt((ulong)lengthSquared);
            pushX = pushX * reach / length;
            pushY = pushY * reach / length;
        }

        return new Step((int)pushX, (int)pushY);
    }

    private bool HoldsGround(int unit)
    {
        foreach (var holders in _holders)
        {
            if (holders.HoldsGround(_movers.EntityAt(unit)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The direction in which the first of two units on the same point steps away from the
    /// second. The second gets the opposite one.
    /// </summary>
    private static FixedVector2 DirectionBetween(EntityId first, EntityId second)
    {
        long low = Math.Min(first.Value, second.Value);
        long high = Math.Max(first.Value, second.Value);

        // Spread over the circle by two odd multipliers, so that a pile of units parts all ways.
        var direction = FixedVector2.FromAngle(Angle.FromUnits((int)(((low * 1597) + (high * 987)) % Angle.UnitsPerTurn)));
        return first.Value == low ? direction : -direction;
    }

    private FixedVector2 StepStanding(int unit, Fixed widest, FixedVector2 position, Step step)
    {
        var next = Stand(position, step);
        if (next != position)
        {
            return next;
        }

        // A unit pushed straight at ground it cannot step onto tries the same step turned a
        // quarter turn, so that two units pressed against a wall one behind the other still part.
        // It takes that step only if it leads no nearer to any unit it is too close to: a unit
        // pressed against the ground from two sides stays where it is and rests.
        next = Stand(position, new Step(-step.Y, step.X));
        return next != position && LeadsNoNearer(unit, widest, position, next) ? next : position;
    }

    /// <summary>
    /// True when a standing unit's step brings it no nearer to any unit of its own side that it
    /// stands too close to.
    /// </summary>
    private bool LeadsNoNearer(int unit, Fixed widest, FixedVector2 position, FixedVector2 next)
    {
        ref var mover = ref _movers.ValueAt(unit);
        var owner = _blocking.OwnerOf(unit);
        _blocking.Near(position, widest, _near);
        foreach (var other in _near)
        {
            if (other == unit || SpacingRule.AreEnemyGroundUnits(owner, _blocking.OwnerOf(other)))
            {
                continue;
            }

            var otherPosition = _blocking.StoodAt(other);
            var keep = SpacingRule.DistanceOwnUnitsKeep(mover, _movers.ValueAt(other));
            var now = FixedVector2.DistanceSquaredRaw(position, otherPosition);
            if (now < FixedVector2.SquareRaw(keep) && FixedVector2.DistanceSquaredRaw(next, otherPosition) < now)
            {
                return false;
            }
        }

        return true;
    }

    private FixedVector2 Stand(FixedVector2 position, Step step)
    {
        var x = Fixed.FromRaw(step.X);
        var y = Fixed.FromRaw(step.Y);
        var next = position + new FixedVector2(x, y);
        if (!_grid.IsSegmentClear(position, next))
        {
            // Along the blocked ground, by the part of the step that is free, the longer part first.
            var alongX = position + new FixedVector2(x, Fixed.Zero);
            var alongY = position + new FixedVector2(Fixed.Zero, y);
            var (first, second) = Math.Abs(step.X) >= Math.Abs(step.Y) ? (alongX, alongY) : (alongY, alongX);
            if (first != position && _grid.IsSegmentClear(position, first))
            {
                next = first;
            }
            else if (second != position && _grid.IsSegmentClear(position, second))
            {
                next = second;
            }
            else
            {
                return position;
            }
        }

        return _grid.KeepClear(next, Mover.StandOff);
    }

    private FixedVector2 StepWalking(in Mover mover, FixedVector2 position, Step step)
    {
        var next = position + new FixedVector2(Fixed.FromRaw(step.X), Fixed.FromRaw(step.Y));
        return _grid.IsSegmentClear(position, next)
            && _grid.KeepClear(next, Mover.Clearance) == next
            && _grid.IsCorridorClear(next, MovementSystem.AimOf(mover, _routes), Mover.Clearance)
            ? next
            : position;
    }

    /// <summary>A step, in distance units.</summary>
    private readonly record struct Step(int X, int Y);
}
