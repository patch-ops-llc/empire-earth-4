using Sim.Data;

namespace Sim.Tests.Data;

/// <summary>What the loader reads from <c>research.jsonc</c> and <c>accents.jsonc</c>, and what it rejects there.</summary>
public class ResearchAndAccentTests
{
    [Fact]
    public void ResearchHasOneEntryPerTierAboveTheFirstWithSecondsAsTicks()
    {
        var data = DataAssert.Valid(SampleData.Files());

        Assert.Equal(
            [new ResearchTier(2, 100, 100, 900), new ResearchTier(3, 150, 200, 1200), new ResearchTier(4, 225, 350, 1500)],
            data.Research);
        Assert.Equal(new ResearchTier(3, 150, 200, 1200), data.ResearchOf(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.ResearchOf(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.ResearchOf(5));
    }

    [Theory]
    [InlineData("\"tier\": 3,", "\"tier\": 4,", "$.tiers[1].tier", "list every tier from 2 to 4 in order; expected tier 3 here, found 4")]
    [InlineData("\"tier\": 2,", "\"tier\": 1,", "$.tiers[0].tier", "expected tier 2 here, found 1")]
    [InlineData("\"seconds\": 60 }", "\"seconds\": \"60.01\" }", "$.tiers[1].seconds", "60.01 s is not a whole number of ticks")]
    [InlineData("\"seconds\": 60 }", "\"seconds\": 0 }", "$.tiers[1].seconds", "must be above 0 and at most 3600, found 0")]
    [InlineData("\"energy\": 200", "\"energy\": -200", "$.tiers[1].cost.energy", "must be between 0 and 1000000, found -200")]
    [InlineData("\"materials\": 150,", "", "$.tiers[1].cost", "missing key \"materials\"")]
    [InlineData("\"seconds\": 60 }", "\"seconds\": 60, \"slots\": 1 }", "$.tiers[1].slots", "unknown key \"slots\"")]
    public void AResearchEntryMustBeInOrderAndSane(string oldText, string newText, string location, string message)
    {
        var files = SampleData.With(SampleData.ResearchPath, oldText, newText);

        DataAssert.RejectedOnce(files, SampleData.ResearchPath, location, message);
    }

    [Fact]
    public void EveryTierAboveTheFirstNeedsAResearchEntry()
    {
        var files = SampleData.With(
            SampleData.ResearchPath,
            """
            ,
                { "tier": 4, "cost": { "materials": 225, "energy": 350 }, "seconds": 75 }
            """,
            "");

        DataAssert.RejectedOnce(files, SampleData.ResearchPath, "$.tiers", "needs an entry for every tier from 2 to 4; tier 4 is missing");
    }

    [Fact]
    public void ThereIsNoResearchPastTheLastTier()
    {
        var files = SampleData.With(
            SampleData.ResearchPath,
            "\"seconds\": 75 }",
            "\"seconds\": 75 }, { \"tier\": 5, \"cost\": { \"materials\": 350, \"energy\": 550 }, \"seconds\": 90 }");

        DataAssert.RejectedOnce(
            files,
            SampleData.ResearchPath,
            "$.tiers[3].tier",
            "tracks have tiers 1 to 4, so the last entry is tier 4; found tier 5");
    }

    [Fact]
    public void AccentsAreInTrackOrderWhateverTheOrderOfTheFile()
    {
        var files = SampleData.With(SampleData.AccentsPath, "\"firepower\": {", "\"zz\": {");
        files.Edit(SampleData.AccentsPath, "\"logistics\": {", "\"firepower\": {");
        files.Edit(SampleData.AccentsPath, "\"zz\": {", "\"logistics\": {");

        var data = DataAssert.Valid(files);

        Assert.Equal([0, 1, 2, 3, 4], data.Accents.Select(accent => accent.Track));
        Assert.Equal(["Supplied", "Hardened", "Rapid", "Standardized", "Overgunned"], data.Accents.Select(accent => accent.Name));
    }

    [Fact]
    public void EveryPerkKeepsItsNumbersExactly()
    {
        var data = DataAssert.Valid(SampleData.Files());

        var firepower = data.AccentOf(data.TrackRoles.Firepower)!;
        Assert.Equal("Overgunned", firepower.Name);
        Assert.Equal(
            new PerkSet { RangeBonus = new(Number("1.15")), AttackRateBonus = new(Number("1.15")) },
            firepower.LevelOne);
        Assert.Equal(
            new PerkSet { Splash = new(Number("0.3"), Number("1")), BlastRadiusBonus = new(Number("1.5")) },
            firepower.LevelTwo);

        var protection = data.AccentOf(data.TrackRoles.Protection)!;
        Assert.Equal(new PerkSet { AreaDamageReduction = new(Number("0.75")) }, protection.LevelOne);
        Assert.Equal(new PerkSet { Plating = new(Number("0.7")) }, protection.LevelTwo);

        var mobility = data.AccentOf(data.TrackRoles.Mobility)!;
        Assert.Equal(new PerkSet { SightBonus = new(2) }, mobility.LevelOne);
        Assert.Equal(new PerkSet { Charge = new(Number("4"), Number("1.5")) }, mobility.LevelTwo);

        var manufacturing = data.AccentOf(data.TrackRoles.Manufacturing)!;
        Assert.Equal(new PerkSet { Batch = new(Number("1.5")) }, manufacturing.LevelOne);
        Assert.Equal(new PerkSet { DeathRefund = new(Number("0.25")) }, manufacturing.LevelTwo);

        var logistics = data.AccentOf(data.TrackRoles.Logistics)!;
        Assert.Equal(new PerkSet { Regeneration = new(Number("0.02"), 100) }, logistics.LevelOne);
        Assert.Equal(new PerkSet { SupplyReduction = new(1, 1) }, logistics.LevelTwo);

        Assert.Null(data.AccentOf(data.TrackRoles.Extraction));
    }

    [Fact]
    public void LevelTwoIncludesLevelOne()
    {
        var firepower = DataAssert.Valid(SampleData.Files()).Accents[0];

        Assert.Empty(firepower.PerksAt(0).Kinds);
        Assert.Equal([PerkKind.RangeBonus, PerkKind.AttackRateBonus], firepower.PerksAt(1).Kinds);
        Assert.Equal(
            [PerkKind.RangeBonus, PerkKind.AttackRateBonus, PerkKind.Splash, PerkKind.BlastRadiusBonus],
            firepower.PerksAt(2).Kinds);
        Assert.Equal(firepower.PerksAt(2), firepower.PerksAt(3));
    }

    [Fact]
    public void ATrackMayGiveNoAccentAndALevelMayGiveNoPerk()
    {
        var files = SampleData.With(
            SampleData.AccentsPath,
            """
            "level_1": { "sight_bonus": { "tiles": 2 } },
            """,
            """
            "level_1": {},
            """);
        files.Edit(
            SampleData.AccentsPath,
            """
            ,
                "logistics": {
                  "name": "Supplied",
                  "level_1": { "regeneration": { "share_per_second": "0.02", "delay_seconds": 5 } },
                  "level_2": { "supply_reduction": { "amount": 1, "minimum": 1 } }
                }
            """,
            "");

        var data = DataAssert.Valid(files);

        Assert.Equal(4, data.Accents.Count);
        Assert.Null(data.AccentOf(data.TrackRoles.Logistics));
        Assert.Equal(PerkSet.None, data.AccentOf(data.TrackRoles.Mobility)!.LevelOne);
    }

    [Theory]
    [InlineData("\"mobility\": {", "\"speed\": {", "$.accents.speed", "unknown track 'speed'; tracks.jsonc has: firepower, protection, mobility, manufacturing, logistics, extraction")]
    [InlineData("\"name\": \"Rapid\",", "", "$.accents.mobility", "missing key \"name\"")]
    [InlineData("\"name\": \"Rapid\",", "\"name\": \"\",", "$.accents.mobility.name", "must not be empty")]
    [InlineData("\"level_2\": { \"plating\": { \"multiplier\": \"0.7\" } }", "\"level_2\": []", "$.accents.protection.level_2", "expected an object, found a list")]
    [InlineData("\"splash\": {", "\"spalsh\": {", "$.accents.firepower.level_2.spalsh", "unknown key \"spalsh\"")]
    [InlineData("\"radius\": 1 }", "\"radius\": 1, \"falloff\": \"0.5\" }", "$.accents.firepower.level_2.splash.falloff", "unknown key \"falloff\"")]
    [InlineData("\"distance\": 4, ", "", "$.accents.mobility.level_2.charge", "missing key \"distance\"")]
    public void AnAccentMustHaveTheShapeTheLoaderKnows(string oldText, string newText, string location, string message)
    {
        var files = SampleData.With(SampleData.AccentsPath, oldText, newText);

        DataAssert.RejectedOnce(files, SampleData.AccentsPath, location, message);
    }

    [Theory]
    [InlineData("\"range_bonus\": { \"multiplier\": \"1.15\" }", "\"range_bonus\": { \"multiplier\": \"0.15\" }", "$.accents.firepower.level_1.range_bonus.multiplier", "must be between 1 and 100, found 0.15")]
    [InlineData("\"attack_rate_bonus\": { \"multiplier\": \"1.15\" }", "\"attack_rate_bonus\": { \"multiplier\": 115 }", "$.accents.firepower.level_1.attack_rate_bonus.multiplier", "must be between 1 and 100, found 115")]
    [InlineData("\"share\": \"0.3\"", "\"share\": \"30\"", "$.accents.firepower.level_2.splash.share", "must be above 0 and at most 1, found 30")]
    [InlineData("\"radius\": 1", "\"radius\": 0", "$.accents.firepower.level_2.splash.radius", "must be above 0 and at most 64, found 0")]
    [InlineData("\"blast_radius_bonus\": { \"multiplier\": \"1.5\" }", "\"blast_radius_bonus\": { \"multiplier\": \"0.5\" }", "$.accents.firepower.level_2.blast_radius_bonus.multiplier", "must be between 1 and 100, found 0.5")]
    [InlineData("\"multiplier\": \"0.75\"", "\"multiplier\": \"1.25\"", "$.accents.protection.level_1.area_damage_reduction.multiplier", "must be above 0 and at most 1, found 1.25")]
    [InlineData("\"multiplier\": \"0.7\"", "\"multiplier\": \"0\"", "$.accents.protection.level_2.plating.multiplier", "must be above 0 and at most 1, found 0")]
    [InlineData("\"tiles\": 2", "\"tiles\": 0", "$.accents.mobility.level_1.sight_bonus.tiles", "must be between 1 and 64, found 0")]
    [InlineData("\"tiles\": 2", "\"tiles\": \"2.5\"", "$.accents.mobility.level_1.sight_bonus.tiles", "expected a whole number, found text")]
    [InlineData("\"distance\": 4", "\"distance\": 400", "$.accents.mobility.level_2.charge.distance", "must be above 0 and at most 64, found 400")]
    [InlineData("\"distance\": 4, \"multiplier\": \"1.5\"", "\"distance\": 4, \"multiplier\": \"0.5\"", "$.accents.mobility.level_2.charge.multiplier", "must be between 1 and 100, found 0.5")]
    [InlineData("\"time_multiplier\": \"1.5\"", "\"time_multiplier\": \"0\"", "$.accents.manufacturing.level_1.batch.time_multiplier", "must be above 0 and at most 100, found 0")]
    [InlineData("\"share\": \"0.25\"", "\"share\": \"25\"", "$.accents.manufacturing.level_2.death_refund.share", "must be above 0 and at most 1, found 25")]
    [InlineData("\"share_per_second\": \"0.02\"", "\"share_per_second\": 2", "$.accents.logistics.level_1.regeneration.share_per_second", "must be above 0 and at most 1, found 2")]
    [InlineData("\"delay_seconds\": 5", "\"delay_seconds\": \"5.01\"", "$.accents.logistics.level_1.regeneration.delay_seconds", "5.01 s is not a whole number of ticks")]
    [InlineData("\"amount\": 1", "\"amount\": 0", "$.accents.logistics.level_2.supply_reduction.amount", "must be between 1 and 100, found 0")]
    [InlineData("\"minimum\": 1", "\"minimum\": 0", "$.accents.logistics.level_2.supply_reduction.minimum", "must be between 1 and 100, found 0")]
    public void APerkNumberMustBeInsideItsBounds(string oldText, string newText, string location, string message)
    {
        var files = SampleData.With(SampleData.AccentsPath, oldText, newText);

        DataAssert.RejectedOnce(files, SampleData.AccentsPath, location, message);
    }

    [Theory]
    [InlineData("\"level_2\": { \"plating\": { \"multiplier\": \"0.7\" } }", "\"level_2\": { \"range_bonus\": { \"multiplier\": \"1.3\" } }", "$.accents.protection.level_2.range_bonus", "\"range_bonus\" is already given at $.accents.firepower.level_1.range_bonus; a kind of perk may appear only once")]
    [InlineData("\"level_2\": { \"death_refund\": { \"share\": \"0.25\" } }", "\"level_2\": { \"batch\": { \"time_multiplier\": \"1.2\" } }", "$.accents.manufacturing.level_2.batch", "\"batch\" is already given at $.accents.manufacturing.level_1.batch")]
    public void AKindOfPerkMayAppearOnlyOnce(string oldText, string newText, string location, string message)
    {
        var files = SampleData.With(SampleData.AccentsPath, oldText, newText);

        DataAssert.RejectedOnce(files, SampleData.AccentsPath, location, message);
    }

    [Fact]
    public void AMistakeInTracksIsNotRepeatedInResearchAndAccents()
    {
        var files = SampleData.With(SampleData.TracksPath, "[\"First\", \"Second\", \"Third\", \"Fourth\"]", "[\"First\", \"Second\", \"Third\", \"\"]");

        DataAssert.RejectedOnce(files, SampleData.TracksPath, "$.epochs[3]", "must not be empty");
    }

    private static DataNumber Number(string text)
    {
        Assert.True(DataNumber.TryParse(text, out var number), text);
        return number;
    }
}
