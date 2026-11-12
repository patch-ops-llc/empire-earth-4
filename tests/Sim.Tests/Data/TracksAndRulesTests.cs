using Sim.Data;

namespace Sim.Tests.Data;

/// <summary>What the loader rejects in <c>tracks.jsonc</c> and <c>rules.jsonc</c>.</summary>
public class TracksAndRulesTests
{
    [Fact]
    public void ATrackMustNameOneTierPerEpoch()
    {
        var files = SampleData.With(SampleData.TracksPath, "[\"P1\", \"P2\", \"P3\", \"P4\"]", "[\"P1\", \"P2\", \"P3\"]");

        DataAssert.RejectedOnce(files, SampleData.TracksPath, "$.tracks[1].tiers", "must name 4 tiers, one per epoch, found 3");
    }

    [Fact]
    public void ATrackIdMustBeUnique()
    {
        var files = SampleData.With(SampleData.TracksPath, "\"id\": \"logistics\"", "\"id\": \"firepower\"");

        DataAssert.RejectedOnce(files, SampleData.TracksPath, "$.tracks[4].id", "track 'firepower' is defined twice");
    }

    [Fact]
    public void ATrackGroupMustBeKnown()
    {
        var files = SampleData.With(SampleData.TracksPath, "\"short\": \"LG\", \"group\": \"more\"", "\"short\": \"LG\", \"group\": \"bigger\"");

        DataAssert.RejectedOnce(files, SampleData.TracksPath, "$.tracks[4].group", "unknown group 'bigger'");
    }

    [Fact]
    public void ANameMustNotBeEmpty()
    {
        var files = SampleData.With(SampleData.TracksPath, "\"name\": \"Logistics\"", "\"name\": \"\"");

        DataAssert.RejectedOnce(files, SampleData.TracksPath, "$.tracks[4].name", "must not be empty");
    }

    [Fact]
    public void EveryTrackTheRulesNameMustBeDefined()
    {
        var files = SampleData.With(SampleData.TracksPath, "\"id\": \"mobility\"", "\"id\": \"movement\"");
        files.Edit(SampleData.TracksPath, "\"id\": \"extraction\"", "\"id\": \"mining\"");

        DataAssert.RejectedOnce(
            files,
            SampleData.TracksPath,
            "$.tracks",
            "the rules name the tracks firepower, protection, mobility, manufacturing, logistics, extraction, and each needs an entry with that ID; missing: mobility, extraction");
    }

    [Fact]
    public void TheRulesFindTheirTracksWhereverTheFileListsThem()
    {
        var standard = DataAssert.Valid(SampleData.Files());
        var files = SampleData.With(SampleData.TracksPath, "\"id\": \"firepower\", \"name\": \"Firepower\"", "\"id\": \"extraction\", \"name\": \"Firepower\"");
        files.Edit(SampleData.TracksPath, "\"id\": \"extraction\", \"name\": \"Extraction\"", "\"id\": \"firepower\", \"name\": \"Extraction\"");
        var swapped = DataAssert.Valid(files);

        Assert.Equal(new TrackRoles(0, 1, 2, 3, 4, 5), standard.TrackRoles);
        Assert.Equal(new TrackRoles(5, 1, 2, 3, 4, 0), swapped.TrackRoles);
    }

    [Theory]
    [InlineData("\"tracks_needed\": 3", "\"tracks_needed\": 7", "$.epoch.tracks_needed", "must be between 1 and 6, found 7")]
    [InlineData("\"tracks_needed\": 3", "\"tracks_needed\": 0", "$.epoch.tracks_needed", "must be between 1 and 6, found 0")]
    [InlineData("\"reach\": 2", "\"reach\": -1", "$.epoch.reach", "must be between 0 and 4, found -1")]
    [InlineData("\"effective_tier_window\": 2", "\"effective_tier_window\": 5", "$.effective_tier_window", "must be between 0 and 4, found 5")]
    [InlineData("\"accent_slots\": 2", "\"accent_slots\": 7", "$.accent_slots", "must be between 0 and 6, found 7")]
    [InlineData("\"epoch_cap\": 4", "\"epoch_cap\": 5", "$.match.epoch_cap", "must be between 1 and 4, found 5")]
    [InlineData("\"start_epoch\": 1", "\"start_epoch\": 0", "$.match.start_epoch", "must be between 1 and 4, found 0")]
    [InlineData("\"wall_crossing_cost\": 20", "\"wall_crossing_cost\": 0", "$.wall_crossing_cost", "must be between 1 and 1000, found 0")]
    [InlineData("\"workers\": 6", "\"workers\": -6", "$.start.workers", "must be between 0 and 1000, found -6")]
    public void CountsOfTracksAndTiersMustFitTheTracksFile(string oldText, string newText, string location, string message)
    {
        var files = SampleData.With(SampleData.RulesPath, oldText, newText);

        DataAssert.RejectedOnce(files, SampleData.RulesPath, location, message);
    }

