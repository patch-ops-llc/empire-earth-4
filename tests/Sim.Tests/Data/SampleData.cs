using System.Text;
using Sim.Data;

namespace Sim.Tests.Data;

/// <summary>
/// A small, valid data set held in memory. Each test changes one thing in it and checks what the
/// loader says. It has the shape of the real data with smaller numbers: the six tracks with four
/// tiers each, one line and a 40 by 16 map.
/// </summary>
internal static class SampleData
{
    public const string TracksPath = "tracks.jsonc";
    public const string RulesPath = "rules.jsonc";
    public const string ResearchPath = "research.jsonc";
    public const string ResourcesPath = "resources.jsonc";
    public const string AccentsPath = "accents.jsonc";
    public const string GuardPath = "lines/guard.jsonc";
    public const string MapPath = "maps/arena.jsonc";

    public const string Tracks = """
        // Comments are allowed anywhere.
        {
          "epochs": ["First", "Second", "Third", "Fourth"],
          "tracks": [
            { "id": "firepower", "name": "Firepower", "short": "FP", "group": "better", "tiers": ["F1", "F2", "F3", "F4"] },
            { "id": "protection", "name": "Protection", "short": "PR", "group": "better", "tiers": ["P1", "P2", "P3", "P4"] },
            { "id": "mobility", "name": "Mobility", "short": "MO", "group": "better", "tiers": ["M1", "M2", "M3", "M4"] },
            { "id": "manufacturing", "name": "Manufacturing", "short": "MF", "group": "more", "tiers": ["A1", "A2", "A3", "A4"] },
            { "id": "logistics", "name": "Logistics", "short": "LG", "group": "more", "tiers": ["L1", "L2", "L3", "L4"] },
            { "id": "extraction", "name": "Extraction", "short": "EX", "group": "richer", "tiers": ["E1", "E2", "E3", "E4"] }
          ]
        }
        """;

    public const string Research = """
        {
          "tiers": [
            { "tier": 2, "cost": { "materials": 100, "energy": 100 }, "seconds": 45 },
            { "tier": 3, "cost": { "materials": 150, "energy": 200 }, "seconds": 60 },
            { "tier": 4, "cost": { "materials": 225, "energy": 350 }, "seconds": 75 }
          ]
        }
        """;

    /// <summary>The numbers of design 4.1.</summary>
    public const string Resources = """
        {
          "materials": { "per_worker_per_minute": 45, "workers_per_deposit": 8, "full_rate_amount": 8000, "depleted_rate": "0.5" },
          "energy": { "per_worker_per_minute": 30, "workers_per_generator": 4 },
          "worker": {
            "hp": 50, "speed": 3, "sight": 6, "supply": 1, "spacing": 1,
            "cost": { "materials": 50, "energy": 0 },
            "build_seconds": 12
          }
        }
        """;

    public const string Accents = """
        {
          "accents": {
            "firepower": {
              "name": "Overgunned",
              "level_1": { "range_bonus": { "multiplier": "1.15" }, "attack_rate_bonus": { "multiplier": "1.15" } },
              "level_2": { "splash": { "share": "0.3", "radius": 1 }, "blast_radius_bonus": { "multiplier": "1.5" } }
            },
            "protection": {
              "name": "Hardened",
              "level_1": { "area_damage_reduction": { "multiplier": "0.75" } },
              "level_2": { "plating": { "multiplier": "0.7" } }
            },
            "mobility": {
              "name": "Rapid",
              "level_1": { "sight_bonus": { "tiles": 2 } },
              "level_2": { "charge": { "distance": 4, "multiplier": "1.5" } }
            },
            "manufacturing": {
              "name": "Standardized",
              "level_1": { "batch": { "time_multiplier": "1.5" } },
              "level_2": { "death_refund": { "share": "0.25" } }
            },
            "logistics": {
              "name": "Supplied",
              "level_1": { "regeneration": { "share_per_second": "0.02", "delay_seconds": 5 } },
              "level_2": { "supply_reduction": { "amount": 1, "minimum": 1 } }
            }
          }
        }
        """;

    public const string Rules = """
        {
          "roster": {
            "lines": ["guard", "cavalry_armor", "air"],
            "structures": ["capitol", "barracks"]
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
          "match": { "start_epoch": 1, "epoch_cap": 4 },
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
          "counters": { "cavalry_armor": "1.5", "structures": "0.5" },
          "cannot_hit": ["air"],
          "chassis": [
            { "epoch": 1, "id": "hoplite" },
            { "epoch": 2, "id": "legionary" },
            { "epoch": 4, "id": "grenadier", "attack": { "kind": "ranged", "range": 3, "projectile_speed": "20" } }
          ]
        }
        """;

