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
    public const string LineInfantryPath = "lines/line_infantry.jsonc";
    public const string GuardPath = "lines/guard.jsonc";
    public const string CavalryArmorPath = "lines/cavalry_armor.jsonc";
    public const string ArtilleryPath = "lines/artillery.jsonc";
    public const string AirPath = "lines/air.jsonc";

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
            "deposit_size": 2,
            "deposits": { "main_per_player": 1, "natural_per_player": 1, "contested": 2 },
            "chokepoint_width": { "min": 2, "max": 4 },
            "open_field_min_width": 6
          },
          "combat": { "look_interval_ticks": 4, "unit_reach_margin": "0.25", "structure_reach": "0.75", "fortification": "0.5" },
          "debug": { "spawn": true }
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

    /// <summary>Line infantry (design 2.4): range 5, and one more at epochs 4, 6 and 8.</summary>
    public const string LineInfantry = """
        {
          "id": "line_infantry",
          "primary": "manufacturing",
          "secondary": "firepower",
          "built_at": "barracks",
          "base": {
            "hp": 60, "dps": 6, "speed": "3.0", "sight": 8, "supply": 2, "spacing": "1",
            "cost": { "materials": 40, "energy": 0 },
            "build_seconds": 12
          },
          "attack": { "kind": "ranged", "interval_seconds": "1", "range": 5, "projectile_speed": 20 },
          "counters": { "guard": "1.25" },
          "chassis": [
            { "epoch": 1, "id": "peltast" },
            { "epoch": 2, "id": "auxiliary" },
            { "epoch": 3, "id": "crossbowman" },
            { "epoch": 4, "id": "musketeer", "attack": { "range": 6 } },
            { "epoch": 5, "id": "rifleman", "attack": { "range": 6 } },
            { "epoch": 6, "id": "infantryman", "attack": { "range": 7 } },
            { "epoch": 7, "id": "assault_rifleman", "attack": { "range": 7 } },
            { "epoch": 8, "id": "combat_drone", "attack": { "range": 8 } },
            { "epoch": 9, "id": "synth_trooper", "attack": { "range": 8 } }
          ]
        }
        """;

    /// <summary>Cavalry/Armor (design 2.4): melee, and ranged at 5 from epoch 5.</summary>
    public const string CavalryArmor = """
        {
          "id": "cavalry_armor",
          "primary": "mobility",
          "secondary": "protection",
          "built_at": "works",
          "base": {
            "hp": 160, "dps": 10, "speed": "4.5", "sight": 10, "supply": 3, "spacing": "1.5",
            "cost": { "materials": 90, "energy": 30 },
            "build_seconds": 24
          },
          "attack": { "kind": "melee", "interval_seconds": "1.5" },
          "counters": { "line_infantry": "1.25", "artillery": "1.5" },
          "cannot_hit": ["air"],
          "chassis": [
            { "epoch": 1, "id": "hippeus" },
            { "epoch": 2, "id": "eques" },
            { "epoch": 3, "id": "knight" },
            { "epoch": 4, "id": "cuirassier" },
            { "epoch": 5, "id": "landship", "attack": { "kind": "ranged", "range": 5, "projectile_speed": 20 } },
            { "epoch": 6, "id": "medium_tank", "attack": { "kind": "ranged", "range": 5, "projectile_speed": 20 } },
            { "epoch": 7, "id": "main_battle_tank", "attack": { "kind": "ranged", "range": 5, "projectile_speed": 20 } },
            { "epoch": 8, "id": "autonomous_tank", "attack": { "kind": "ranged", "range": 5, "projectile_speed": 20 } },
            { "epoch": 9, "id": "grav_tank", "attack": { "kind": "ranged", "range": 5, "projectile_speed": 20 } }
          ]
        }
        """;

    /// <summary>Artillery (design 2.4): range 9, 11 from epoch 4 and 13 from epoch 7; minimum range 3; blast radius 1.5.</summary>
    public const string Artillery = """
        {
          "id": "artillery",
          "primary": "firepower",
          "secondary": "logistics",
          "built_at": "works",
          "base": {
            "hp": 70, "dps": 12, "speed": "1.8", "sight": 8, "supply": 3, "spacing": "1",
            "cost": { "materials": 90, "energy": 50 },
            "build_seconds": 30
          },
          "attack": { "kind": "ranged", "interval_seconds": 4, "range": 9, "min_range": 3, "blast_radius": "1.5", "projectile_speed": 10 },
          "counters": { "structures": "3", "cavalry_armor": "0.5" },
          "cannot_hit": ["air"],
          "chassis": [
            { "epoch": 1, "id": "ballista" },
            { "epoch": 2, "id": "onager" },
            { "epoch": 3, "id": "trebuchet" },
            { "epoch": 4, "id": "field_cannon", "attack": { "range": 11 } },
            { "epoch": 5, "id": "howitzer", "attack": { "range": 11 } },
            { "epoch": 6, "id": "rocket_artillery", "attack": { "range": 11 } },
            { "epoch": 7, "id": "self_propelled_gun", "attack": { "range": 13 } },
            { "epoch": 8, "id": "railgun", "attack": { "range": 13 } },
            { "epoch": 9, "id": "particle_lance", "attack": { "range": 13 } }
          ]
        }
        """;

    /// <summary>Air (design 2.4): fast strike units from epoch 5.</summary>
    public const string Air = """
        {
          "id": "air",
          "primary": "mobility",
          "secondary": "firepower",
          "built_at": "airfield",
          "base": {
            "hp": 80, "dps": 14, "speed": "8.0", "sight": 12, "supply": 3, "spacing": "1",
            "cost": { "materials": 100, "energy": 80 },
            "build_seconds": 30
          },
          "attack": { "kind": "ranged", "interval_seconds": "1", "range": 4, "projectile_speed": 20 },
          "counters": { "artillery": "1.5", "cavalry_armor": "1.25" },
          "chassis": [
            { "epoch": 5, "id": "biplane" },
            { "epoch": 6, "id": "fighter_bomber" },
            { "epoch": 7, "id": "strike_jet" },
            { "epoch": 8, "id": "combat_uav" },
            { "epoch": 9, "id": "aerospace_fighter" }
          ]
        }
        """;

    /// <summary>
    /// A new copy of the files, for a test that edits one. It has every line of the design that
    /// attacks; Support has no weapon and waits for the milestone that adds healing.
    /// </summary>
    public static InMemoryDataFiles Files() => new InMemoryDataFiles()
        .Set(TracksPath, Tracks)
        .Set(RulesPath, Rules)
        .Set(ResearchPath, Research)
        .Set(SampleData.ResourcesPath, SampleData.Resources)
        .Set(AccentsPath, SampleData.Accents)
        .Set(LineInfantryPath, LineInfantry)
        .Set(GuardPath, Guard)
        .Set(CavalryArmorPath, CavalryArmor)
        .Set(ArtilleryPath, Artillery)
        .Set(AirPath, Air)
        .Set(SampleData.CapitolPath, SampleData.Capitol)
        .Set(SampleData.MapPath, new MapBuilder().ToJson());

    /// <summary>The chassis with this ID.</summary>
    public static ChassisDef Chassis(string id) => Data.Chassis.Single(chassis => chassis.Id == id);

    /// <summary>The index of the track with this ID.</summary>
    public static int Track(string id) => Data.Tracks.ToList().FindIndex(track => track.Id == id);

    /// <summary>Tiers in the order the design writes a mix: FP, PR, MO, MF, LG, EX.</summary>
    public static int[] Mix(int firepower, int protection, int mobility, int manufacturing, int logistics, int extraction) =>
        [firepower, protection, mobility, manufacturing, logistics, extraction];

    private static string TrackJson((string Id, string Name, string Short, string Group) track)
    {
        var tiers = string.Join(", ", Enumerable.Range(1, TierCount).Select(tier => $"\"{track.Short} {tier}\""));
        return $$"""{ "id": "{{track.Id}}", "name": "{{track.Name}}", "short": "{{track.Short}}", "group": "{{track.Group}}", "tiers": [{{tiers}}] }""";
    }
}
