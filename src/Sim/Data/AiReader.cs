namespace Sim.Data;

internal static class AiReader
{
    private const string Roster = "the roster in rules.jsonc";

    /// <summary>Reads one personality file.</summary>
    public static AiPersonalityDef Read(JsonValue root, string expectedId, IReadOnlyList<TrackDef> tracks, int tierCount, RuleSet rules)
    {
        var file = root.Object();
        file.Optional(DataRules.SchemaKey)?.String();

        var idValue = file.Required("id");
        var id = idValue.Id();
        if (id.Length > 0 && id != expectedId)
        {
            idValue.Error($"must be '{expectedId}' to match the file name, found '{id}'");
        }

        var name = file.Required("name").String();

        var economy = file.Required("economy").Object();
        var workerTargets = ReadWorkerTargets(economy.Required("workers"), tierCount);
        var expandAtMaterials = economy.Required("expand_at_materials").Integer(0, DataRules.MaxPrice);
        economy.End();

        var research = file.Required("research").Object();
        var weights = ReadWeights(research.Required("weights"), tracks);
        research.End();

        var army = file.Required("army").Object();
        var lines = ReadLines(army.Required("lines"), rules);
        army.End();

        var fight = ReadFight(file.Required("fight"), rules);
        var scouting = file.Optional("scouting") is { } scoutingValue ? ReadScouting(scoutingValue, tracks, tierCount, rules) : AiScoutingDef.None;

        file.End();
        return new AiPersonalityDef(id, name, workerTargets, expandAtMaterials, weights, lines, fight) { Scouting = scouting };
    }

    private static AiScoutingDef ReadScouting(JsonValue value, IReadOnlyList<TrackDef> tracks, int tierCount, RuleSet rules)
    {
        var trackIds = new List<string>(tracks.Count);
        foreach (var track in tracks)
        {
            trackIds.Add(track.Id);
        }

        var scouting = value.Object();
        var everyTicks = scouting.Required("every_seconds").SecondsAsTicks();
        var responses = new List<AiResponseDef>();
        foreach (var rowValue in scouting.Required("responses").Array(0, DataRules.MaxAiResponses))
        {
            var row = rowValue.Object();
            var sees = new List<AiSeesDef>();
            foreach (var seesValue in row.Required("sees").Array(1))
            {
                sees.Add(ReadSees(seesValue, trackIds, tierCount, rules));
            }

            var does = ReadDoes(row.Required("does"), trackIds, tierCount, rules);
            row.End();
            responses.Add(new AiResponseDef(sees, does));
        }

        scouting.End();
        return new AiScoutingDef(everyTicks, responses);
    }

    private static AiSeesDef ReadSees(JsonValue value, List<string> trackIds, int tierCount, RuleSet rules)
    {
        var sees = value.Object();
        var accents = new List<AiAccent>();
        if (sees.Optional("accents") is { } accentsValue)
        {
            foreach (var accentValue in accentsValue.Array(1))
            {
                var accent = accentValue.Object();
                var track = ReadTrack(accent.Required("track"), trackIds);
                var level = accent.Required("level").Integer(1, DataRules.AccentLevels);
                accent.End();
                accents.Add(new AiAccent(track, level));
            }
        }

        var onMainLine = false;
        if (sees.Optional("on_main_line") is { } onMainLineValue)
        {
            onMainLine = onMainLineValue.Boolean();
            if (accents.Count == 0)
            {
                onMainLineValue.Error("says which units the accents are looked for on, so it needs \"accents\"");
            }
        }

        var line = 0;
        var units = 0;
        if (sees.Optional("units") is { } unitsValue)
        {
            var entry = unitsValue.Object();
            var lineValue = entry.Required("line");
            line = lineValue.IndexIn(lineValue.Id(), rules.Lines, "line", Roster);
            units = entry.Required("count").Integer(1, DataRules.MaxAiUnits);
            entry.End();
        }

        DataNumber? ratio = sees.Optional("army_count_ratio") is { } ratioValue
            ? ratioValue.Number(DataRules.Zero, DataNumber.FromInteger(DataRules.MaxAiRatio), aboveMin: true)
            : null;
        var epochsAhead = sees.Optional("epochs_ahead")?.Integer(1, tierCount) ?? 0;
        sees.End();

        if (accents.Count == 0 && units == 0 && ratio is null && epochsAhead == 0)
        {
            value.Error("sees nothing; give at least one of \"accents\", \"units\", \"army_count_ratio\" and \"epochs_ahead\"");
        }

        return new AiSeesDef(accents, onMainLine, line, units, ratio, epochsAhead);
    }