    public static InMemoryDataFiles Files() => new InMemoryDataFiles()
        .Set(TracksPath, Tracks)
        .Set(RulesPath, Rules)
        .Set(ResearchPath, Research)
        .Set(ResourcesPath, Resources)
        .Set(AccentsPath, Accents)
        .Set(GuardPath, Guard)
        .Set(MapPath, new MapBuilder().ToJson());

    /// <summary>The sample data with one piece of text in one file replaced.</summary>
    public static InMemoryDataFiles With(string path, string oldText, string newText) => Files().Edit(path, oldText, newText);

    /// <summary>Replaces one piece of text in one file. The text must be there, so no test passes by a typo.</summary>
    public static InMemoryDataFiles Edit(this InMemoryDataFiles files, string path, string oldText, string newText)
    {
        var text = Encoding.UTF8.GetString(files.ReadFile(path).Span);
        Assert.Contains(oldText, text, StringComparison.Ordinal);
        return files.Set(path, text.Replace(oldText, newText, StringComparison.Ordinal));
    }

    /// <summary>The sample data with its map changed.</summary>
    public static InMemoryDataFiles WithMap(Action<MapBuilder> change)
    {
        var map = new MapBuilder();
        change(map);
        return Files().Set(MapPath, map.ToJson());
    }
}

/// <summary>
/// Builds the sample map: mirrored left to right, with a cliff wall down the middle that has a pass
/// 3 tiles wide (y 2 to 4) and a gap 6 tiles wide (y 8 to 13).
/// </summary>
internal sealed class MapBuilder
{
    public const int Width = 40;
    public const int Height = 16;

    public MapBuilder()
    {
        for (var y = 0; y < Height; y++)
        {
            var row = new string('.', Width).ToCharArray();
            if (y is not ((>= 2 and <= 4) or (>= 8 and <= 13)))
            {
                row[19] = '#';
                row[20] = '#';
            }

            Rows.Add(row);
        }
    }

    public string Id { get; set; } = "arena";

    public string Symmetry { get; set; } = "mirror_x";

    public List<char[]> Rows { get; } = [];

    public List<string> Starts { get; } =
    [
        """{ "x": 4, "y": 7 }""",
        """{ "x": 34, "y": 7 }""",
    ];

    public List<string> Deposits { get; } =
    [
        """{ "kind": "main", "x": 2, "y": 4 }""",
        """{ "kind": "main", "x": 36, "y": 4 }""",
        """{ "kind": "natural", "x": 12, "y": 12 }""",
        """{ "kind": "natural", "x": 26, "y": 12 }""",
        """{ "kind": "contested", "x": 15, "y": 2 }""",
        """{ "kind": "contested", "x": 23, "y": 2 }""",
    ];

    public List<string> Markers { get; } =
    [
        """{ "kind": "chokepoint", "x": 19, "y": 2, "size_x": 2, "size_y": 3, "across": "y" }""",
        """{ "kind": "open_field", "x": 19, "y": 8, "size_x": 2, "size_y": 6, "across": "y" }""",
    ];

    /// <summary>Sets the same terrain on a tile and on its mirror tile.</summary>
    public void SetBoth(int x, int y, char terrain)
    {
        Rows[y][x] = terrain;
        Rows[y][Width - 1 - x] = terrain;
    }

    public string ToJson() => $$"""
        {
          "id": "{{Id}}",
          "width": {{Width}},
          "height": {{Height}},
          "symmetry": "{{Symmetry}}",
          "starts": [{{string.Join(", ", Starts)}}],
          "deposits": [{{string.Join(", ", Deposits)}}],
          "markers": [{{string.Join(", ", Markers)}}],
          "terrain": [{{string.Join(", ", Rows.Select(row => $"\"{new string(row)}\""))}}]
        }
        """;
}

internal static class DataAssert
{
    public static GameData Valid(IDataFiles files)
    {
        var result = DataLoader.Load(files);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Empty(result.Errors);
        return result.Data;
    }

    /// <summary>The data is rejected with exactly one error, at this place and saying this.</summary>
    public static void RejectedOnce(IDataFiles files, string file, string location, string messagePart)
    {
        var result = DataLoader.Load(files);
        Assert.False(result.IsValid);
        Assert.Null(result.Data);
        var error = Assert.Single(result.Errors);
        Assert.Equal((file, location), (error.File, error.Location));
        Assert.Contains(messagePart, error.Message, StringComparison.Ordinal);
    }

    /// <summary>The data is rejected and one of the errors is at this place and says this.</summary>
    public static void Rejected(IDataFiles files, string file, string location, string messagePart)
    {
        var result = DataLoader.Load(files);
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.File == file && error.Location == location && error.Message.Contains(messagePart, StringComparison.Ordinal));
    }
}
