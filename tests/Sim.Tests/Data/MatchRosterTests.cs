using Sim.Data;

namespace Sim.Tests.Data;

/// <summary>
/// <c>match.lines</c> and <c>match.structures</c> in <c>rules.jsonc</c>: what a match is played
/// with (design 6). The loader requires a data file for each, and in it what a player needs to
/// train the line or place the structure before the epoch cap.
/// </summary>
public class MatchRosterTests
{
    private const string Match = "\"match\": { \"start_epoch\": 1, \"epoch_cap\": 4 }";
    private const string GuardWithEveryEpoch = "{ \"epoch\": 2, \"id\": \"legionary\" },\n    { \"epoch\": 3, \"id\": \"man_at_arms\" },";

    /// <summary>
    /// The sample data with its four structure files, a Guard chassis for each of the four epochs
    /// and a match played with what is given.
    /// </summary>
    private static InMemoryDataFiles PlayedWith(string lines, string structures) => SampleStructures.Files()
        .Edit(SampleData.GuardPath, "{ \"epoch\": 2, \"id\": \"legionary\" },", GuardWithEveryEpoch)
        .Edit(SampleData.RulesPath, Match, $"\"match\": {{ \"start_epoch\": 1, \"epoch_cap\": 4, \"lines\": {lines}, \"structures\": {structures} }}");

    private static InMemoryDataFiles Complete() => PlayedWith("[\"guard\"]", "[\"wall\", \"capitol\", \"barracks\"]");

    [Fact]
    public void RulesThatDoNotSayWhatAMatchIsPlayedWithRequireNoFileForIt()
    {
        var data = DataAssert.Valid(SampleData.Files());

        Assert.Equal(new MatchRules(1, 4), data.Rules.Match);
        Assert.Null(data.Rules.Match.Lines);
        Assert.Null(data.Rules.Match.Structures);
        Assert.Equal(["capitol"], data.Structures.Select(structure => structure.Id));
    }

    [Fact]
    public void WhatAMatchIsPlayedWithIsReadAsRosterIndexesInRisingOrder()
    {
        var data = DataAssert.Valid(Complete());

        Assert.Equal(["guard"], data.Rules.Match.Lines!.Select(line => data.Rules.Lines[line]));
        Assert.Equal(["barracks", "capitol", "wall"], data.Rules.Match.Structures!.Select(structure => data.Rules.Structures[structure]));
        Assert.Equal((1, 4), (data.Rules.Match.StartEpoch, data.Rules.Match.EpochCap));
    }

    [Fact]
    public void EitherListMayBeGivenWithoutTheOther()
    {
        var onlyLines = DataAssert.Valid(SampleData.Files()
            .Edit(SampleData.GuardPath, "{ \"epoch\": 2, \"id\": \"legionary\" },", GuardWithEveryEpoch)
            .Edit(SampleData.RulesPath, Match, "\"match\": { \"start_epoch\": 1, \"epoch_cap\": 4, \"lines\": [\"guard\"] }"));
        var onlyStructures = DataAssert.Valid(SampleData.With(SampleData.RulesPath, Match, "\"match\": { \"start_epoch\": 1, \"epoch_cap\": 4, \"structures\": [\"capitol\"] }"));

        Assert.Equal((1, null), (onlyLines.Rules.Match.Lines!.Count, onlyLines.Rules.Match.Structures));
        Assert.Equal((null, 1), (onlyStructures.Rules.Match.Lines, onlyStructures.Rules.Match.Structures!.Count));
    }

    [Fact]
    public void TheListsAreInTheContentHash()
    {
        var without = DataAssert.Valid(SampleData.Files());
        var with = DataAssert.Valid(SampleData.With(SampleData.RulesPath, Match, "\"match\": { \"start_epoch\": 1, \"epoch_cap\": 4, \"structures\": [\"capitol\"] }"));

        Assert.NotEqual(without.ContentHash, with.ContentHash);
    }

    [Theory]
    [InlineData("[\"guard\", \"pikes\"]", "[\"capitol\", \"barracks\"]", "$.match.lines[1]", "unknown line 'pikes'; the roster in rules.jsonc has: air, cavalry_armor, guard")]
    [InlineData("[\"guard\"]", "[\"capitol\", \"barracks\", \"keep\"]", "$.match.structures[2]", "unknown structure 'keep'; the roster in rules.jsonc has: barracks, capitol, tower, wall")]
    [InlineData("[\"guard\", \"guard\"]", "[\"capitol\", \"barracks\"]", "$.match.lines[1]", "line 'guard' is listed twice")]
    [InlineData("[\"guard\"]", "[\"capitol\", \"barracks\", \"capitol\"]", "$.match.structures[2]", "structure 'capitol' is listed twice")]
    [InlineData("[]", "[\"capitol\", \"barracks\"]", "$.match.lines", "at least 1")]
    [InlineData("\"guard\"", "[\"capitol\", \"barracks\"]", "$.match.lines", "expected a list")]
    [InlineData("[\"Guard\"]", "[\"capitol\", \"barracks\"]", "$.match.lines[0]", "lowercase")]
    public void AListThatIsNotOfTheRosterEachOnceIsRejected(string lines, string structures, string location, string message)
    {
        DataAssert.RejectedOnce(PlayedWith(lines, structures), SampleData.RulesPath, location, message);
    }

    [Fact]
    public void TheStructuresMustListTheOneEveryPlayerStartsWith()
    {
        var files = PlayedWith("[\"guard\"]", "[\"barracks\", \"wall\"]");

        DataAssert.RejectedOnce(files, SampleData.RulesPath, "$.match.structures", "must list 'capitol': every player starts with it");
    }

