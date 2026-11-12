namespace Sim.Data;

/// <summary>
/// What the data must hold for everything <c>rules.jsonc</c> says a match is played with
/// (<see cref="MatchRules.Lines"/> and <see cref="MatchRules.Structures"/>, design 6): a file for
/// each, and in it what a player needs to train the line or place the structure before the epoch
/// cap. A roster entry that is not listed there may still be without a file.
/// </summary>
/// <remarks>
/// A file that is there and could not be read has its own errors and is passed over here, so one
/// mistake is reported once.
/// </remarks>
internal static class MatchRosterChecker
{
    private const string Listed = "\"match\" in rules.jsonc";

    public static void Check(
        RuleSet rules,
        IReadOnlyList<LineDef> lines,
        IReadOnlyList<StructureDef> structures,
        IReadOnlyList<string> linePaths,
        IReadOnlyList<string> structurePaths,
        ErrorList errors)
    {
        var epochCap = rules.Match.EpochCap;
        foreach (var index in rules.Match.Lines ?? [])
        {
            var id = rules.Lines[index];
            var file = $"{DataRules.LinesFolder}{id}{DataRules.FileExtension}";
            var line = lines.FirstOrDefault(line => line.Index == index);
            if (line is null)
            {
                if (!linePaths.Contains(file))
                {
                    errors.Add(file, "", $"this file is required and is missing: a match is played with line '{id}' ({Listed})");
                }

                continue;
            }

            // "Older chassis stay available" (design 2.2), so a line has one for every epoch from the first.
            for (var epoch = 1; epoch <= epochCap; epoch++)
            {
                if (!line.Chassis.Any(chassis => chassis.Epoch == epoch))
                {
                    errors.Add(file, "$.chassis", $"a match is played with this line up to epoch {epochCap} ({Listed}), so it needs a chassis for epoch {epoch}");
                }
            }

            if (rules.Match.Structures is { } playedWith && !playedWith.Contains(line.BuiltAt))
            {
                errors.Add(
                    file,
                    "$.built_at",
                    $"a match is played with this line and not with structure '{rules.Structures[line.BuiltAt]}' ({Listed}), so nothing could train it");
            }
        }

        foreach (var index in rules.Match.Structures ?? [])
        {
            var id = rules.Structures[index];
            var file = $"{DataRules.StructuresFolder}{id}{DataRules.FileExtension}";
            var structure = structures.FirstOrDefault(structure => structure.Index == index);
            if (structure is null)
            {
                // The starting structure's missing file is reported by the loader itself.
                if (!structurePaths.Contains(file) && id != DataRules.StartStructure)
                {
                    errors.Add(file, "", $"this file is required and is missing: a match is played with structure '{id}' ({Listed})");
                }

                continue;
            }

            if (structure.UnlockTier > epochCap)
            {
                errors.Add(
                    file,
                    "$.unlock_tier",
                    $"a match is played with this structure and no track passes tier {epochCap} ({Listed}), so it could never be placed");
            }
        }
    }
}
