using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Economy;

/// <summary>
/// Assigns workers to work sites and gives them their places (design 4.1). It holds no state of
/// its own: who is assigned where is in each <see cref="Worker"/>.
/// </summary>
/// <remarks>
/// <para>
/// The places of a site are the tiles that share a side with it, and a worker stands at the center
/// of its tile. A tile no unit can stand on, or one another site covers, is no place. A deposit
/// lets a worker use each of its tiles and a Generator lets as many workers use its tiles as the
/// data says.
/// </para>
/// <para>
/// A worker takes the free place nearest to it at the moment it is assigned, ties to the lower y
/// and then the lower x, and holds it from then on, also while it walks there. Only a place it can
/// walk to counts. When none is free it waits on a tile beside the site: the nearest one within
/// <see cref="WaitingDistance"/> tiles that is no place of any site and that no other assigned
/// worker stands on or walks to. When there is no such tile it waits where it stands.
/// </para>
/// <para>
/// Freed places go to the waiting workers in the order they began to wait, ties to the lowest ID,
/// each taking the free place nearest to it. A worker that is assigned while others wait does not
/// pass them: they are served first, and it takes what is left.
/// </para>
/// </remarks>
public sealed class WorkAssignments
{
    /// <summary>How far from a site, in tiles, a worker without a place waits.</summary>
    public const int WaitingDistance = 2;

    private readonly ResourceRules _rules;
    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Worker> _workers;
    private readonly ComponentStore<WorkSite> _sites;
    private readonly ICommandHandler _walk;

    // Scratch for one call, emptied at its start: the positions of waiting workers in their store. Not state.
    private readonly List<int> _waiting = [];
    private readonly Comparison<int> _byWaitingSince;

    /// <param name="rules">The numbers of <c>resources.jsonc</c>.</param>
    /// <param name="grid">The map grid.</param>
    /// <param name="bodies">The store of bodies.</param>
    /// <param name="workers">The store of workers.</param>
    /// <param name="sites">The store of work sites.</param>
    /// <param name="walk">
    /// The handler of the move order. A worker is sent to its tile with a move order of its own,
    /// so it walks there as any unit walks to a point.
    /// </param>
    public WorkAssignments(
        ResourceRules rules,
        MapGrid grid,
        ComponentStore<Body> bodies,
        ComponentStore<Worker> workers,
        ComponentStore<WorkSite> sites,
        ICommandHandler walk)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(workers);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(walk);
        _rules = rules;
        _grid = grid;
        _bodies = bodies;
        _workers = workers;
        _sites = sites;
        _walk = walk;

