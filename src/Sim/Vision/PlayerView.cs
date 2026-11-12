using Sim.Combat;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Research;
using Sim.Setup;
using Sim.Tech;

namespace Sim.Vision;

/// <summary>
/// A match as one player may know it (design 5.3): the player's own things, the units and
/// structures of other players that the player sees, the structures it remembers, and the ground
/// and deposits the player has explored. Whatever
/// shows a match to a player or plays it for one, the interface, the minimap, the AI, asks here
/// and not the match, and so cannot show or use what the fog hides.
/// </summary>
/// <remarks>
/// <para>
/// A view holds nothing but the match and the player's number. It answers from the match as it is
/// when asked, so one view lasts the whole match, also across a restored snapshot, and any number
/// of views of one match can be made. It never changes the match.
/// </para>
/// <para>
/// <b>What it answers.</b>
/// </para>
/// <list type="bullet">
/// <item>The fog: <see cref="FogAt"/> for a tile, <see cref="CopyFog"/> for all of them at once,
/// and <see cref="FogRevision"/> to tell whether they have changed.</item>
/// <item>The ground: <see cref="TryGetTerrain"/>, for explored tiles only.</item>
/// <item>Deposits and the player's Generators: <see cref="CollectWorkSites"/>.</item>
/// <item>Units: <see cref="CollectUnits"/>, <see cref="CollectUnitsNear"/> and
/// <see cref="CollectUnitsIn"/> list the player's own and the others it sees;
/// <see cref="TryGetUnit"/> and <see cref="Sees"/> ask about one; <see cref="Inspect"/> gives what
/// inspecting a unit shows.</item>
/// <item>Structures: <see cref="CollectStructures"/> lists the player's own, the others it sees and
/// the others it remembers, each of the last as it was when last seen;
/// <see cref="TryGetStructure"/> and <see cref="TryGetStructureAt"/> ask about one;
/// <see cref="KnowsStructure"/> says whether an attack order may name it;
/// <see cref="CollectTerritories"/> gives the territories that follow from them.</item>
/// <item>Building: <see cref="CheckStructure"/> and <see cref="CountOf"/> for a build menu;
/// <see cref="CheckSite"/>, <see cref="CanBuildOnTile"/> and
/// <see cref="IsTooCloseToSeenEnemyTerritory"/> for a placement preview. They answer what the
/// order itself will be judged by when it is given.</item>
/// <item>What only the owner knows of its own units: <see cref="TryGetOwnOrder"/>,
/// <see cref="TryGetOwnWalk"/>, <see cref="TryGetOwnWork"/> and <see cref="TryGetOwnBuildOrder"/>.</item>
/// <item>Shots in flight: <see cref="CollectShots"/>.</item>
/// <item>The events of the last tick that the player may know of: <see cref="CollectEvents"/>.</item>
/// <item>The player's account: <see cref="Materials"/>, <see cref="Energy"/>,
/// <see cref="SupplyUsed"/>, <see cref="SupplyCap"/>, <see cref="Tech"/> and
/// <see cref="ResearchSlot"/>.</item>
/// </list>
/// <para>
/// <b>What it is not.</b> It is not a guard against a changed program: every machine of a match
/// holds the whole match, and a program that reads the match itself sees everything. It keeps an
/// honest interface honest.
/// </para>
/// </remarks>
public sealed class PlayerView
{
    private readonly Match _match;
    private readonly MatchVision _vision;

    // Scratch for one question, emptied at its start.
    private readonly List<EntityId> _found = [];
    private readonly List<SeenStructure> _known = [];

