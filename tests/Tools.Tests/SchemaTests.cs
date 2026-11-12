using System.Text.Json;

namespace Tools.Tests;

/// <summary>
/// The schemas in <c>schemas/</c> give editors autocomplete and inline errors. These tests keep them
/// in step with the data: every data file must name its schema and satisfy it, and each schema must
/// reject the mistakes an editor should underline.
/// </summary>
public class SchemaTests
{
    private static readonly JsonDocumentOptions Jsonc = new() { CommentHandling = JsonCommentHandling.Skip };

    private static readonly string[] OneFileOfEachKind = ["tracks.jsonc", "rules.jsonc", "research.jsonc", "resources.jsonc", "accents.jsonc", "lines/x.jsonc", "structures/x.jsonc", "maps/x.jsonc", "ai/x.jsonc"];

    public static TheoryData<string> DataFiles() =>
        [.. new DiskDataFiles(Repository.DataFolder).ListFiles()];

    [Theory]
    [MemberData(nameof(DataFiles))]
    public void EveryDataFileNamesItsSchemaAndSatisfiesIt(string file)
    {
        var path = Path.Combine(Repository.DataFolder, file);
        using var document = JsonDocument.Parse(File.ReadAllText(path), Jsonc);

        var named = document.RootElement.GetProperty("$schema").GetString()!;
        var namedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, named));
        Assert.Equal(Path.GetFullPath(SchemaPathFor(file)), namedPath);

        Assert.Empty(Validate(file, document.RootElement));
    }

    [Theory]
    [InlineData("tracks.jsonc", "\"id\": \"firepower\",", "", "$.tracks[0]: has no id")]
    [InlineData("tracks.jsonc", "\"group\": \"richer\"", "\"group\": \"bigger\"", "$.tracks[5].group: is not one of the allowed values")]
    [InlineData("tracks.jsonc", "\"id\": \"mobility\"", "\"id\": \"Mobility\"", "$.tracks[2].id: does not match")]
    [InlineData("rules.jsonc", "\"tracks_needed\": 3", "\"tracks_needed\": \"3\"", "$.epoch.tracks_needed: is not of type integer")]
    [InlineData("rules.jsonc", "\"accent_slots\": 2,", "\"accent_slots\": 2, \"accent_slot\": 2,", "$: has an unknown key accent_slot")]
    [InlineData("rules.jsonc", "\"damage\": \"1.25\"", "\"damage\": 1.25", "$.per_tier.damage: matches none of the allowed forms")]
    [InlineData("rules.jsonc", "\"manufacturing_cost\": \"0.90\"", "\"manufacturing_cost\": \"90%\"", "$.per_step.manufacturing_cost: matches none of the allowed forms")]
    [InlineData("rules.jsonc", "\"capitol\",", "\"capitol\", \"capitol\",", "$.roster.structures: has the same item twice")]
    [InlineData("rules.jsonc", "\"look_interval_ticks\": 4", "\"look_interval_ticks\": 0", "$.combat.look_interval_ticks: is below 1")]
    [InlineData("rules.jsonc", "\"fortification\": \"0.5\"", "\"fortification\": 0.5", "$.combat.fortification: matches none of the allowed forms")]
    [InlineData("rules.jsonc", "\"structure_reach\": \"0.75\"", "\"structure_reach\": \"0.75\", \"reach\": 1", "$.combat: has an unknown key reach")]
    [InlineData("research.jsonc", "\"seconds\": 45", "\"seconds\": 45.5", "$.tiers[0].seconds: matches none of the allowed forms")]
    [InlineData("research.jsonc", "\"tier\": 2", "\"tier\": 1", "$.tiers[0].tier: is below 2")]
    [InlineData("research.jsonc", "\"materials\": 100,", "\"material\": 100,", "$.tiers[0].cost: has an unknown key material")]
    [InlineData("research.jsonc", "\"energy\": 2800", "\"energy\": -2800", "$.tiers[7].cost.energy: is below 0")]
    [InlineData("resources.jsonc", "\"depleted_rate\": \"0.5\"", "\"depleted_rate\": 0.5", "$.materials.depleted_rate: matches none of the allowed forms")]
    [InlineData("resources.jsonc", "\"workers_per_deposit\": 8", "\"workers_per_deposit\": 0", "$.materials.workers_per_deposit: is below 1")]
    [InlineData("resources.jsonc", "\"workers_per_generator\": 4", "\"generator_slots\": 4", "$.energy: has an unknown key generator_slots")]
    [InlineData("resources.jsonc", "\"sight\": 6,", "", "$.worker: has no sight")]
    [InlineData("resources.jsonc", "\"energy\": 0 }", "\"energy\": -1 }", "$.worker.cost.energy: is below 0")]
    [InlineData("accents.jsonc", "\"firepower\": {", "\"Firepower\": {", "$.accents.Firepower (key): does not match")]
    [InlineData("accents.jsonc", "\"name\": \"Rapid\",", "", "$.accents.mobility: has no name")]
    [InlineData("accents.jsonc", "\"splash\": {", "\"spalsh\": {", "$.accents.firepower.level_2: has an unknown key spalsh")]
    [InlineData("accents.jsonc", "\"multiplier\": \"0.75\"", "\"multiplier\": 0.75", "$.accents.protection.level_1.area_damage_reduction.multiplier: matches none of the allowed forms")]
    [InlineData("accents.jsonc", "\"tiles\": 2", "\"tiles\": \"2\"", "$.accents.mobility.level_1.sight_bonus.tiles: is not of type integer")]
    [InlineData("accents.jsonc", "\"amount\": 1, ", "", "$.accents.logistics.level_2.supply_reduction: has no amount")]
    [InlineData("accents.jsonc", "\"share\": \"0.25\"", "\"share\": \"0.25\", \"of\": \"energy\"", "$.accents.manufacturing.level_2.death_refund: has an unknown key of")]
    [InlineData("lines/guard.jsonc", "\"speed\": \"2.6\"", "\"speed\": 2.6", "$.base.speed: matches none of the allowed forms")]
    [InlineData("lines/guard.jsonc", "\"kind\": \"melee\", ", "", "$.attack: has no kind")]
    [InlineData("lines/guard.jsonc", "\"kind\": \"ranged\"", "\"kind\": \"thrown\"", "$.chassis[3].attack.kind: is not one of the allowed values")]
    [InlineData("lines/guard.jsonc", "\"epoch\": 1", "\"epoch\": 0", "$.chassis[0].epoch: is below 1")]
    [InlineData("lines/guard.jsonc", "\"cavalry_armor\": \"1.5\"", "\"Cavalry\": \"1.5\"", "$.counters.Cavalry (key): does not match")]
    [InlineData("lines/guard.jsonc", "\"sight\": 8,", "\"sight\": 8, \"sihgt\": 8,", "$.base: has an unknown key sihgt")]
    [InlineData("lines/artillery.jsonc", "\"blast_radius\": \"1.5\"", "\"blast_radius\": 1.5", "$.attack.blast_radius: matches none of the allowed forms")]
    [InlineData("lines/artillery.jsonc", "\"min_range\": 3", "\"minimum_range\": 3", "$.attack: has an unknown key minimum_range")]
    [InlineData("lines/line_infantry.jsonc", "\"attack\": { \"range\": 6 }", "\"attack\": { \"reach\": 6 }", "$.chassis[3].attack: has an unknown key reach")]
    [InlineData("lines/cavalry_armor.jsonc", "\"cannot_hit\": [\"air\"]", "\"cannot_hit\": [\"air\", \"air\"]", "$.cannot_hit: has the same item twice")]
    [InlineData("structures/capitol.jsonc", "\"footprint\": 4,", "\"footprint\": 0,", "$.footprint: is below 1")]
    [InlineData("structures/capitol.jsonc", "\"hp\": 2000,", "\"hp\": 2000.5,", "$.hp: matches none of the allowed forms")]
    [InlineData("structures/capitol.jsonc", "\"sight\": 10,", "", "$: has no sight")]
    [InlineData("structures/capitol.jsonc", "\"trains\": [\"worker\"]", "\"trains\": []", "$.trains: has fewer than 1 items")]
    [InlineData("structures/capitol.jsonc", "\"research_slots\": 1,", "\"research_slot\": 1,", "$: has an unknown key research_slot")]
    [InlineData("structures/barracks.jsonc", "\"energy\": 0 }", "\"energy\": -1 }", "$.cost.energy: is below 0")]
    [InlineData("structures/barracks.jsonc", "[\"line_infantry\", \"guard\"]", "[\"guard\", \"guard\"]", "$.trains: has the same item twice")]
    [InlineData("structures/generator.jsonc", "\"work_site\": \"energy\"", "\"work_site\": \"materials\"", "$.work_site: is not one of the allowed values")]
    [InlineData("structures/academy.jsonc", "\"max_per_player\": 2", "\"max_per_player\": 0", "$.max_per_player: is below 1")]
    [InlineData("structures/wall.jsonc", "\"build_seconds\": 10 }", "\"seconds\": 10 }", "$.wall.gate: has an unknown key seconds")]
    [InlineData("structures/wall.jsonc", "\"gate\": {", "\"door\": {", "$.wall: has no gate")]
    [InlineData("structures/tower.jsonc", "\"projectile_speed\": 20,", "", "$.attack: has no projectile_speed")]
    [InlineData("structures/tower.jsonc", "\"range\": 7,", "\"range\": 7, \"min_range\": 2,", "$.attack: has an unknown key min_range")]
    [InlineData("structures/tower.jsonc", "\"can_hit_all_from_protection\": 5", "\"can_hit_all_from_protection\": 1", "$.attack.can_hit_all_from_protection: is below 2")]
    [InlineData("maps/ridge.jsonc", "\"symmetry\": \"mirror_x\"", "\"symmetry\": \"rotate_180\"", "$.symmetry: is not one of the allowed values")]
    [InlineData("maps/ridge.jsonc", "{ \"x\": 82, \"y\": 46 }", "{ \"x\": 82, \"y\": 46 }, { \"x\": 40, \"y\": 46 }", "$.starts: has more than 2 items")]
    [InlineData("maps/ridge.jsonc", "\"across\": \"y\" },", "\"across\": \"z\" },", "$.markers[0].across: is not one of the allowed values")]
    [InlineData("maps/ridge.jsonc", "FFFFFF", "FFwFFF", "$.terrain[4]: does not match")]
    [InlineData("ai/swarm.jsonc", "\"expand_at_materials\": 600", "\"expand_at\": 600", "$.economy: has an unknown key expand_at")]
    [InlineData("ai/swarm.jsonc", "{ \"epoch\": 1, \"workers\": 18 }", "{ \"epoch\": 0, \"workers\": 18 }", "$.economy.workers[0].epoch: is below 1")]
    [InlineData("ai/swarm.jsonc", "\"manufacturing\": 6", "\"manufacturing\": \"6\"", "$.research.weights.manufacturing: is not of type integer")]
    [InlineData("ai/elite.jsonc", "\"firepower\": 6", "\"Firepower\": 6", "$.research.weights.Firepower (key): does not match")]
    [InlineData("ai/elite.jsonc", "{ \"line\": \"artillery\", \"share\": 1 }", "{ \"line\": \"artillery\", \"share\": 0 }", "$.army.lines[1].share: is below 1")]
    [InlineData("ai/elite.jsonc", "\"name\": \"Elite\",", "", "$: has no name")]
    [InlineData("ai/swarm.jsonc", "\"every_seconds\": 180", "\"every_seconds\": 180.5", "$.scouting.every_seconds: matches none of the allowed forms")]
    [InlineData("ai/swarm.jsonc", "\"army_count_ratio\": \"1.5\"", "\"army_count_ratio\": 1.5", "$.scouting.responses[0].sees[0].army_count_ratio: matches none of the allowed forms")]
    [InlineData("ai/swarm.jsonc", "{ \"track\": \"firepower\", \"tiers\": 1 }", "{ \"track\": \"firepower\" }", "$.scouting.responses[0].does.research: has fewer than 2 keys")]
    [InlineData("ai/swarm.jsonc", "{ \"track\": \"firepower\", \"tiers\": 1 }", "{ \"track\": \"firepower\", \"tiers\": 1, \"until_accent_level\": 1 }", "$.scouting.responses[0].does.research: has more than 2 keys")]
    [InlineData("ai/swarm.jsonc", "\"passage\": \"chokepoint\"", "\"passage\": \"ford\"", "$.scouting.responses[0].does.passage: is not one of the allowed values")]
    [InlineData("ai/elite.jsonc", "{ \"epochs_ahead\": 2 }", "{ }", "$.scouting.responses[3].sees[0]: has fewer than 1 keys")]
    [InlineData("ai/elite.jsonc", "{ \"catch_up_first\": true }", "{ \"catch_up\": true }", "$.scouting.responses[3].does: has an unknown key catch_up")]
    [InlineData("ai/elite.jsonc", "{ \"line\": \"artillery\", \"count\": 3 }", "{ \"line\": \"artillery\" }", "$.scouting.responses[1].sees[1].units: has no count")]
    public void ASchemaRejectsTheMistakesAnEditorShouldUnderline(string file, string oldText, string newText, string expectedError)
    {
        var text = File.ReadAllText(Path.Combine(Repository.DataFolder, file));
        Assert.Contains(oldText, text, StringComparison.Ordinal);
        var start = text.IndexOf(oldText, StringComparison.Ordinal);
        text = string.Concat(text.AsSpan(0, start), newText, text.AsSpan(start + oldText.Length));
        using var document = JsonDocument.Parse(text, Jsonc);

        var errors = Validate(file, document.RootElement);

        Assert.Contains(errors, error => error.StartsWith(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void EverySchemaFileBelongsToAKindOfDataFile()
    {
        var used = OneFileOfEachKind
            .Select(file => Path.GetFileName(SchemaPathFor(file)))
            .Order(StringComparer.Ordinal);
        var present = Directory.EnumerateFiles(Repository.SchemasFolder)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);

        Assert.Equal(used, present);
    }

    private static IReadOnlyList<string> Validate(string file, JsonElement instance)
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(SchemaPathFor(file)));
        return new JsonSchemaSubset(schema.RootElement).Validate(instance);
    }

    private static string SchemaPathFor(string file)
    {
        var name = file switch
        {
            "tracks.jsonc" => "tracks.schema.json",
            "rules.jsonc" => "rules.schema.json",
            "research.jsonc" => "research.schema.json",
            "resources.jsonc" => "resources.schema.json",
            "accents.jsonc" => "accents.schema.json",
            _ when file.StartsWith("lines/", StringComparison.Ordinal) => "line.schema.json",
            _ when file.StartsWith("structures/", StringComparison.Ordinal) => "structure.schema.json",
            _ when file.StartsWith("maps/", StringComparison.Ordinal) => "map.schema.json",
            _ when file.StartsWith("ai/", StringComparison.Ordinal) => "ai.schema.json",
            _ => throw new InvalidOperationException($"No schema is known for {file}."),
        };
        return Path.Combine(Repository.SchemasFolder, name);
    }
}
