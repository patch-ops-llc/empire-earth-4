using System.Globalization;
using Sim.Core;
using Sim.Data;
using Sim.Setup;
using Sim.Structures;

namespace Game;

/// <summary>
/// What a structure is, in the words and numbers the interface shows. Any part that says
/// something about a structure asks here, so that a line in the bottom panel and a panel that
/// describes the selection in full use one wording.
/// </summary>
public static class StructureWords
{
    /// <summary>True when the entity is a structure that stands on the map.</summary>
    public static bool IsStructure(Match match, EntityId entity) =>
        match.World.Entities.IsAlive(entity) && match.Structures.Contains(entity);

    /// <summary>The structure of the roster an entity is, or null for anything that is no structure.</summary>
    public static StructureDef? DefOf(Match match, EntityId entity) =>
        match.Structures.TryGet(entity, out PlacedStructure placed) ? match.Data.StructureOf(placed.Type) : null;

    /// <summary>The name of a structure for a sentence, from the ID of its file: "capitol" is "Capitol".</summary>
    public static string NameOf(StructureDef structure)
    {
        var words = structure.Id.Replace('_', ' ');
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>The name of the structure an entity is, or "Structure" when the data does not name it.</summary>
    public static string NameOf(Match match, EntityId entity) =>
        DefOf(match, entity) is { } structure ? NameOf(structure) : "Structure";

    /// <summary>
    /// Hit points as a whole number, from thousandths. They are rounded up, so that a structure
    /// that still stands never shows none.
    /// </summary>
    public static string HitPoints(long thousandths) => ((thousandths + 999) / 1000).ToString(CultureInfo.InvariantCulture);

    /// <summary>One structure in a line: its name and its hit points, as "Capitol: 2000 of 2000 hit points".</summary>
    public static string Describe(Match match, EntityId entity)
    {
        var name = NameOf(match, entity);
        return match.Health.TryGet(entity, out var health)
            ? $"{name}: {HitPoints(health.HitPoints)} of {HitPoints(health.Maximum)} hit points"
            : name;
    }
}
