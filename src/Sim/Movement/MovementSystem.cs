using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Movement;

/// <summary>
/// Moves every mover that is walking by the distance of its speed, oldest entity first.
/// </summary>
/// <remarks>
/// <para>
/// A mover with a clear way walks a straight line and arrives exactly on its destination. A mover
/// with a route walks from one tile of the route to a later one, and skips ahead whenever it can:
/// it looks along the route for the furthest tile it can walk straight to, and takes the straight
/// line to its destination as soon as that is clear. So the walk bends only where an obstacle
/// makes it.
/// </para>
/// <para>
/// A mover of a group does not aim for the center of a route tile but for the point its
/// <see cref="Mover.Spread"/> away from it, so the group walks beside its shared route as it stood
/// when it got the order. It may aim for such a point only when the point keeps its distance from
/// blocked ground and the straight line from the tile's center to it is clear as well: the point
/// then lies on the route's side of every obstacle, and the mover can always come back to the
/// route from it. Where the ground leaves no
/// such point in sight, the mover halves its spread, as often as it takes, and the group closes
/// up. The spread does not grow again before the next order.
/// </para>
/// <para>
/// "Can walk straight to" means that a corridor <see cref="Mover.Clearance"/> wide to each side is
/// free of blocked tiles. Two neighboring tiles of a route always pass that test from center to
/// center, so a mover without a spread that stands on a tile of its route can always go on.
/// </para>
/// <para>
/// A mover never steps onto a tile it cannot stand on. No walk chosen here leads onto one; should
/// a step do so all the same, the mover stops where it is and its order ends.
/// </para>
/// <para>
/// Enemy units are in the way where the spacing rule says so (design 2.4, <see cref="SpacingRule.AllowsStep"/>),
/// tested against where each stands at the moment this mover moves. Routes ignore units. A mover
/// whose step an enemy does not allow keeps its order and steers round: it takes a step along the
/// circle around the nearest enemy in its way, a little outside that enemy's spacing, if that
/// step is allowed, keeps its distance from blocked ground and its way to the point it aims for
/// clear, and brings it nearer that point.
/// Otherwise it waits (<see cref="Mover.Waiting"/>), and goes on when the way opens. Units of its
/// own side are never in its way.
/// </para>
/// </remarks>
public sealed class MovementSystem : ISystem
{
    /// <summary>
    /// A mover on a route looks ahead when it reaches the point it aims for, every tick on the way
    /// to the route's first tile, and otherwise every this many ticks. Movers take turns, by
    /// their IDs.
    /// </summary>
    public const int LookInterval = 4;

    /// <summary>A spread shorter than this, an eighth of a tile, is dropped when it is halved.</summary>
    public static Fixed LeastSpread => Fixed.FromRaw(Fixed.RawOne / 8);

    /// <summary>
    /// How far outside an enemy's spacing a mover steers round it: 1/32 of a tile. The straight
    /// step from one point of that circle to the next then stays outside the spacing itself.
    /// </summary>
    public static Fixed SteerMargin => Fixed.FromRaw(Fixed.RawOne / 32);

    /// <summary>The longest step along the circle round an enemy: a quarter of a tile, for the same reason.</summary>
    public static Fixed LongestSteerStep => Fixed.FromRaw(Fixed.RawOne / 4);

    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly RouteCache _routes;
    private readonly EnemyBlocking _blocking;

