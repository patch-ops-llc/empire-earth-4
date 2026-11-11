using System.Text;
using Sim.Data;

namespace Sim.Tests.Data;

/// <summary>What the loader reads from valid data, and how it treats files and JSON in general.</summary>
public class DataLoaderTests
{
    [Fact]
    public void LoadsTheSampleData()
    {
        var data = DataAssert.Valid(SampleData.Files());

        Assert.Equal(4, data.TierCount);
        Assert.Equal(["First", "Second", "Third", "Fourth"], data.EpochNames);
        Assert.Equal(64, data.ContentHash.Length);
        Assert.Single(data.Lines);
        Assert.Single(data.Maps);
    }

    [Fact]
    public void TracksKeepTheOrderOfTheFile()
    {
        var data = DataAssert.Valid(SampleData.Files());

        Assert.Equal(["firepower", "protection", "mobility", "manufacturing", "logistics", "extraction"], data.Tracks.Select(track => track.Id));
        var logistics = data.Tracks[4];
        Assert.Equal(("Logistics", "LG", TrackGroup.More), (logistics.Name, logistics.ShortName, logistics.Group));
        Assert.Equal(["L1", "L2", "L3", "L4"], logistics.TierNames);
    }

    [Fact]
    public void RulesKeepEveryNumberExactly()
    {
        var rules = DataAssert.Valid(SampleData.Files()).Rules;

        Assert.Equal(new EpochRules(3, 2), rules.Epoch);
        Assert.Equal(new PriceMultiplier(Number("0.5"), Number("0.5")), rules.ResearchPrice.CatchUp);
        Assert.Equal(new PriceMultiplier(Number("1"), Number("1")), rules.ResearchPrice.Frontier);
        Assert.Equal(new PriceMultiplier(Number("1.5"), Number("1")), rules.ResearchPrice.Ahead);
        Assert.Equal(2, rules.EffectiveTierWindow);
        Assert.Equal(new PerTierRules(Number("1.25"), Number("1.25"), Number("1.08"), Number("1.12")), rules.PerTier);
        Assert.Equal(new PerStepRules(Number("1.15"), Number("0.9"), Number("0.8")), rules.PerStep);
        Assert.Equal(2, rules.AccentSlots);
        Assert.Equal(new SupplyCapRules(40, 15), rules.SupplyCap);
        Assert.Equal(20, rules.WallCrossingCost);
        Assert.Equal(new MatchRules(1, 4), rules.Match);
        Assert.Equal(new StartRules(6, 400, 100), rules.Start);
        Assert.Equal(new MapRules(2, 6, 2, 1, 1, 2, 2, 4, 6), rules.Map);
    }

    [Fact]
    public void RosterIdsAreSortedSoThatAnIndexIsADenseId()
    {
        var rules = DataAssert.Valid(SampleData.Files()).Rules;

        Assert.Equal(["air", "cavalry_armor", "guard"], rules.Lines);
        Assert.Equal(["barracks", "capitol"], rules.Structures);
    }

    [Fact]
    public void LineReferencesBecomeIndexesAndSecondsBecomeTicks()
    {
        var guard = DataAssert.Valid(SampleData.Files()).Lines[0];

        Assert.Equal(("guard", 2), (guard.Id, guard.Index));
        Assert.Equal((1, 0, 0), (guard.PrimaryTrack, guard.SecondaryTrack, guard.BuiltAt));
        Assert.Equal(new LineProfile(Number("130"), Number("8"), Number("2.6"), 8, 2, Number("1"), 70, 10, 360), guard.Base);
        Assert.Equal(new AttackDef(AttackKind.Melee, 20, null, null), guard.Attack);
        Assert.Equal([new LineCounter(1, Number("1.5"))], guard.Counters);
        Assert.Equal(Number("0.5"), guard.StructureCounter);
        Assert.Equal([0], guard.CannotHit);
    }

    [Fact]
    public void AChassisOverridesOnlyTheAttackKeysItNames()
    {
        var guard = DataAssert.Valid(SampleData.Files()).Lines[0];

        Assert.Equal(["hoplite", "legionary", "grenadier"], guard.Chassis.Select(chassis => chassis.Id));
        Assert.Equal([1, 2, 4], guard.Chassis.Select(chassis => chassis.Epoch));
        Assert.Equal(guard.Attack, guard.Chassis[0].Attack);
        Assert.Equal(new AttackDef(AttackKind.Ranged, 20, Number("3"), Number("20")), guard.Chassis[2].Attack);
    }

