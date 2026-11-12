using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Research;
using Sim.Structures;
using Sim.Vision;

namespace Sim.Construction;

/// <summary>
/// The rules of construction (design 2.5 and 5.4) and what its orders and its system share: where
/// a structure may be placed, the build orders under way, and the structures that are building
/// themselves. It holds no state of its own: that is in the stores of <see cref="BuildOrder"/>
/// and <see cref="Foundation"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A build order</b> is paid when it is given, to one worker, which walks to the site. It ends
/// with the whole price given back when it is cancelled, when the worker dies or is given any
/// other order, and when the foundation cannot be laid as the worker arrives.
/// </para>
/// <para>
/// <b>Placement is checked twice</b> (design 5.4): when the order is given, by
/// <see cref="CheckStructure"/> and <see cref="CheckSiteAsKnown"/>, against what the player may
/// know, and again when the worker arrives, by <see cref="CheckArrival"/>, with the true state,
/// which adds the two rules that are judged only then: territories of different players never
/// overlap, and no enemy ground unit stands on the site.
/// </para>
/// <para>
/// <b>What a player may know</b> of a site is what <see cref="Vision"/> says: the ground it has
/// explored, its own structures, and the structures of others that it sees or remembers. An
/// order onto ground the player has not explored, or onto a structure it has never seen, is
/// therefore given, and ends with the whole price back when the worker arrives and finds the
/// site taken. A refusal when the order is given would tell the player what the fog hides.
/// </para>
/// <para>
/// The interface reads the same rules for its build menu and its placement preview, so that what
/// it shows as allowed is what the simulation allows.
/// </para>
/// </remarks>
public sealed class ConstructionRules
{
    private readonly GameData _data;
    private readonly MatchResearch _research;
    private readonly ComponentStore<Territory> _territories;
    private readonly int _playerCount;

