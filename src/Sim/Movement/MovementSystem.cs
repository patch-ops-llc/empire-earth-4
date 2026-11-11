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
/// with a route walks from the center of one tile of the route to the center of a later one, and
/// skips ahead whenever it can: it looks along the route for the furthest tile it can walk
/// straight to, and takes the straight line to its destination as soon as that is clear. So the
/// walk bends only where an obstacle makes it.
/// </para>
/// <para>
/// "Can walk straight to" means that a corridor <see cref="Mover.Clearance"/> wide to each side is
/// free of blocked tiles. Two neighboring tiles of a route always pass that test, so a mover that
/// stands on a tile of its route can always go on.
/// </para>
/// <para>
/// A mover never steps onto a tile it cannot stand on. No walk chosen here leads onto one; should
/// a step do so all the same, the mover stops where it is and its order ends.
/// </para>
/// </remarks>
public sealed class MovementSystem : ISystem
{
    /// <summary>
    /// A mover on a route looks ahead when it reaches a tile of the route, every tick on the way
    /// to the route's first tile, and otherwise every this many ticks. Movers take turns, by
    /// their IDs.
    /// </summary>
    public const int LookInterval = 4;

    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly RouteCache _routes;

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
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

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

            ref var body = ref _bodies.Get(entity);
            var turnToLook = (world.Tick + entity.Value) % LookInterval == 0;
            body.Position = Walk(ref mover, body.Position, turnToLook);
        }
    }

    /// <summary>Carries out one tick of a mover's order and returns where it stands afterwards.</summary>
    private FixedVector2 Walk(ref Mover mover, FixedVector2 position, bool turnToLook)
    {
        Route? route = null;
        var looked = false;
        if (mover.State == MoveState.Following)
        {
            route = _routes.Get(mover.RouteStart, mover.RouteGoal);
            if (turnToLook || mover.RouteNode == 0)
            {
                LookAhead(ref mover, route, position);
                looked = true;
            }
        }

        var left = mover.Speed;
        while (true)
        {
            var target = mover.State == MoveState.Following ? CenterOf(route!.Tiles[mover.RouteNode]) : mover.Destination;
            if (position == target)
            {
                if (mover.State == MoveState.Moving)
                {
                    mover.Stop();
                    break;
                }

                // Standing on a tile of the route. A look ahead from here always finds the next
                // tile at least. Should it find nothing, the next tile is taken blind, so that
                // this loop ends whatever the look found.
                var before = mover.RouteNode;
                if (!looked)
                {
                    LookAhead(ref mover, route!, position);
                    looked = true;
                }

                if (mover.State == MoveState.Following && mover.RouteNode == before)
                {
                    if (before + 1 < route!.Tiles.Count)
                    {
                        mover.RouteNode = before + 1;
                    }
                    else
                    {
                        // The last tile of the route holds the destination.
                        mover.WalkStraightTo(mover.Destination);
                    }
                }

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
            looked = false;
        }

        return position;
    }

    /// <summary>
    /// Moves the mover's aim along its route to a later tile it can walk straight to, or to its
    /// destination, which ends the route. Leaves it as it is when it can see neither.
    /// </summary>
    /// <remarks>
    /// It tries the destination first, then halves the way back toward the tile it is aiming for.
    /// That takes few tries and finds a tile that is far along, not always the furthest; the next
    /// look goes on from there.
    /// </remarks>
    private void LookAhead(ref Mover mover, Route route, FixedVector2 position)
    {
        var aim = mover.RouteNode;
        var count = route.Tiles.Count;
        for (var probe = count; probe > aim; probe = aim + ((probe - aim) / 2))
        {
            var point = probe == count ? mover.Destination : CenterOf(route.Tiles[probe]);
            if (!_grid.IsCorridorClear(position, point, Mover.Clearance))
            {
                continue;
            }

            if (probe == count)
            {
                mover.WalkStraightTo(mover.Destination);
            }
            else
            {
                mover.RouteNode = probe;
            }

            return;
        }
    }

    private FixedVector2 CenterOf(int tile) => MapGrid.TileCenter(tile % _grid.Width, tile / _grid.Width);
}
