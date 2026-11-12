using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Research;
using Sim.Structures;

namespace Sim.Setup;

/// <summary>
/// One match, as <see cref="MatchSetup.Create"/> built it: the world, the tick loop, and every store
/// by its type. The session, the replay tool, the view, the AI and the tests all hold a match and
/// reach the simulation through it.
/// </summary>
/// <remarks>
/// Everything here except what is inside <see cref="World"/> is fixed for the whole match, apart
/// from what is worked out from the world, which <see cref="RefreshDerivedState"/> names. A system
/// that adds a store adds its property here, in alphabetical order.
/// </remarks>
public sealed class Match
{
    internal Match()
    {
    }

    public required GameData Data { get; init; }

    public required MapDef Map { get; init; }

    /// <summary>The players. A player's number is its position in this list.</summary>
    public required IReadOnlyList<PlayerSetup> Players { get; init; }

    public required World World { get; init; }

    public required Simulation Simulation { get; init; }

    public required MapGrid Grid { get; init; }

    /// <summary>Owner and position of everything on the map.</summary>
    public required ComponentStore<Body> Bodies { get; init; }

    /// <summary>The combat numbers of the match and of each player's tech. Not state: see <see cref="CombatRules"/>.</summary>
    public required CombatRules Combat { get; init; }

    /// <summary>Chassis, order and target of every unit of a chassis.</summary>
    public required ComponentStore<Combatant> Combatants { get; init; }

    /// <summary>Hit points of everything that can be damaged.</summary>
    public required ComponentStore<Health> Health { get; init; }

    /// <summary>Speed, facing and move order of everything that can move.</summary>
    public required ComponentStore<Mover> Movers { get; init; }

    /// <summary>
    /// Every player's tiers and research slots, and what follows from the tiers: the table of
    /// blueprints per player. The table is not state: see <see cref="MatchResearch"/>.
    /// </summary>
    public required MatchResearch Research { get; init; }

    /// <summary>The routes found so far. Not state: see <see cref="RouteCache"/>.</summary>
    public required RouteCache Routes { get; init; }

    /// <summary>Every shot in flight.</summary>
    public required ComponentStore<Shot> Shots { get; init; }

    /// <summary>Which entities are near a place. Not state: see <see cref="Map.SpatialIndex"/>.</summary>
    public required SpatialIndex SpatialIndex { get; init; }

    /// <summary>Every player's Materials and Energy.</summary>
    public required Stockpiles Stockpiles { get; init; }

    /// <summary>
    /// Keeps the tiles of <see cref="Grid"/> in line with <see cref="Structures"/>. Not state: see
    /// <see cref="Sim.Structures.StructureTiles"/>.
    /// </summary>
    public required StructureTiles StructureTiles { get; init; }

    /// <summary>Every structure standing on the map, with the tiles it blocks.</summary>
    public required ComponentStore<PlacedStructure> Structures { get; init; }

    /// <summary>Each player's supply cap and used supply. Not state: see <see cref="SupplyAccount"/>.</summary>
    public required SupplyAccount Supply { get; init; }

    /// <summary>The ground each Capitol and Outpost holds for its owner.</summary>
    public required ComponentStore<Territory> Territories { get; init; }

    /// <summary>Assigns workers to work sites. Not state: see <see cref="WorkAssignments"/>.</summary>
    public required WorkAssignments Work { get; init; }

    /// <summary>What each worker is assigned to, with its place and its count of ticks.</summary>
    public required ComponentStore<Worker> Workers { get; init; }

    /// <summary>The deposits and Generators.</summary>
    public required ComponentStore<WorkSite> WorkSites { get; init; }

    /// <summary>
    /// Brings everything that is worked out from the state, and is not state itself, in line with
    /// the world as it is now. Call it after the world has been changed from outside a tick: after
    /// restoring a snapshot, and after a fixture has placed things.
    /// </summary>
    /// <remarks>
    /// The order matters: the tiles under structures and the regions first, with the route cache,
    /// whose routes belong to those tiles; then each player's blueprints; then the spatial index.
    /// </remarks>
    public void RefreshDerivedState()
    {
        StructureTiles.Rebuild();
        Research.RefreshTech();
        SpatialIndex.Rebuild();
    }

    /// <summary>
    /// Places a unit that can move: an entity with a <see cref="Body"/> and a <see cref="Mover"/>.
    /// Call it before the first tick, or inside a tick from a system or a command handler, so that
    /// every machine creates the same entities in the same order.
    /// </summary>
    /// <param name="player">The owner, a position in <see cref="Players"/>.</param>
    /// <param name="position">Where its center goes. The tile there must be passable.</param>
    /// <param name="speedPerTick">The distance it covers in one tick, in tiles.</param>
    public EntityId SpawnUnit(int player, FixedVector2 position, Fixed speedPerTick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, Players.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(speedPerTick.Raw, nameof(speedPerTick));
        if (!Grid.IsPassable(position))
        {
            throw new ArgumentException($"A unit cannot stand at {position}.", nameof(position));
        }

        var entity = World.Entities.Create();
        Bodies.Add(entity, new Body { Owner = player, Position = position });
        Movers.Add(entity, new Mover { Speed = speedPerTick });
        return entity;
    }
}
