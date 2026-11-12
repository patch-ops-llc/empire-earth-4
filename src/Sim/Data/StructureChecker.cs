namespace Sim.Data;

/// <summary>
/// What the structure files must agree on with the files around them. Only files that were read
/// without an error are compared, so one mistake is reported once.
/// </summary>
internal static class StructureChecker
{
    public static void Check(IReadOnlyList<StructureDef> structures, IReadOnlyList<LineDef> lines, RuleSet rules, ErrorList errors)
    {
        foreach (var structure in structures)
        {
            var file = FileOf(structure);

            // A line's file names the structure that builds it, and that structure's file lists
            // the lines it trains. Where both files exist, the two must say the same.
            foreach (var line in lines)
            {
                var listed = structure.Trains.Contains(line.Index);
                var builtHere = line.BuiltAt == structure.Index;
                if (builtHere && !listed)
                {
                    errors.Add(
                        file,
                        structure.Trains.Count == 0 && !structure.TrainsWorkers ? "$" : "$.trains",
                        $"line '{line.Id}' names this structure in \"built_at\", so \"trains\" must list it");
                }
                else if (listed && !builtHere)
                {
                    errors.Add(
                        file,
                        "$.trains",
                        $"line '{line.Id}' is built at '{rules.Structures[line.BuiltAt]}' by its \"built_at\", so it cannot be trained here");
                }
            }

            // The match setup places one for every player and the map checks measure every
            // start with its footprint and its territory.
            if (structure.Id == DataRules.StartStructure && structure.TerritoryRadius is null)
            {
                errors.Add(file, "$", "every player starts with this structure, so it needs \"territory_radius\"");
            }
        }
    }

    private static string FileOf(StructureDef structure) => $"{DataRules.StructuresFolder}{structure.Id}{DataRules.FileExtension}";
}
