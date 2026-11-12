using Sim.Combat;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Research;
using Sim.Structures;
using Sim.Tech;

namespace Sim.Vision;

/// <summary>
/// What each player of a match sees (design 5.3): it works the fog of war out from where the units
/// and structures stand, keeps what each player remembers of the others' structures, and answers
/// whether a player sees something. Combat asks it before a unit picks a target or takes an
/// attack order, construction asks it what a player knows of a site, and
/// <see cref="PlayerView"/> asks it for everything it hands out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who sees.</b> Every unit, from its center: a unit of a chassis as far as its owner's
/// blueprint of the chassis says, which holds the sight accent, and a worker as far as the
/// worker's profile says. Every finished structure, from the center of its footprint, as far as
/// its file says; no track changes that. A structure that is still building itself sees the
/// tiles of its own footprint and no more (design 2.5). Nothing blocks sight.
/// </para>
/// <para>
/// <b>When.</b> <see cref="VisionSystem"/> works the grid out again in every tick whose number is
/// a multiple of <see cref="RefreshTicks"/>, and the match setup does once, for what the players
/// see at the start. In between, the grid holds what the last time found, however the units have
/// moved since.
/// </para>
/// <para>
/// <b>Who is seen.</b> A player always sees its own. It sees another player's unit while the tile
/// under the unit's center is visible in the grid as it stands, and another player's structure
/// while any tile of its footprint is. That is asked anew whenever somebody wants to know, so a
/// unit that walks out of what its enemy last saw is lost to it in that tick, and one that walks
/// into it is seen in that tick.
/// </para>
/// <para>
/// <b>What is remembered.</b> A structure of another player that a player has seen stays with
/// that player as it was when last seen, in <see cref="Memory"/>, also after it is gone. The
/// player forgets it when it sees one of the structure's tiles and the structure is not there.
/// The memories are brought up to date whenever the grid is: once with the grid as it was, which
/// is the last look at what goes out of sight, and once with the grid as it is now.
/// </para>
/// </remarks>
public sealed class MatchVision
{
    private readonly GameData _data;
    private readonly World _world;
    private readonly MapGrid _map;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<Foundation> _foundations;
    private readonly ComponentStore<Health> _health;
    private readonly ComponentStore<PlacedStructure> _structures;
    private readonly ComponentStore<Worker> _workers;
    private readonly ResearchState _research;
    private readonly Func<int, PlayerTech> _techOf;
    private readonly Fixed _workerSight;

