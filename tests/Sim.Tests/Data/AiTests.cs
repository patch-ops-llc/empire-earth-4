using Sim.Data;

namespace Sim.Tests.Data;

/// <summary>
/// The sample data with a personality of the computer player added. The file is not part of
/// <see cref="SampleData.Files"/>, whose content hash is pinned.
/// </summary>
internal static class SamplePersonality
{
    public const string RaiderPath = "ai/raider.jsonc";

    public const string Raider = """
        {
          "id": "raider",
          "name": "Raider",
          "economy": {
            "workers": [
              { "epoch": 1, "workers": 12 },
              { "epoch": 3, "workers": 20 }
            ],
            "expand_at_materials": 600
          },
          "research": {
            "weights": { "firepower": 2, "protection": 1, "mobility": 6, "manufacturing": 3, "logistics": 4, "extraction": 5 }
          },
          "fight": {
            "attack_ratio": "1.25",
            "assumed_enemy_value": 300,
            "retreat_losses": "0.5",
            "reinforce_units": 4,
            "passage": "open_field",
            "raid": { "line": "cavalry_armor", "party": 3 },
            "reaction_seconds": "0.5"
          },
          "army": {
            "lines": [
              { "line": "cavalry_armor", "share": 3 },
              { "line": "guard", "share": 1 }
            ]
          }
        }
        """;

    public static InMemoryDataFiles Files() => SampleData.Files().Set(RaiderPath, Raider);

    public static InMemoryDataFiles With(string oldText, string newText) => Files().Edit(RaiderPath, oldText, newText);
}

/// <summary>What the loader reads from <c>ai/&lt;id&gt;.jsonc</c>, and what it rejects there.</summary>
public class AiTests
{
    private const int Guard = 2;
    private const int CavalryArmor = 1;

    [Fact]
    public void ADataSetNeedsNoPersonality()
    {
        var data = DataAssert.Valid(SampleData.Files());

        Assert.Empty(data.Personalities);
        Assert.Null(data.PersonalityOf("raider"));
    }

    [Fact]
    public void APersonalityIsReadWithEveryValueOfItsFile()
    {
        var data = DataAssert.Valid(SamplePersonality.Files());

        var raider = Assert.Single(data.Personalities);
        Assert.Same(raider, data.PersonalityOf("raider"));
        Assert.Equal(("raider", "Raider", 600), (raider.Id, raider.Name, raider.ExpandAtMaterials));
        Assert.Equal([new AiWorkerTarget(1, 12), new AiWorkerTarget(3, 20)], raider.WorkerTargets);

        // One weight per track, in the order of tracks.jsonc and not of the file.
        Assert.Equal(["firepower", "protection", "mobility", "manufacturing", "logistics", "extraction"], data.Tracks.Select(track => track.Id));
        Assert.Equal([2, 1, 6, 3, 4, 5], raider.TrackWeights);

        // The lines keep the order of the file and are named by their place in the roster.
        Assert.Equal([new AiLineShare(CavalryArmor, 3), new AiLineShare(Guard, 1)], raider.Lines);
    }

    [Fact]
    public void HowItFightsIsReadWithEveryValueOfItsFile()
    {
        var fight = DataAssert.Valid(SamplePersonality.Files()).PersonalityOf("raider")!.Fight;
        Assert.False(fight.IsNone);

        Assert.Equal((125, 2), (fight.AttackRatio.Unscaled, fight.AttackRatio.Scale));
        Assert.Equal(300, fight.AssumedEnemyValue);
        Assert.Equal((5, 1), (fight.RetreatLosses.Unscaled, fight.RetreatLosses.Scale));
        Assert.Equal(4, fight.ReinforceUnits);
        Assert.Equal(MarkerKind.OpenField, fight.Passage);
        Assert.Equal((CavalryArmor, 3), (fight.RaidLine, fight.RaidParty));
        Assert.Equal(SimInfo.TicksPerSecond / 2, fight.ReactionTicks);
    }

    /// <summary>
    /// Code that makes a personality may leave the fight block out, as code written before there
    /// was one does: it gets the block that means none, which no file can give.
    /// </summary>
    [Fact]
    public void APersonalityMadeWithoutAFightBlockHasTheOneThatMeansNone()
    {
        var plain = new AiPersonalityDef("plain", "Plain", [new AiWorkerTarget(1, 6)], 600, [1, 1, 1, 1, 1, 1], []);

        Assert.Same(AiFightDef.None, plain.Fight);
        Assert.True(plain.Fight.IsNone);
        Assert.Equal(0, AiFightDef.None.RaidParty);
        Assert.Equal(plain, new AiPersonalityDef("plain", "Plain", plain.WorkerTargets, 600, plain.TrackWeights, plain.Lines, AiFightDef.None));
    }