    [Fact]
    public void AnOverrideThatKeepsTheKindKeepsTheRestOfTheAttack()
    {
        var files = SampleData.With(
            SampleData.GuardPath,
            """
            "attack": { "kind": "melee", "interval_seconds": "1" },
            """,
            """
            "attack": { "kind": "ranged", "interval_seconds": "1", "range": 5, "projectile_speed": 20 },
            """);
        files.Edit(SampleData.GuardPath, """{ "epoch": 2, "id": "legionary" }""", """{ "epoch": 2, "id": "legionary", "attack": { "range": "5.5", "interval_seconds": "1.5" } }""");
        files.Edit(SampleData.GuardPath, """{ "kind": "ranged", "range": 3, "projectile_speed": "20" }""", """{ "kind": "melee" }""");

        var guard = DataAssert.Valid(files).Lines[0];

        Assert.Equal(new AttackDef(AttackKind.Ranged, 30, Number("5.5"), Number("20")), guard.Chassis[1].Attack);
        Assert.Equal(new AttackDef(AttackKind.Melee, 20, null, null), guard.Chassis[2].Attack);
    }

    [Fact]
    public void MapKeepsTerrainAndObjects()
    {
        var map = DataAssert.Valid(SampleData.WithMap(map => map.SetBoth(0, 15, 'F'))).Maps[0];

        Assert.Equal(("arena", 40, 16, MapSymmetry.MirrorX), (map.Id, map.Width, map.Height, map.Symmetry));
        Assert.Equal(40 * 16, map.Tiles.Count);
        Assert.Equal(TerrainKind.Open, map.TerrainAt(0, 0));
        Assert.Equal(TerrainKind.Cliff, map.TerrainAt(19, 0));
        Assert.Equal(TerrainKind.Open, map.TerrainAt(19, 2));
        Assert.Equal(TerrainKind.Forest, map.TerrainAt(39, 15));
        Assert.Equal([new MapStart(4, 7), new MapStart(34, 7)], map.Starts);
        Assert.Equal(6, map.Deposits.Count);
        Assert.Equal(new MapDeposit(DepositKind.Natural, 12, 12), map.Deposits[2]);
        Assert.Equal(new MapMarker(MarkerKind.Chokepoint, new TileRect(19, 2, 2, 3), MapAxis.Y), map.Markers[0]);
        Assert.Equal(3, map.Markers[0].Width);
        Assert.Equal(6, map.Markers[1].Width);
    }

    [Fact]
    public void ASchemaKeyAndAByteOrderMarkAreAccepted()
    {
        var files = SampleData.With(SampleData.GuardPath, "\"id\": \"guard\",", "\"$schema\": \"../../../schemas/line.schema.json\", \"id\": \"guard\",");
        var tracks = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(SampleData.Tracks)).ToArray();
        files.Set(SampleData.TracksPath, tracks);