    /// <param name="data">The loaded game data.</param>
    /// <param name="world">The world of the match.</param>
    /// <param name="map">The tiles of the match.</param>
    /// <param name="grid">The store of the fog of war, which the world holds.</param>
    /// <param name="memory">The store of the structures each player remembers, which the world holds.</param>
    /// <param name="bodies">Where everything stands.</param>
    /// <param name="combatants">The units of a chassis.</param>
    /// <param name="foundations">The structures that are building themselves.</param>
    /// <param name="health">The hit points of everything that can be damaged.</param>
    /// <param name="structures">The structures standing on the map.</param>
    /// <param name="workers">The workers.</param>
    /// <param name="research">Every player's tiers and research slots.</param>
    /// <param name="techOf">Gives a player's tech as it is now: in a match, <see cref="MatchResearch.Tech"/>.</param>
    public MatchVision(
        GameData data,
        World world,
        MapGrid map,
        VisionGrid grid,
        StructureMemory memory,
        ComponentStore<Body> bodies,
        ComponentStore<Combatant> combatants,
        ComponentStore<Foundation> foundations,
        ComponentStore<Health> health,
        ComponentStore<PlacedStructure> structures,
        ComponentStore<Worker> workers,
        ResearchState research,
        Func<int, PlayerTech> techOf)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(foundations);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(workers);
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(techOf);
        Grid = grid;
        Memory = memory;
        RefreshTicks = data.Rules.Vision.RefreshTicks;
        _data = data;
        _world = world;
        _map = map;
        _bodies = bodies;
        _combatants = combatants;
        _foundations = foundations;
        _health = health;
        _structures = structures;
        _workers = workers;
        _research = research;
        _techOf = techOf;
        _workerSight = Fixed.FromInt(data.Resources.Worker.Sight);
    }

    /// <summary>The fog of war: what each player has explored and what it sees now.</summary>
    public VisionGrid Grid { get; }

    /// <summary>The structures of other players that each player has seen, each as it was when last seen.</summary>
    public StructureMemory Memory { get; }

    /// <summary>The grid is worked out again in every tick whose number is a multiple of this (design 5.3: 4).</summary>
    public int RefreshTicks { get; }

    /// <summary>
    /// How far an entity sees, in tiles, from its center. False for one that does not see so: a
    /// deposit, a shot, an entity that is gone, and a structure that is still building itself,
    /// which sees its own footprint and nothing round it.
    /// </summary>
    public bool TryGetSight(EntityId entity, out Fixed sight)
    {
        sight = Fixed.Zero;
        if (!_world.Entities.IsAlive(entity) || !_bodies.TryGet(entity, out var body))
        {
            return false;
        }

        if (_combatants.TryGet(entity, out var unit))
        {
            sight = SightOf(body.Owner, unit.Chassis);
            return true;
        }

        if (_workers.Contains(entity))
        {
            sight = _workerSight;
            return true;
        }

        if (_structures.TryGet(entity, out var structure)
            && !_foundations.Contains(entity)
            && _data.StructureOf(structure.Type) is { } def)
        {
            sight = Fixed.FromInt(def.Sight);
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when a player sees the unit that stands at <paramref name="body"/>: its own always,
    /// and another player's while the tile under its center is visible to the player. A structure
    /// is seen by its footprint: ask <see cref="Sees(int, EntityId)"/> or <see cref="SeesStructure"/>.
    /// </summary>
    public bool Sees(int player, in Body body) => body.Owner == player || Grid.IsVisible(player, body.Position);

    /// <summary>
    /// True when the entity is alive, stands on the map and the player sees it: a unit by the
    /// tile under its center, a structure by any tile of its footprint, and the player's own
    /// always.
    /// </summary>
    public bool Sees(int player, EntityId entity) =>
        _world.Entities.IsAlive(entity)
        && _bodies.TryGet(entity, out var body)
        && (_structures.TryGet(entity, out var structure) ? SeesStructure(player, body.Owner, structure.Tiles) : Sees(player, in body));

    /// <summary>
    /// True when a player sees a structure that stands on these tiles: its own always, and
    /// another player's while any of the tiles is visible to the player (design 5.3).
    /// </summary>
    public bool SeesStructure(int player, int owner, TileSquare tiles) => owner == player || Grid.IsAnyVisible(player, tiles);

    /// <summary>
    /// The structure of another player as the player remembers it. False when the player has
    /// never seen it, and when the structure is gone and the player sees one of its tiles now,
    /// which is how a memory ends. True also for a structure the player sees now: what is
    /// remembered of it is then no more than <see cref="RefreshTicks"/> old.
    /// </summary>
    public bool Remembers(int player, EntityId entity, out RememberedStructure seen) =>
        Memory.TryGet(player, entity, out seen) && !IsSeenToBeGone(player, in seen);

    /// <summary>
    /// True when a player knows of a structure: it is the player's own, or the player sees it,
    /// or remembers it. This is what an attack order may name (design 5.3). A remembered
    /// structure may be gone; the player learns that by looking.
    /// </summary>
    public bool KnowsStructure(int player, EntityId entity) =>
        (_structures.Contains(entity) && Sees(player, entity)) || Remembers(player, entity, out _);

    /// <summary>
    /// A structure that stands, as it is now: what a player who sees it sees of it, and what is
    /// written down for the player to remember. False for anything else.
    /// </summary>
    public bool TryDescribeStructure(EntityId entity, out RememberedStructure seen)
    {
        if (_world.Entities.IsAlive(entity) && _structures.TryGet(entity, out var structure) && _bodies.TryGet(entity, out var body))
        {
            seen = Describe(entity, in structure, body.Owner);
            return true;
        }

        seen = default;
        return false;
    }

    /// <summary>
    /// True when a player knows of something on these tiles that a structure cannot stand on
    /// (design 5.4: placement is checked against what the player may know): a tile off the map;
    /// a tile the player has explored whose ground is not open; a structure of the player's own;
    /// another player's structure that the player sees or remembers. Ground the player has not
    /// explored, and a structure it has never seen, are not known.
    /// </summary>
    public bool KnowsBlocked(int player, TileSquare tiles)
    {
        for (var y = tiles.Y; y < tiles.Y + tiles.Size; y++)
        {
            for (var x = tiles.X; x < tiles.X + tiles.Size; x++)
            {
                if (!_map.Contains(x, y) || (Grid.IsExplored(player, x, y) && !_map.IsOpenGround(x, y)))
                {
                    return true;
                }
            }
        }

        for (var i = 0; i < _structures.Count; i++)
        {
            var entity = _structures.EntityAt(i);
            var structure = _structures.ValueAt(i).Tiles;
            if (Overlap(structure, tiles)
                && _world.Entities.IsAlive(entity)
                && _bodies.TryGet(entity, out var body)
                && SeesStructure(player, body.Owner, structure))
            {
                return true;
            }
        }

        if (player < 0 || player >= Memory.PlayerCount)
        {
            return false;
        }

        // What stands and is seen was found above, so what is left here is known from memory alone.
        for (var i = 0; i < Memory.Count(player); i++)
        {
            ref readonly var seen = ref Memory.At(player, i);
            if (Overlap(seen.Tiles, tiles) && !IsSeenToBeGone(player, in seen))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Works out what every player sees from where the units and structures stand now, and
    /// brings what each player remembers of the others' structures up to date. Everything that
    /// was visible and is not seen any more stays explored.
    /// </summary>
    /// <remarks>
    /// In a match only <see cref="VisionSystem"/> and the match setup call this. A fixture that
    /// places units or structures before the first tick calls it afterwards, if they are to see
    /// before that tick has run. It is not for <see cref="Setup.Match.RefreshDerivedState"/>: the
    /// grid and the memories are state, and a restored snapshot has them as they were.
    /// </remarks>
    public void Refresh()
    {
        // The last look with the sight there was: a structure that goes out of sight now is
        // remembered as it is in this tick, and not as it was when sight was last worked out.
        Remember();

        Grid.BeginRefresh();

        // What is visible is the union of what everything sees, so the order changes nothing.
        for (var i = 0; i < _combatants.Count; i++)
        {
            var entity = _combatants.EntityAt(i);
            if (_world.Entities.IsAlive(entity) && _bodies.TryGet(entity, out var body))
            {
                Grid.Reveal(body.Owner, body.Position, SightOf(body.Owner, _combatants.ValueAt(i).Chassis));
            }
        }

        for (var i = 0; i < _workers.Count; i++)
        {
            var entity = _workers.EntityAt(i);
            if (_world.Entities.IsAlive(entity) && _bodies.TryGet(entity, out var body))
            {
                Grid.Reveal(body.Owner, body.Position, _workerSight);
            }
        }

        for (var i = 0; i < _structures.Count; i++)
        {
            var entity = _structures.EntityAt(i);
            if (!_world.Entities.IsAlive(entity) || !_bodies.TryGet(entity, out var body))
            {
                continue;
            }

            var structure = _structures.ValueAt(i);
            if (!_foundations.Contains(entity) && _data.StructureOf(structure.Type) is { } def)
            {
                Grid.Reveal(body.Owner, body.Position, Fixed.FromInt(def.Sight));
            }
            else
            {
                Grid.RevealTiles(body.Owner, structure.Tiles);
            }
        }

        Grid.EndRefresh();

        Remember();
    }

    private static bool Overlap(TileSquare a, TileSquare b) =>
        a.X < b.X + b.Size && b.X < a.X + a.Size && a.Y < b.Y + b.Size && b.Y < a.Y + a.Size;

    /// <summary>
    /// With the grid as it stands: writes down every structure that another player sees, as it
    /// is now, and forgets every structure that is gone and of which the player sees a tile.
    /// </summary>
    private void Remember()
    {
        var players = Memory.PlayerCount;
        for (var i = 0; i < _structures.Count; i++)
        {
            var entity = _structures.EntityAt(i);
            if (!_world.Entities.IsAlive(entity) || !_bodies.TryGet(entity, out var body))
            {
                continue;
            }

            var structure = _structures.ValueAt(i);
            var described = false;
            var seen = default(RememberedStructure);
            for (var player = 0; player < players; player++)
            {
                if (player == body.Owner || !Grid.IsAnyVisible(player, structure.Tiles))
                {
                    continue;
                }

                if (!described)
                {
                    seen = Describe(entity, in structure, body.Owner);
                    described = true;
                }

                Memory.Set(player, in seen);
            }
        }

        for (var player = 0; player < players; player++)
        {
            for (var i = Memory.Count(player) - 1; i >= 0; i--)
            {
                if (IsSeenToBeGone(player, in Memory.At(player, i)))
                {
                    Memory.RemoveAt(player, i);
                }
            }
        }
    }

    /// <summary>True when the structure of a memory no longer stands and the player sees one of its tiles now.</summary>
    private bool IsSeenToBeGone(int player, in RememberedStructure seen) =>
        !(_world.Entities.IsAlive(seen.Entity) && _structures.Contains(seen.Entity)) && Grid.IsAnyVisible(player, seen.Tiles);

    private RememberedStructure Describe(EntityId entity, in PlacedStructure structure, int owner)
    {
        _health.TryGet(entity, out var health);
        var def = _data.StructureOf(structure.Type);
        var isBuilding = _foundations.TryGet(entity, out var foundation);
        return new RememberedStructure
        {
            Entity = entity,
            Owner = owner,
            Type = structure.Type,
            X = structure.X,
            Y = structure.Y,
            Size = structure.Size,
            HitPoints = health.HitPoints,
            MaxHitPoints = health.Maximum,
            IsFinished = !isBuilding,
            Built = isBuilding ? foundation.Built : def?.BuildTicks ?? 0,
            LookTier = def?.Track is { } track ? _research.Tier(owner, track) : 0,
            Banner = BannerOf(owner, entity),
            Tick = _world.Tick,
        };
    }

    /// <summary>The track in research in the slot a structure gives its owner, or <see cref="RememberedStructure.NoTrack"/>.</summary>
    private int BannerOf(int owner, EntityId entity)
    {
        for (var i = 0; i < ResearchState.SlotsPerPlayer; i++)
        {
            ref readonly var slot = ref _research.Slot(owner, i);
            if (slot.IsOpen && slot.IsBusy && slot.Holder == entity)
            {
                return slot.Track;
            }
        }

        return RememberedStructure.NoTrack;
    }

    private Fixed SightOf(int player, int chassis) => Fixed.FromInt(_techOf(player).Blueprints[chassis].Sight);
}
