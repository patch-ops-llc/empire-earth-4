using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Construction;
using Sim.Tests.Data;
using Sim.Tests.Economy;
using Sim.Tests.Tech;

namespace Sim.Tests.Vision;

/// <summary>
/// Matches for the scenario tests of the fog of war with structures: the design's data, which the
/// combat tests use, with the files of the structures of design 2.5 whose sight differs, on maps
/// made in code. Players have no start; a test places what its scenario needs.
/// </summary>
internal static class FogMatches
{
    /// <summary>3 by 3 tiles, sight 8, territory radius 12 (design 2.5).</summary>
    public const string Outpost = """
        {
          "id": "outpost",
          "footprint": 3,
          "hp": 1200,
          "cost": { "materials": 250, "energy": 0 },
          "build_seconds": 45,
          "sight": 8,
          "territory_radius": 12
        }
        """;

    /// <summary>3 by 3 tiles, sight 6. It shows its owner's Manufacturing tier.</summary>
    public const string Barracks = """
        {
          "id": "barracks",
          "track": "manufacturing",
          "footprint": 3,
          "hp": 1000,
          "cost": { "materials": 150, "energy": 0 },
          "build_seconds": 35,
          "sight": 6,
          "trains": ["line_infantry", "guard"]
        }
        """;

    /// <summary>2 by 2 tiles, sight 10, range 7. It shows its owner's Protection tier and attacks with its owner's Firepower tier.</summary>
    public const string Tower = """
        {
          "id": "tower",
          "track": "protection",
          "footprint": 2,
          "hp": 600,
          "cost": { "materials": 120, "energy": 30 },
          "build_seconds": 30,
          "sight": 10,
          "attack": {
            "dps": 10,
            "interval_seconds": 1,
            "range": 7,
            "projectile_speed": 20
          }
        }
        """;

    private static readonly Lazy<GameData> Loaded = new(() => DataAssert.Valid(Files()));

    /// <summary>The design's data with a Capitol, an Outpost, a Generator, a Barracks, an Academy, a Tower and the wall.</summary>
    public static GameData Data => Loaded.Value;

    public static InMemoryDataFiles Files() => DesignData.Files()
        .Set("structures/outpost.jsonc", Outpost)
        .Set(EconomyMatches.GeneratorPath, EconomyMatches.Generator)
        .Set("structures/barracks.jsonc", Barracks)
        .Set("structures/academy.jsonc", ConstructionMatches.Academy)
        .Set("structures/tower.jsonc", Tower)
        .Set("structures/wall.jsonc", ConstructionMatches.Wall);

    /// <summary>A match of players without starts, on the data of these tests.</summary>
    public static Match Create(MapDef map, int players = 2) =>
        MatchSetup.Create(Data, map, 1, [.. Enumerable.Repeat(PlayerSetup.WithoutStart, players)]);

    public static StructureDef Structure(string id) => Data.StructureOf(id);

    /// <summary>Places a finished structure with its lowest tile at (x, y), and works sight out again.</summary>
    public static EntityId Place(this Match match, int player, string structure, int x, int y)
    {
        var entity = match.PlaceStructure(player, structure, x, y);
        match.Vision.Refresh();
        return entity;
    }

    /// <summary>
    /// Lays a structure that is still building itself, as a worker's build order does when the
    /// worker arrives, with its lowest tile at (x, y), and works sight out again.
    /// </summary>
    public static EntityId Lay(this Match match, int player, string structure, int x, int y)
    {
        var def = match.Data.StructureOf(structure);
        var entity = match.LayStructure(player, def, x, y);
        match.Foundations.Add(entity, new Foundation { Materials = def.Materials, Energy = def.Energy });
        match.Vision.Refresh();
        return entity;
    }

    /// <summary>
    /// Destroys a structure, as cancelling one does. The next tick takes it off the map; sight
    /// is not worked out again here.
    /// </summary>
    public static void Demolish(this Match match, EntityId structure) => match.World.Entities.Destroy(structure);
}