    public MovementSystem(MapGrid grid, ComponentStore<Body> bodies, ComponentStore<Mover> movers, RouteCache routes)
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
    }

    /// <summary>
    /// The point a walking mover is heading for now: its destination on a straight walk, and on a
    /// route the center of the tile at <see cref="Mover.RouteNode"/> moved by its
    /// <see cref="Mover.Spread"/>. The route's first tile is aimed for at its center, where the
    /// order made sure the mover can go.
    /// </summary>
    public static FixedVector2 AimOf(in Mover mover, RouteCache routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return mover.State == MoveState.Following
            ? AimOf(mover, routes.Get(mover.RouteStart, mover.RouteGoal), routes.Grid.Width)
            : mover.Destination;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var began = false;
        for (var i = 0; i < _movers.Count; i++)
        {
            ref var mover = ref _movers.ValueAt(i);
            if (mover.State is not (MoveState.Moving or MoveState.Following))
            {
                continue;
            }

            var entity = _movers.EntityAt(i);
            if (!world.Entities.IsAlive(entity))
            {
                continue;
            }

            if (!began)
            {
                // A unit moves at most its speed in this system, give or take the rounding of a step.
                _blocking.Begin(world, beyondSpeed: Fixed.FromRaw(2));
                began = true;
            }

            ref var body = ref _bodies.Get(entity);
            var turnToLook = (world.Tick + entity.Value) % LookInterval == 0;
            body.Position = Walk(ref mover, i, body.Position, turnToLook);
        }
    }

    private static FixedVector2 AimOf(in Mover mover, Route route, int width)
    {
        var center = CenterOf(route.Tiles[mover.RouteNode], width);
        return mover.RouteNode == 0 ? center : center + mover.Spread;
    }

    private static FixedVector2 CenterOf(int tile, int width) => MapGrid.TileCenter(tile % width, tile / width);

    /// <summary>Carries out one tick of a mover's order and returns where it stands afterwards.</summary>
    private FixedVector2 Walk(ref Mover mover, int unit, FixedVector2 position, bool turnToLook)
    {
        Route? route = null;
        if (mover.State == MoveState.Following)
        {
            route = _routes.Get(mover.RouteStart, mover.RouteGoal);

            // A mover that stands on the point it aims for looks ahead below, as part of arriving.
            if ((turnToLook || mover.RouteNode == 0) && position != AimOf(mover, route, _grid.Width))
            {
                LookAhead(ref mover, route, position);
            }
        }

        var left = mover.Speed;
        mover.Waiting = false;
        while (true)
        {
            var target = mover.State == MoveState.Following ? AimOf(mover, route!, _grid.Width) : mover.Destination;
            if (position == target)
            {
                if (mover.State == MoveState.Moving)
                {
                    mover.Stop();
                    break;
                }

                Arrive(ref mover, route!, position);
                continue;
            }

            if (left <= Fixed.Zero)
            {
                break;
            }

            var next = FixedVector2.MoveTowards(position, target, left);
            if (!_grid.IsPassable(next))
            {
                mover.Stop();
                break;
            }

            if (!_blocking.Allows(unit, mover.Spacing, position, next, out var blocker))
            {
                // An enemy is in the way. Whether the mover steers round or waits, its tick is spent.
                if (TrySteerRound(unit, mover.Spacing, position, target, left, blocker, out next))
                {
                    mover.Facing = (next - position).ToAngle();
                    position = next;
                }
                else
                {
                    mover.Waiting = true;
                }

                break;
            }

            // From the whole way left, not from the step, whose rounding would make the angle coarse.
            mover.Facing = (target - position).ToAngle();
            if (next == target)
            {
                // What is left of the tick's distance goes into the next stretch.
                left -= FixedVector2.Distance(position, target);
            }
            else
            {
                left = Fixed.Zero;
            }

            position = next;
        }

        return position;
    }

    /// <summary>
    /// Finds a step round the enemy in a mover's way: along the circle around that enemy, first
    /// toward the side the mover's aim lies on. False when neither side has a step to take.
    /// </summary>
    private bool TrySteerRound(int unit, Fixed spacing, FixedVector2 position, FixedVector2 target, Fixed reach, EnemyBlocking.Blocker blocker, out FixedVector2 next)
    {
        next = position;
        var fromEnemyX = (long)position.X.Raw - blocker.Position.X.Raw;
        var fromEnemyY = (long)position.Y.Raw - blocker.Position.Y.Raw;
        long distance = IntegerMath.Sqrt((ulong)((fromEnemyX * fromEnemyX) + (fromEnemyY * fromEnemyY)));
        if (distance == 0)
        {
            return false;
        }

        // The circle to walk on, and the turn around its middle that one step makes: the length of
        // the step over the radius, in angle units, of which a whole turn of 2 pi has 4,096.
        var radius = blocker.Spacing + SteerMargin;
        var step = Fixed.Min(reach, LongestSteerStep);
        var turn = Angle.FromUnits(Math.Max(1, (int)((long)step.Raw * 652 / radius.Raw)));
        var outward = new FixedVector2(
            Fixed.FromRaw((int)(fromEnemyX * Fixed.RawOne / distance)),
            Fixed.FromRaw((int)(fromEnemyY * Fixed.RawOne / distance)));

        // The side the aim lies on comes first; a mover that heads straight at the enemy picks a
        // side by its ID.
        var toTarget = target - position;
        var side = Math.Sign((-fromEnemyY * toTarget.X.Raw) + (fromEnemyX * toTarget.Y.Raw));
        if (side == 0)
        {
            side = (_movers.EntityAt(unit).Value & 1) == 0 ? 1 : -1;
        }

        // A step must bring the mover nearer its aim, so that it never walks back or to and fro.
        // A mover inside the circle whose aim lies outside it is measured from the point of the
        // circle it is nearest to: its first step leads out onto the circle, which on its own
        // gains nothing.
        var leavesCircle = distance < radius.Raw
            && FixedVector2.DistanceSquaredRaw(blocker.Position, target) > FixedVector2.SquareRaw(radius);
        var measuredFrom = leavesCircle ? blocker.Position + (outward * radius) : position;
        var before = FixedVector2.DistanceSquaredRaw(measuredFrom, target);
        for (var attempt = 0; attempt < 2; attempt++, side = -side)
        {
            var angle = side > 0 ? turn : -turn;
            var cos = angle.Cos();
            var sin = angle.Sin();
            var turned = new FixedVector2((outward.X * cos) - (outward.Y * sin), (outward.X * sin) + (outward.Y * cos));
            var point = FixedVector2.MoveTowards(position, blocker.Position + (turned * radius), reach);
            if (point != position
                && FixedVector2.DistanceSquaredRaw(point, target) < before
                && _grid.IsSegmentClear(position, point)
                && _grid.KeepClear(point, Mover.Clearance) == point
                && _grid.IsCorridorClear(point, target, Mover.Clearance)
                && _blocking.Allows(unit, spacing, position, point, out _))
            {
                next = point;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gives a mover that stands on the point it was aiming for its next aim. Every call takes the
    /// mover further along its route or makes its spread smaller, so a walk always goes on.
    /// </summary>
    private void Arrive(ref Mover mover, Route route, FixedVector2 position)
    {
        if (mover.Spread != FixedVector2.Zero)
        {
            if (!LookAhead(ref mover, route, position))
            {
                // Nothing further is in sight from here with this spread. The aim moves toward the
                // center of the same tile, along a line that was clear when this point was chosen.
                var half = mover.Spread / 2;
                mover.Spread = half.LengthSquaredRaw() < FixedVector2.SquareRaw(LeastSpread) ? FixedVector2.Zero : half;
            }

            return;
        }

        // On the center of a route tile. The next tile is beside this one, so the walk to its
        // center is clear; a look from here may find a tile further along.
        if (mover.RouteNode + 1 < route.Tiles.Count)
        {
            mover.RouteNode++;
            LookAhead(ref mover, route, position);
        }
        else
        {
            // The order made sure of the walk from the center of the last tile to the destination.
            mover.WalkStraightTo(mover.Destination);
        }
    }

    /// <summary>
    /// Moves the mover's aim along its route to a later tile it can walk straight to, or to its
    /// destination, which ends the route. Returns false, having changed nothing, when it can see
    /// neither.
    /// </summary>
    /// <remarks>
    /// It tries the destination first, then halves the way back toward the tile it is aiming for.
    /// That takes few tries and finds a tile that is far along, not always the furthest; the next
    /// look goes on from there.
    /// </remarks>
    private bool LookAhead(ref Mover mover, Route route, FixedVector2 position)
    {
        var aim = mover.RouteNode;
        var count = route.Tiles.Count;
        for (var probe = count; probe > aim; probe = aim + ((probe - aim) / 2))
        {
            if (probe == count)
            {
                if (_grid.IsCorridorClear(position, mover.Destination, Mover.Clearance))
                {
                    mover.WalkStraightTo(mover.Destination);
                    return true;
                }

                continue;
            }

            var center = CenterOf(route.Tiles[probe], _grid.Width);
            var point = center + mover.Spread;
            if ((point == center || (_grid.KeepClear(point, Mover.Clearance) == point && _grid.IsCorridorClear(center, point, Mover.Clearance)))
                && _grid.IsCorridorClear(position, point, Mover.Clearance))
            {
                mover.RouteNode = probe;
                return true;
            }
        }

        return false;
    }
}