        // A position in the store ascends with the entity's ID, so it breaks the tie.
        _byWaitingSince = (left, right) =>
        {
            var order = _workers.ValueAt(left).WaitingSince.CompareTo(_workers.ValueAt(right).WaitingSince);
            return order != 0 ? order : left.CompareTo(right);
        };
    }

    /// <summary>The number of workers that can hold a place at a site at once.</summary>
    public int PlacesOf(in WorkSite site) =>
        site.Kind == WorkSiteKind.Deposit ? _rules.Materials.WorkersPerDeposit : _rules.Energy.WorkersPerGenerator;

    /// <summary>The number of workers that hold a place at a site, walking to it or standing at it.</summary>
    public int HoldersOf(World world, EntityId site)
    {
        ArgumentNullException.ThrowIfNull(world);

        var holders = 0;
        for (var i = 0; i < _workers.Count; i++)
        {
            ref var worker = ref _workers.ValueAt(i);
            if (worker.HoldsPlace && worker.Site == site && world.Entities.IsAlive(_workers.EntityAt(i)))
            {
                holders++;
            }
        }

        return holders;
    }

    /// <summary>
    /// Assigns a worker to a site, in place of whatever it was assigned to: it takes a place and
    /// walks there, or waits beside the site when no place is free. Call it inside a tick.
    /// </summary>
    /// <returns>
    /// False, with nothing changed, when the entity is no living worker, the site no living work
    /// site, or no place of the site is one the worker can walk to.
    /// </returns>
    public bool Assign(World world, EntityId entity, EntityId site)
    {
        ArgumentNullException.ThrowIfNull(world);

        var workerIndex = _workers.IndexOf(entity);
        var siteIndex = _sites.IndexOf(site);
        if (workerIndex < 0
            || siteIndex < 0
            || !world.Entities.IsAlive(entity)
            || !world.Entities.IsAlive(site)
            || !_bodies.TryGet(entity, out var body))
        {
            return false;
        }

        var target = _sites.ValueAt(siteIndex);
        var region = _grid.RegionOf(body.Position);
        if (region == 0 || !HasPlaceIn(world, target, region))
        {
            return false;
        }

        ref var worker = ref _workers.ValueAt(workerIndex);
        worker = new Worker { Site = site };
        ServeWaiting(world, site);
        if (!TakePlace(world, entity, ref worker, target, body, region))
        {
            Wait(world, entity, ref worker, target, body, region);
        }

        return true;
    }

    /// <summary>Ends a worker's assignment: it gives up its place and its count of ticks. It stands or walks on as it was.</summary>
    public void Release(EntityId entity)
    {
        var index = _workers.IndexOf(entity);
        if (index >= 0)
        {
            _workers.ValueAt(index) = default;
        }
    }

    /// <summary>
    /// A worker that leaves for an order that keeps its assignment, as the build order of a later
    /// milestone does: it gives up its place and its count of ticks and stays assigned to its
    /// site. <see cref="Return"/> brings it back. Nothing happens to a worker without a site.
    /// </summary>
    public void StepAway(EntityId entity)
    {
        var index = _workers.IndexOf(entity);
        if (index >= 0 && _workers.ValueAt(index).State != WorkerState.Idle)
        {
            _workers.ValueAt(index) = new Worker { State = WorkerState.Away, Site = _workers.ValueAt(index).Site };
        }
    }

    /// <summary>
    /// Sends a worker that stepped away back to its site, where it takes a place as a worker
    /// newly assigned does. Call it inside a tick.
    /// </summary>
    /// <returns>False, with the worker left without an assignment, when it cannot go back, and false for a worker that had not stepped away.</returns>
    public bool Return(World world, EntityId entity)
    {
        ArgumentNullException.ThrowIfNull(world);

        var index = _workers.IndexOf(entity);
        if (index < 0 || _workers.ValueAt(index).State != WorkerState.Away)
        {
            return false;
        }

        if (Assign(world, entity, _workers.ValueAt(index).Site))
        {
            return true;
        }

        _workers.ValueAt(index) = default;
        return false;
    }

    /// <summary>
    /// Call it inside a tick, after a structure was laid on a square of tiles. Every worker whose
    /// place, or the tile it waits on, is now under the structure takes a place anew at its
    /// site, as a worker newly assigned does, in the order of the workers' IDs. A worker that can
    /// no longer walk to any place of its site is left without an assignment.
    /// </summary>
    public void LeaveTiles(World world, TileSquare square)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (var i = 0; i < _workers.Count; i++)
        {
            var worker = _workers.ValueAt(i);
            var entity = _workers.EntityAt(i);
            if (worker.State is WorkerState.Idle or WorkerState.Away
                || !square.Covers(worker.Tile % _grid.Width, worker.Tile / _grid.Width)
                || !world.Entities.IsAlive(entity))
            {
                continue;
            }

            if (!Assign(world, entity, worker.Site))
            {
                _workers.ValueAt(i) = default;
            }
        }
    }

    /// <summary>
    /// Gives free places to the waiting workers, in the order they began to wait.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="site">The site whose waiting workers are served. None serves those of every site.</param>
    public void ServeWaiting(World world, EntityId site = default)
    {
        ArgumentNullException.ThrowIfNull(world);

        _waiting.Clear();
        for (var i = 0; i < _workers.Count; i++)
        {
            ref var worker = ref _workers.ValueAt(i);
            if (worker.State == WorkerState.Waiting
                && (site == EntityId.None || worker.Site == site)
                && world.Entities.IsAlive(_workers.EntityAt(i)))
            {
                _waiting.Add(i);
            }
        }

        if (_waiting.Count == 0)
        {
            return;
        }

        _waiting.Sort(_byWaitingSince);
        foreach (var index in _waiting)
        {
            ref var worker = ref _workers.ValueAt(index);
            var entity = _workers.EntityAt(index);
            if (_sites.TryGet(worker.Site, out var target) && world.Entities.IsAlive(worker.Site) && _bodies.TryGet(entity, out var body))
            {
                TakePlace(world, entity, ref worker, target, body, _grid.RegionOf(body.Position));
            }
        }
    }

    private bool TakePlace(World world, EntityId entity, ref Worker worker, in WorkSite site, in Body body, int region)
    {
        if (HoldersOf(world, worker.Site) >= PlacesOf(site))
        {
            return false;
        }

        var found = false;
        long nearest = 0;
        var nearestX = 0;
        var nearestY = 0;
        for (var i = 0; i < site.PlaceTileCount; i++)
        {
            var (x, y) = site.PlaceTile(i);
            if (!CanStandOn(world, x, y, region) || IsHeld(world, TileOf(x, y)))
            {
                continue;
            }

            var distance = FixedVector2.DistanceSquaredRaw(body.Position, MapGrid.TileCenter(x, y));
            if (!found || IsNearer(distance, x, y, nearest, nearestX, nearestY))
            {
                (found, nearest, nearestX, nearestY) = (true, distance, x, y);
            }
        }

        if (!found)
        {
            return false;
        }

        worker.State = WorkerState.Walking;
        worker.Tile = TileOf(nearestX, nearestY);
        worker.Timer = 0;
        worker.WaitingSince = 0;
        WalkTo(world, entity, body.Owner, nearestX, nearestY);
        return true;
    }

    private void Wait(World world, EntityId entity, ref Worker worker, in WorkSite site, in Body body, int region)
    {
        var found = false;
        long nearest = 0;
        var nearestX = body.Position.X.FloorToInt();
        var nearestY = body.Position.Y.FloorToInt();
        for (var y = site.Y - WaitingDistance; y < site.Y + site.Size + WaitingDistance; y++)
        {
            for (var x = site.X - WaitingDistance; x < site.X + site.Size + WaitingDistance; x++)
            {
                if (!CanStandOn(world, x, y, region) || IsPlaceOfASite(world, x, y) || IsClaimed(world, TileOf(x, y)))
                {
                    continue;
                }

                var distance = FixedVector2.DistanceSquaredRaw(body.Position, MapGrid.TileCenter(x, y));
                if (!found || IsNearer(distance, x, y, nearest, nearestX, nearestY))
                {
                    (found, nearest, nearestX, nearestY) = (true, distance, x, y);
                }
            }
        }

        worker.State = WorkerState.Waiting;
        worker.Tile = TileOf(nearestX, nearestY);
        worker.Timer = 0;
        worker.WaitingSince = world.Tick;
        WalkTo(world, entity, body.Owner, nearestX, nearestY);
    }

    private static bool IsNearer(long distance, int x, int y, long nearest, int nearestX, int nearestY) =>
        distance < nearest || (distance == nearest && (y < nearestY || (y == nearestY && x < nearestX)));

    private int TileOf(int x, int y) => (y * _grid.Width) + x;

    private void WalkTo(World world, EntityId entity, int owner, int x, int y)
    {
        var center = MapGrid.TileCenter(x, y);
        ReadOnlySpan<EntityId> one = [entity];
        _walk.Execute(world, new Command(CommandType.Move, owner, one, center.X, center.Y));
    }

    /// <summary>True when some place of the site is one a worker in this region can walk to, taken or not.</summary>
    private bool HasPlaceIn(World world, in WorkSite site, int region)
    {
        for (var i = 0; i < site.PlaceTileCount; i++)
        {
            var (x, y) = site.PlaceTile(i);
            if (CanStandOn(world, x, y, region))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a worker in this region can walk to the tile and stand on it: no site covers it.</summary>
    private bool CanStandOn(World world, int x, int y, int region)
    {
        if (_grid.RegionOf(x, y) != region)
        {
            return false;
        }

        for (var i = 0; i < _sites.Count; i++)
        {
            if (_sites.ValueAt(i).Covers(x, y) && world.Entities.IsAlive(_sites.EntityAt(i)))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsPlaceOfASite(World world, int x, int y)
    {
        for (var i = 0; i < _sites.Count; i++)
        {
            if (_sites.ValueAt(i).HasPlaceAt(x, y) && world.Entities.IsAlive(_sites.EntityAt(i)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a living worker holds the tile as its place.</summary>
    private bool IsHeld(World world, int tile)
    {
        for (var i = 0; i < _workers.Count; i++)
        {
            ref var worker = ref _workers.ValueAt(i);
            if (worker.HoldsPlace && worker.Tile == tile && world.Entities.IsAlive(_workers.EntityAt(i)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a living worker holds the tile as its place or waits on it.</summary>
    private bool IsClaimed(World world, int tile)
    {
        for (var i = 0; i < _workers.Count; i++)
        {
            ref var worker = ref _workers.ValueAt(i);
            if (worker.State != WorkerState.Idle && worker.Tile == tile && world.Entities.IsAlive(_workers.EntityAt(i)))
            {
                return true;
            }
        }

        return false;
    }
}