    public ConstructionRules(
        GameData data,
        World world,
        MapGrid grid,
        ComponentStore<Body> bodies,
        ComponentStore<BuildOrder> orders,
        ComponentStore<Combatant> combatants,
        ComponentStore<Foundation> foundations,
        ComponentStore<Mover> movers,
        ComponentStore<PlacedStructure> structures,
        ComponentStore<Territory> territories,
        ComponentStore<Worker> workers,
        Stockpiles stockpiles,
        MatchResearch research,
        WorkAssignments work,
        StructureFactory factory,
        SpatialIndex spatialIndex,
        ICommandHandler walk,
        SimEventList events,
        int playerCount)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(foundations);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(territories);
        ArgumentNullException.ThrowIfNull(workers);
        ArgumentNullException.ThrowIfNull(stockpiles);
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(spatialIndex);
        ArgumentNullException.ThrowIfNull(walk);
        ArgumentNullException.ThrowIfNull(events);
        _data = data;
        World = world;
        Grid = grid;
        Bodies = bodies;
        Orders = orders;
        Combatants = combatants;
        Foundations = foundations;
        Movers = movers;
        Structures = structures;
        _territories = territories;
        Workers = workers;
        Stockpiles = stockpiles;
        _research = research;
        Work = work;
        Factory = factory;
        SpatialIndex = spatialIndex;
        Walk = walk;
        Events = events;
        _playerCount = playerCount;
    }

    /// <summary>
    /// A worker has arrived at a site when its center is this near the nearest edge of the
    /// footprint, the exact distance included: 3/4 of a tile (design 5.4).
    /// </summary>
    /// <remarks>
    /// This and <see cref="FoundationRefund"/> are the two numbers of construction that the
    /// design fixes. They are here until the design wants to change one, when they move to a
    /// <c>construction</c> block of the rules with the same values.
    /// </remarks>
    public static Fixed ArrivalReach => Fixed.FromRaw(3 * Fixed.RawOne / 4);

    /// <summary>The part of the price that cancelling a laid foundation gives back: 75% (design 2.5).</summary>
    public static Fraction FoundationRefund => new(3, 4);

    /// <summary>
    /// What each player sees and remembers, which the checks made when an order is given go by.
    /// Without it a player knows everything, as in a match without fog.
    /// </summary>
    public MatchVision? Vision { get; init; }

    /// <summary>The build orders under way, each a value of the worker that carries it out.</summary>
    public ComponentStore<BuildOrder> Orders { get; }

    /// <summary>The structures that are building themselves. A structure that is not here is finished.</summary>
    public ComponentStore<Foundation> Foundations { get; }

    internal World World { get; }

    internal MapGrid Grid { get; }

    internal ComponentStore<Body> Bodies { get; }

    internal ComponentStore<Combatant> Combatants { get; }

    internal ComponentStore<Mover> Movers { get; }

    internal ComponentStore<PlacedStructure> Structures { get; }

    internal ComponentStore<Worker> Workers { get; }

    internal Stockpiles Stockpiles { get; }

    internal WorkAssignments Work { get; }

    internal StructureFactory Factory { get; }

    internal SpatialIndex SpatialIndex { get; }

    /// <summary>The handler of the move order itself, which a worker walks to its site by. It ends no build order.</summary>
    internal ICommandHandler Walk { get; }

    internal SimEventList Events { get; }

    /// <summary>What cancelling a laid foundation gives back of one resource: 75% of what was paid, rounded half up.</summary>
    public static int RefundOf(int paid)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(paid);
        return checked((int)(FoundationRefund * paid).RoundHalfUp(1));
    }

    /// <summary>True for a player of the match.</summary>
    public bool HasPlayer(int player) => player >= 0 && player < _playerCount;

    /// <summary>
    /// The structure a build order may name by this roster index, or null: one that has no file,
    /// or the wall, which is placed in runs by an order of its own.
    /// </summary>
    public StructureDef? Buildable(int structure) =>
        _data.StructureOf(structure) is { Wall: null } def ? def : null;

    /// <summary>True for a structure that stands and is finished: it gives what its file says.</summary>
    public bool IsFinished(EntityId structure) =>
        World.Entities.IsAlive(structure) && Structures.Contains(structure) && !Foundations.Contains(structure);

    /// <summary>
    /// True for a structure that must stand inside its owner's territory. Capitols and Outposts
    /// need not, since they are how territory grows (design 5.4).
    /// </summary>
    public static bool NeedsTerritory(StructureDef structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        return structure.TerritoryRadius is null;
    }

    /// <summary>
    /// How many of a structure a player has, counting those that are building themselves and the
    /// build orders on their way (design 2.5).
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="structure">The structure's roster index.</param>
    /// <param name="except">A worker whose build order is not counted, because another is about to replace it.</param>
    public int CountOf(int player, int structure, EntityId except = default)
    {
        var count = 0;
        for (var i = 0; i < Structures.Count; i++)
        {
            var entity = Structures.EntityAt(i);
            if (Structures.ValueAt(i).Type == structure && IsOwnAndAlive(entity, player))
            {
                count++;
            }
        }

        for (var i = 0; i < Orders.Count; i++)
        {
            var worker = Orders.EntityAt(i);
            if (Orders.ValueAt(i).Type == structure && worker != except && IsOwnAndAlive(worker, player))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Whether a player may order a structure at all, wherever it goes: what a build menu shows.
    /// The first of these that applies: <see cref="BuildProblem.NoStructure"/>,
    /// <see cref="BuildProblem.Locked"/>, <see cref="BuildProblem.LimitReached"/>,
    /// <see cref="BuildProblem.CannotAfford"/>.
    /// </summary>
    public BuildProblem CheckStructure(int player, int structure)
    {
        if (!HasPlayer(player) || Buildable(structure) is not { } def)
        {
            return BuildProblem.NoStructure;
        }

        return !IsUnlocked(player, def) ? BuildProblem.Locked
            : IsAtLimit(player, def) ? BuildProblem.LimitReached
            : !Stockpiles.Covers(player, def.Materials, def.Energy) ? BuildProblem.CannotAfford
            : BuildProblem.None;
    }

    /// <summary>
    /// Whether a structure of a player can stand with its lowest tile at (x, y), as far as the
    /// ground goes and with the true state: <see cref="BuildProblem.SiteNotFree"/> when a tile of
    /// the footprint is off the map, not open ground or under a deposit or a structure, and
    /// <see cref="BuildProblem.OutsideTerritory"/> when the structure needs territory and a tile
    /// is outside the player's. Units on the site and enemy territory nearby are judged when the
    /// worker arrives. An order, when it is given, is judged by <see cref="CheckSiteAsKnown"/>.
    /// </summary>
    public BuildProblem CheckSite(int player, StructureDef structure, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(structure);
        return !Factory.HasRoomFor(structure, x, y) ? BuildProblem.SiteNotFree
            : NeedsTerritory(structure) && !Territory.Holds(_territories, World.Entities, player, x, y, structure.Footprint) ? BuildProblem.OutsideTerritory
            : BuildProblem.None;
    }

    /// <summary>
    /// True when a structure of a player may cover the tile, with the true state: what
    /// <see cref="CheckSite"/> asks of each tile of a footprint. A placement preview asks
    /// <see cref="CanBuildOnTileAsKnown"/>.
    /// </summary>
    public bool CanBuildOnTile(int player, StructureDef structure, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(structure);
        return Grid.IsPassable(x, y)
            && (!NeedsTerritory(structure) || Territory.Holds(_territories, World.Entities, player, x, y, 1));
    }

    /// <summary>
    /// Whether a player may order a structure with its lowest tile at (x, y), as far as the
    /// player may know (design 5.4): what the order itself is judged by, and what a placement
    /// preview shows. <see cref="BuildProblem.SiteNotFree"/> when the player knows of something
    /// in the way on a tile of the footprint (<see cref="MatchVision.KnowsBlocked"/>);
    /// <see cref="BuildProblem.OutsideTerritory"/> when the structure needs territory and a tile
    /// is outside the player's; <see cref="BuildProblem.NotExplored"/> when it is a Capitol or an
    /// Outpost, which need no territory, and the player has not explored every tile.
    /// </summary>
    /// <remarks>
    /// A structure that needs territory may be ordered onto ground of the territory that the
    /// player has not explored: nothing is known against it. <see cref="CheckArrival"/> judges
    /// the site as it is when the worker gets there.
    /// </remarks>
    public BuildProblem CheckSiteAsKnown(int player, StructureDef structure, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (Vision is not { } vision)
        {
            return CheckSite(player, structure, x, y);
        }

        var tiles = new TileSquare(x, y, structure.Footprint);
        return vision.KnowsBlocked(player, tiles) ? BuildProblem.SiteNotFree
            : NeedsTerritory(structure)
                ? Territory.Holds(_territories, World.Entities, player, x, y, structure.Footprint) ? BuildProblem.None : BuildProblem.OutsideTerritory
            : vision.Grid.AreAllExplored(player, tiles) ? BuildProblem.None
            : BuildProblem.NotExplored;
    }

    /// <summary>
    /// True when, as far as the player may know, a structure of the player may cover the tile:
    /// what <see cref="CheckSiteAsKnown"/> asks of each tile of a footprint. A placement preview
    /// marks the tiles for which this is false.
    /// </summary>
    public bool CanBuildOnTileAsKnown(int player, StructureDef structure, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (Vision is not { } vision)
        {
            return CanBuildOnTile(player, structure, x, y);
        }

        return !vision.KnowsBlocked(player, new TileSquare(x, y, 1))
            && (NeedsTerritory(structure)
                ? Territory.Holds(_territories, World.Entities, player, x, y, 1)
                : vision.Grid.IsExplored(player, x, y));
    }

    /// <summary>
    /// <see cref="IsTooCloseToEnemyTerritory"/>, counting only the enemy Capitols and Outposts
    /// the player sees at this moment: what a placement preview may mark (design 5.4). The order
    /// is given all the same, and judged with the true state when the worker arrives.
    /// </summary>
    public bool IsTooCloseToSeenEnemyTerritory(int player, StructureDef structure, int x, int y) =>
        IsTooCloseToEnemyTerritory(player, structure, x, y, seenOnly: true);

    /// <summary>
    /// True when a Capitol or Outpost with its lowest tile at (x, y) would give a territory that
    /// overlaps an enemy's (design 5.4): its center is nearer the center of an enemy Capitol or
    /// Outpost, finished or not, than the sum of the two radii. Always false for other structures.
    /// </summary>
    /// <remarks>
    /// This reads the true state. An order is judged by it only when its worker arrives; a
    /// placement preview shows <see cref="IsTooCloseToSeenEnemyTerritory"/>.
    /// </remarks>
    public bool IsTooCloseToEnemyTerritory(int player, StructureDef structure, int x, int y) =>
        IsTooCloseToEnemyTerritory(player, structure, x, y, seenOnly: false);

    private bool IsTooCloseToEnemyTerritory(int player, StructureDef structure, int x, int y, bool seenOnly)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (structure.TerritoryRadius is not { } radius)
        {
            return false;
        }

        // In half tiles, as a territory keeps its center.
        long centerX = (2L * x) + structure.Footprint;
        long centerY = (2L * y) + structure.Footprint;
        for (var i = 0; i < Structures.Count; i++)
        {
            var other = Structures.ValueAt(i);
            var entity = Structures.EntityAt(i);
            if (_data.StructureOf(other.Type)?.TerritoryRadius is not { } otherRadius
                || !World.Entities.IsAlive(entity)
                || !Bodies.TryGet(entity, out var body)
                || body.Owner == player
                || (seenOnly && Vision is { } vision && !vision.SeesStructure(player, body.Owner, other.Tiles)))
            {
                continue;
            }

            var dx = centerX - ((2L * other.X) + other.Size);
            var dy = centerY - ((2L * other.Y) + other.Size);
            long apart = 2L * (radius + otherRadius);
            if ((dx * dx) + (dy * dy) < apart * apart)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every rule of placement with the true state, as it is judged when the worker of a build
    /// order arrives (design 5.4). The first that fails is the answer.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="structure">The structure.</param>
    /// <param name="x">The x of the lowest tile of the footprint.</param>
    /// <param name="y">The y of the lowest tile of the footprint.</param>
    /// <param name="worker">The worker whose order this is, which the limit does not count twice.</param>
    public BuildProblem CheckArrival(int player, StructureDef structure, int x, int y, EntityId worker = default)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (!IsUnlocked(player, structure))
        {
            return BuildProblem.Locked;
        }

        var site = CheckSite(player, structure, x, y);
        return site != BuildProblem.None ? site
            : IsAtLimit(player, structure, worker) ? BuildProblem.LimitReached
            : IsTooCloseToEnemyTerritory(player, structure, x, y) ? BuildProblem.TooCloseToEnemyTerritory
            : HasEnemyOn(new TileSquare(x, y, structure.Footprint), player) ? BuildProblem.EnemyOnSite
            : BuildProblem.None;
    }

    internal bool IsUnlocked(int player, StructureDef structure) =>
        _research.Tech(player).StructureOf(structure.Index) is { Unlocked: true };

    internal bool IsAtLimit(int player, StructureDef structure, EntityId except = default) =>
        structure.MaxPerPlayer is { } most && CountOf(player, structure.Index, except) >= most;

    /// <summary>
    /// True for a unit that stands on the ground and so can be in the way of a foundation. Every
    /// unit that moves is one until the Air line arrives; it belongs here when it does.
    /// </summary>
    internal bool IsGroundUnit(EntityId entity) => Movers.Contains(entity);

    /// <summary>
    /// Ends a build order before its foundation: the whole price goes back to the worker's owner
    /// and the owner is told why. The worker itself is left as it is; who ends the order says
    /// what the worker does next.
    /// </summary>
    internal void EndOrder(EntityId worker, BuildProblem why)
    {
        var order = Orders.Get(worker);
        var owner = Bodies.Get(worker).Owner;
        Stockpiles.Add(owner, order.Materials, order.Energy);
        Orders.Remove(worker);
        Events.Add(SimEventKind.BuildOrderEnded, owner, worker, position: order.Tiles.Center, amount: (long)why);
    }

    private bool IsOwnAndAlive(EntityId entity, int player) =>
        World.Entities.IsAlive(entity) && Bodies.TryGet(entity, out var body) && body.Owner == player;

    private bool HasEnemyOn(TileSquare square, int player)
    {
        for (var i = 0; i < Movers.Count; i++)
        {
            var entity = Movers.EntityAt(i);
            if (IsGroundUnit(entity)
                && World.Entities.IsAlive(entity)
                && Bodies.TryGet(entity, out var body)
                && body.Owner != player
                && square.Covers(body.Position))
            {
                return true;
            }
        }

        return false;
    }
}
