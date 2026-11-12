using Sim.Combat;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tech;
using Sim.Vision;

namespace Ai;

/// <summary>One of the player's own workers, as the computer player sees it.</summary>
/// <param name="Entity">The worker.</param>
/// <param name="TileX">The x of the tile it stands on.</param>
/// <param name="TileY">The y of the tile it stands on.</param>
/// <param name="State">What it is doing about its work.</param>
/// <param name="Site">The site it is assigned to, or <see cref="EntityId.None"/>.</param>
/// <param name="IsBuilding">True while it carries a build order. Any other order would end that one.</param>
internal readonly record struct OwnWorker(EntityId Entity, int TileX, int TileY, WorkerState State, EntityId Site, bool IsBuilding);

/// <summary>One of the player's own structures. (X, Y) is the lowest tile of its footprint. Hit points are in thousandths.</summary>
internal readonly record struct OwnStructure(EntityId Entity, int Type, int X, int Y, int Size, bool IsFinished, long HitPoints);

/// <summary>A build order of the player's that a worker is still walking to.</summary>
internal readonly record struct OwnBuildOrder(int Type, int X, int Y, int Size);

/// <summary>One of the player's own fighting units: a unit of a chassis.</summary>
/// <param name="Entity">The unit.</param>
/// <param name="Position">Where it stands.</param>
/// <param name="Chassis">Its chassis, as the index in the roster.</param>
/// <param name="Order">The order it is on.</param>
/// <param name="Target">What it is attacking, or <see cref="EntityId.None"/>.</param>
internal readonly record struct OwnFighter(EntityId Entity, FixedVector2 Position, int Chassis, UnitOrder Order, EntityId Target);

/// <summary>
/// A deposit the player knows of, or one of its own Generators.
/// </summary>
/// <param name="Entity">The site.</param>
/// <param name="Kind">Deposit or Generator.</param>
/// <param name="X">The x of its lowest tile.</param>
/// <param name="Y">The y of its lowest tile.</param>
/// <param name="Size">The side of its square of tiles.</param>
/// <param name="Places">The workers it takes at once.</param>
/// <param name="Holders">The workers of the player that hold a place at it.</param>
/// <param name="IsHeld">True when it pays the player: a Generator of its own, or a deposit with every tile in its territory.</param>
/// <param name="IsDepleted">True for a deposit the player holds that has dropped to the lower rate.</param>
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
/// holds what the player of its seat may know and nothing else (design 5.5: the same fog as a
/// human): it asks the <see cref="PlayerView"/> of its seat, which answers through the fog of war.
/// </summary>
/// <remarks>
/// <para>
/// Three things a player knows of its own that the view does not hand out are read from the
/// match, each for an entity the view lists as the player's own and for nothing else: the queue
/// and the rally point of a structure of its own, and whether a deposit that pays the player
/// pays the lower rate, which the player sees in what its workers there earn.
/// </para>
/// <para>
/// Of the map file it reads what every player is told before the match begins: the size of the
/// map, where each seat starts, and the markers of design 5.3, which the design gives the
/// computer player in place of an analysis of the terrain. The ground and the deposits it learns
/// by exploring, like anyone else.
/// </para>
/// </remarks>
internal sealed class AiSight
{
    private readonly Match _match;
    private readonly PlayerView _view;
    private readonly long _fullRateAmount;
    private readonly List<SeenUnit> _units = [];
    private readonly List<SeenStructure> _structures = [];
    private readonly List<SeenWorkSite> _workSites = [];
    private readonly List<Territory> _territories = [];
    private readonly byte[] _fog;
    private readonly bool[] _covered;