    [Fact]
    public void ALineAMatchIsPlayedWithMustHaveItsFile()
    {
        var files = PlayedWith("[\"guard\", \"cavalry_armor\"]", "[\"capitol\", \"barracks\"]");

        DataAssert.RejectedOnce(
            files,
            "lines/cavalry_armor.jsonc",
            "",
            "this file is required and is missing: a match is played with line 'cavalry_armor' (\"match\" in rules.jsonc)");
    }

    [Theory]
    [InlineData("barracks")]
    [InlineData("wall")]
    public void AStructureAMatchIsPlayedWithMustHaveItsFile(string structure)
    {
        // Nothing is trained at the Barracks here, so that only the missing file is at fault.
        var files = PlayedWith("[\"guard\"]", "[\"wall\", \"capitol\", \"barracks\"]")
            .Edit(SampleData.RulesPath, "\"lines\": [\"guard\"], ", "");
        if (structure == "barracks")
        {
            files.Edit(SampleData.GuardPath, "\"built_at\": \"barracks\"", "\"built_at\": \"tower\"");
            files.Edit(SampleStructures.TowerPath, "\"sight\": 10,", "\"sight\": 10, \"trains\": [\"guard\"],");
        }

        files.Remove($"structures/{structure}.jsonc");

        DataAssert.RejectedOnce(
            files,
            $"structures/{structure}.jsonc",
            "",
            $"this file is required and is missing: a match is played with structure '{structure}' (\"match\" in rules.jsonc)");
    }

    [Fact]
    public void AStructureOfTheRosterThatAMatchIsNotPlayedWithMayStillHaveNoFile()
    {
        var files = Complete();
        files.Remove(SampleStructures.TowerPath);

        var data = DataAssert.Valid(files);

        Assert.Equal(["barracks", "capitol", "wall"], data.Structures.Select(structure => structure.Id));
    }

    [Fact]
    public void AMissingStartingStructureIsReportedOnce()
    {
        var files = Complete();
        files.Remove(SampleData.CapitolPath);

        var result = DataLoader.Load(files);

        Assert.Single(result.Errors, error => error.File == SampleData.CapitolPath);
    }

    [Fact]
    public void AFileThatIsThereAndCannotBeReadIsNotAlsoReportedMissing()
    {
        var files = Complete().Edit(SampleStructures.WallPath, "\"hp\": 400", "\"hp\": 0");

        DataAssert.RejectedOnce(files, SampleStructures.WallPath, "$.hp", "must be above 0");
    }

    [Theory]
    [InlineData("{ \"epoch\": 1, \"id\": \"hoplite\" },", 1)]
    [InlineData("{ \"epoch\": 3, \"id\": \"man_at_arms\" },", 3)]
    [InlineData(",\n    { \"epoch\": 4, \"id\": \"grenadier\", \"attack\": { \"kind\": \"ranged\", \"range\": 3, \"projectile_speed\": \"20\" } }", 4)]
    public void ALineAMatchIsPlayedWithNeedsAChassisForEveryEpochUpToTheCap(string chassis, int epoch)
    {
        var files = Complete().Edit(SampleData.GuardPath, chassis, "");

        DataAssert.RejectedOnce(
            files,
            SampleData.GuardPath,
            "$.chassis",
            $"a match is played with this line up to epoch 4 (\"match\" in rules.jsonc), so it needs a chassis for epoch {epoch}");
    }

    [Fact]
    public void ALowerCapAsksForFewerChassis()
    {
        var files = Complete()
            .Edit(SampleData.GuardPath, "\n    { \"epoch\": 3, \"id\": \"man_at_arms\" },", "")
            .Edit(SampleData.RulesPath, "\"epoch_cap\": 4", "\"epoch_cap\": 2");

        DataAssert.Valid(files);
    }

    [Fact]
    public void ALineThatAMatchIsNotPlayedWithMayLackAChassis()
    {
        var files = PlayedWith("[\"guard\"]", "[\"capitol\", \"barracks\"]")
            .Edit(SampleData.GuardPath, "\n    { \"epoch\": 3, \"id\": \"man_at_arms\" },", "")
            .Edit(SampleData.RulesPath, "\"lines\": [\"guard\"], ", "");

        DataAssert.Valid(files);
    }

    [Fact]
    public void ALineAMatchIsPlayedWithMustBeBuiltAtAStructureTheMatchIsPlayedWith()
    {
        var files = PlayedWith("[\"guard\"]", "[\"capitol\", \"wall\"]");

        DataAssert.RejectedOnce(
            files,
            SampleData.GuardPath,
            "$.built_at",
            "a match is played with this line and not with structure 'barracks' (\"match\" in rules.jsonc), so nothing could train it");
    }

    [Fact]
    public void AStructureAMatchIsPlayedWithMustUnlockNoLaterThanTheCap()
    {
        var files = PlayedWith("[\"guard\"]", "[\"capitol\", \"barracks\", \"tower\"]")
            .Edit(SampleStructures.TowerPath, "\"sight\": 10,", "\"sight\": 10, \"unlock_tier\": 4,")
            .Edit(SampleData.RulesPath, "\"epoch_cap\": 4", "\"epoch_cap\": 3");

        DataAssert.RejectedOnce(
            files,
            SampleStructures.TowerPath,
            "$.unlock_tier",
            "a match is played with this structure and no track passes tier 3 (\"match\" in rules.jsonc), so it could never be placed");
    }

    [Fact]
    public void AStructureThatUnlocksAtTheCapIsAccepted()
    {
        var files = PlayedWith("[\"guard\"]", "[\"capitol\", \"barracks\", \"tower\"]")
            .Edit(SampleStructures.TowerPath, "\"sight\": 10,", "\"sight\": 10, \"unlock_tier\": 4,");

        DataAssert.Valid(files);
    }
}
