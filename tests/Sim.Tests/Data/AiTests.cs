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

    /// <summary>A response table that uses every key a row can have.</summary>
    public const string Scouting = """
        {
            "every_seconds": 180,
            "responses": [
              {
                "sees": [
                  { "accents": [ { "track": "manufacturing", "level": 1 }, { "track": "logistics", "level": 2 } ], "army_count_ratio": "1.5" },
                  { "units": { "line": "guard", "count": 3 }, "epochs_ahead": 2 }
                ],
                "does": {
                  "army_supply": [ { "line": "guard", "share": "0.3" }, { "line": "air", "share": 1 } ],
                  "research": { "track": "firepower", "tiers": 1 },
                  "passage": "chokepoint",
                  "catch_up_first": true
                }
              },
              {
                "sees": [ { "accents": [ { "track": "protection", "level": 2 } ], "on_main_line": true } ],
                "does": { "research": { "track": "protection", "until_accent_level": 1 } }
              },
              {
                "sees": [ { "epochs_ahead": 1 } ],
                "does": { "research": { "track": "firepower", "up_to_enemy": "protection" } }
              }
            ]
          }
        """;

    private const string FightStart = "\"fight\": {";

    public static InMemoryDataFiles Files() => SampleData.Files().Set(RaiderPath, Raider);

    /// <summary>The sample personality with a <c>scouting</c> block before its <c>fight</c> block.</summary>
    public static InMemoryDataFiles WithScouting(string scouting = Scouting)
    {
        Assert.Contains(FightStart, Raider, StringComparison.Ordinal);
        return With(FightStart, "\"scouting\": " + scouting + ", " + FightStart);
    }

    public static InMemoryDataFiles WithScouting(string oldText, string newText)
    {
        Assert.Contains(oldText, Scouting, StringComparison.Ordinal);
        return WithScouting(Scouting.Replace(oldText, newText, StringComparison.Ordinal));
    }

    public static InMemoryDataFiles With(string oldText, string newText) => Files().Edit(RaiderPath, oldText, newText);
}

/// <summary>What the loader reads from <c>ai/&lt;id&gt;.jsonc</c>, and what it rejects there.</summary>
public class AiTests
{
    private const int Guard = 2;
    private const int CavalryArmor = 1;
    private const int Air = 0;

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

    [Fact]
    public void WithoutAScoutingBlockItSendsNoScoutAndHasNoResponse()
    {
        var scouting = DataAssert.Valid(SamplePersonality.Files()).PersonalityOf("raider")!.Scouting;

        Assert.Same(AiScoutingDef.None, scouting);
        Assert.False(scouting.SendsScouts);
        Assert.Empty(scouting.Responses);
    }

    [Fact]
    public void TheResponseTableIsReadWithEveryValueOfItsFile()
    {
        var data = DataAssert.Valid(SamplePersonality.WithScouting());
        var scouting = data.PersonalityOf("raider")!.Scouting;
        int Track(string id) => data.Tracks.ToList().FindIndex(track => track.Id == id);

        Assert.True(scouting.SendsScouts);
        Assert.Equal(180 * SimInfo.TicksPerSecond, scouting.EveryTicks);
        Assert.Equal(3, scouting.Responses.Count);

        // The first row: either of two things seen, and every kind of thing done.
        var first = scouting.Responses[0];
        Assert.Equal(2, first.Sees.Count);
        Assert.Equal([new AiAccent(Track("manufacturing"), 1), new AiAccent(Track("logistics"), 2)], first.Sees[0].Accents);
        Assert.False(first.Sees[0].OnMainLine);
        Assert.Equal(0, first.Sees[0].Units);
        Assert.Equal((15, 1), (first.Sees[0].ArmyCountRatio!.Value.Unscaled, first.Sees[0].ArmyCountRatio!.Value.Scale));
        Assert.Equal(0, first.Sees[0].EpochsAhead);
        Assert.Empty(first.Sees[1].Accents);
        Assert.Equal((Guard, 3), (first.Sees[1].Line, first.Sees[1].Units));
        Assert.Null(first.Sees[1].ArmyCountRatio);
        Assert.Equal(2, first.Sees[1].EpochsAhead);
        Assert.Equal([Guard, Air], first.Does.ArmySupply.Select(share => share.Line));
        Assert.Equal([(3L, 1), (1L, 0)], first.Does.ArmySupply.Select(share => (share.Share.Unscaled, share.Share.Scale)));
        Assert.Equal(new AiResearchResponse(Track("firepower"), AiResearchUntil.Tiers, 1), first.Does.Research);
        Assert.Equal(MarkerKind.Chokepoint, first.Does.Passage);
        Assert.True(first.Does.CatchUpFirst);

        // The other two: the other ends of a research, and everything else left out.
        var second = scouting.Responses[1];
        Assert.True(Assert.Single(second.Sees).OnMainLine);
        Assert.Equal(new AiResearchResponse(Track("protection"), AiResearchUntil.AccentLevel, 1), second.Does.Research);
        Assert.Empty(second.Does.ArmySupply);
        Assert.Null(second.Does.Passage);
        Assert.False(second.Does.CatchUpFirst);
        Assert.Equal(new AiResearchResponse(Track("firepower"), AiResearchUntil.EnemyTier, Track("protection")), scouting.Responses[2].Does.Research);
    }

