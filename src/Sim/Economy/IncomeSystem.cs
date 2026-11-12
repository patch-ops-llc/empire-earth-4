using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;

namespace Sim.Economy;

/// <summary>
/// Pays the workers standing at their places, oldest worker first, and gives freed places to the
/// workers waiting for one (design 4.1).
/// </summary>
/// <remarks>
/// <para>
/// A worker has arrived when it stands still on the center of its place. From that tick it counts
/// its own ticks, and every <see cref="SimInfo.TicksPerSecond"/> ticks it is due a payment: the
/// first comes one second after it arrived. A payment is made when its conditions hold in that
/// tick and skipped when they do not, and the count runs on either way. A deposit pays a worker
/// whose owner has all of the deposit's tiles inside territory, and a Generator pays its owner's
/// workers.
/// </para>
/// <para>
/// A deposit pays the whole rate until the Materials it has delivered reach the full-rate amount,
/// and the depleted rate from the payment after the one that reaches it.
/// </para>
/// <para>
/// A worker loses its assignment when its site is gone, and when it holds a place and has come to
/// rest anywhere but on it. Then the places freed in this tick, by a death among them, are given
/// to the waiting workers as <see cref="WorkAssignments"/> says.
/// </para>
/// </remarks>
public sealed class IncomeSystem : ISystem
{
    private readonly GameData _data;
    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly ComponentStore<Worker> _workers;
    private readonly ComponentStore<WorkSite> _sites;
    private readonly ComponentStore<Territory> _territories;
    private readonly Stockpiles _stockpiles;
    private readonly WorkAssignments _work;
    private readonly IPlayerTechSource _tech;
    private readonly long _fullRateAmount;

    public IncomeSystem(
        GameData data,
        MapGrid grid,
        ComponentStore<Body> bodies,
        ComponentStore<Mover> movers,
        ComponentStore<Worker> workers,
        ComponentStore<WorkSite> sites,
        ComponentStore<Territory> territories,
        Stockpiles stockpiles,
        WorkAssignments work,
        IPlayerTechSource tech)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(workers);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(territories);
        ArgumentNullException.ThrowIfNull(stockpiles);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(tech);
        _data = data;
        _grid = grid;
        _bodies = bodies;
        _movers = movers;
        _workers = workers;
        _sites = sites;
        _territories = territories;
        _stockpiles = stockpiles;
        _work = work;
        _tech = tech;
        _fullRateAmount = (long)data.Resources.Materials.FullRateAmount * Stockpiles.PerResource;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (var i = 0; i < _workers.Count; i++)
        {
            ref var worker = ref _workers.ValueAt(i);
            if (worker.State == WorkerState.Idle)
            {
                continue;
            }

            var entity = _workers.EntityAt(i);
            if (!world.Entities.IsAlive(entity))
            {
                continue;
            }

            var siteIndex = _sites.IndexOf(worker.Site);
            if (siteIndex < 0
                || !world.Entities.IsAlive(worker.Site)
                || !_bodies.TryGet(entity, out var body)
                || !_movers.TryGet(entity, out var mover))
            {
                worker = default;
                continue;
            }

            if (worker.State is WorkerState.Waiting or WorkerState.Away)
            {
                continue;
            }

            if (mover.State != MoveState.Idle)
            {
                // On its way to its place. A worker at work has no order to walk on, so it is not here.
                continue;
            }

            if (body.Position != MapGrid.TileCenter(worker.Tile % _grid.Width, worker.Tile / _grid.Width))
            {
                worker = default;
                continue;
            }

            if (worker.State == WorkerState.Walking)
            {
                worker.State = WorkerState.Working;
                worker.Timer = 0;
                continue;
            }

            worker.Timer++;
            if (worker.Timer >= SimInfo.TicksPerSecond)
            {
                worker.Timer = 0;
                Pay(world, body.Owner, ref _sites.ValueAt(siteIndex));
            }
        }

        _work.ServeWaiting(world);
    }

    /// <summary>Makes the payment a worker of <paramref name="owner"/> is due at a site, if its conditions hold.</summary>
    private void Pay(World world, int owner, ref WorkSite site)
    {
        if (!CanEarn(world, owner, site))
        {
            return;
        }

        var depleted = site.Kind == WorkSiteKind.Deposit && site.Delivered >= _fullRateAmount;
        var payment = IncomeRules.Payment(_data.Resources, site.Resource, _tech.Tech(owner).IncomeMultiplier, depleted);
        _stockpiles.AddThousandths(owner, site.Resource, payment);
        if (site.Kind == WorkSiteKind.Deposit)
        {
            site.Delivered = checked(site.Delivered + payment);
        }
    }

    /// <summary>
    /// The conditions of a payment. A Generator that is still being built, once structures are
    /// built by workers, is one more condition here.
    /// </summary>
    private bool CanEarn(World world, int owner, in WorkSite site) =>
        site.Kind == WorkSiteKind.Deposit
            ? Territory.Holds(_territories, world.Entities, owner, site.X, site.Y, site.Size)
            : site.Owner == owner;
}
