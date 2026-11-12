using Sim.Core;
using Sim.Map;

namespace Sim.Movement;

/// <summary>
/// Finds the routes that movers are waiting for, oldest mover first, until the tick's budget is
/// spent. A mover whose route has been found starts walking in the same tick; the others wait
/// where they stand for a later tick.
/// </summary>
/// <remarks>
/// <para>
/// The budget is counted in tiles the search expanded, never in time, so every machine finds the
/// same routes in the same tick. Each tick starts with the whole budget and nothing is carried
/// over. A search is started while any budget is left and always runs to its end, so the oldest
/// waiting mover gets its route every tick, however long the search.
/// </para>
/// <para>
/// A route costs what its search expands, once in a tick for all the movers that share it. It
/// costs the same when the route is already in the cache: what is in the cache is not state, and
/// the tick in which a mover starts walking is.
/// </para>
/// <para>
/// A route is found on the grid of the mover's owner (<see cref="MapGrid.For"/>). Movers
/// share a route when they walk the same grid between the same ends: all players with no gate
/// to walk through walk the same one.
/// </para>
/// </remarks>
public sealed class RoutePlanningSystem : ISystem
{
    /// <summary>The tiles that route searches may expand in one tick before the rest wait. A search across the 96 by 96 map `ridge` expands a few thousand.</summary>
    public const int ExpansionsPerTick = 4096;

    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly RouteCache _routes;
    private readonly List<(int View, int Start, int Goal)> _found = [];

    public RoutePlanningSystem(ComponentStore<Body> bodies, ComponentStore<Mover> movers, RouteCache routes)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(routes);
        _bodies = bodies;
        _movers = movers;
        _routes = routes;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        _found.Clear();
        var spent = 0;
        for (var i = 0; i < _movers.Count; i++)
        {
            ref var mover = ref _movers.ValueAt(i);
            var entity = _movers.EntityAt(i);
            if (mover.State != MoveState.Planning || !world.Entities.IsAlive(entity))
            {
                continue;
            }

            var owner = _bodies.Get(entity).Owner;
            var ends = (_routes.Grid.For(owner).View, mover.RouteStart, mover.RouteGoal);
            if (!_found.Contains(ends))
            {
                if (spent >= ExpansionsPerTick)
                {
                    continue;
                }

                spent = checked(spent + _routes.Get(owner, mover.RouteStart, mover.RouteGoal).Expansions);
                _found.Add(ends);
            }

            mover.State = MoveState.Following;
        }
    }
}