    [Fact]
    public void TheEpochCapMustNotBeBelowTheStartEpoch()
    {
        var files = SampleData.With(SampleData.RulesPath, "\"start_epoch\": 1, \"epoch_cap\": 4", "\"start_epoch\": 3, \"epoch_cap\": 2");

        DataAssert.RejectedOnce(files, SampleData.RulesPath, "$.match.epoch_cap", "must not be below start_epoch (3), found 2");
    }

    [Theory]
    [InlineData("\"damage\": \"1.25\"", "\"damage\": \"0\"", "$.per_tier.damage", "must be above 0 and at most 100, found 0")]
    [InlineData("\"quality_cost\": \"1.15\"", "\"quality_cost\": \"-1.15\"", "$.per_step.quality_cost", "must be above 0 and at most 100, found -1.15")]
    [InlineData("\"cost\": \"1.5\"", "\"cost\": \"150\"", "$.research_price.ahead.cost", "must be above 0 and at most 100, found 150")]
    public void AMultiplierMustBeAboveZeroAndSane(string oldText, string newText, string location, string message)
    {
        var files = SampleData.With(SampleData.RulesPath, oldText, newText);

        DataAssert.RejectedOnce(files, SampleData.RulesPath, location, message);
    }

    [Fact]
    public void ARosterIdMustBeUnique()
    {
        var files = SampleData.With(SampleData.RulesPath, "[\"guard\", \"cavalry_armor\", \"air\"]", "[\"guard\", \"air\", \"guard\"]");

        DataAssert.RejectedOnce(files, SampleData.RulesPath, "$.roster.lines[2]", "line 'guard' is listed twice");
    }

    [Fact]
    public void ALineMayNotTakeTheNameThatMeansEveryStructure()
    {
        var files = SampleData.With(SampleData.RulesPath, "[\"guard\", \"cavalry_armor\", \"air\"]", "[\"guard\", \"cavalry_armor\", \"air\", \"structures\"]");

        DataAssert.RejectedOnce(files, SampleData.RulesPath, "$.roster.lines[3]", "'structures' is reserved");
    }

    [Theory]
    [InlineData("\"min\": 2, \"max\": 4", "\"min\": 5, \"max\": 4", "$.map.chokepoint_width.max", "must not be below min (5), found 4")]
    [InlineData("\"open_field_min_width\": 6", "\"open_field_min_width\": 4", "$.map.open_field_min_width", "must be above the widest chokepoint (4), found 4")]
    [InlineData("\"deposit_size\": 2", "\"deposit_size\": 0", "$.map.deposit_size", "must be between 1 and 8, found 0")]
    public void TheMapRulesMustMakeSense(string oldText, string newText, string location, string message)
    {
        var files = SampleData.With(SampleData.RulesPath, oldText, newText);

        DataAssert.RejectedOnce(files, SampleData.RulesPath, location, message);
    }

    [Fact]
    public void AMistakeInRulesIsNotRepeatedForEveryFileThatUsesRules()
    {
        var files = SampleData.With(SampleData.RulesPath, "\"structures\": [\"capitol\", \"barracks\"]", "\"structures\": []");

        DataAssert.RejectedOnce(files, SampleData.RulesPath, "$.roster.structures", "must have at least 1 entries, found 0");
    }

    [Theory]
    [InlineData("\"debug\": { \"spawn\": true }", "\"debug\": { \"spawn\": false }")]
    [InlineData("\"debug\": { \"spawn\": true }", "\"debug\":{\"spawn\":true}")]
    public void TheDevelopmentSpawnIsSwitchedOnOrOff(string oldText, string newText)
    {
        var data = DataAssert.Valid(SampleData.With(SampleData.RulesPath, oldText, newText));

        Assert.Equal(newText.Contains("true", StringComparison.Ordinal), data.Rules.Debug.Spawn);
    }

    [Theory]
    [InlineData("\"debug\": { \"spawn\": true }", "\"debug\": { \"spawn\": 1 }", "$.debug.spawn", "expected true or false, found a number")]
    [InlineData("\"debug\": { \"spawn\": true }", "\"debug\": { \"spawn\": \"true\" }", "$.debug.spawn", "expected true or false, found text")]
    [InlineData("\"debug\": { \"spawn\": true }", "\"debug\": { }", "$.debug", "missing key \"spawn\"")]
    [InlineData(",\n  \"debug\": { \"spawn\": true }", "", "$", "missing key \"debug\"")]
    [InlineData("\"debug\": { \"spawn\": true }", "\"debug\": { \"spawn\": true, \"god_mode\": true }", "$.debug.god_mode", "unknown key \"god_mode\"")]
    public void TheDebugBlockMustBeThereWithItsSwitchAndNothingElse(string oldText, string newText, string location, string message)
    {
        var files = SampleData.With(SampleData.RulesPath, oldText, newText);

        DataAssert.RejectedOnce(files, SampleData.RulesPath, location, message);
    }
}
