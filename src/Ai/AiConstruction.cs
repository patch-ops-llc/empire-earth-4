using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;

namespace Ai;

/// <summary>
/// Places the computer player's structures: it chooses the site, gives the order to its workers,
/// and remembers a site at which an order came to nothing, so that it tries elsewhere.
/// </summary>
internal sealed class AiConstruction
{
    /// <summary>How long a site at which an order came to nothing is left alone: three minutes.</summary>
    public const int RefusedTicks = 3 * 60 * Sim.SimInfo.TicksPerSecond;

    private readonly AiSight _sight;
    private readonly List<OwnBuildOrder> _watched = [];
    private readonly List<EntityId> _builders = [];
    private bool _heldThisRound;

    public AiConstruction(AiSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        _sight = sight;
        Sites = new AiSites(sight);
    }

    public AiSites Sites { get; }

    /// <summary>
    /// Looks at what became of the orders it gave. An order that is neither on its way nor a
    /// structure was refused or ended, and its site is left alone for a while. Call it first in a
    /// round, after the sight has read the match.
    /// </summary>
    public void BeginRound()
    {
        _heldThisRound = false;
        Sites.Forget(_sight.Tick);
        for (var i = _watched.Count - 1; i >= 0; i--)
        {
            var order = _watched[i];
            if (IsStructureAt(order))
            {
                _watched.RemoveAt(i);
            }
            else if (!_sight.BuildOrders.Contains(order))
            {
                Sites.Refuse(order.Type, order.X, order.Y, _sight.Tick + RefusedTicks);
                _watched.RemoveAt(i);
            }
        }
    }

    /// <summary>The structures of a type the player has that are finished.</summary>
    public int FinishedCount(int type)
    {
        var count = 0;
        foreach (var structure in _sight.Structures)
        {
            if (structure.Type == type && structure.IsFinished)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The structures of a type that are ordered or building themselves.</summary>
    public int UnderWayCount(int type)
    {
        var count = 0;
        foreach (var structure in _sight.Structures)
        {
            if (structure.Type == type && !structure.IsFinished)
            {
                count++;
            }
        }

        foreach (var order in _sight.BuildOrders)
        {
            if (order.Type == type)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Counts a structure among the round's purchases and says whether to look for its site now:
    /// false when the player may not order it, and when the budget does not cover it, in which
    /// case the price of the first such structure of the round is held back.
    /// </summary>
    public bool CanOrder(StructureDef structure, AiBudget budget)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(budget);

        if (_sight.CheckStructure(structure.Index) is BuildProblem.NoStructure or BuildProblem.Locked or BuildProblem.LimitReached)
        {
            return false;
        }

        budget.Want(structure.Materials, structure.Energy);
        if (budget.Covers(structure.Materials, structure.Energy))
        {
            return true;
        }

        if (!_heldThisRound)
        {
            budget.Hold(structure.Materials, structure.Energy);
            _heldThisRound = true;
        }

        return false;
    }

    /// <summary>
    /// Gives the order to build a structure with its lowest tile at (x, y) to every worker that
    /// carries no build order, of which the simulation takes the nearest, and pays for it.
    /// </summary>
    /// <returns>False, with nothing ordered, when the player has no such worker.</returns>
    public bool Order(StructureDef structure, int x, int y, AiBudget budget, List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(commands);

        _builders.Clear();
        foreach (var worker in _sight.Workers)
        {
            if (!worker.IsBuilding)
            {
                _builders.Add(worker.Entity);
            }
        }

        if (_builders.Count == 0)
        {
            return false;
        }

        var point = MapGrid.TileCenter(x, y);
        commands.Add(new Command(
            CommandType.Build,
            _sight.Player,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_builders),
            point.X,
            point.Y,
            contentId: structure.Index));
        budget.Pay(structure.Materials, structure.Energy);

        // Until its foundation is laid the order blocks nothing, so the round's later sites keep clear of it here.
        var order = new OwnBuildOrder(structure.Index, x, y, structure.Footprint);
        _sight.BuildOrders.Add(order);
        _watched.Add(order);
        return true;
    }

    /// <summary>
    /// The site for an Outpost that claims a deposit: the nearest to the deposit whose territory
    /// would hold every tile of it and overlap no territory of another player that this player
    /// knows of, and of those the nearest to home.
    /// </summary>
    public bool TryFindSiteAt(KnownSite deposit, StructureDef outpost, int homeX, int homeY, out int x, out int y)
    {
        ArgumentNullException.ThrowIfNull(outpost);

        x = 0;
        y = 0;
        if (outpost.TerritoryRadius is not { } radius)
        {
            return false;
        }

        bool Claims(int siteX, int siteY)
        {
            var territory = Territory.Around(_sight.Player, siteX, siteY, outpost.Footprint, radius);
            for (var tileY = deposit.Y; tileY < deposit.Y + deposit.Size; tileY++)
            {
                for (var tileX = deposit.X; tileX < deposit.X + deposit.Size; tileX++)
                {
                    if (!territory.Contains(tileX, tileY))
                    {
                        return false;
                    }
                }
            }

            return !_sight.IsTooCloseToKnownEnemyTerritory(outpost, siteX, siteY);
        }

        return Sites.TryFind(
            outpost,
            deposit.X + (deposit.Size / 2),
            deposit.Y + (deposit.Size / 2),
            radius,
            homeX,
            homeY,
            Claims,
            out x,
            out y);
    }

    private bool IsStructureAt(OwnBuildOrder order)
    {
        foreach (var structure in _sight.Structures)
        {
            if (structure.Type == order.Type && structure.X == order.X && structure.Y == order.Y)
            {
                return true;
            }
        }

        return false;
    }
}
