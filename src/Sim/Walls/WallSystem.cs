using Sim.Construction;
using Sim.Core;
using Sim.Map;
using Sim.Movement;

namespace Sim.Walls;

/// <summary>
/// Takes the wall orders one tick further (design 2.5): each worker with a wall order lays the
/// next tile of its run when it has come to it.
/// </summary>
/// <remarks>
/// <para>
/// The orders are taken oldest first, by the oldest tile each still has. One tile of an order is
/// dealt with in a tick:
/// </para>
/// <list type="bullet">
/// <item>The order of a worker that died ends, and every tile not yet laid is given back.</item>
/// <item>
/// A worker whose center is within <see cref="ConstructionRules.ArrivalReach"/> of the nearest
/// edge of its next tile has arrived. Every rule of placement is judged again with the state as
/// it is now (<see cref="ConstructionRules.CheckArrival"/>): the tile is laid, or skipped and
/// given back.
/// </item>
/// <item>
/// A worker that stands still anywhere else cannot get nearer. The tile is skipped and given
/// back.
/// </item>
/// </list>
/// <para>
/// Then the worker walks on to the tile after it. When none is left it goes back to the work
/// site it was assigned to, if it had one, and stands otherwise.
/// </para>
/// <para>
/// A tile is laid as any foundation is: the owner's units on it are moved off, it blocks
/// everyone from this tick, and it builds itself in the build time of one tile, with
/// <see cref="ConstructionSystem"/>. No worker stays with it. The worker stands beside the tile
/// it lays, so it walks along one side of its wall.
/// </para>
/// <para>
/// <b>Where it runs.</b> Right after construction, for the same reasons: after movement, so that
/// a tile is laid in the tick in which its worker arrives; after the deaths, so that the order
/// of a worker killed in this tick ends in it; after the structures that leave, so that a tile
/// is judged on the ground as it is in this tick. A tile laid here is counted by construction
/// from the next tick, as a foundation laid by a build order is.
/// </para>
/// </remarks>
public sealed class WallSystem : ISystem
{
    private readonly WallOrders _walls;
    private readonly ConstructionRules _rules;

    // Scratch for one tick, emptied before each use. Not state.
    private readonly List<EntityId> _workers = [];
    private readonly HashSet<EntityId> _seen = [];

    public WallSystem(WallOrders walls, ConstructionRules rules)
    {
        ArgumentNullException.ThrowIfNull(walls);
        ArgumentNullException.ThrowIfNull(rules);
        _walls = walls;
        _rules = rules;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        // The orders that there are now, so that ending one does not disturb the walk.
        var plans = _walls.Plans;
        _workers.Clear();
        _seen.Clear();
        for (var i = 0; i < plans.Count; i++)
        {
            var worker = plans.ValueAt(i).Worker;
            if (_seen.Add(worker))
            {
                _workers.Add(worker);
            }
        }

        var laid = false;
        foreach (var worker in _workers)
        {
            if (!world.Entities.IsAlive(worker))
            {
                _walls.EndOrder(worker, BuildProblem.WorkerDied);
                continue;
            }

            var plan = _walls.NextOf(worker);
            var tile = plans.Get(plan);
            ref var mover = ref _rules.Movers.Get(worker);
            if (WallOrders.IsWithinReach(_rules.Bodies.Get(worker).Position, tile.X, tile.Y))
            {
                var problem = _rules.CheckArrival(tile.Owner, _walls.Wall!, tile.X, tile.Y);
                if (problem == BuildProblem.None)
                {
                    _walls.Drop(plan);
                    _rules.LayFoundation(world, tile.Owner, _walls.Wall!, tile.X, tile.Y, tile.Materials, tile.Energy);
                    laid = true;
                }
                else
                {
                    _walls.Skip(plan, problem);
                }
            }
            else if (mover.State == MoveState.Idle)
            {
                _walls.Skip(plan, BuildProblem.CannotReach);
            }
            else
            {
                continue;
            }

            var next = _walls.NextOf(worker);
            if (next == EntityId.None)
            {
                _rules.Movers.Get(worker).Stop();
                _rules.Work.Return(world, worker);
            }
            else
            {
                _walls.SendTo(world, worker, plans.Get(next).X, plans.Get(next).Y);
            }
        }

        if (laid)
        {
            // Units were moved and structures added after the index of this tick was made.
            _rules.SpatialIndex.Rebuild();
        }
    }
}
