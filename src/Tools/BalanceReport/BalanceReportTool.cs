using System.Globalization;
using Sim.Data;
using Sim.Tech;

namespace Tools.BalanceReport;

/// <summary>
/// Prints what the tech rules make of a list of tier mixes, as CSV: each mix's epoch, supply cap
/// and income, the price of every track's next tier, and the blueprint of every chassis. It reads
/// the data through the loader and computes with the resolver the game uses, so the numbers are the
/// game's numbers.
/// </summary>
public sealed class BalanceReportTool : ITool
{
    public const string DefaultFolder = "game/data";

    private const string Usage =
        "Usage: balance-report [--data <folder>] [--epoch-cap <tier>] [--table mixes|research|chassis] [[<name>=]<tiers> ...]";

    private static readonly string[] Tables = ["mixes", "research", "chassis"];

    public string Name => "balance-report";

    public string Summary =>
        "Print epoch, research prices, supply cap and chassis blueprints for tier mixes, as CSV. "
        + $"Arguments: [--data <folder>, default {DefaultFolder}] [--epoch-cap <tier>] [--table mixes|research|chassis] [[<name>=]<tiers> ...].";

    public int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        var folder = DefaultFolder;
        string? capText = null;
        string? table = null;
        var mixTexts = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                mixTexts.Add(argument);
                continue;
            }

            if (argument is not ("--data" or "--epoch-cap" or "--table"))
            {
                return UsageError(errors, $"unknown option '{argument}'");
            }

            if (i + 1 >= arguments.Count)
            {
                return UsageError(errors, $"{argument} needs a value");
            }

            var value = arguments[++i];
            switch (argument)
            {
                case "--data":
                    folder = value;
                    break;
                case "--epoch-cap":
                    capText = value;
                    break;
                default:
                    table = value;
                    break;
            }
        }

        if (table is not null && Array.IndexOf(Tables, table) < 0)
        {
            return UsageError(errors, $"unknown table '{table}'; use {string.Join(", ", Tables)}");
        }

        if (!Directory.Exists(folder))
        {
            errors.WriteLine($"balance-report: there is no folder '{folder}'.");
            return ExitCodes.Usage;
        }

        var result = DataLoader.Load(new DiskDataFiles(folder));
        if (!result.IsValid)
        {
            errors.WriteLine(
                $"balance-report: {folder} has {result.Errors.Count} {(result.Errors.Count == 1 ? "error" : "errors")}; run datacheck {folder} to list them.");
            return ExitCodes.Failed;
        }

        var data = result.Data;
        var epochCap = data.Rules.Match.EpochCap;
        if (capText is not null
            && (!int.TryParse(capText, NumberStyles.None, CultureInfo.InvariantCulture, out epochCap) || epochCap < 1 || epochCap > data.TierCount))
        {
            return UsageError(errors, $"--epoch-cap must be a tier from 1 to {data.TierCount}, found '{capText}'");
        }

        var mixes = new List<(string Name, PlayerTech Tech)>();
        if (mixTexts.Count == 0)
        {
            var start = Enumerable.Repeat(data.Rules.Match.StartEpoch, data.Tracks.Count).ToArray();
            mixes.Add(("start", TechResolver.Resolve(data, start, epochCap)));
        }

        foreach (var text in mixTexts)
        {
            if (ParseMix(text, data, out var name, out var tiers) is { } problem)
            {
                return UsageError(errors, problem);
            }

            if (mixes.Exists(mix => mix.Name == name))
            {
                return UsageError(errors, $"two mixes are named '{name}'");
            }

            mixes.Add((name, TechResolver.Resolve(data, tiers, epochCap)));
        }

        var first = true;
        foreach (var name in Tables)
        {
            if (table is not null && table != name)
            {
                continue;
            }

            if (!first)
            {
                output.WriteLine();
            }

            first = false;
            switch (name)
            {
                case "mixes":
                    WriteMixes(data, mixes, output);
                    break;
                case "research":
                    WriteResearch(data, mixes, output);
                    break;
                default:
                    WriteChassis(data, mixes, output);
                    break;
            }
        }

        return ExitCodes.Ok;
    }

    private static int UsageError(TextWriter errors, string problem)
    {
        errors.WriteLine($"balance-report: {problem}.");
        errors.WriteLine(Usage);
        return ExitCodes.Usage;
    }

    /// <summary>Reads <c>name=1,1,2,4,4,2</c> or <c>1,1,2,4,4,2</c>. Returns what is wrong with it, or null.</summary>
    private static string? ParseMix(string text, GameData data, out string name, out int[] tiers)
    {
        var separator = text.IndexOf('=', StringComparison.Ordinal);
        var tiersText = separator < 0 ? text : text[(separator + 1)..];
        var parts = tiersText.Split(',');
        name = separator < 0 ? string.Join('/', parts) : text[..separator];
        tiers = new int[parts.Length];

        var order = string.Join(", ", data.Tracks.Select(track => track.Id));
        if (name.Length == 0)
        {
            return $"the mix '{text}' has no name before '='";
        }

        if (parts.Length != data.Tracks.Count)
        {
            return $"the mix '{text}' needs {data.Tracks.Count} tiers, one per track in the order {order}; found {parts.Length}";
        }

        for (var track = 0; track < parts.Length; track++)
        {
            if (!int.TryParse(parts[track], NumberStyles.None, CultureInfo.InvariantCulture, out tiers[track])
                || tiers[track] < 1
                || tiers[track] > data.TierCount)
            {
                return $"in the mix '{text}' the tier of {data.Tracks[track].Id} must be a whole number from 1 to {data.TierCount}, found '{parts[track]}'";
            }
        }

        return null;
    }

    private static void WriteMixes(GameData data, List<(string Name, PlayerTech Tech)> mixes, TextWriter output)
    {
        output.WriteLine(ReportFormat.Row(["mix", .. data.Tracks.Select(track => track.Id), "epoch", "epoch_name", "supply_cap", "income_multiplier"]));
        foreach (var (name, tech) in mixes)
        {
            output.WriteLine(ReportFormat.Row(
            [
                name,
                .. tech.Tiers.Select(Whole),
                Whole(tech.Epoch),
                data.EpochNames[tech.Epoch - 1],
                Whole(tech.SupplyCap),
                ReportFormat.Rounded(tech.IncomeMultiplier, 4),
            ]));
        }
    }

    private static void WriteResearch(GameData data, List<(string Name, PlayerTech Tech)> mixes, TextWriter output)
    {
        output.WriteLine(ReportFormat.Row(["mix", "track", "tier", "status", "band", "materials", "energy", "seconds"]));
        foreach (var (name, tech) in mixes)
        {
            foreach (var offer in tech.Research)
            {
                var price = offer.Price;
                output.WriteLine(ReportFormat.Row(
                [
                    name,
                    data.Tracks[offer.Track].Id,
                    offer.Status == ResearchStatus.AtTopTier ? "" : Whole(offer.Tier),
                    offer.Status switch
                    {
                        ResearchStatus.Available => "available",
                        ResearchStatus.AtTopTier => "at_top_tier",
                        ResearchStatus.AboveEpochCap => "above_epoch_cap",
                        _ => "beyond_reach",
                    },
                    price?.Band switch
                    {
                        ResearchBand.CatchUp => "catch_up",
                        ResearchBand.Frontier => "frontier",
                        ResearchBand.Ahead => "ahead",
                        _ => "",
                    },
                    price is { } paid ? Whole(paid.Materials) : "",
                    price is { } alsoPaid ? Whole(alsoPaid.Energy) : "",
                    price is { } timed ? ReportFormat.Seconds(timed.Ticks) : "",
                ]));
            }
        }
    }

    private static void WriteChassis(GameData data, List<(string Name, PlayerTech Tech)> mixes, TextWriter output)
    {
        output.WriteLine(ReportFormat.Row(
        [
            "mix", "line", "chassis", "epoch", "unlocked",
            .. data.Tracks.Select(track => $"offset_{track.Id}"),
            "accents", "hit_points", "damage_per_second", "damage_per_hit", "attack", "attack_interval_seconds",
            "range", "min_range", "blast_radius", "speed", "sight", "supply", "materials", "energy",
            "build_seconds", "batch_size", "batch_build_seconds",
        ]));
        foreach (var (name, tech) in mixes)
        {
            foreach (var line in data.Lines)
            {
                foreach (var chassis in line.Chassis)
                {
                    var blueprint = tech.Blueprints[chassis.Index];
                    output.WriteLine(ReportFormat.Row(
                    [
                        name,
                        line.Id,
                        chassis.Id,
                        Whole(chassis.Epoch),
                        blueprint.Unlocked ? "yes" : "no",
                        .. blueprint.Offsets.Select(Whole),
                        string.Join(" + ", blueprint.Accents.Select(accent => $"{data.AccentOf(accent.Track)!.Name} {(accent.Level == 1 ? "I" : "II")}")),
                        ReportFormat.Thousandths(blueprint.HitPoints),
                        ReportFormat.Thousandths(blueprint.DamagePerSecond),
                        ReportFormat.Rounded(blueprint.DamagePerHit, 3),
                        blueprint.Attack.Kind == AttackKind.Melee ? "melee" : "ranged",
                        ReportFormat.Seconds(blueprint.Attack.IntervalTicks),
                        ReportFormat.Tiles(blueprint.Range),
                        ReportFormat.Tiles(blueprint.MinRange),
                        ReportFormat.Tiles(blueprint.BlastRadius),
                        ReportFormat.Rounded(blueprint.SpeedPerTick.ToFraction() * Sim.SimInfo.TicksPerSecond, 3),
                        Whole(blueprint.Sight),
                        Whole(blueprint.Supply),
                        Whole(blueprint.Materials),
                        Whole(blueprint.Energy),
                        ReportFormat.Seconds(blueprint.BuildTicks),
                        Whole(blueprint.BatchSize),
                        ReportFormat.Seconds(blueprint.BatchBuildTicks),
                    ]));
                }
            }
        }
    }

    private static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);
}
