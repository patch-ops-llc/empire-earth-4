using System.Globalization;
using Sim.Data;

namespace Tools.Battles;

/// <summary>
/// The balance harness (technical plan, M23): fights the four acceptance battles of design 3.1
/// headless and reports, for each, the winner, the army value kept and whether the target was
/// met. Every battle is fought twice, the two armies' sides of the map and seats swapped; a
/// target is met only when both fights meet it, and the difference between the two is reported.
/// </summary>
/// <remarks>
/// A missed target never fails the tool: the targets are what the numbers in the data are tuned
/// toward, and the design expects one of them to be missed with the starting numbers. The tool
/// fails only when a battle cannot be fought as the design fixes it, because the data lacks a
/// chassis or a player with the army's tiers could not build the army.
/// </remarks>
public sealed class BattlesTool : ITool
{
    public const string DefaultFolder = "game/data";

    private const string Usage =
        """
        Usage:
          battles [--data <folder>] [--battle <number>]... [--seconds <number>]

        --data     The data folder, default game/data. The armies fight with its numbers.
        --battle   Fight only the battle with this number, 1 to 4. May be given more than once.
        --seconds  Stop a fight that has no winner after this much game time, default 600.
        """;

    public string Name => "battles";

    public string Summary =>
        "Fight the four acceptance battles of the design headless, both ways round, and report winner, army value kept and whether each target was met. "
        + $"Arguments: [--data <folder>, default {DefaultFolder}] [--battle <number>]... [--seconds <number>].";

    public int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        var folder = DefaultFolder;
        var mostTicks = Battle.DefaultMostTicks;
        var wanted = new List<int>();
        for (var i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case "--data" when i + 1 < arguments.Count:
                    folder = arguments[++i];
                    break;
                case "--battle" when i + 1 < arguments.Count
                    && int.TryParse(arguments[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    && AcceptanceBattles.All.Any(battle => battle.Number == number):
                    wanted.Add(number);
                    i++;
                    break;
                case "--seconds" when i + 1 < arguments.Count
                    && int.TryParse(arguments[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                    && seconds is >= 1 and <= 24 * 60 * 60:
                    mostTicks = seconds * Sim.SimInfo.TicksPerSecond;
                    i++;
                    break;
                default:
                    errors.WriteLine($"battles: unknown or incomplete argument '{arguments[i]}'.");
                    errors.WriteLine(Usage);
                    return ExitCodes.Usage;
            }
        }

        if (!Directory.Exists(folder))
        {
            errors.WriteLine($"battles: there is no folder '{folder}'.");
            return ExitCodes.Usage;
        }

        var loaded = DataLoader.Load(new DiskDataFiles(folder));
        if (!loaded.IsValid)
        {
            errors.WriteLine($"battles: the data in {folder} is not valid. Run datacheck on it.");
            return ExitCodes.Failed;
        }

        var battles = AcceptanceBattles.All.Where(battle => wanted.Count == 0 || wanted.Contains(battle.Number)).ToList();
        foreach (var battle in battles)
        {
            if (Battle.Problem(loaded.Data, battle) is { } problem)
            {
                errors.WriteLine($"battles: battle {battle.Number} cannot be fought with the data in {folder}: {problem}.");
                return ExitCodes.Failed;
            }
        }

        output.WriteLine(
            $"battles: the acceptance battles of design 3.1 with the data in {folder}. Each is fought twice: with the mass army in the west as the "
            + "first player, then in the east as the second. A target is met only when both fights meet it. A missed target is reported and fails nothing.");

        var reports = new List<BattleReport>();
        foreach (var battle in battles)
        {
            var report = new BattleReport(
                battle,
                Battle.Fight(loaded.Data, battle, massWest: true, mostTicks),
                Battle.Fight(loaded.Data, battle, massWest: false, mostTicks));
            reports.Add(report);
            output.WriteLine();
            report.Write(output);
        }

        output.WriteLine();
        BattleReport.WriteSummary(reports, output);
        return ExitCodes.Ok;
    }
}
