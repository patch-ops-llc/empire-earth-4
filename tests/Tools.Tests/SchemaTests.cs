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

    private static readonly string[] OneFileOfEachKind = ["tracks.jsonc", "rules.jsonc", "research.jsonc", "accents.jsonc", "lines/x.jsonc", "maps/x.jsonc"];

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
    [InlineData("research.jsonc", "\"seconds\": 45", "\"seconds\": 45.5", "$.tiers[0].seconds: matches none of the allowed forms")]
    [InlineData("research.jsonc", "\"tier\": 2", "\"tier\": 1", "$.tiers[0].tier: is below 2")]
    [InlineData("research.jsonc", "\"materials\": 100,", "\"material\": 100,", "$.tiers[0].cost: has an unknown key material")]
    [InlineData("research.jsonc", "\"energy\": 2800", "\"energy\": -2800", "$.tiers[7].cost.energy: is below 0")]
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
    [InlineData("maps/ridge.jsonc", "\"symmetry\": \"mirror_x\"", "\"symmetry\": \"rotate_180\"", "$.symmetry: is not one of the allowed values")]
    [InlineData("maps/ridge.jsonc", "{ \"x\": 82, \"y\": 46 }", "{ \"x\": 82, \"y\": 46 }, { \"x\": 40, \"y\": 46 }", "$.starts: has more than 2 items")]
    [InlineData("maps/ridge.jsonc", "\"across\": \"y\" },", "\"across\": \"z\" },", "$.markers[0].across: is not one of the allowed values")]
    [InlineData("maps/ridge.jsonc", "FFFFFF", "FFwFFF", "$.terrain[4]: does not match")]
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
            "accents.jsonc" => "accents.schema.json",
            _ when file.StartsWith("lines/", StringComparison.Ordinal) => "line.schema.json",
            _ when file.StartsWith("maps/", StringComparison.Ordinal) => "map.schema.json",
            _ => throw new InvalidOperationException($"No schema is known for {file}."),
        };
        return Path.Combine(Repository.SchemasFolder, name);
    }
}