    [Fact]
    public void TheResponseTableIsPartOfTheContentHash()
    {
        var plain = DataAssert.Valid(SamplePersonality.WithScouting()).ContentHash;

        Assert.NotEqual(DataAssert.Valid(SamplePersonality.Files()).ContentHash, plain);
        Assert.NotEqual(DataAssert.Valid(SamplePersonality.WithScouting("\"army_count_ratio\": \"1.5\"", "\"army_count_ratio\": \"1.6\"")).ContentHash, plain);
    }

    [Theory]
    [InlineData("\"every_seconds\": 180", "\"every_seconds\": 0", "$.scouting.every_seconds", "must be above 0 and at most 3600, found 0")]
    [InlineData("\"every_seconds\": 180,", "", "$.scouting", "missing key \"every_seconds\"")]
    [InlineData("\"track\": \"manufacturing\"", "\"track\": \"speed\"", "$.scouting.responses[0].sees[0].accents[0].track", "unknown track 'speed'; tracks.jsonc has: firepower, protection, mobility, manufacturing, logistics, extraction")]
    [InlineData("\"level\": 2 } ], \"army", "\"level\": 3 } ], \"army", "$.scouting.responses[0].sees[0].accents[1].level", "must be between 1 and 2, found 3")]
    [InlineData("\"army_count_ratio\": \"1.5\"", "\"army_count_ratio\": 0", "$.scouting.responses[0].sees[0].army_count_ratio", "must be above 0 and at most 100, found 0")]
    [InlineData("\"line\": \"guard\", \"count\": 3", "\"line\": \"navy\", \"count\": 3", "$.scouting.responses[0].sees[1].units.line", "unknown line 'navy'; the roster in rules.jsonc has: air, cavalry_armor, guard")]
    [InlineData("\"count\": 3", "\"count\": 0", "$.scouting.responses[0].sees[1].units.count", "must be between 1 and 200, found 0")]
    [InlineData("\"epochs_ahead\": 2", "\"epochs_ahead\": 5", "$.scouting.responses[0].sees[1].epochs_ahead", "must be between 1 and 4, found 5")]
    [InlineData("{ \"epochs_ahead\": 1 }", "{ }", "$.scouting.responses[2].sees[0]", "sees nothing; give at least one of \"accents\", \"units\", \"army_count_ratio\" and \"epochs_ahead\"")]
    [InlineData("[ { \"epochs_ahead\": 1 } ]", "[ ]", "$.scouting.responses[2].sees", "must have at least 1 entries, found 0")]
    [InlineData("{ \"epochs_ahead\": 1 }", "{ \"on_main_line\": true, \"epochs_ahead\": 1 }", "$.scouting.responses[2].sees[0].on_main_line", "says which units the accents are looked for on, so it needs \"accents\"")]
    [InlineData("{ \"epochs_ahead\": 1 }", "{ \"epochs_ahead\": 1, \"towers\": 2 }", "$.scouting.responses[2].sees[0].towers", "unknown key \"towers\"")]
    [InlineData("\"share\": \"0.3\"", "\"share\": \"1.3\"", "$.scouting.responses[0].does.army_supply[0].share", "must be above 0 and at most 1, found 1.3")]
    [InlineData("{ \"line\": \"air\", \"share\": 1 }", "{ \"line\": \"guard\", \"share\": 1 }", "$.scouting.responses[0].does.army_supply[1].line", "line 'guard' is listed twice")]
    [InlineData("\"track\": \"firepower\", \"tiers\": 1", "\"track\": \"firepower\"", "$.scouting.responses[0].does.research", "needs exactly one of \"tiers\", \"until_accent_level\" and \"up_to_enemy\" to say until when, found 0")]
    [InlineData("\"track\": \"firepower\", \"tiers\": 1", "\"track\": \"firepower\", \"tiers\": 1, \"until_accent_level\": 1", "$.scouting.responses[0].does.research", "needs exactly one of \"tiers\", \"until_accent_level\" and \"up_to_enemy\" to say until when, found 2")]
    [InlineData("\"tiers\": 1", "\"tiers\": 0", "$.scouting.responses[0].does.research.tiers", "must be between 1 and 4, found 0")]
    [InlineData("\"until_accent_level\": 1", "\"until_accent_level\": 3", "$.scouting.responses[1].does.research.until_accent_level", "must be between 1 and 2, found 3")]
    [InlineData("\"up_to_enemy\": \"protection\"", "\"up_to_enemy\": \"armor\"", "$.scouting.responses[2].does.research.up_to_enemy", "unknown track 'armor'; tracks.jsonc has: firepower, protection, mobility, manufacturing, logistics, extraction")]
    [InlineData("\"passage\": \"chokepoint\"", "\"passage\": \"bridge\"", "$.scouting.responses[0].does.passage", "unknown marker kind 'bridge'; use chokepoint or open_field")]
    [InlineData("\"catch_up_first\": true", "\"catch_up_first\": 1", "$.scouting.responses[0].does.catch_up_first", "expected true or false, found a number")]
    [InlineData("\"does\": { \"research\": { \"track\": \"firepower\", \"up_to_enemy\": \"protection\" } }", "\"does\": { }", "$.scouting.responses[2].does", "does nothing; give at least one of \"army_supply\", \"research\", \"passage\" and \"catch_up_first\"")]
    [InlineData("\"does\": { \"research\": { \"track\": \"firepower\", \"up_to_enemy\": \"protection\" } }", "\"did\": { }", "$.scouting.responses[2]", "missing key \"does\"")]
    public void AMistakeInTheResponseTableIsReportedWhereItIs(string oldText, string newText, string location, string message)
    {
        DataAssert.Rejected(SamplePersonality.WithScouting(oldText, newText), SamplePersonality.RaiderPath, location, message);
    }

