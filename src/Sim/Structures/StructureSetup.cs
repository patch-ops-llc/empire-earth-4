using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;

namespace Sim.Structures;

/// <summary>
/// Puts structures on the map. A structure comes in two steps (design 2.5): it is laid, from
/// which moment it blocks its tiles and has its full hit points, and it is finished, from which
/// moment it gives what its file says: a territory, a research slot, a Generator's places.
/// <see cref="PlaceStructure"/> does both at once, which is how the match setup places the
/// starting Capitols and how tests and fixtures place anything. Construction by workers lays a
/// structure with <see cref="LayStructure"/> and finishes it with <see cref="FinishStructure"/>.
/// </summary>
/// <remarks>
/// Call these before the first tick, or inside a tick from a system or a command handler, so that
/// every machine creates the same entities in the same order.
/// </remarks>
public static class StructureSetup
{
    /// <summary>
    /// True when the structure can stand with its lowest tile at (x, y): every tile of its
    /// footprint is on the map, open ground and under no other structure. It does not look at
    /// units standing there, at territory or at the price; those are rules of the order that
    /// places a structure.
    /// </summary>
    public static bool HasRoomFor(this Match match, StructureDef structure, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(structure);
        return match.StructureTiles.IsFree(new TileSquare(x, y, structure.Footprint));
    }

    /// <summary>
    /// Places a finished structure with its lowest tile at (x, y): <see cref="LayStructure"/>
    /// and <see cref="FinishStructure"/> in one.
    /// </summary>
    public static EntityId PlaceStructure(this Match match, int player, StructureDef structure, int x, int y)
    {
        var entity = match.LayStructure(player, structure, x, y);
        match.FinishStructure(entity);
        return entity;
    }

    /// <summary>Places a finished structure, named by its ID, with its lowest tile at (x, y).</summary>
    public static EntityId PlaceStructure(this Match match, int player, string structureId, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.PlaceStructure(player, match.Data.StructureOf(structureId), x, y);
    }

    /// <summary>
    /// Lays a structure with its lowest tile at (x, y): an entity with a <see cref="Body"/> at the
    /// center of the footprint, the structure's full hit points under its owner's Protection tier,
    /// and its place in the store of structures. Its tiles are blocked at once, and units on
    /// their way through them find another. Reported as a <see cref="SimEventKind.StructurePlaced"/>.
    /// </summary>
    /// <param name="match">The match.</param>
    /// <param name="player">The owner.</param>
    /// <param name="structure">One of the structures of the match's data.</param>
    /// <param name="x">The x of the lowest tile of the footprint.</param>
    /// <param name="y">The y of the lowest tile of the footprint.</param>
    /// <exception cref="ArgumentException">The structure has no room there: see <see cref="HasRoomFor"/>.</exception>
    public static EntityId LayStructure(this Match match, int player, StructureDef structure, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, match.Players.Count);
        if (match.Research.Tech(player).StructureOf(structure.Index) is not { } blueprint
            || !ReferenceEquals(blueprint.Structure, structure))
        {
            throw new ArgumentException($"Structure '{structure.Id}' is not one of the match's data.", nameof(structure));
        }

        if (!match.HasRoomFor(structure, x, y))
        {
            throw new ArgumentException(
                $"Structure '{structure.Id}' has no room at tile ({x}, {y}): its {structure.Footprint} by {structure.Footprint} tiles must be open ground under no other structure.",
                nameof(x));
        }

        var entity = match.World.Entities.Create();
        var center = CenterOf(new TileSquare(x, y, structure.Footprint));
        match.Bodies.Add(entity, new Body { Owner = player, Position = center });
        match.Health.Add(entity, Health.Full(blueprint.HitPoints));
        match.Structures.Add(entity, new PlacedStructure { Type = structure.Index, X = x, Y = y, Size = structure.Footprint });

        match.StructureTiles.Changed(match.World);
        match.Simulation.Events.Add(SimEventKind.StructurePlaced, player, entity, position: center, amount: structure.Index);
        return entity;
    }

    /// <summary>The middle of a square of tiles, where the body of a structure standing on it is.</summary>
    public static FixedVector2 CenterOf(TileSquare tiles) => new(
        Fixed.FromRaw(((2 * tiles.X) + tiles.Size) * (Fixed.RawOne / 2)),
        Fixed.FromRaw(((2 * tiles.Y) + tiles.Size) * (Fixed.RawOne / 2)));

    /// <summary>
    /// Gives a laid structure what comes with completion (design 2.5): the territory of a Capitol
    /// or an Outpost, a research slot, the work site of a Generator. Call it once for a structure.
    /// </summary>
    /// <exception cref="ArgumentException">The entity is no living structure.</exception>
    /// <exception cref="InvalidOperationException">
    /// The structure gives a research slot and its owner already has every slot a player can have.
    /// The order that places a structure refuses one too many before it is laid.
    /// </exception>
    public static void FinishStructure(this Match match, EntityId entity)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (!match.World.Entities.IsAlive(entity)
            || !match.Structures.TryGet(entity, out var structure)
            || !match.Bodies.TryGet(entity, out var body)
            || match.Data.StructureOf(structure.Type) is not { } def)
        {
            throw new ArgumentException($"Entity {entity} is no living structure.", nameof(entity));
        }

        if (def.TerritoryRadius is { } radius)
        {
            match.Territories.Add(entity, Territory.Around(body.Owner, structure.X, structure.Y, structure.Size, radius));
        }

        if (def.IsEnergyWorkSite)
        {
            match.WorkSites.Add(
                entity,
                new WorkSite { Kind = WorkSiteKind.Generator, Owner = body.Owner, X = structure.X, Y = structure.Y, Size = structure.Size });
        }

        if (def.ResearchSlots > 0)
        {
            if (def.Id == DataRules.StartStructure)
            {
                // Design 1.3: the slot a player has for as long as they have a finished Capitol.
                match.Research.OpenPlayerSlot(body.Owner);
            }
            else if (!match.Research.OpenSlot(body.Owner, entity))
            {
                throw new InvalidOperationException(
                    $"Player {body.Owner} already has every research slot a player can have, so '{def.Id}' cannot give one more.");
            }
        }
    }
}
