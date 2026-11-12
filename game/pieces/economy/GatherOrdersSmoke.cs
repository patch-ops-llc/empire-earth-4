using Sim.Core;
using Sim.Economy;

namespace Game;

/// <summary>
/// The smoke test's work orders: 45 seconds of play in which the local player's workers are sent
/// to deposits. Half go to a deposit inside the player's territory and earn there. The others go
/// to one outside it, where they stand and earn nothing, and are then sent to join the first.
/// </summary>
public sealed class GatherOrdersSmoke : SmokePart
{
    // When the workers at the deposit outside the territory are sent to the one inside it.
    private const int JoinAt = 200;

    // Long enough for the workers to walk back across the map from where the parts before this
    // one left them, and to be paid for a while.
    private const int Length = 900;

    private EntityId[] _first = [];
    private EntityId[] _second = [];
    private EntityId _inside;
    private EntityId _outside;
    private long _materialsBefore;

    public override string Name => "Work orders";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        switch (tick)
        {
            case 0:
                Begin(orders);
                break;

            case JoinAt:
                if (!AllAssigned(orders, _first, _inside) || !AllAssigned(orders, _second, _outside))
                {
                    orders.Fail($"a worker is not assigned to the deposit it was sent to {JoinAt} ticks ago.");
                    break;
                }

                if (_second.Length > 0)
                {
                    orders.Submit(new Command(CommandType.Gather, orders.Player, _second, targetEntity: _inside));
                }

                break;

            case Length - 1:
                if (orders.Match.Stockpiles.Materials(orders.Player) <= _materialsBefore)
                {
                    orders.Fail(
                        $"the player's Materials did not grow in the {Length - 1} ticks since workers were sent to a deposit in the player's territory. " +
                        "If the workers need longer to walk there now, this part needs more ticks.");
                }

                break;
        }
    }

    private void Begin(SmokeOrders orders)
    {
        var match = orders.Match;
        var workers = Array.FindAll(orders.OwnUnits(), match.Workers.Contains);
        if (workers.Length == 0)
        {
            orders.Fail("the player has no worker to send to a deposit.");
            return;
        }

        if (!FindDeposits(orders))
        {
            orders.Fail("the map has no deposit inside the player's territory, or none outside it.");
            return;
        }

        // The first half, the larger when the number is odd, works inside the territory.
        var half = (workers.Length + 1) / 2;
        _first = workers[..half];
        _second = workers[half..];
        _materialsBefore = match.Stockpiles.Materials(orders.Player);

        orders.Submit(new Command(CommandType.Gather, orders.Player, _first, targetEntity: _inside));
        if (_second.Length > 0)
        {
            orders.Submit(new Command(CommandType.Gather, orders.Player, _second, targetEntity: _outside));
        }
    }

    // The first deposit, in ID order, that lies inside the player's territory, and the deposit
    // outside it that is nearest to that one, the first in ID order of those equally near.
    private bool FindDeposits(SmokeOrders orders)
    {
        var match = orders.Match;
        var sites = match.WorkSites;
        var deposits = new List<(EntityId Entity, WorkSite Site, bool Inside)>();
        for (var i = 0; i < sites.Count; i++)
        {
            var entity = sites.EntityAt(i);
            var site = sites.ValueAt(i);
            if (site.Kind == WorkSiteKind.Deposit && match.World.Entities.IsAlive(entity))
            {
                var inside = Territory.Holds(match.Territories, match.World.Entities, orders.Player, site.X, site.Y, site.Size);
                deposits.Add((entity, site, inside));
            }
        }

        deposits.Sort((a, b) => a.Entity.CompareTo(b.Entity));
        var home = deposits.FindIndex(deposit => deposit.Inside);
        if (home < 0)
        {
            return false;
        }

        _inside = deposits[home].Entity;
        _outside = EntityId.None;
        var nearest = long.MaxValue;
        foreach (var (entity, site, inside) in deposits)
        {
            long dx = site.X - deposits[home].Site.X;
            long dy = site.Y - deposits[home].Site.Y;
            var distance = (dx * dx) + (dy * dy);
            if (!inside && distance < nearest)
            {
                _outside = entity;
                nearest = distance;
            }
        }

        return _outside != EntityId.None;
    }

    private static bool AllAssigned(SmokeOrders orders, EntityId[] workers, EntityId site)
    {
        foreach (var worker in workers)
        {
            if (!orders.Match.Workers.TryGet(worker, out var work) || work.Site != site)
            {
                return false;
            }
        }

        return true;
    }
}
