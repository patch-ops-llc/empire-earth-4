using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;

namespace Sim.Structures;

/// <summary>
/// Puts structures on a match, for the match setup, tests and fixtures: the calls of the match's
/// <see cref="StructureFactory"/>, and <see cref="PlaceStructure"/>, which lays a structure and
/// finishes it at once. That is how the match setup places the starting Capitols. In a match,
/// construction lays a structure when its worker arrives and finishes it when it has built itself.
/// </summary>
/// <remarks>
/// Call these before the first tick, or inside a tick from a system or a command handler, so that
/// every machine creates the same entities in the same order.
/// </remarks>
public static class StructureSetup
{
    /// <summary>True when the structure can stand with its lowest tile at (x, y): see <see cref="StructureFactory.HasRoomFor"/>.</summary>
    public static bool HasRoomFor(this Match match, StructureDef structure, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.StructureFactory.HasRoomFor(structure, x, y);
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

    /// <summary>Lays a structure with its lowest tile at (x, y): see <see cref="StructureFactory.Lay"/>.</summary>
    public static EntityId LayStructure(this Match match, int player, StructureDef structure, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(match);
        return match.StructureFactory.Lay(player, structure, x, y);
    }

    /// <summary>The middle of a square of tiles, where the body of a structure standing on it is.</summary>
    public static FixedVector2 CenterOf(TileSquare tiles) => tiles.Center;

    /// <summary>Gives a laid structure what comes with completion: see <see cref="StructureFactory.Finish"/>.</summary>
    public static void FinishStructure(this Match match, EntityId entity)
    {
        ArgumentNullException.ThrowIfNull(match);
        match.StructureFactory.Finish(entity);
    }
}
