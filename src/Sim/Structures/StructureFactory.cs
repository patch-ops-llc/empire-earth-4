using Sim.Combat;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Research;

namespace Sim.Structures;

/// <summary>
/// Puts structures on the map. A structure comes in two steps (design 2.5): it is laid, from
/// which moment it blocks its tiles and has its full hit points, and it is finished, from which
/// moment it gives what its file says: a territory, a research slot, a Generator's places.
/// Construction lays a structure when its worker arrives and finishes it when it has built
/// itself; the match setup and fixtures do both at once, through <see cref="StructureSetup"/>.
/// </summary>
/// <remarks>
/// Call these before the first tick, or inside a tick from a system or a command handler, so that
/// every machine creates the same entities in the same order.
/// </remarks>
public sealed class StructureFactory
{
    private readonly GameData _data;
    private readonly World _world;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Health> _health;
    private readonly ComponentStore<PlacedStructure> _structures;
    private readonly ComponentStore<Foundation> _foundations;
    private readonly ComponentStore<Territory> _territories;
    private readonly ComponentStore<Turret> _turrets;
    private readonly ComponentStore<WorkSite> _workSites;
    private readonly StructureTiles _tiles;
    private readonly MatchResearch _research;
    private readonly SimEventList _events;
    private readonly int _playerCount;

    public StructureFactory(
        GameData data,
        World world,
        ComponentStore<Body> bodies,
        ComponentStore<Health> health,
        ComponentStore<PlacedStructure> structures,
        ComponentStore<Foundation> foundations,
        ComponentStore<Territory> territories,
        ComponentStore<Turret> turrets,
        ComponentStore<WorkSite> workSites,
        StructureTiles tiles,
        MatchResearch research,
        SimEventList events,
        int playerCount)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(foundations);
        ArgumentNullException.ThrowIfNull(territories);
        ArgumentNullException.ThrowIfNull(turrets);
        ArgumentNullException.ThrowIfNull(workSites);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(events);
        _data = data;
        _world = world;
        _bodies = bodies;
        _health = health;
        _structures = structures;
        _foundations = foundations;
        _territories = territories;
        _turrets = turrets;
        _workSites = workSites;
        _tiles = tiles;
        _research = research;
        _events = events;
        _playerCount = playerCount;
    }

    /// <summary>
    /// True when the structure can stand with its lowest tile at (x, y): every tile of its
    /// footprint is on the map, open ground and under no other structure. It does not look at
    /// units standing there, at territory or at the price; those are rules of the order that
    /// places a structure.
    /// </summary>
    public bool HasRoomFor(StructureDef structure, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(structure);
        return _tiles.IsFree(new TileSquare(x, y, structure.Footprint));
    }

    /// <summary>
    /// Lays a structure with its lowest tile at (x, y): an entity with a <see cref="Body"/> at the
    /// center of the footprint, the structure's full hit points under its owner's Protection tier,
    /// and its place in the store of structures. Its tiles are blocked at once, and units on
    /// their way through them find another. Reported as a <see cref="SimEventKind.StructurePlaced"/>.
    /// </summary>
    /// <remarks>
    /// A structure that builds itself is laid with its <see cref="Foundation"/>, so that the
    /// tiles see it as unfinished from the start: a gate without one is open to its owner at once.
    /// </remarks>
    /// <param name="player">The owner.</param>
    /// <param name="structure">One of the structures of the match's data, or its gate.</param>
    /// <param name="x">The x of the lowest tile of the footprint.</param>
    /// <param name="y">The y of the lowest tile of the footprint.</param>
    /// <param name="foundation">What it has while it builds itself, or null for a structure that does not.</param>
    /// <exception cref="ArgumentException">The structure has no room there: see <see cref="HasRoomFor"/>.</exception>
    public EntityId Lay(int player, StructureDef structure, int x, int y, Foundation? foundation = null)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, _playerCount);
        if (_research.Tech(player).StructureOf(structure.Index) is not { } blueprint
            || !ReferenceEquals(blueprint.Structure, structure))
        {
            throw new ArgumentException($"Structure '{structure.Id}' is not one of the match's data.", nameof(structure));
        }

        if (!HasRoomFor(structure, x, y))
        {
            throw new ArgumentException(
                $"Structure '{structure.Id}' has no room at tile ({x}, {y}): its {structure.Footprint} by {structure.Footprint} tiles must be open ground under no other structure.",
                nameof(x));
        }

        var entity = _world.Entities.Create();
        var center = new TileSquare(x, y, structure.Footprint).Center;
        _bodies.Add(entity, new Body { Owner = player, Position = center });
        _health.Add(entity, Health.Full(blueprint.HitPoints));
        _structures.Add(entity, new PlacedStructure { Type = structure.Index, X = x, Y = y, Size = structure.Footprint });
        if (foundation is { } unfinished)
        {
            _foundations.Add(entity, unfinished);
        }

        _tiles.Changed(_world);
        _events.Add(SimEventKind.StructurePlaced, player, entity, position: center, amount: structure.Index);
        return entity;
    }

    /// <summary>
    /// Gives a laid structure what comes with completion (design 2.5): the territory of a Capitol
    /// or an Outpost, a research slot, the work site of a Generator, and for a gate its owner's
    /// way through it. Call it once for a structure, after its <see cref="Foundation"/> is gone.
    /// </summary>
    /// <exception cref="ArgumentException">The entity is no living structure.</exception>
    /// <exception cref="InvalidOperationException">
    /// The structure gives a research slot and its owner already has every slot a player can have.
    /// The order that places a structure refuses one too many before it is laid.
    /// </exception>
    public void Finish(EntityId entity)
    {
        if (!_world.Entities.IsAlive(entity)
            || !_structures.TryGet(entity, out var structure)
            || !_bodies.TryGet(entity, out var body)
            || _data.StructureOf(structure.Type) is not { } def)
        {
            throw new ArgumentException($"Entity {entity} is no living structure.", nameof(entity));
        }

        if (def.IsGate)
        {
            // The gate opens to its owner, whose units on their way may now have a shorter one.
            _tiles.Changed(_world);
        }

        if (def.TerritoryRadius is { } radius)
        {
            _territories.Add(entity, Territory.Around(body.Owner, structure.X, structure.Y, structure.Size, radius));
        }

        if (def.IsEnergyWorkSite)
        {
            _workSites.Add(
                entity,
                new WorkSite { Kind = WorkSiteKind.Generator, Owner = body.Owner, X = structure.X, Y = structure.Y, Size = structure.Size });
        }

        if (def.Attack is not null)
        {
            // A Tower's attack comes with completion (design 2.5): ready, and with no target yet.
            _turrets.Add(entity, default);
        }

        if (def.ResearchSlots > 0)
        {
            if (def.Id == DataRules.StartStructure)
            {
                // Design 1.3: the slot a player has for as long as they have a finished Capitol.
                _research.OpenPlayerSlot(body.Owner);
            }
            else if (!_research.OpenSlot(body.Owner, entity))
            {
                throw new InvalidOperationException(
                    $"Player {body.Owner} already has every research slot a player can have, so '{def.Id}' cannot give one more.");
            }
        }
    }
}
