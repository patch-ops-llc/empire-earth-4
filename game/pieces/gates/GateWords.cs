using System.Globalization;
using Sim.Data;
using Sim.Vision;

namespace Game;

/// <summary>
/// Gates in the words the interface shows (18b): what a selected gate is, who walks through
/// it, and what several tiles of wall with gates among them are called.
/// </summary>
public static class GateWords
{
    /// <summary>A number of gates: "1 gate", "3 gates".</summary>
    public static string Gates(int count) => string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? "gate" : "gates")}");

    /// <summary>What several selected tiles are called, by how many of them are wall and how many are gates.</summary>
    public static string TitleOf(StructureDef wall, int tiles, int gates)
    {
        var name = StructureWords.NameOf(wall).ToLowerInvariant();
        return gates == 0 ? $"{WallWords.Tiles(tiles)} of {name}"
            : gates == tiles ? Capital(Gates(gates))
            : $"{WallWords.Tiles(tiles - gates)} of {name}, {Gates(gates)}";
    }

    /// <summary>Who walks through one gate of the player's own, and what it costs.</summary>
    public static string OwnFacts(StructureDef gate, in SeenStructure seen) => string.Create(
        CultureInfo.InvariantCulture,
        $"{(seen.IsFinished ? "Your units walk through it. No other player's do." : "Shut to every unit while it builds itself. Then your units walk through it, and no other player's.")} Sight {gate.Sight} · {ResearchWords.Price(gate.Materials, gate.Energy)}.");

    /// <summary>Who walks through one gate of another player's.</summary>
    public static string OthersFacts(in SeenStructure seen) =>
        seen.IsFinished
            ? "Its owner's units walk through it. Yours do not: units ordered to a place they can only reach through it break it down."
            : "Shut to every unit while it builds itself. Units of yours ordered to a place they can only reach through it break it down.";

    /// <summary>Who walks through several selected tiles of the player's own, gates among them.</summary>
    public static string SeveralFacts(int tiles, int gates) =>
        gates == tiles
            ? "Your units walk through a finished gate. No other player's do."
            : $"No unit walks through the wall, yours neither. Your units walk through {(gates == 1 ? "the gate" : "the gates")} once built.";

    /// <summary>The wall a gate stands in, counted from the pieces the player knows of, the gate among them.</summary>
    public static string PieceOf(int length, bool own) =>
        length <= 1
            ? "It stands alone: units walk round it."
            : string.Create(CultureInfo.InvariantCulture, $"The gate of a wall of {length} tiles {(own ? "of yours" : "that you know of")}.");

    private static string Capital(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
}