    private static AiDoesDef ReadDoes(JsonValue value, List<string> trackIds, int tierCount, RuleSet rules)
    {
        var does = value.Object();
        var armySupply = new List<AiSupplyShare>();
        if (does.Optional("army_supply") is { } supplyValue)
        {
            foreach (var entryValue in supplyValue.Array(1))
            {
                var entry = entryValue.Object();
                var lineValue = entry.Required("line");
                var lineId = lineValue.Id();
                var line = lineValue.IndexIn(lineId, rules.Lines, "line", Roster);
                var share = entry.Required("share").Share();
                entry.End();
                if (lineId.Length > 0 && armySupply.Exists(listed => listed.Line == line))
                {
                    lineValue.Error($"line '{lineId}' is listed twice");
                }
                else
                {
                    armySupply.Add(new AiSupplyShare(line, share));
                }
            }
        }

        AiResearchResponse? research = does.Optional("research") is { } researchValue ? ReadResearch(researchValue, trackIds, tierCount) : null;
        MarkerKind? passage = does.Optional("passage") is { } passageValue ? MapReader.ReadMarkerKind(passageValue) : null;
        var catchUpFirst = does.Optional("catch_up_first")?.Boolean() ?? false;
        does.End();

        if (armySupply.Count == 0 && research is null && passage is null && !catchUpFirst)
        {
            value.Error("does nothing; give at least one of \"army_supply\", \"research\", \"passage\" and \"catch_up_first\"");
        }

        return new AiDoesDef(armySupply, research, passage, catchUpFirst);
    }

    private static AiResearchResponse ReadResearch(JsonValue value, List<string> trackIds, int tierCount)
    {
        var research = value.Object();
        var track = ReadTrack(research.Required("track"), trackIds);
        var ends = 0;
        var until = AiResearchUntil.Tiers;
        var amount = 1;
        if (research.Optional("tiers") is { } tiersValue)
        {
            ends++;
            amount = tiersValue.Integer(1, tierCount);
        }

        if (research.Optional("until_accent_level") is { } levelValue)
        {
            ends++;
            until = AiResearchUntil.AccentLevel;
            amount = levelValue.Integer(1, DataRules.AccentLevels);
        }

        if (research.Optional("up_to_enemy") is { } enemyValue)
        {
            ends++;
            until = AiResearchUntil.EnemyTier;
            amount = ReadTrack(enemyValue, trackIds);
        }

        research.End();
        if (ends != 1)
        {
            value.Error($"needs exactly one of \"tiers\", \"until_accent_level\" and \"up_to_enemy\" to say until when, found {ends}");
        }

        return new AiResearchResponse(track, until, amount);
    }

    private static int ReadTrack(JsonValue value, List<string> trackIds) =>
        value.IndexIn(value.Id(), trackIds, "track", DataRules.TracksFile);