    public AiSight(Match match, int player)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, match.Players.Count);
        _match = match;
        _view = new PlayerView(match, player);
        Player = player;
        _fullRateAmount = match.Data.Resources.Materials.FullRateAmount * Stockpiles.PerResource;
        _fog = new byte[_view.Width * _view.Height];
        _covered = new bool[_fog.Length];

        foreach (var marker in match.Map.Markers)
        {
            Markers.Add(marker);
        }

        for (var other = 0; other < match.Players.Count; other++)
        {
            if (other != player && match.Players[other].HasStart)
            {
                var start = match.Map.Starts[match.Players[other].Start];
                var half = match.Data.StartStructure.Footprint / 2;
                OtherStarts.Add((start.X + half, start.Y + half));
            }
        }
    }

    public int Player { get; }

    public GameData Data => _match.Data;

    /// <summary>The ticks that have run.</summary>
    public int Tick => _view.Tick;

    public int MapWidth => _view.Width;

    public int MapHeight => _view.Height;

    /// <summary>The markers of the map file (design 5.3): the passages a map names, each a chokepoint or an open field.</summary>
    public List<MapMarker> Markers { get; } = [];

    /// <summary>The middle tile of the start of every other seat of the match, in the order of the seats.</summary>
    public List<(int X, int Y)> OtherStarts { get; } = [];

    /// <summary>The Materials in the stockpile, in whole Materials.</summary>
    public int Materials { get; private set; }

    /// <summary>The Energy in the stockpile, in whole Energy.</summary>
    public int Energy { get; private set; }

    public int SupplyUsed { get; private set; }

    public int SupplyCap { get; private set; }

    /// <summary>The supply left under the cap: zero at the cap and above it, where a displaced accent can put a player (design 2.3).</summary>
    public int SupplyFree => Math.Max(0, SupplyCap - SupplyUsed);

    public PlayerTech Tech => _view.Tech;

    /// <summary>The player's workers, in the order of their IDs.</summary>
    public List<OwnWorker> Workers { get; } = [];

    /// <summary>
    /// The worker that is away to look at ground the player has not seen, or none. It is not
    /// sent to work while it is, and it still takes an order to build.
    /// </summary>
    public EntityId Explorer { get; set; }

    /// <summary>The player's fighting units on the map, in the order of their IDs.</summary>
    public List<OwnFighter> Fighters { get; } = [];

    /// <summary>The units of other players that the player sees now, in the order of their IDs.</summary>
    public List<SeenUnit> Enemies { get; } = [];

    /// <summary>The player's structures, finished or not, in the order of their IDs.</summary>
    public List<OwnStructure> Structures { get; } = [];

    /// <summary>
    /// The structures of other players that the player sees or remembers, in the order of their
    /// IDs. A remembered one is as it was when last seen, and may be gone.
    /// </summary>
    public List<SeenStructure> EnemyStructures { get; } = [];

    /// <summary>The player's build orders that have no foundation yet.</summary>
    public List<OwnBuildOrder> BuildOrders { get; } = [];

    /// <summary>The deposits the player knows of and its own Generators, in the order of their IDs.</summary>
    public List<KnownSite> Sites { get; } = [];

    /// <summary>The workers in the queues of the player's structures.</summary>
    public int WorkersInTraining { get; private set; }

    /// <summary>Reads the match as the player knows it now. Everything else here answers from this reading or from the view.</summary>
    public void Refresh()
    {
        Materials = (int)Math.Min(int.MaxValue, _view.Materials / Stockpiles.PerResource);
        Energy = (int)Math.Min(int.MaxValue, _view.Energy / Stockpiles.PerResource);
        SupplyUsed = _view.SupplyUsed;
        SupplyCap = _view.SupplyCap;
        _view.CopyFog(_fog);

        Workers.Clear();
        Fighters.Clear();
        Enemies.Clear();
        BuildOrders.Clear();
        _view.CollectUnits(_units);
        foreach (var unit in _units)
        {
            if (unit.Owner != Player)
            {
                Enemies.Add(unit);
            }
            else if (unit.IsWorker)
            {
                _view.TryGetOwnWork(unit.Entity, out var work);
                var isBuilding = _view.TryGetOwnBuildOrder(unit.Entity, out var order);
                Workers.Add(new OwnWorker(
                    unit.Entity, unit.Position.X.FloorToInt(), unit.Position.Y.FloorToInt(), work.State, work.Site, isBuilding));
                if (isBuilding)
                {
                    BuildOrders.Add(new OwnBuildOrder(order.Type, order.X, order.Y, order.Size));
                }
            }
            else if (unit.Chassis != SeenUnit.NoChassis && _view.TryGetOwnOrder(unit.Entity, out var combatant))
            {
                Fighters.Add(new OwnFighter(unit.Entity, unit.Position, unit.Chassis, combatant.Order, combatant.Target));
            }
        }

        Structures.Clear();
        EnemyStructures.Clear();
        Array.Clear(_covered);
        WorkersInTraining = 0;
        _view.CollectStructures(_structures);
        foreach (var structure in _structures)
        {
            Cover(structure.X, structure.Y, structure.Size);
            if (structure.Owner != Player)
            {
                EnemyStructures.Add(structure);
                continue;
            }

            Structures.Add(new OwnStructure(
                structure.Entity, structure.Type, structure.X, structure.Y, structure.Size, structure.IsFinished, structure.HitPoints));
            if (_match.Production.Producers.TryGet(structure.Entity, out var producer))
            {
                for (var position = 0; position < producer.Count; position++)
                {
                    if (producer[position].IsWorker)
                    {
                        WorkersInTraining += producer[position].Count;
                    }
                }
            }
        }

        _view.CollectTerritories(_territories);

        Sites.Clear();
        _view.CollectWorkSites(_workSites);
        foreach (var site in _workSites)
        {
            Cover(site.X, site.Y, site.Size);
            var isDeposit = site.Kind == WorkSiteKind.Deposit;
            var isHeld = !isDeposit || HoldsTiles(site.X, site.Y, site.Size);
            var holders = 0;
            foreach (var worker in Workers)
            {
                if (worker.Site == site.Entity && worker.State is WorkerState.Walking or WorkerState.Working)
                {
                    holders++;
                }
            }

            Sites.Add(new KnownSite(
                site.Entity,
                site.Kind,
                site.X,
                site.Y,
                site.Size,
                isDeposit ? Data.Resources.Materials.WorkersPerDeposit : Data.Resources.Energy.WorkersPerGenerator,
                holders,
                isHeld,
                IsDepleted: isDeposit && isHeld && _match.WorkSites.Get(site.Entity).Delivered >= _fullRateAmount));
        }
    }

    /// <summary>The events of the last tick that the player may know of, in their order. The list is emptied first.</summary>
    public void CollectEvents(List<SimEvent> events) => _view.CollectEvents(events);

    /// <summary>True when the player has a research slot with nothing in it.</summary>
    public int FreeResearchSlots => CountSlots(free: true);

    /// <summary>The research slots the player has, busy or not.</summary>
    public int OpenResearchSlots => CountSlots(free: false);

    /// <summary>True while one of the player's slots is researching the track.</summary>
    public bool IsResearching(int track)
    {
        for (var slot = 0; slot < Sim.Research.ResearchState.SlotsPerPlayer; slot++)
        {
            var candidate = _view.ResearchSlot(slot);
            if (candidate.IsBusy && candidate.Track == track)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when a research of the player's ends within so many ticks. The prices of its units
    /// change when one does, so an order priced now could be refused when it is carried out.
    /// </summary>
    public bool ResearchEndsWithin(int ticks)
    {
        for (var slot = 0; slot < Sim.Research.ResearchState.SlotsPerPlayer; slot++)
        {
            var candidate = _view.ResearchSlot(slot);
            if (candidate.IsBusy && candidate.TicksLeft <= ticks)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the player may order the structure at all, wherever it goes.</summary>
    public BuildProblem CheckStructure(int structure) => _view.CheckStructure(structure);

    /// <summary>How many of a structure the player has, counting those under way.</summary>
    public int CountOf(int structure) => _view.CountOf(structure);

    /// <summary>
    /// Whether an order for the structure with its lowest tile at (x, y) would be taken, as far
    /// as the player knows: what the order itself is judged by.
    /// </summary>
    public BuildProblem CheckSite(StructureDef structure, int x, int y) => _view.CheckSite(structure, x, y);

    /// <summary>
    /// True when a Capitol or Outpost at (x, y) would give a territory that overlaps a territory of
    /// another player that this player knows of: one it sees or remembers.
    /// </summary>
    public bool IsTooCloseToKnownEnemyTerritory(StructureDef structure, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (structure.TerritoryRadius is not { } radius)
        {
            return false;
        }

        // In half tiles, as a territory keeps its center.
        long centerX = (2L * x) + structure.Footprint;
        long centerY = (2L * y) + structure.Footprint;
        foreach (var territory in _territories)
        {
            if (territory.Owner == Player)
            {
                continue;
            }

            var dx = centerX - territory.CenterX2;
            var dy = centerY - territory.CenterY2;
            long apart = 2L * (radius + territory.Radius);
            if ((dx * dx) + (dy * dy) < apart * apart)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True for a tile the player has seen at some time.</summary>
    public bool IsExplored(int x, int y) => IsOnMap(x, y) && _fog[(y * MapWidth) + x] != (byte)FogState.Unexplored;

    /// <summary>True for a tile the player sees now.</summary>
    public bool IsVisible(int x, int y) => IsOnMap(x, y) && _fog[(y * MapWidth) + x] == (byte)FogState.Visible;

    /// <summary>
    /// True for a tile the player knows a unit can stand on: ground it has explored that is open,
    /// under no structure it knows of and no deposit.
    /// </summary>
    public bool CanStandOn(int x, int y) =>
        IsExplored(x, y)
        && !_covered[(y * MapWidth) + x]
        && _view.TryGetTerrain(x, y, out var terrain)
        && terrain == TerrainKind.Open;

    /// <summary>True when every tile of the square is inside the player's territory.</summary>
    public bool HoldsTiles(int x, int y, int size)
    {
        for (var tileY = y; tileY < y + size; tileY++)
        {
            for (var tileX = x; tileX < x + size; tileX++)
            {
                if (!IsInTerritory(tileX, tileY, own: true))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>True when a tile of the square is inside a territory of another player that this player knows of.</summary>
    public bool IsClaimedByAnother(int x, int y, int size)
    {
        for (var tileY = y; tileY < y + size; tileY++)
        {
            for (var tileX = x; tileX < x + size; tileX++)
            {
                if (IsInTerritory(tileX, tileY, own: false))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The orders in the queue of one of the player's structures, or -1 for a structure that
    /// trains nothing or is not finished, and for one that is not the player's.
    /// </summary>
    public int OrdersAt(EntityId structure) =>
        IsOwnStructure(structure) && _match.Production.Producers.TryGet(structure, out var producer) ? producer.Count : -1;

    /// <summary>True when a rally point has been set for a structure of the player's.</summary>
    public bool HasRallyPoint(EntityId structure) =>
        IsOwnStructure(structure) && _match.Production.Producers.TryGet(structure, out var producer) && producer.RallySet;

    /// <summary>
    /// Adds the player's fighting units to a count by line: those on the map, and those in the
    /// queues of its structures. The list has one entry per line of the roster.
    /// </summary>
    public void CountUnitsByLine(int[] unitsByLine)
    {
        ArgumentNullException.ThrowIfNull(unitsByLine);

        var chassis = Data.Chassis;
        foreach (var fighter in Fighters)
        {
            unitsByLine[chassis[fighter.Chassis].Line]++;
        }

        foreach (var structure in Structures)
        {
            if (!_match.Production.Producers.TryGet(structure.Entity, out var producer))
            {
                continue;
            }

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

    /// <summary>
    /// What a unit of a chassis is worth: its price in Materials and Energy together, as the
    /// player's own tiers make it. The player knows no other player's prices, so it values an
    /// enemy's unit at what the same chassis would cost itself.
    /// </summary>
    public int ValueOf(int chassis)
    {
        var blueprint = Tech.Blueprints[chassis];
        return blueprint.Materials + blueprint.Energy;
    }

    private bool IsOnMap(int x, int y) => x >= 0 && y >= 0 && x < MapWidth && y < MapHeight;

    private void Cover(int x, int y, int size)
    {
        for (var tileY = Math.Max(0, y); tileY < Math.Min(MapHeight, y + size); tileY++)
        {
            for (var tileX = Math.Max(0, x); tileX < Math.Min(MapWidth, x + size); tileX++)
            {
                _covered[(tileY * MapWidth) + tileX] = true;
            }
        }
    }

    private bool IsInTerritory(int tileX, int tileY, bool own)
    {
        foreach (var territory in _territories)
        {
            if ((territory.Owner == Player) == own && territory.Contains(tileX, tileY))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOwnStructure(EntityId entity)
    {
        foreach (var structure in Structures)
        {
            if (structure.Entity == entity)
            {
                return true;
            }
        }

        return false;
    }

    private int CountSlots(bool free)
    {
        var count = 0;
        for (var slot = 0; slot < Sim.Research.ResearchState.SlotsPerPlayer; slot++)
        {
            var candidate = _view.ResearchSlot(slot);
            if (free ? candidate.IsFree : candidate.IsOpen)
            {
                count++;
            }
        }

        return count;
    }
}
