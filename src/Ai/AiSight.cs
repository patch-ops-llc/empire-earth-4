using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Production;
using Sim.Setup;
using Sim.Tech;

namespace Ai;

/// <summary>One of the player's own workers, as the computer player sees it.</summary>
/// <param name="Entity">The worker.</param>
/// <param name="TileX">The x of the tile it stands on.</param>
/// <param name="TileY">The y of the tile it stands on.</param>
/// <param name="State">What it is doing about its work.</param>
/// <param name="Site">The site it is assigned to, or <see cref="EntityId.None"/>.</param>
/// <param name="IsBuilding">True while it carries a build order. Any other order would end that one.</param>
internal readonly record struct OwnWorker(EntityId Entity, int TileX, int TileY, WorkerState State, EntityId Site, bool IsBuilding);

/// <summary>One of the player's own structures. (X, Y) is the lowest tile of its footprint.</summary>
internal readonly record struct OwnStructure(EntityId Entity, int Type, int X, int Y, int Size, bool IsFinished);

/// <summary>A build order of the player's that a worker is still walking to.</summary>
internal readonly record struct OwnBuildOrder(int Type, int X, int Y, int Size);

/// <summary>
/// A deposit the player knows of, or one of its own Generators.
/// </summary>
/// <param name="Entity">The site.</param>
/// <param name="Kind">Deposit or Generator.</param>
/// <param name="X">The x of its lowest tile.</param>
/// <param name="Y">The y of its lowest tile.</param>
/// <param name="Size">The side of its square of tiles.</param>
/// <param name="Places">The workers it takes at once.</param>
/// <param name="Holders">The workers that hold a place at it.</param>
/// <param name="IsHeld">True when it pays the player: a Generator of its own, or a deposit with every tile in its territory.</param>
/// <param name="IsDepleted">True for a deposit that has dropped to the lower rate.</param>
internal readonly record struct KnownSite(
    EntityId Entity,
    WorkSiteKind Kind,
    int X,
    int Y,
    int Size,
    int Places,
    int Holders,
    bool IsHeld,
    bool IsDepleted)
{
    public int FreePlaces => Math.Max(0, Places - Holders);
}

/// <summary>
/// Everything the computer player reads of a match, and the only place in which it reads one. It
/// holds what the player may know and nothing else: the player's own units, structures, orders,
/// stockpile and tech, and the map.
/// </summary>
/// <remarks>
/// Until fog exists, what a player may know of the map and of the other players is everything
/// that is on screen: every deposit, and every territory. When the view of one player lands
/// (<c>PlayerView</c>), this class reads that view and the rest of the computer player does not
/// change: the members here are named after the ones the view has.
/// </remarks>
internal sealed class AiSight
{
    private readonly Match _match;
    private readonly long _fullRateAmount;

