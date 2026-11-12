using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Data;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Tests.Tech;

namespace Sim.Tests.Combat;

/// <summary>
/// Matches for the tests of structures in combat: those of <see cref="CombatMatches"/>, with the
/// structures of design 2.5 that combat tells apart in the data. Each test places its structures
/// itself, before the first tick.
/// </summary>
/// <remarks>
/// A Capitol of 4 by 4 tiles and 2,000 hit points; a Barracks of 3 by 3 and 1,000; a Generator
/// and an Academy of 2 by 2, with 500 and 800; a Wall of one tile and 400; and a Tower of 2 by 2
/// and 600, which attacks for 10 once a second at range 7 with shots flying 20 tiles per second,
/// and cannot hit Air until its owner has Protection 5.
/// </remarks>
internal static class SiegeMatches
{
    public const string Capitol = "capitol";
    public const string Barracks = "barracks";
    public const string Generator = "generator";
    public const string Academy = "academy";
    public const string Wall = "wall";
    public const string Tower = "tower";

    public const string TowerPath = "structures/tower.jsonc";

    public const string Hoplite = CombatMatches.Hoplite;
    public const string Peltast = CombatMatches.Peltast;
    public const string Ballista = CombatMatches.Ballista;
    public const string Knight = "knight";

    private const string CapitolFile = """
        {
          "id": "capitol",
          "track": "logistics",
          "footprint": 4,
          "hp": 2000,
          "cost": { "materials": 400, "energy": 0 },
          "build_seconds": 90,
          "sight": 10,
          "territory_radius": 6,
          "research_slots": 1,
          "trains": ["worker"]
        }
        """;

    private const string BarracksFile = """
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

    private const string GeneratorFile = """
        {
          "id": "generator",
          "track": "extraction",
          "footprint": 2,
          "hp": 500,
          "cost": { "materials": 100, "energy": 0 },
          "build_seconds": 25,
          "sight": 6,
          "work_site": "energy"
        }
        """;

    private const string AcademyFile = """
        {
          "id": "academy",
          "footprint": 2,
          "hp": 800,
          "cost": { "materials": 200, "energy": 100 },
          "build_seconds": 45,
          "sight": 6,
          "research_slots": 1,
          "max_per_player": 2
        }
        """;

    private const string WallFile = """
        {
          "id": "wall",
          "track": "protection",
          "footprint": 1,
          "hp": 400,
          "cost": { "materials": 20, "energy": 0 },
          "build_seconds": 5,
          "sight": 2,
          "wall": { "gate": { "cost": { "materials": 40, "energy": 0 }, "build_seconds": 10 } }
        }
        """;

    private const string TowerFile = """
        {
          "id": "tower",
          "track": "protection",
          "footprint": 2,
          "hp": 600,
          "cost": { "materials": 120, "energy": 30 },
          "build_seconds": 30,
          "sight": 10,
          "attack": {
            "dps": 10, "interval_seconds": 1, "range": 7, "projectile_speed": 20,
            "cannot_hit": ["air"], "can_hit_all_from_protection": 5
          }
        }
        """;

    private static readonly Lazy<GameData> Loaded = new(() => DataAssert.Valid(Files()));

    /// <summary>The design's numbers with the structures above.</summary>
    public static GameData Data => Loaded.Value;

    /// <summary>A new copy of the files, for a test that edits one.</summary>
    public static InMemoryDataFiles Files() => DesignData.Files()
        .Set(SampleData.MapPath, SampleMap())
        .Set(SampleData.CapitolPath, CapitolFile)
        .Set("structures/barracks.jsonc", BarracksFile)
        .Set("structures/generator.jsonc", GeneratorFile)
        .Set("structures/academy.jsonc", AcademyFile)
        .Set("structures/wall.jsonc", WallFile)
        .Set(TowerPath, TowerFile);

    /// <summary>The sample map, with its second start where a Capitol of 4 tiles mirrors the first.</summary>
    private static string SampleMap()
    {
        var map = new MapBuilder();
        map.Starts[1] = """{ "x": 32, "y": 7 }""";
        return map.ToJson();
    }

    public static Match Create(MapDef map, GameData? data = null, int players = 2) =>
        MatchSetup.Create(data ?? Data, map, 1, [.. Enumerable.Repeat(PlayerSetup.WithoutStart, players)]);

    /// <summary>An open field of 32 by 24 tiles.</summary>
    public static Match Field(GameData? data = null) => Create(TestMaps.Open(32, 24), data);

    /// <summary>A unit of a chassis of the match's own data.</summary>
    public static EntityId Unit(this Match match, int player, string chassis, string x, string y)
    {
        var unit = match.SpawnChassis(player, match.Data.Chassis.Single(candidate => candidate.Id == chassis).Index, TestMatches.Point(x, y));

        // What the players see is worked out again, as CombatMatches.Spawn does, so that an
        // order given for the first tick finds what the unit sees.
        match.Vision.Refresh();
        return unit;
    }

    /// <summary>Lays a structure and does not finish it: a foundation.</summary>
    public static EntityId Lay(this Match match, int player, string structure, int x, int y) =>
        match.LayStructure(player, match.Data.StructureOf(structure), x, y);

    public static int StructureIndex(string structure) => Data.StructureOf(structure).Index;

    public static long Squared(string distance) => CombatMatches.Squared(distance);

    /// <summary>The development placement of a structure, by its place in the roster, with its lowest tile on the tile of the point.</summary>
    public static Command PlaceCommand(int player, int structure, FixedVector2 point) =>
        new(CommandType.DebugPlace, player, targetX: point.X, targetY: point.Y, contentId: structure);

    /// <summary>Queues a development placement for the next tick to run.</summary>
    public static void OrderPlace(this Match match, int player, string structure, int x, int y) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, PlaceCommand(player, StructureIndex(structure), MapGrid.TileCenter(x, y)));

    /// <summary>Runs ticks and returns every event of them, in order.</summary>
    public static List<SimEvent> RunAndCollect(this Match match, int ticks)
    {
        var events = new List<SimEvent>();
        for (var tick = 0; tick < ticks; tick++)
        {
            match.Run(1);
            events.AddRange(match.Simulation.Events);
        }

        return events;
    }

    /// <summary>The events of the tick that has just run in which an entity attacked: its shots fired and its melee hits.</summary>
    public static SimEvent[] AttacksOf(this Match match, EntityId attacker) =>
        [.. match.Simulation.Events.Where(reported =>
            reported.Other == attacker && reported.Kind is SimEventKind.ShotFired or SimEventKind.MeleeHit)];

    /// <summary>Runs ticks until an event of a kind is reported, and returns the first of that tick. The events of that tick stay on the list.</summary>
    public static SimEvent RunTo(this Match match, SimEventKind kind, int limit = 400)
    {
        for (var tick = 0; tick < limit; tick++)
        {
            match.Run(1);
            foreach (var reported in match.Simulation.Events)
            {
                if (reported.Kind == kind)
                {
                    return reported;
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"No {kind} in {limit} ticks.");
    }

    /// <summary>The center of a footprint whose lowest tile is (x, y).</summary>
    public static FixedVector2 CenterOf(int x, int y, int size) => new TileSquare(x, y, size).Center;
}
