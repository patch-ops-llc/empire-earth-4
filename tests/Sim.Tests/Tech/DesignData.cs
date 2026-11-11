using Sim.Data;
using Sim.Tests.Data;

namespace Sim.Tests.Tech;

/// <summary>
/// The game design's starting numbers for all nine tiers, held in memory, so that the resolver can
/// be tested against the design's worked examples whatever <c>game/data</c> is tuned to. It also
/// has what the first playable leaves out: tiers 5 to 9 and the chassis that need them.
/// </summary>
internal static class DesignData
{
    public const string TracksPath = "tracks.jsonc";
    public const string RulesPath = "rules.jsonc";
    public const string ResearchPath = "research.jsonc";
    public const string AccentsPath = "accents.jsonc";

    private const int TierCount = 9;

    private static readonly Lazy<GameData> Loaded = new(() => DataAssert.Valid(Files()));

    /// <summary>The fixed track order of design 1.1: Firepower, Protection, Mobility, Manufacturing, Logistics, Extraction.</summary>
    private static readonly (string Id, string Name, string Short, string Group)[] TrackList =
    [
        ("firepower", "Firepower", "FP", "better"),
        ("protection", "Protection", "PR", "better"),
        ("mobility", "Mobility", "MO", "better"),
        ("manufacturing", "Manufacturing", "MF", "more"),
        ("logistics", "Logistics", "LG", "more"),
        ("extraction", "Extraction", "EX", "richer"),
    ];

    public static GameData Data => Loaded.Value;

    public static string Tracks { get; } = $$"""
        {
          "epochs": [{{string.Join(", ", Enumerable.Range(1, TierCount).Select(epoch => $"\"Epoch {epoch}\""))}}],
          "tracks": [
            {{string.Join(",\n    ", TrackList.Select(TrackJson))}}
          ]
        }
        """;

    /// <summary>Design 1.3, 1.4, 2.3, 4.1 and 5.2, with the full game's epoch cap. The map rules fit the small sample map.</summary>
    public const string Rules = """
        {
          "roster": {
            "lines": ["line_infantry", "guard", "cavalry_armor", "artillery", "support", "air"],
            "structures": ["capitol", "outpost", "generator", "barracks", "works", "airfield", "academy", "wall", "tower",
                           "strategic_defense", "strategic_battery"]
          },
          "epoch": { "tracks_needed": 3, "reach": 2 },
          "research_price": {
            "catch_up": { "cost": "0.5", "time": "0.5" },
            "frontier": { "cost": "1", "time": "1" },
            "ahead": { "cost": "1.5", "time": "1" }
          },
          "effective_tier_window": 2,
          "per_tier": { "damage": "1.25", "hp": "1.25", "speed": "1.08", "income": "1.12" },
          "per_step": { "quality_cost": "1.15", "manufacturing_cost": "0.90", "manufacturing_time": "0.80" },
          "accent_slots": 2,
          "supply_cap": { "base": 40, "per_logistics_tier": 15 },
          "wall_crossing_cost": 20,
          "match": { "start_epoch": 1, "epoch_cap": 9 },
          "start": { "workers": 6, "materials": 400, "energy": 100 },
          "map": {
            "start_territory": { "footprint": 2, "radius": 6 },
            "deposit_size": 2,
            "deposits": { "main_per_player": 1, "natural_per_player": 1, "contested": 2 },
            "chokepoint_width": { "min": 2, "max": 4 },
            "open_field_min_width": 6
          }
        }
        """;

    /// <summary>The price table of design 1.3.</summary>
    public const string Research = """
        {
          "tiers": [
            { "tier": 2, "cost": { "materials": 100, "energy": 100 }, "seconds": 45 },
            { "tier": 3, "cost": { "materials": 150, "energy": 200 }, "seconds": 60 },
            { "tier": 4, "cost": { "materials": 225, "energy": 350 }, "seconds": 75 },
            { "tier": 5, "cost": { "materials": 350, "energy": 550 }, "seconds": 90 },
            { "tier": 6, "cost": { "materials": 500, "energy": 850 }, "seconds": 105 },
            { "tier": 7, "cost": { "materials": 750, "energy": 1300 }, "seconds": 120 },
            { "tier": 8, "cost": { "materials": 1100, "energy": 1900 }, "seconds": 135 },
            { "tier": 9, "cost": { "materials": 1600, "energy": 2800 }, "seconds": 150 }
          ]
        }
        """;

    /// <summary>The line profile of the Guard (design 2.4) with a chassis for every epoch (design 2.1).</summary>
    public const string Guard = """
        {
          "id": "guard",
          "primary": "protection",
          "secondary": "firepower",
          "built_at": "barracks",
          "base": {
            "hp": 130, "dps": 8, "speed": "2.6", "sight": 8, "supply": 2, "spacing": "1",
            "cost": { "materials": 70, "energy": 10 },
            "build_seconds": 18
          },
          "attack": { "kind": "melee", "interval_seconds": "1" },
          "counters": { "cavalry_armor": "1.5" },
          "cannot_hit": ["air"],
          "chassis": [
            { "epoch": 1, "id": "hoplite" },
            { "epoch": 2, "id": "legionary" },
            { "epoch": 3, "id": "man_at_arms" },
            { "epoch": 4, "id": "grenadier", "attack": { "kind": "ranged", "range": 3, "projectile_speed": 20 } },
            { "epoch": 5, "id": "stormtrooper", "attack": { "kind": "ranged", "range": 3, "projectile_speed": 20 } },
            { "epoch": 6, "id": "anti_tank_trooper", "attack": { "kind": "ranged", "range": 3, "projectile_speed": 20 } },
            { "epoch": 7, "id": "armored_infantry", "attack": { "kind": "ranged", "range": 3, "projectile_speed": 20 } },
            { "epoch": 8, "id": "exosuit_trooper", "attack": { "kind": "ranged", "range": 3, "projectile_speed": 20 } },
            { "epoch": 9, "id": "void_marine", "attack": { "kind": "ranged", "range": 3, "projectile_speed": 20 } }
          ]
        }
        """;

    /// <summary>A new copy of the files, for a test that edits one.</summary>
    public static InMemoryDataFiles Files() => new InMemoryDataFiles()
        .Set(TracksPath, Tracks)
        .Set(RulesPath, Rules)
        .Set(ResearchPath, Research)
        .Set(AccentsPath, SampleData.Accents)
        .Set("lines/guard.jsonc", Guard)
        .Set(SampleData.MapPath, new MapBuilder().ToJson());

    /// <summary>Tiers in the order the design writes a mix: FP, PR, MO, MF, LG, EX.</summary>
    public static int[] Mix(int firepower, int protection, int mobility, int manufacturing, int logistics, int extraction) =>
        [firepower, protection, mobility, manufacturing, logistics, extraction];

    private static string TrackJson((string Id, string Name, string Short, string Group) track)
    {
        var tiers = string.Join(", ", Enumerable.Range(1, TierCount).Select(tier => $"\"{track.Short} {tier}\""));
        return $$"""{ "id": "{{track.Id}}", "name": "{{track.Name}}", "short": "{{track.Short}}", "group": "{{track.Group}}", "tiers": [{{tiers}}] }""";
    }
}