        DataAssert.Valid(files);
    }

    [Theory]
    [InlineData(SampleData.TracksPath, "tracks.jsonc")]
    [InlineData(SampleData.RulesPath, "rules.jsonc")]
    [InlineData(SampleData.ResearchPath, "research.jsonc")]
    [InlineData(SampleData.AccentsPath, "accents.jsonc")]
    [InlineData(SampleData.GuardPath, "lines/")]
    [InlineData(SampleData.MapPath, "maps/")]
    public void EveryKindOfFileIsRequired(string removed, string reportedAs)
    {
        var files = SampleData.Files();
        files.Remove(removed);

        DataAssert.Rejected(files, reportedAs, "", removed.Contains('/', StringComparison.Ordinal) ? "needs at least one" : "missing");
    }

    [Theory]
    [InlineData("rule.jsonc")]
    [InlineData("tracks.json")]
    [InlineData("notes.txt")]
    [InlineData("lines/old/guard.jsonc")]
    [InlineData("structures/capitol.jsonc")]
    public void AFileTheLoaderDoesNotKnowIsAnError(string path)
    {
        var files = SampleData.Files().Set(path, "{}");

        DataAssert.RejectedOnce(files, path, "", "unexpected file");
    }

    [Theory]
    [InlineData("lines/Guard.jsonc")]
    [InlineData("maps/my map.jsonc")]
    [InlineData("lines/2nd.jsonc")]
    public void AFileNameMustBeAnId(string path)
    {
        var files = SampleData.Files().Set(path, "{}");

        DataAssert.RejectedOnce(files, path, "", "name the file after its ID");
    }

    [Fact]
    public void ASyntaxErrorIsReportedWithItsLine()
    {
        var files = SampleData.With(SampleData.GuardPath, "\"built_at\": \"barracks\",", "\"built_at\": \"barracks\"");

        DataAssert.RejectedOnce(files, SampleData.GuardPath, "line 6, column 3", "not valid JSON");
    }

    [Fact]
    public void ATrailingCommaIsASyntaxError()
    {
        var files = SampleData.With(SampleData.GuardPath, "\"cannot_hit\": [\"air\"],", "\"cannot_hit\": [\"air\",],");

        DataAssert.RejectedOnce(files, SampleData.GuardPath, "line 13, column 24", "not valid JSON");
    }

    [Fact]
    public void AKeyGivenTwiceIsAnError()
    {
        var files = SampleData.With(SampleData.GuardPath, "\"sight\": 8,", "\"sight\": 8, \"sight\": 9,");

        var result = DataLoader.Load(files);

        var error = Assert.Single(result.Errors);
        Assert.Equal(SampleData.GuardPath, error.File);
        Assert.Contains("not valid JSON", error.Message, StringComparison.Ordinal);
        Assert.Contains("sight", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownKeyIsAnError()
    {
        var files = SampleData.With(SampleData.GuardPath, "\"sight\": 8,", "\"sight\": 8, \"sihgt\": 9,");

        DataAssert.RejectedOnce(files, SampleData.GuardPath, "$.base.sihgt", "unknown key \"sihgt\"");
    }

    [Fact]
    public void AMissingKeyIsAnError()
    {
        var files = SampleData.With(SampleData.GuardPath, "\"sight\": 8,", "");

        DataAssert.RejectedOnce(files, SampleData.GuardPath, "$.base", "missing key \"sight\"");
    }

    [Fact]
    public void AValueOfTheWrongShapeIsReportedOnceAndNotForEachKeyInside()
    {
        var files = SampleData.With(SampleData.GuardPath, "\"cost\": { \"materials\": 70, \"energy\": 10 },", "\"cost\": 80,");

        DataAssert.RejectedOnce(files, SampleData.GuardPath, "$.base.cost", "expected an object, found a number");
    }

    [Theory]
    [InlineData("\"speed\": 2.6,", "write 2.6 as text")]
    [InlineData("\"speed\": 26e-1,", "write 26e-1 as text")]
    [InlineData("\"speed\": \"2,6\",", "is not a decimal number")]
    [InlineData("\"speed\": \"fast\",", "is not a decimal number")]
    [InlineData("\"speed\": \"2.6 \",", "is not a decimal number")]
    [InlineData("\"speed\": true,", "expected a number, found true or false")]
    public void AFractionMustBeWrittenAsPlainDecimalText(string replacement, string message)
    {
        var files = SampleData.With(SampleData.GuardPath, "\"speed\": \"2.6\",", replacement);

        DataAssert.RejectedOnce(files, SampleData.GuardPath, "$.base.speed", message);
    }

    [Theory]
    [InlineData("\"sight\": \"8\",", "expected a whole number, found text")]
    [InlineData("\"sight\": 8.5,", "expected a whole number, found 8.5")]
    [InlineData("\"sight\": 8.0,", "expected a whole number, found 8.0")]
    [InlineData("\"sight\": null,", "expected a whole number, found null")]
    public void ACountMustBeAWholeNumber(string replacement, string message)
    {
        var files = SampleData.With(SampleData.GuardPath, "\"sight\": 8,", replacement);

        DataAssert.RejectedOnce(files, SampleData.GuardPath, "$.base.sight", message);
    }

    [Fact]
    public void EveryErrorIsReportedInOneRunGroupedByFile()
    {
        var files = SampleData.With(SampleData.GuardPath, "\"sight\": 8,", "\"sight\": 0,");
        files.Edit(SampleData.GuardPath, "\"hp\": 130,", "\"hp\": 0,");
        files.Edit(SampleData.MapPath, "\"symmetry\": \"mirror_x\"", "\"symmetry\": \"rotate\"");
        files.Set("notes.txt", "x");

        var result = DataLoader.Load(files);

        Assert.Equal(
            [
                "lines/guard.jsonc: $.base.hp: must be above 0 and at most 1000000, found 0",
                "lines/guard.jsonc: $.base.sight: must be between 1 and 64, found 0",
                "maps/arena.jsonc: $.symmetry: unknown symmetry 'rotate'; use mirror_x or mirror_y",
                "notes.txt: unexpected file; the loader reads tracks.jsonc, rules.jsonc, research.jsonc, accents.jsonc, lines/<id>.jsonc and maps/<id>.jsonc",
            ],
            result.Errors.Select(error => error.ToString()));
    }

    [Fact]
    public void AMistakeInTracksIsNotRepeatedForEveryFileThatUsesTracks()
    {
        var files = SampleData.With(SampleData.TracksPath, "\"id\": \"protection\"", "\"id\": \"Protection\"");

        DataAssert.RejectedOnce(files, SampleData.TracksPath, "$.tracks[1].id", "is not an ID");
    }

    private static DataNumber Number(string text)
    {
        Assert.True(DataNumber.TryParse(text, out var number), text);
        return number;
    }
}