    private static AiFightDef ReadFight(JsonValue value, RuleSet rules)
    {
        var fight = value.Object();
        var attackRatio = fight.Required("attack_ratio").Number(DataNumber.FromInteger(1), DataNumber.FromInteger(DataRules.MaxAiRatio));
        var assumedEnemyValue = fight.Required("assumed_enemy_value").Integer(0, DataRules.MaxPrice);
        var retreatLosses = fight.Required("retreat_losses").Share();
        var reinforceUnits = fight.Required("reinforce_units").Integer(1, DataRules.MaxAiUnits);
        var passage = MapReader.ReadMarkerKind(fight.Required("passage"));

        var raid = fight.Required("raid").Object();
        var lineValue = raid.Required("line");
        var raidLine = lineValue.IndexIn(lineValue.Id(), rules.Lines, "line", Roster);
        var raidParty = raid.Required("party").Integer(1, DataRules.MaxAiUnits);
        raid.End();

        var reactionTicks = fight.Required("reaction_seconds").SecondsAsTicks();
        fight.End();
        return new AiFightDef(attackRatio, assumedEnemyValue, retreatLosses, reinforceUnits, passage, raidLine, raidParty, reactionTicks);
    }

    private static List<AiWorkerTarget> ReadWorkerTargets(JsonValue value, int tierCount)
    {
        var targets = new List<AiWorkerTarget>();
        foreach (var entryValue in value.Array(1))
        {
            var errorsBefore = entryValue.ErrorCount;
            var entry = entryValue.Object();
            var epochValue = entry.Required("epoch");
            var epoch = epochValue.Integer(1, tierCount);
            var workers = entry.Required("workers").Integer(1, DataRules.MaxAiWorkers);
            entry.End();
            if (entryValue.ErrorCount != errorsBefore)
            {
                continue;
            }

            if (targets.Count == 0 && epoch != 1)
            {
                epochValue.Error($"the first entry says how many workers it keeps from the start, so its epoch must be 1, found {epoch}");
            }
            else if (targets.Count > 0 && epoch <= targets[^1].Epoch)
            {
                epochValue.Error($"epochs must rise from one entry to the next, found {epoch} after {targets[^1].Epoch}");
            }
            else
            {
                targets.Add(new AiWorkerTarget(epoch, workers));
            }
        }

        // A file with no valid entry has an error already; this keeps WorkersAt safe to call.
        if (targets.Count == 0)
        {
            targets.Add(new AiWorkerTarget(1, 1));
        }

        return targets;
    }

    private static int[] ReadWeights(JsonValue value, IReadOnlyList<TrackDef> tracks)
    {
        var weights = new int[tracks.Count];
        var trackIds = new List<string>(tracks.Count);
        foreach (var track in tracks)
        {
            trackIds.Add(track.Id);
        }

        var given = new bool[tracks.Count];
        var errorsBefore = value.ErrorCount;
        foreach (var (key, weightValue) in value.Object().Members())
        {
            var weight = weightValue.Integer(1, DataRules.MaxAiWeight);
            var index = trackIds.IndexOf(key);
            if (index < 0)
            {
                weightValue.Error($"unknown track '{key}'; {DataRules.TracksFile} has: {string.Join(", ", trackIds)}");
                continue;
            }

            weights[index] = weight;
            given[index] = true;
        }

        if (value.ErrorCount == errorsBefore)
        {
            for (var i = 0; i < given.Length; i++)
            {
                if (!given[i])
                {
                    value.Error($"needs a weight for every track, and has none for '{trackIds[i]}'");
                }
            }
        }

        return weights;
    }

    private static List<AiLineShare> ReadLines(JsonValue value, RuleSet rules)
    {
        var lines = new List<AiLineShare>();
        foreach (var entryValue in value.Array(1))
        {
            var errorsBefore = entryValue.ErrorCount;
            var entry = entryValue.Object();
            var lineValue = entry.Required("line");
            var lineId = lineValue.Id();
            var line = lineValue.IndexIn(lineId, rules.Lines, "line", Roster);
            var share = entry.Required("share").Integer(1, DataRules.MaxAiWeight);
            entry.End();
            if (entryValue.ErrorCount != errorsBefore || lineId.Length == 0)
            {
                continue;
            }

            if (lines.Exists(listed => listed.Line == line))
            {
                lineValue.Error($"line '{lineId}' is listed twice");
            }
            else
            {
                lines.Add(new AiLineShare(line, share));
            }
        }

        return lines;
    }
}