    /// <param name="match">The match.</param>
    /// <param name="player">The player whose view this is, a position in <see cref="Match.Players"/>.</param>
    public PlayerView(Match match, int player)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, match.Players.Count);
        _match = match;
        _vision = match.Vision;
        Player = player;
    }

    /// <summary>The player whose view this is.</summary>
    public int Player { get; }

    /// <summary>The number of the tick that runs next. Everyone knows it.</summary>
    public int Tick => _match.World.Tick;

    /// <summary>The width of the map in tiles. Everyone knows the size of the map.</summary>
    public int Width => _match.Map.Width;

    /// <summary>The height of the map in tiles.</summary>
    public int Height => _match.Map.Height;

    /// <summary>
    /// Another number than last time when the fog may look different: after sight was worked out
    /// again with another result, and after a snapshot was restored. What draws the fog keeps the
    /// number it drew and draws again when this differs.
    /// </summary>
    public int FogRevision => _vision.Grid.Revision;

    /// <summary>The player's Materials, in thousandths.</summary>
    public long Materials => _match.Stockpiles.Materials(Player);

    /// <summary>The player's Energy, in thousandths.</summary>
    public long Energy => _match.Stockpiles.Energy(Player);

    /// <summary>The supply the player uses.</summary>
    public int SupplyUsed => _match.Supply.Used(Player);

    /// <summary>The most supply the player may use.</summary>
    public int SupplyCap => _match.Supply.Cap(Player);

    /// <summary>Everything that follows from the player's tiers: its epoch, its research offers, its blueprints.</summary>
    public PlayerTech Tech => _match.Research.Tech(Player);

    /// <summary>One of the player's research slots, from 0 to <see cref="ResearchState.SlotsPerPlayer"/> - 1.</summary>
    public ResearchSlot ResearchSlot(int slot) => _match.Research.State.Slot(Player, slot);

    /// <summary>What the player knows of a tile. A tile off the map is <see cref="FogState.Unexplored"/>.</summary>
    public FogState FogAt(int x, int y) => _vision.Grid.StateOf(Player, x, y);

    /// <summary>
    /// Writes what the player knows of every tile, one byte for each, the number of its
    /// <see cref="FogState"/> (0 unexplored, 1 explored, 2 visible): row by row from y = 0, and in
    /// a row from x = 0. For a fog texture and a minimap.
    /// </summary>
    /// <param name="states">Room for <see cref="Width"/> times <see cref="Height"/> bytes.</param>
    public void CopyFog(Span<byte> states) => _vision.Grid.CopyStates(Player, states);

    /// <summary>
    /// The terrain of a tile the player has explored. False for a tile it has not, and for one off
    /// the map. Terrain never changes, so what was seen once is known for good.
    /// </summary>
    public bool TryGetTerrain(int x, int y, out TerrainKind terrain)
    {
        if (!_vision.Grid.IsExplored(Player, x, y))
        {
            terrain = default;
            return false;
        }

        terrain = _match.Map.TerrainAt(x, y);
        return true;
    }

    /// <summary>
    /// Lists the deposits of which the player has explored at least one tile, and the player's own
    /// Generators, in ascending ID order. The list is emptied first.
    /// </summary>
    public void CollectWorkSites(List<SeenWorkSite> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);
        sites.Clear();
        var store = _match.WorkSites;
        for (var i = 0; i < store.Count; i++)
        {
            var entity = store.EntityAt(i);
            ref var site = ref store.ValueAt(i);
            if (_match.World.Entities.IsAlive(entity) && Knows(in site))
            {
                sites.Add(new SeenWorkSite(entity, site.Kind, site.X, site.Y, site.Size));
            }
        }
    }

    /// <summary>
    /// True when the entity is one of the player's own, or another player's that the player sees
    /// now: a unit by the tile under its center, a structure by any tile of its footprint.
    /// </summary>
    public bool Sees(EntityId entity) => _vision.Sees(Player, entity);

    /// <summary>
    /// A unit, if it is the player's own or the player sees it. False otherwise, for an entity
    /// that is gone, and for a structure, which <see cref="TryGetStructure"/> answers.
    /// </summary>
    public bool TryGetUnit(EntityId entity, out SeenUnit unit)
    {
        if (_match.World.Entities.IsAlive(entity)
            && _match.Bodies.TryGet(entity, out var body)
            && !_match.Structures.Contains(entity)
            && _vision.Sees(Player, in body))
        {
            unit = Describe(entity, in body);
            return true;
        }

        unit = default;
        return false;
    }

    /// <summary>
    /// Lists every unit the player may know of: its own, and those of other players that it sees
    /// now, in ascending ID order. The list is emptied first. Structures are not units:
    /// <see cref="CollectStructures"/> lists them.
    /// </summary>
    public void CollectUnits(List<SeenUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        units.Clear();
        var bodies = _match.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var entity = bodies.EntityAt(i);
            ref var body = ref bodies.ValueAt(i);
            if (_match.World.Entities.IsAlive(entity) && !_match.Structures.Contains(entity) && _vision.Sees(Player, in body))
            {
                units.Add(Describe(entity, in body));
            }
        }
    }

    /// <summary>
    /// Lists the units the player may know of whose center is within a distance of a point, the
    /// distance itself included, in ascending ID order. The list is emptied first.
    /// </summary>
    public void CollectUnitsNear(FixedVector2 center, Fixed radius, List<SeenUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        _match.SpatialIndex.QueryCircle(center, radius, _found);
        DescribeFound(units);
    }

    /// <summary>
    /// Lists the units the player may know of whose center is inside a rectangle, its edges
    /// included, in ascending ID order. The list is emptied first.
    /// </summary>
    /// <param name="min">The corner with the lowest x and y.</param>
    /// <param name="max">The corner with the highest x and y.</param>
    /// <param name="units">Receives the units.</param>
    public void CollectUnitsIn(FixedVector2 min, FixedVector2 max, List<SeenUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        _match.SpatialIndex.QueryBox(min, max, _found);
        DescribeFound(units);
    }

    /// <summary>
    /// Lists every structure the player may know of, in ascending ID order: its own, those of
    /// other players that it sees now, and those of other players that it has seen and does not
    /// see now, each as it was when last seen (<see cref="SeenStructure.IsRemembered"/>). The
    /// list is emptied first.
    /// </summary>
    /// <remarks>
    /// A remembered structure may be gone. It leaves the list when the player sees one of its
    /// tiles and it is not there.
    /// </remarks>
    public void CollectStructures(List<SeenStructure> structures)
    {
        ArgumentNullException.ThrowIfNull(structures);
        structures.Clear();
        var standing = _match.Structures;
        var memory = _vision.Memory;
        var remembered = memory.Count(Player);
        var i = 0;
        var j = 0;
        while (i < standing.Count || j < remembered)
        {
            // Both lists ascend by ID. A structure in both is listed as it stands when the
            // player sees it, and as it is remembered when not.
            var stands = i < standing.Count ? standing.EntityAt(i) : EntityId.None;
            var past = j < remembered ? memory.At(Player, j).Entity : EntityId.None;
            var takeStanding = j >= remembered || (i < standing.Count && stands <= past);
            var takeRemembered = i >= standing.Count || (j < remembered && past <= stands);
            if ((takeStanding && TryGetSeenStructure(stands, out var structure))
                || (takeRemembered && TryGetRememberedStructure(past, out structure)))
            {
                structures.Add(structure);
            }

            i += takeStanding ? 1 : 0;
            j += takeRemembered ? 1 : 0;
        }
    }

    /// <summary>
    /// A structure, if the player may know of it: as it is when it is the player's own or the
    /// player sees it, and otherwise as the player remembers it. False for a structure the player
    /// has never seen, and for a remembered one that the player now sees is gone.
    /// </summary>
    public bool TryGetStructure(EntityId entity, out SeenStructure structure) =>
        TryGetSeenStructure(entity, out structure) || TryGetRememberedStructure(entity, out structure);

    /// <summary>
    /// The structure the player knows of on a tile: one that stands there and is seen, or else
    /// one remembered there. What a click on the map finds.
    /// </summary>
    public bool TryGetStructureAt(int x, int y, out SeenStructure structure)
    {
        var standing = _match.Structures;
        for (var i = 0; i < standing.Count; i++)
        {
            if (standing.ValueAt(i).Tiles.Covers(x, y) && TryGetSeenStructure(standing.EntityAt(i), out structure))
            {
                return true;
            }
        }

        var memory = _vision.Memory;
        for (var i = 0; i < memory.Count(Player); i++)
        {
            ref readonly var seen = ref memory.At(Player, i);
            if (seen.Tiles.Covers(x, y) && TryGetRememberedStructure(seen.Entity, out structure))
            {
                return true;
            }
        }

        structure = default;
        return false;
    }

    /// <summary>
    /// True when the player knows of the structure: it is its own, or it sees it, or it remembers
    /// it. An attack order may name such a structure (design 5.3), and no other.
    /// </summary>
    public bool KnowsStructure(EntityId entity) => _vision.KnowsStructure(Player, entity);

    /// <summary>
    /// Lists the territories the player may know of: its own, and those of the finished Capitols
    /// and Outposts of other players that it sees or remembers. The list is emptied first.
    /// </summary>
    /// <remarks>
    /// A territory of another player is worked out from the structure as the player knows it, so
    /// it stays with a remembered structure and is not there for one the player has never seen,
    /// nor for one that was still building itself when last seen.
    /// </remarks>
    public void CollectTerritories(List<Territory> territories)
    {
        ArgumentNullException.ThrowIfNull(territories);
        territories.Clear();
        var store = _match.Territories;
        for (var i = 0; i < store.Count; i++)
        {
            var territory = store.ValueAt(i);
            if (territory.Owner == Player && _match.World.Entities.IsAlive(store.EntityAt(i)))
            {
                territories.Add(territory);
            }
        }

        CollectStructures(_known);
        foreach (var structure in _known)
        {
            if (structure.Owner != Player
                && structure.IsFinished
                && _match.Data.StructureOf(structure.Type)?.TerritoryRadius is { } radius)
            {
                territories.Add(Territory.Around(structure.Owner, structure.X, structure.Y, structure.Size, radius));
            }
        }
    }

    /// <summary>
    /// Whether the player may order a structure at all, wherever it goes: what a build menu
    /// shows. See <see cref="ConstructionRules.CheckStructure"/>.
    /// </summary>
    /// <param name="structure">The structure's place in the roster.</param>
    public BuildProblem CheckStructure(int structure) => _match.Construction.CheckStructure(Player, structure);

    /// <summary>
    /// How many of a structure the player has, counting those that are building themselves and
    /// the build orders on their way.
    /// </summary>
    /// <param name="structure">The structure's place in the roster.</param>
    public int CountOf(int structure) => _match.Construction.CountOf(Player, structure);

    /// <summary>
    /// Whether an order for a structure with its lowest tile at (x, y) would be given, as far as
    /// the ground goes: the same check the order gets, against what the player may know (design
    /// 5.4). See <see cref="ConstructionRules.CheckSiteAsKnown"/>.
    /// </summary>
    public BuildProblem CheckSite(StructureDef structure, int x, int y) =>
        _match.Construction.CheckSiteAsKnown(Player, structure, x, y);

    /// <summary>
    /// True when, as far as the player may know, a structure of the player may cover the tile.
    /// A placement preview marks the tiles of the footprint for which this is false; the site is
    /// accepted exactly when it is true for every tile.
    /// </summary>
    public bool CanBuildOnTile(StructureDef structure, int x, int y) =>
        _match.Construction.CanBuildOnTileAsKnown(Player, structure, x, y);

    /// <summary>
    /// True when a Capitol or Outpost with its lowest tile at (x, y) would stand too close to an
    /// enemy Capitol or Outpost that the player sees at this moment (design 5.4). It is a warning
    /// for a placement preview: the order is given all the same, and is judged against every
    /// enemy territory, seen or not, when the worker arrives.
    /// </summary>
    public bool IsTooCloseToSeenEnemyTerritory(StructureDef structure, int x, int y) =>
        _match.Construction.IsTooCloseToSeenEnemyTerritory(Player, structure, x, y);

    /// <summary>
    /// What inspecting a unit or a structure shows (design 3.2). For a unit of a chassis: the
    /// effective Firepower, Protection and Mobility tiers and the accents of its owner's
    /// blueprint of its chassis. For a structure: its owner's Protection tier, its owner's
    /// Firepower tier if it attacks and zero if not, zero for Mobility, and no accents. False for
    /// a unit that is not of a chassis, such as a worker, for a unit or structure the player does
    /// not see, and for a structure the player only remembers.
    /// </summary>
    public bool Inspect(EntityId entity, out Inspection inspection)
    {
        inspection = default;
        if (TryGetSeenStructure(entity, out var structure))
        {
            var built = _match.Research.Tech(structure.Owner).StructureOf(structure.Type);
            inspection = new Inspection(built?.Attack?.EffectiveFirepower ?? 0, built?.ProtectionTier ?? 0, 0, []);
            return true;
        }

        if (!TryGetUnit(entity, out var unit) || unit.Chassis == SeenUnit.NoChassis)
        {
            return false;
        }

        var blueprint = _match.Research.Tech(unit.Owner).Blueprints[unit.Chassis];
        var roles = _match.Data.TrackRoles;
        inspection = new Inspection(
            blueprint.EffectiveTiers[roles.Firepower],
            blueprint.EffectiveTiers[roles.Protection],
            blueprint.EffectiveTiers[roles.Mobility],
            blueprint.Accents);
        return true;
    }

    /// <summary>The order, the target and the wait of one of the player's own units of a chassis. False for anything else.</summary>
    public bool TryGetOwnOrder(EntityId entity, out Combatant order) => TryGetOwn(_match.Combatants, entity, out order);

    /// <summary>Where one of the player's own units is walking, and how. False for anything else.</summary>
    public bool TryGetOwnWalk(EntityId entity, out Mover walk) => TryGetOwn(_match.Movers, entity, out walk);

    /// <summary>What one of the player's own workers is assigned to. False for anything else.</summary>
    public bool TryGetOwnWork(EntityId entity, out Worker work) => TryGetOwn(_match.Workers, entity, out work);

    /// <summary>
    /// The build order one of the player's own workers is carrying out: the structure and the
    /// site it is walking to, where nothing stands yet. False for anything else.
    /// </summary>
    public bool TryGetOwnBuildOrder(EntityId entity, out BuildOrder order) => TryGetOwn(_match.BuildOrders, entity, out order);

    /// <summary>
    /// Lists the shots in flight that the player may know of: those its own units fired, and those
    /// of other players that are over a tile the player sees now, in ascending ID order. The list
    /// is emptied first.
    /// </summary>
    public void CollectShots(List<SeenShot> shots)
    {
        ArgumentNullException.ThrowIfNull(shots);
        shots.Clear();
        var store = _match.Shots;
        for (var i = 0; i < store.Count; i++)
        {
            var entity = store.EntityAt(i);
            ref var shot = ref store.ValueAt(i);
            if (_match.World.Entities.IsAlive(entity) && (shot.Owner == Player || _vision.Grid.IsVisible(Player, shot.Position)))
            {
                shots.Add(new SeenShot(entity, shot.Owner, shot.Position, shot.Line));
            }
        }
    }

    /// <summary>
    /// Lists the events of the last tick that the player may know of, in the order they happened.
    /// The list is emptied first. Call it after each tick: the match keeps the events of one tick.
    /// </summary>
    /// <remarks>
    /// The player knows of every event whose <see cref="SimEvent.Player"/> it is. It knows of a
    /// death, a melee hit, a shot fired, a shot that lands, a shot that is lost, an attack made
    /// with Charge and a unit that regenerates also when it is
    /// another player's and happened on a tile the player sees now, and of a structure that is
    /// laid, finished or removed when it sees a tile of the structure's footprint now. A
    /// structure whose hit points ran out is reported by a death as well, which goes by the tile
    /// under its center like any other. A hit on one of the player's
    /// own is on the tile that unit stands on, which the unit sees itself from the first time
    /// sight is worked out after it appeared. Every other kind is known to its player alone, so a kind
    /// that others should see when it happens in their sight is added to
    /// <see cref="IsSeenWhereItHappens"/>. An event is handed over whole: it may name an entity
    /// the player does not see, such as the unit that fired from unseen ground, and the view
    /// answers nothing about that entity.
    /// </remarks>
    public void CollectEvents(List<SimEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        events.Clear();
        foreach (var happened in _match.Simulation.Events)
        {
            if (happened.Player != Player && IsKnownToEveryone(happened.Kind))
            {
                events.Add(ResearchEvent.AsAnnounced(in happened));
            }
            else if (Knows(in happened))
            {
                events.Add(happened);
            }
        }
    }

    /// <summary>True when the player may know of an event: see <see cref="CollectEvents"/>.</summary>
    public bool Knows(in SimEvent happened) =>
        happened.Player == Player
        || IsKnownToEveryone(happened.Kind)
        || (IsSeenWhereItHappens(happened.Kind) && SeesWhereItHappened(in happened));

    /// <summary>
    /// True for the kinds of event that every player is told of, wherever they happen: a change
    /// of epoch, which is announced to all players (design 1.4).
    /// </summary>
    public static bool IsKnownToEveryone(SimEventKind kind) => kind is SimEventKind.EpochChanged;

    /// <summary>
    /// True for the kinds of event that anyone sees who sees the tile they happen on. Every other
    /// kind is known only to the player it names.
    /// </summary>
    public static bool IsSeenWhereItHappens(SimEventKind kind) =>
        kind is SimEventKind.Death or SimEventKind.MeleeHit or SimEventKind.ShotFired or SimEventKind.ShotHit or SimEventKind.ShotLost
            or SimEventKind.SplashHit or SimEventKind.ChargedAttack or SimEventKind.Regenerated
        || IsOfAStructure(kind)
        || IsOfAnArea(kind);

    /// <summary>The kinds of event that happen to a structure, which is seen by any tile of its footprint.</summary>
    private static bool IsOfAStructure(SimEventKind kind) =>
        kind is SimEventKind.StructurePlaced or SimEventKind.StructureFinished or SimEventKind.StructureRemoved;

    /// <summary>
    /// The kinds of event that happen in a circle, whose radius is the event's amount as the raw
    /// value of a <see cref="Fixed"/>. One is seen by any tile of the circle.
    /// </summary>
    private static bool IsOfAnArea(SimEventKind kind) => kind is SimEventKind.Blast or SimEventKind.Splash;

    private bool SeesWhereItHappened(in SimEvent happened)
    {
        if (IsOfAStructure(happened.Kind) && _match.Data.StructureOf((int)happened.Amount) is { } structure)
        {
            return _vision.Grid.IsAnyVisible(Player, TilesAround(happened.Position, structure.Footprint));
        }

        return _vision.Grid.IsVisible(Player, happened.Position)
            || (IsOfAnArea(happened.Kind) && SeesTileWithin(happened.Position, happened.Amount));
    }

    /// <summary>
    /// True when the player sees a tile whose center is within a distance of a point, the
    /// distance itself included: the measure by which a tile is in sight (design 5.3).
    /// </summary>
    private bool SeesTileWithin(FixedVector2 center, long radiusRaw)
    {
        var grid = _vision.Grid;
        if (radiusRaw <= 0)
        {
            return false;
        }

        var radius = Math.Min(radiusRaw, (long)Math.Max(grid.Width, grid.Height) * Fixed.RawOne);
        const long Half = Fixed.RawOne / 2;
        // Shifting right rounds down, also below zero.
        var firstRow = (int)Math.Max(0, (center.Y.Raw - radius) >> Fixed.FractionalBits);
        var lastRow = (int)Math.Min(grid.Height - 1, (center.Y.Raw + radius) >> Fixed.FractionalBits);
        var firstTile = (int)Math.Max(0, (center.X.Raw - radius) >> Fixed.FractionalBits);
        var lastTile = (int)Math.Min(grid.Width - 1, (center.X.Raw + radius) >> Fixed.FractionalBits);
        for (var y = firstRow; y <= lastRow; y++)
        {
            var dy = (y * (long)Fixed.RawOne) + Half - center.Y.Raw;
            for (var x = firstTile; x <= lastTile; x++)
            {
                var dx = (x * (long)Fixed.RawOne) + Half - center.X.Raw;
                if ((dx * dx) + (dy * dy) <= radius * radius && grid.IsVisible(Player, x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The square of tiles with this middle.</summary>
    private static TileSquare TilesAround(FixedVector2 center, int size)
    {
        var half = Fixed.FromRaw(size * (Fixed.RawOne / 2));
        return new TileSquare((center.X - half).FloorToInt(), (center.Y - half).FloorToInt(), size);
    }

    /// <summary>A structure that stands and that the player sees, as it is now.</summary>
    private bool TryGetSeenStructure(EntityId entity, out SeenStructure structure)
    {
        if (_vision.TryDescribeStructure(entity, out var seen) && _vision.SeesStructure(Player, seen.Owner, seen.Tiles))
        {
            structure = From(in seen, isRemembered: false);
            return true;
        }

        structure = default;
        return false;
    }

    /// <summary>A structure of another player that the player does not see now, as the player remembers it.</summary>
    private bool TryGetRememberedStructure(EntityId entity, out SeenStructure structure)
    {
        if (_vision.Remembers(Player, entity, out var seen) && !(_match.Structures.Contains(entity) && _vision.Sees(Player, entity)))
        {
            structure = From(in seen, isRemembered: true);
            return true;
        }

        structure = default;
        return false;
    }

    private static SeenStructure From(in RememberedStructure seen, bool isRemembered) => new(
        seen.Entity,
        seen.Owner,
        seen.Type,
        seen.X,
        seen.Y,
        seen.Size,
        seen.HitPoints,
        seen.MaxHitPoints,
        seen.IsFinished,
        seen.Built,
        seen.LookTier,
        seen.Banner,
        isRemembered,
        seen.Tick);

    private bool Knows(in WorkSite site)
    {
        if (site.Kind != WorkSiteKind.Deposit)
        {
            return site.Owner == Player;
        }

        for (var y = site.Y; y < site.Y + site.Size; y++)
        {
            for (var x = site.X; x < site.X + site.Size; x++)
            {
                if (_vision.Grid.IsExplored(Player, x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryGetOwn<T>(ComponentStore<T> store, EntityId entity, out T value)
        where T : struct, IStateValue
    {
        value = default;
        return _match.World.Entities.IsAlive(entity)
            && _match.Bodies.TryGet(entity, out var body)
            && body.Owner == Player
            && store.TryGet(entity, out value);
    }

    private void DescribeFound(List<SeenUnit> units)
    {
        units.Clear();
        foreach (var entity in _found)
        {
            if (TryGetUnit(entity, out var unit))
            {
                units.Add(unit);
            }
        }
    }

    private SeenUnit Describe(EntityId entity, in Map.Body body)
    {
        _match.Health.TryGet(entity, out var health);
        _match.Movers.TryGet(entity, out var mover);
        var chassis = _match.Combatants.TryGet(entity, out var combatant) ? combatant.Chassis : SeenUnit.NoChassis;
        return new SeenUnit(
            entity, body.Owner, body.Position, mover.Facing, chassis, _match.Workers.Contains(entity), health.HitPoints, health.Maximum);
    }
}
