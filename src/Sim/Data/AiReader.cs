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

        file.End();
        return new AiPersonalityDef(id, name, workerTargets, expandAtMaterials, weights, lines);
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
