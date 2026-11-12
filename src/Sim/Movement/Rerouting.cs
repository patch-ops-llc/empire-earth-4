using System.Runtime.InteropServices;
using Sim.Core;
using Sim.Map;
using Sim.Numerics;

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
/// Sending again is the move order's own work, so that a unit rerouted here ends up exactly as if
/// its player had repeated the order: the same destination when it can still be reached, the
/// nearest tile it can reach when not, one shared route for units going to the same place. Units
/// are taken in ascending order of their IDs. Only the mover changes: an attack order or a
/// worker's assignment that made the unit walk stays as it is.
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

    // Scratch for one call. Not state.
    private readonly List<Walk> _walks = [];
    private readonly List<EntityId> _group = [];

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

        _walks.Clear();
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

            _walks.Add(new Walk(entity, body.Owner, mover.Destination));
        }

        // One order for the units of a player that go to the same point, so that they share
        // routes as they did when the player sent them.
        for (var first = 0; first < _walks.Count; first++)
        {
            var walk = _walks[first];
            if (walk.Entity == EntityId.None)
            {
                continue;
            }

            _group.Clear();
            _group.Add(walk.Entity);
            for (var other = first + 1; other < _walks.Count; other++)
            {
                if (_walks[other].Owner == walk.Owner && _walks[other].Destination == walk.Destination)
                {
                    _group.Add(_walks[other].Entity);
                    _walks[other] = default;
                }
            }

            _move.Execute(
                world,
                new Command(CommandType.Move, walk.Owner, CollectionsMarshal.AsSpan(_group), walk.Destination.X, walk.Destination.Y));
        }
    }

    private readonly record struct Walk(EntityId Entity, int Owner, FixedVector2 Destination);
}