    /// <summary>Both rules are worked out in whole numbers, and reaching the number of the file is enough.</summary>
    [Theory]
    [InlineData(499, 400, false)]
    [InlineData(500, 400, true)]
    [InlineData(501, 400, true)]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, true)]
    public void AnArmyIsEnoughToAttackFromTheRatioOfTheFile(int own, int enemy, bool enough)
    {
        var fight = DataAssert.Valid(SamplePersonality.Files()).PersonalityOf("raider")!.Fight;

        Assert.Equal(enough, fight.IsEnoughToAttack(own, enemy));
    }

    [Theory]
    [InlineData(49, 100, false)]
    [InlineData(50, 100, true)]
    [InlineData(2, 5, false)]
    [InlineData(3, 5, true)]
    [InlineData(0, 0, false)]
    public void TooMuchIsLostFromTheShareOfTheFile(int lost, int whole, bool tooMuch)
    {
        var fight = DataAssert.Valid(SamplePersonality.Files()).PersonalityOf("raider")!.Fight;

        Assert.Equal(tooMuch, fight.IsTooMuchLost(lost, whole));
    }

    [Fact]
    public void TheWorkersOfAnEpochAreThoseOfTheLastEntryAtOrBelowIt()
    {
        var raider = DataAssert.Valid(SamplePersonality.Files()).PersonalityOf("raider")!;

        Assert.Equal([12, 12, 20, 20], Enumerable.Range(1, 4).Select(raider.WorkersAt));
        Assert.Equal(20, raider.MostWorkers);
    }

    [Fact]
    public void PersonalitiesAreSortedByIdAndTheirFilesArePartOfTheContentHash()
    {
        var files = SamplePersonality.Files()
            .Set("ai/defender.jsonc", SamplePersonality.Raider.Replace("\"id\": \"raider\"", "\"id\": \"defender\"", StringComparison.Ordinal));

        var data = DataAssert.Valid(files);

        Assert.Equal(["defender", "raider"], data.Personalities.Select(personality => personality.Id));
        Assert.NotEqual(DataAssert.Valid(SamplePersonality.Files()).ContentHash, data.ContentHash);
        Assert.NotEqual(DataAssert.Valid(SampleData.Files()).ContentHash, DataAssert.Valid(SamplePersonality.Files()).ContentHash);
    }

    [Fact]
    public void ALineOfTheRosterNeedsNoFileYet()
    {
        var data = DataAssert.Valid(SamplePersonality.With("\"line\": \"cavalry_armor\"", "\"line\": \"air\""));

        Assert.Null(data.LineOf(data.Personalities[0].Lines[0].Line));
    }

    [Theory]
    [InlineData("\"id\": \"raider\"", "\"id\": \"reaver\"", "$.id", "must be 'raider' to match the file name, found 'reaver'")]
    [InlineData("\"name\": \"Raider\",", "", "$", "missing key \"name\"")]
    [InlineData("\"name\": \"Raider\"", "\"name\": \" Raider\"", "$.name", "must not be empty or start or end with a space")]
    [InlineData("\"expand_at_materials\": 600", "\"expand_at_materials\": -1", "$.economy.expand_at_materials", "must be between 0 and 1000000, found -1")]
    [InlineData("\"expand_at_materials\": 600", "\"expand_at_materials\": 600, \"expand_at_energy\": 5", "$.economy.expand_at_energy", "unknown key \"expand_at_energy\"")]
    [InlineData("{ \"epoch\": 1, \"workers\": 12 }", "{ \"epoch\": 2, \"workers\": 12 }", "$.economy.workers[0].epoch", "its epoch must be 1, found 2")]
    [InlineData("{ \"epoch\": 3, \"workers\": 20 }", "{ \"epoch\": 1, \"workers\": 20 }", "$.economy.workers[1].epoch", "epochs must rise from one entry to the next, found 1 after 1")]
    [InlineData("{ \"epoch\": 3, \"workers\": 20 }", "{ \"epoch\": 5, \"workers\": 20 }", "$.economy.workers[1].epoch", "must be between 1 and 4, found 5")]
    [InlineData("{ \"epoch\": 3, \"workers\": 20 }", "{ \"epoch\": 3, \"workers\": 0 }", "$.economy.workers[1].workers", "must be between 1 and 200, found 0")]
    [InlineData("\"mobility\": 6", "\"mobility\": 0", "$.research.weights.mobility", "must be between 1 and 1000, found 0")]
    [InlineData("\"mobility\": 6", "\"mobility\": \"6\"", "$.research.weights.mobility", "expected a whole number, found text")]
    [InlineData("\"mobility\": 6, ", "", "$.research.weights", "needs a weight for every track, and has none for 'mobility'")]
    [InlineData("\"mobility\": 6", "\"speed\": 6", "$.research.weights.speed", "unknown track 'speed'; tracks.jsonc has: firepower, protection, mobility, manufacturing, logistics, extraction")]
    [InlineData("\"line\": \"guard\"", "\"line\": \"guards\"", "$.army.lines[1].line", "unknown line 'guards'; the roster in rules.jsonc has: air, cavalry_armor, guard")]
    [InlineData("\"line\": \"guard\"", "\"line\": \"cavalry_armor\"", "$.army.lines[1].line", "line 'cavalry_armor' is listed twice")]
    [InlineData("\"share\": 3", "\"share\": 0", "$.army.lines[0].share", "must be between 1 and 1000, found 0")]
    [InlineData("\"share\": 3", "\"part\": 3", "$.army.lines[0]", "missing key \"share\"")]
    [InlineData("\"attack_ratio\": \"1.25\"", "\"attack_ratio\": \"0.9\"", "$.fight.attack_ratio", "must be between 1 and 100, found 0.9")]
    [InlineData("\"attack_ratio\": \"1.25\"", "\"attack_ratio\": \"101\"", "$.fight.attack_ratio", "must be between 1 and 100, found 101")]
    [InlineData("\"assumed_enemy_value\": 300", "\"assumed_enemy_value\": -1", "$.fight.assumed_enemy_value", "must be between 0 and 1000000, found -1")]
    [InlineData("\"retreat_losses\": \"0.5\"", "\"retreat_losses\": \"1.5\"", "$.fight.retreat_losses", "must be above 0 and at most 1, found 1.5")]
    [InlineData("\"reinforce_units\": 4", "\"reinforce_units\": 0", "$.fight.reinforce_units", "must be between 1 and 200, found 0")]
    [InlineData("\"passage\": \"open_field\"", "\"passage\": \"bridge\"", "$.fight.passage", "unknown marker kind 'bridge'; use chokepoint or open_field")]
    [InlineData("\"passage\": \"open_field\",", "", "$.fight", "missing key \"passage\"")]
    [InlineData("\"line\": \"cavalry_armor\", \"party\": 3", "\"line\": \"navy\", \"party\": 3", "$.fight.raid.line", "unknown line 'navy'; the roster in rules.jsonc has: air, cavalry_armor, guard")]
    [InlineData("\"party\": 3", "\"party\": 201", "$.fight.raid.party", "must be between 1 and 200, found 201")]
    [InlineData("\"party\": 3", "\"party\": 3, \"every\": 2", "$.fight.raid.every", "unknown key \"every\"")]
    [InlineData("\"reaction_seconds\": \"0.5\"", "\"reaction_seconds\": \"0.51\"", "$.fight.reaction_seconds", "0.51 s is not a whole number of ticks; there are 20 ticks per second")]
    [InlineData("\"reaction_seconds\": \"0.5\"", "\"reaction_second\": \"0.5\"", "$.fight", "missing key \"reaction_seconds\"")]
    public void AMistakeInAPersonalityIsReportedWhereItIs(string oldText, string newText, string location, string message)
    {
        DataAssert.Rejected(SamplePersonality.With(oldText, newText), SamplePersonality.RaiderPath, location, message);
    }

    [Fact]
    public void AnUnknownTrackIsReportedOnceAndNotAgainAsAMissingWeight()
    {
        DataAssert.RejectedOnce(
            SamplePersonality.With("\"mobility\": 6", "\"speed\": 6"),
            SamplePersonality.RaiderPath,
            "$.research.weights.speed",
            "unknown track 'speed'");
    }

    [Fact]
    public void APersonalityNeedsAtLeastOneLineAndOneWorkerTarget()
    {
        var noLines = SamplePersonality.Files();
        noLines.Set(SamplePersonality.RaiderPath, SamplePersonality.Raider[..SamplePersonality.Raider.IndexOf("\"lines\": [", StringComparison.Ordinal)] + "\"lines\": [] } }");
        DataAssert.RejectedOnce(noLines, SamplePersonality.RaiderPath, "$.army.lines", "must have at least 1 entries, found 0");
    }

    [Fact]
    public void AFileInTheFolderMustBeNamedAfterAnId()
    {
        var files = SampleData.Files().Set("ai/Raider.jsonc", SamplePersonality.Raider);

        DataAssert.RejectedOnce(files, "ai/Raider.jsonc", "", "name the file after its ID");
    }
}
