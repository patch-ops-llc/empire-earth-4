using Sim.Data;

namespace Tools.DataCheck;

/// <summary>
/// Loads a data folder through the same loader the game uses and reports everything wrong with it.
/// CI runs it on <c>game/data</c> for every pull request.
/// </summary>
public sealed class DataCheckTool : ITool
{
    public const string DefaultFolder = "game/data";

    public string Name => "datacheck";

    public string Summary => $"Check the game data. Arguments: [data folder], default {DefaultFolder}.";

    public int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        if (arguments.Count > 1)
        {
            errors.WriteLine("Usage: datacheck [data folder]");
            return ExitCodes.Usage;
        }

        var folder = arguments.Count == 1 ? arguments[0] : DefaultFolder;
        if (!Directory.Exists(folder))
        {
            errors.WriteLine($"datacheck: there is no folder '{folder}'.");
            return ExitCodes.Usage;
        }

        var result = DataLoader.Load(new DiskDataFiles(folder));
        if (!result.IsValid)
        {
            var prefix = folder.TrimEnd('/', '\\');
            foreach (var problem in result.Errors)
            {
                errors.WriteLine($"{prefix}/{problem}");
            }

            errors.WriteLine($"datacheck: {result.Errors.Count} {(result.Errors.Count == 1 ? "error" : "errors")} in {folder}.");
            return ExitCodes.Failed;
        }

        var data = result.Data;
        output.WriteLine($"datacheck: {folder} is valid.");
        output.WriteLine($"  files:        {result.FileCount}");
        output.WriteLine($"  tracks:       {data.Tracks.Count}, with {data.TierCount} tiers each");
        output.WriteLine($"  research:     {data.Research.Count} tiers, from 2 to {data.TierCount}");
        output.WriteLine($"  accents:      {data.Accents.Count}");
        output.WriteLine($"  lines:        {data.Lines.Count}, with {data.Lines.Sum(line => line.Chassis.Count)} chassis");
        output.WriteLine($"  structures:   {data.Structures.Count} of the {data.Rules.Structures.Count} in the roster");
        output.WriteLine($"  maps:         {data.Maps.Count}");
        output.WriteLine($"  ai:           {data.Personalities.Count}");
        output.WriteLine($"  content hash: sha256:{data.ContentHash}");
        return ExitCodes.Ok;
    }
}