    [Fact]
    public void ATableOfMoreRowsThanTheLimitIsRejected()
    {
        var row = "{ \"sees\": [ { \"epochs_ahead\": 1 } ], \"does\": { \"catch_up_first\": true } }";
        var rows = string.Join(", ", Enumerable.Repeat(row, 21));

        DataAssert.Rejected(
            SamplePersonality.WithScouting($"{{ \"every_seconds\": 180, \"responses\": [ {rows} ] }}"),
            SamplePersonality.RaiderPath,
            "$.scouting.responses",
            "must have between 0 and 20 entries, found 21");
        DataAssert.Valid(SamplePersonality.WithScouting("{ \"every_seconds\": 180, \"responses\": [ ] }"));
    }

    /// <summary>"More than 1.5 times its own count" and "to 30% of its supply" are worked out in whole numbers.</summary>
    [Theory]
    [InlineData(6, 4, false)]
    [InlineData(7, 4, true)]
    [InlineData(3, 2, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 0, false)]
    public void AnArmyOutnumbersItsOwnOnlyAboveTheRatioOfTheFile(int enemy, int own, bool outnumbered)
    {
        var sees = DataAssert.Valid(SamplePersonality.WithScouting()).PersonalityOf("raider")!.Scouting.Responses[0].Sees[0];

        Assert.Equal(outnumbered, sees.IsOutnumbered(enemy, own));
        Assert.False(DataAssert.Valid(SamplePersonality.WithScouting()).PersonalityOf("raider")!.Scouting.Responses[0].Sees[1].IsOutnumbered(enemy, own));
    }

    [Theory]
    [InlineData(2, 10, true)]
    [InlineData(3, 10, false)]
    [InlineData(4, 10, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, true)]
    public void ALineIsBelowItsShareOfTheSupplyUntilItReachesIt(int lineSupply, int armySupply, bool below)
    {
        var share = DataAssert.Valid(SamplePersonality.WithScouting()).PersonalityOf("raider")!.Scouting.Responses[0].Does.ArmySupply[0];

        Assert.Equal(below, share.IsBelow(lineSupply, armySupply));
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
        Assert.Same(AiScoutingDef.None, plain.Scouting);
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
