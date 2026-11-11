using Sim.Data;

namespace Sim.Tests.Map;

/// <summary>
/// Maps made in code, as tests and the battle harness make them. They are not held to the rules of
/// a match map: they may have no starts, no deposits and no markers.
/// </summary>
internal static class TestMaps
{
    /// <summary>A map of open ground.</summary>
    public static MapDef Open(int width, int height) =>
        FromRows([.. Enumerable.Repeat(new string('.', width), height)]);

    /// <summary>
    /// A map from one text row per y with one character per x, as in a map file: <c>.</c> open ground,
    /// <c>F</c> forest, <c>#</c> cliff.
    /// </summary>
    public static MapDef FromRows(params string[] rows) => FromRows(rows, MapSymmetry.MirrorX, []);

    public static MapDef FromRows(string[] rows, MapSymmetry symmetry, MapStart[] starts)
    {
        var width = rows[0].Length;
        var tiles = new List<TerrainKind>();
        foreach (var row in rows)
        {
            Assert.Equal(width, row.Length);
            foreach (var character in row)
            {
                tiles.Add(character switch
                {
                    '.' => TerrainKind.Open,
                    'F' => TerrainKind.Forest,
                    '#' => TerrainKind.Cliff,
                    _ => throw new ArgumentException($"Unknown terrain character '{character}'.", nameof(rows)),
                });
            }
        }

        return new MapDef("test", width, rows.Length, symmetry, tiles, starts, [], []);
    }
}
