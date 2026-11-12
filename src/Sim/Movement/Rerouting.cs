using Sim.Core;
using Sim.Map;

namespace Sim.Movement;

/// <summary>
/// Gives the units that are on their way a way that fits the tiles as they are now. Whatever
/// changes the tiles of the <see cref="MapGrid"/> inside a tick, which is placing or removing a
/// structure, calls <see cref="Reroute"/> right after it has changed them and emptied the
/// <see cref="RouteCache"/>.
/// </summary>
/// <remarks>
/// <para>
/// A mover's state names its route by its two end tiles and a position along it, and the route is
/// whatever the grid gives for those ends. So after any change of tiles, blocked or freed, the
/// route a unit is on may be another one, and its position along it means nothing. Every unit that
/// is planning or following a route is therefore sent again to the destination it has. A unit
/// walking a straight line keeps it while the line is still clear, and is sent again when it is
/// not.
/// </para>
/// <para>
/// Sending again is the move order's own work, one order for each unit, so that a unit rerouted
/// here ends up exactly as if its player had sent it, alone, to the place it was already going
/// to: the same destination when it can still be reached, the nearest tile it can reach when not.
/// Units are taken in ascending order of their IDs. Each gets a route of its own, because an
/// order for several units places them by their shape as a group, which would move their
/// destinations. Only the mover changes: an attack order or a worker's assignment that made the
/// unit walk stays as it is.
/// </para>
/// <para>
/// A unit standing on a tile that has become blocked has no region to plan in. It stops; whatever
/// lays a structure where units stand moves them off first.
/// </para>
/// </remarks>
public sealed class Rerouting
{
    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly MoveCommandHandler _move;

    public Rerouting(MapGrid grid, ComponentStore<Body> bodies, ComponentStore<Mover> movers, MoveCommandHandler move)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(move);
        _grid = grid;
        _bodies = bodies;
        _movers = movers;
        _move = move;
    }

    /// <summary>Call it inside a tick, after the tiles have changed and the route cache is empty.</summary>
    public void Reroute(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (var i = 0; i < _movers.Count; i++)
        {
            ref var mover = ref _movers.ValueAt(i);
            var entity = _movers.EntityAt(i);
            if (mover.State == MoveState.Idle
                || !world.Entities.IsAlive(entity)
                || !_bodies.TryGet(entity, out var body))
            {
                continue;
            }

            if (_grid.RegionOf(body.Position) == 0)
            {
                mover.Stop();
                continue;
            }

            if (mover.State == MoveState.Moving
                && _grid.IsCorridorClear(body.Position, mover.Destination, Mover.Clearance))
            {
                continue;
            }

            // The order changes this mover and no other, so walking the store on is safe.
            var destination = mover.Destination;
            ReadOnlySpan<EntityId> one = [entity];
            _move.Execute(world, new Command(CommandType.Move, body.Owner, one, destination.X, destination.Y));
        }
    }
}