    public AiSight(Match match, int player)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, match.Players.Count);
        _match = match;
        Player = player;
        _fullRateAmount = match.Data.Resources.Materials.FullRateAmount * Stockpiles.PerResource;
    }

    public int Player { get; }

    public GameData Data => _match.Data;

    /// <summary>The ticks that have run.</summary>
    public int Tick => _match.World.Tick;

    public int MapWidth => _match.Grid.Width;

    public int MapHeight => _match.Grid.Height;

    /// <summary>The Materials in the stockpile, in whole Materials.</summary>
    public int Materials { get; private set; }

    /// <summary>The Energy in the stockpile, in whole Energy.</summary>
    public int Energy { get; private set; }

    public int SupplyUsed { get; private set; }

    public int SupplyCap { get; private set; }

    public int SupplyFree => Math.Max(0, SupplyCap - SupplyUsed);

    public PlayerTech Tech => _match.Research.Tech(Player);

    /// <summary>The player's workers, in the order of their IDs.</summary>
    public List<OwnWorker> Workers { get; } = [];

    /// <summary>The player's structures, finished or not, in the order of their IDs.</summary>
    public List<OwnStructure> Structures { get; } = [];

    /// <summary>The player's build orders that have no foundation yet.</summary>
    public List<OwnBuildOrder> BuildOrders { get; } = [];

    /// <summary>The deposits the player knows of and its own Generators, in the order of their IDs.</summary>
    public List<KnownSite> Sites { get; } = [];

    /// <summary>The workers in the queues of the player's structures.</summary>
    public int WorkersInTraining { get; private set; }

    /// <summary>Reads the match as it is now. Everything else here answers from this reading or from the match itself.</summary>
    public void Refresh()
    {
        Materials = (int)Math.Min(int.MaxValue, _match.Stockpiles.Materials(Player) / Stockpiles.PerResource);
        Energy = (int)Math.Min(int.MaxValue, _match.Stockpiles.Energy(Player) / Stockpiles.PerResource);
        SupplyUsed = _match.Supply.Used(Player);
        SupplyCap = _match.Supply.Cap(Player);

        Workers.Clear();
        var workers = _match.Workers;
        for (var i = 0; i < workers.Count; i++)
        {
            var entity = workers.EntityAt(i);
            if (IsOwn(entity, out var x, out var y))
            {
                var worker = workers.ValueAt(i);
                Workers.Add(new OwnWorker(entity, x, y, worker.State, worker.Site, _match.BuildOrders.Contains(entity)));
            }
        }

        Structures.Clear();
        var structures = _match.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            var entity = structures.EntityAt(i);
            if (IsOwn(entity, out _, out _))
            {
                var placed = structures.ValueAt(i);
                Structures.Add(new OwnStructure(entity, placed.Type, placed.X, placed.Y, placed.Size, _match.Construction.IsFinished(entity)));
            }
        }

        BuildOrders.Clear();
        var orders = _match.BuildOrders;
        for (var i = 0; i < orders.Count; i++)
        {
            if (IsOwn(orders.EntityAt(i), out _, out _))
            {
                var order = orders.ValueAt(i);
                BuildOrders.Add(new OwnBuildOrder(order.Type, order.X, order.Y, order.Size));
            }
        }

        Sites.Clear();
        var sites = _match.WorkSites;
        for (var i = 0; i < sites.Count; i++)
        {
            var entity = sites.EntityAt(i);
            var site = sites.ValueAt(i);
            if (!_match.World.Entities.IsAlive(entity) || (site.Kind == WorkSiteKind.Generator && site.Owner != Player))
            {
                continue;
            }

            var isDeposit = site.Kind == WorkSiteKind.Deposit;
            Sites.Add(new KnownSite(
                entity,
                site.Kind,
                site.X,
                site.Y,
                site.Size,
                _match.Work.PlacesOf(site),
                _match.Work.HoldersOf(_match.World, entity),
                IsHeld: !isDeposit || HoldsTiles(site.X, site.Y, site.Size),
                IsDepleted: isDeposit && site.Delivered >= _fullRateAmount));
        }

        WorkersInTraining = 0;
        var producers = _match.Production.Producers;
        for (var i = 0; i < producers.Count; i++)
        {
            if (!IsOwn(producers.EntityAt(i), out _, out _))
            {
                continue;
            }

            var producer = producers.ValueAt(i);
            for (var position = 0; position < producer.Count; position++)
            {
                if (producer[position].IsWorker)
                {
                    WorkersInTraining += producer[position].Count;
                }
            }
        }
    }

    /// <summary>True when the player has a research slot with nothing in it.</summary>
    public int FreeResearchSlots => CountSlots(free: true);

    /// <summary>The research slots the player has, busy or not.</summary>
    public int OpenResearchSlots => CountSlots(free: false);

    /// <summary>True while one of the player's slots is researching the track.</summary>
    public bool IsResearching(int track) => _match.Research.State.SlotResearching(Player, track) >= 0;

    /// <summary>Whether the player may order the structure at all, wherever it goes.</summary>
    public BuildProblem CheckStructure(int structure) => _match.Construction.CheckStructure(Player, structure);

    /// <summary>How many of a structure the player has, counting those under way.</summary>
    public int CountOf(int structure) => _match.Construction.CountOf(Player, structure);

    /// <summary>Whether the ground takes the structure with its lowest tile at (x, y), as far as the player knows.</summary>
    public BuildProblem CheckSite(StructureDef structure, int x, int y) => _match.Construction.CheckSite(Player, structure, x, y);

    /// <summary>
    /// True when a Capitol or Outpost at (x, y) would give a territory that overlaps a territory of
    /// another player that this player knows of.
    /// </summary>
    public bool IsTooCloseToKnownEnemyTerritory(StructureDef structure, int x, int y) =>
        _match.Construction.IsTooCloseToEnemyTerritory(Player, structure, x, y);

    /// <summary>True for a tile a unit can stand on: open ground under no structure and no deposit.</summary>
    public bool CanStandOn(int x, int y) => _match.Grid.IsPassable(x, y);

    /// <summary>True when every tile of the square is inside the player's territory.</summary>
    public bool HoldsTiles(int x, int y, int size) =>
        Territory.Holds(_match.Territories, _match.World.Entities, Player, x, y, size);

    /// <summary>True when a tile of the square is inside a territory of another player that this player knows of.</summary>
    public bool IsClaimedByAnother(int x, int y, int size)
    {
        var territories = _match.Territories;
        for (var i = 0; i < territories.Count; i++)
        {
            var territory = territories.ValueAt(i);
            if (territory.Owner == Player || !_match.World.Entities.IsAlive(territories.EntityAt(i)))
            {
                continue;
            }

            for (var tileY = y; tileY < y + size; tileY++)
            {
                for (var tileX = x; tileX < x + size; tileX++)
                {
                    if (territory.Contains(tileX, tileY))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The orders in the queue of one of the player's structures, or -1 for a structure that
    /// trains nothing or is not finished.
    /// </summary>
    public int OrdersAt(EntityId structure) =>
        _match.Production.Producers.TryGet(structure, out var producer) ? producer.Count : -1;

    /// <summary>True when a rally point has been set for the structure.</summary>
    public bool HasRallyPoint(EntityId structure) =>
        _match.Production.Producers.TryGet(structure, out var producer) && producer.RallySet;

    /// <summary>
    /// Adds the player's fighting units to a count by line: those on the map, and those in the
    /// queues of its structures. The list has one entry per line of the roster.
    /// </summary>
    public void CountUnitsByLine(int[] unitsByLine)
    {
        ArgumentNullException.ThrowIfNull(unitsByLine);

        var chassis = Data.Chassis;
        var combatants = _match.Combatants;
        for (var i = 0; i < combatants.Count; i++)
        {
            if (IsOwn(combatants.EntityAt(i), out _, out _))
            {
                unitsByLine[chassis[combatants.ValueAt(i).Chassis].Line]++;
            }
        }

        var producers = _match.Production.Producers;
        for (var i = 0; i < producers.Count; i++)
        {
            if (!IsOwn(producers.EntityAt(i), out _, out _))
            {
                continue;
            }

            var producer = producers.ValueAt(i);
            for (var position = 0; position < producer.Count; position++)
            {
                var order = producer[position];
                if (!order.IsWorker)
                {
                    unitsByLine[chassis[order.Unit].Line] += order.Count;
                }
            }
        }
    }

    /// <summary>What a worker of the player is paid for one second at a site that pays the whole rate, in thousandths.</summary>
    public long PaymentOf(Resource resource) =>
        IncomeRules.Payment(Data.Resources, resource, Tech.IncomeMultiplier, depleted: false);

    private int CountSlots(bool free)
    {
        var count = 0;
        for (var slot = 0; slot < Sim.Research.ResearchState.SlotsPerPlayer; slot++)
        {
            ref readonly var candidate = ref _match.Research.State.Slot(Player, slot);
            if (free ? candidate.IsFree : candidate.IsOpen)
            {
                count++;
            }
        }

        return count;
    }

    private bool IsOwn(EntityId entity, out int tileX, out int tileY)
    {
        if (_match.World.Entities.IsAlive(entity) && _match.Bodies.TryGet(entity, out var body) && body.Owner == Player)
        {
            tileX = body.Position.X.FloorToInt();
            tileY = body.Position.Y.FloorToInt();
            return true;
        }

        tileX = 0;
        tileY = 0;
        return false;
    }
}
