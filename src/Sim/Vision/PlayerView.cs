using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Movement;
using Sim.Numerics;
using Sim.Research;
using Sim.Setup;
using Sim.Tech;

namespace Sim.Vision;

/// <summary>
/// A match as one player may know it (design 5.3): the player's own things, the units of other
/// players that the player sees, and the ground and deposits the player has explored. Whatever
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
/// <item>What only the owner knows of its own units: <see cref="TryGetOwnOrder"/>,
/// <see cref="TryGetOwnWalk"/> and <see cref="TryGetOwnWork"/>.</item>
/// <item>Shots in flight: <see cref="CollectShots"/>.</item>
/// <item>The events of the last tick that the player may know of: <see cref="CollectEvents"/>.</item>
/// <item>The player's account: <see cref="Materials"/>, <see cref="Energy"/>,
/// <see cref="SupplyUsed"/>, <see cref="SupplyCap"/>, <see cref="Tech"/> and
/// <see cref="ResearchSlot"/>.</item>
/// </list>
/// <para>
/// <b>What it is not.</b> It is not a guard against a changed program: every machine of a match
/// holds the whole match, and a program that reads the match itself sees everything. It keeps an
/// honest interface honest. Structures, and those remembered as last seen, arrive with them.
/// </para>
/// </remarks>
public sealed class PlayerView
{
    private readonly Match _match;
    private readonly MatchVision _vision;

    // Scratch for one question, emptied at its start.
    private readonly List<EntityId> _found = [];

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

    /// <summary>True when the entity is one of the player's own, or another player's that the player sees now.</summary>
    public bool Sees(EntityId entity) => _vision.Sees(Player, entity);

    /// <summary>A unit, if it is the player's own or the player sees it. False otherwise, and for an entity that is gone.</summary>
    public bool TryGetUnit(EntityId entity, out SeenUnit unit)
    {
        if (_match.World.Entities.IsAlive(entity)
            && _match.Bodies.TryGet(entity, out var body)
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
    /// now, in ascending ID order. The list is emptied first.
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
            if (_match.World.Entities.IsAlive(entity) && _vision.Sees(Player, in body))
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
    /// What inspecting a unit shows (design 3.2): the effective Firepower, Protection and Mobility
    /// tiers and the accents of its owner's blueprint of its chassis. False for a unit that is not
    /// of a chassis, such as a worker, and for a unit the player does not see.
    /// </summary>
    public bool Inspect(EntityId entity, out Inspection inspection)
    {
        inspection = default;
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
    /// death, a melee hit, a shot fired, a shot that lands and a shot that is lost also when it is
    /// another player's and happened on a tile the player sees now. A hit on one of the player's
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
            if (Knows(in happened))
            {
                events.Add(happened);
            }
        }
    }

    /// <summary>True when the player may know of an event: see <see cref="CollectEvents"/>.</summary>
    public bool Knows(in SimEvent happened) =>
        happened.Player == Player
        || (IsSeenWhereItHappens(happened.Kind) && _vision.Grid.IsVisible(Player, happened.Position));

    /// <summary>
    /// True for the kinds of event that anyone sees who sees the tile they happen on. Every other
    /// kind is known only to the player it names.
    /// </summary>
    public static bool IsSeenWhereItHappens(SimEventKind kind) =>
        kind is SimEventKind.Death or SimEventKind.MeleeHit or SimEventKind.ShotFired or SimEventKind.ShotHit or SimEventKind.ShotLost;

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
