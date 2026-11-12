using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Sim;
using Sim.Data;

namespace Tools.AiMatch;

/// <summary>
/// The headless match (technical plan, M23; design 5.5, item 7): computer players against each
/// other with nobody watching, as fast as the machine allows. It reports the winner, the length
/// of the match, the time of a tick, when each player first ordered an attack, what it paid for
/// research on each track, and what it had built by the end. Every match is
/// played twice, with the seats and starts swapped, as the design asks of the tests that guard
/// against one side of a map being favored (design 5.3).
/// </summary>
/// <remarks>
/// What a match shows is the same on every machine; the times are not. The tool fails only when
/// a match cannot be played as asked. A match without a winner is a finding, not a failure.
/// </remarks>
public sealed class AiMatchTool : ITool
{
    public const string DefaultFolder = "game/data";

    public const string DefaultMap = "ridge";

    public const int DefaultMinutes = 20;

    private const string Usage =
        """
        Usage:
          ai-match [--data <folder>] [--map <id>] [--players <list>] [--minutes <number>] [--delay <ticks>] [--seed <number>]

        --data     The data folder, default game/data.
        --map      The map, by its ID in the data, default ridge.
        --players  The personality of each computer player, by its ID in the data, separated by
                   commas, in the order of the seats: player i takes start i of the map. Default:
                   the first two personalities of the data.
        --minutes  The longest a match may take, in minutes of game time, default 20.
        --delay    The ticks between a computer player's thinking and its commands taking
                   effect, 1 to 20, default 2.
        --seed     The seed of the match, default 1. Nothing in a match is random, so it changes
                   the state hash and nothing else.
        """;

    public string Name => "ai-match";

    public string Summary =>
        "Play a match between computer players headless, as fast as the machine allows, both ways round, and report winner, length, time per tick and what each built. "
        + $"Arguments: [--data <folder>, default {DefaultFolder}] [--map <id>, default {DefaultMap}] [--players <list>] [--minutes <number>, default {DefaultMinutes}] [--delay <ticks>, default 2] [--seed <number>].";

    public int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        var folder = DefaultFolder;
        var mapId = DefaultMap;
        var minutes = DefaultMinutes;
        var delay = HeadlessMatch.CommandDelay;
        ulong seed = 1;
        string[]? wanted = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case "--data" when i + 1 < arguments.Count:
                    folder = arguments[++i];
                    break;
                case "--map" when i + 1 < arguments.Count:
                    mapId = arguments[++i];
                    break;
                case "--players" when i + 1 < arguments.Count:
                    wanted = arguments[++i].Split(',');
                    break;
                case "--minutes" when i + 1 < arguments.Count
                    && int.TryParse(arguments[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    && number is >= 1 and <= 24 * 60:
                    minutes = number;
                    i++;
                    break;
                case "--delay" when i + 1 < arguments.Count
                    && int.TryParse(arguments[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    && number is >= 1 and <= 20:
                    delay = number;
                    i++;
                    break;
                case "--seed" when i + 1 < arguments.Count
                    && ulong.TryParse(arguments[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var number):
                    seed = number;
                    i++;
                    break;
                default:
                    errors.WriteLine($"ai-match: unknown or incomplete argument '{arguments[i]}'.");
                    errors.WriteLine(Usage);
                    return ExitCodes.Usage;
            }
        }

        if (!Directory.Exists(folder))
        {
            errors.WriteLine($"ai-match: there is no folder '{folder}'.");
            return ExitCodes.Usage;
        }

        var loaded = DataLoader.Load(new DiskDataFiles(folder));
        if (!loaded.IsValid)
        {
            errors.WriteLine($"ai-match: the data in {folder} is not valid. Run datacheck on it.");
            return ExitCodes.Failed;
        }

        var data = loaded.Data;
        var map = data.Maps.FirstOrDefault(candidate => candidate.Id == mapId);
        if (map is null)
        {
            errors.WriteLine($"ai-match: the data in {folder} has no map '{mapId}'. Its maps are: {string.Join(", ", data.Maps.Select(candidate => candidate.Id))}.");
            return ExitCodes.Usage;
        }

        wanted ??= [.. data.Personalities.Take(2).Select(personality => personality.Id)];
        var unknown = wanted.FirstOrDefault(id => data.PersonalityOf(id) is null);
        if (unknown is not null)
        {
            errors.WriteLine($"ai-match: the data in {folder} has no personality '{unknown}'. Its personalities are: {string.Join(", ", data.Personalities.Select(personality => personality.Id))}.");
            return ExitCodes.Usage;
        }

        if (wanted.Length < 2 || wanted.Length > map.Starts.Count)
        {
            errors.WriteLine($"ai-match: a match on '{mapId}' has 2 to {map.Starts.Count} players, and {wanted.Length} were named.");
            return ExitCodes.Usage;
        }

        var optimized = typeof(SimInfo).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };
        output.WriteLine(
            $"ai-match: computer players on '{mapId}' with the data in {folder}, seed {seed}, command delay {delay}, at most {minutes} minutes of game time. "
            + "Each match is played twice, with the seats and starts swapped. "
            + (optimized ? "Optimized build." : "NOT an optimized build: run with -c Release for times that mean something."));

        var personalities = wanted.Select(id => data.PersonalityOf(id)!).ToList();
        var orders = new List<List<AiPersonalityDef>> { personalities };
        if (!personalities.SequenceEqual(personalities.AsEnumerable().Reverse()))
        {
            orders.Add([.. personalities.AsEnumerable().Reverse()]);
        }

        var winners = new List<string?>();
        foreach (var order in orders)
        {
            var result = HeadlessMatch.Play(data, map, order, seed, minutes * 60 * SimInfo.TicksPerSecond, delay);
            var tracks = data.Tracks;
            winners.Add(result.Winner is { } winner ? order[winner].Id : null);
            output.WriteLine();
            Write(output, result, tracks);
        }

        output.WriteLine();
        output.WriteLine(Verdict(winners));
        return ExitCodes.Ok;
    }

    private static void Write(TextWriter output, HeadlessResult result, IReadOnlyList<TrackDef> tracks)
    {
        output.WriteLine(string.Join(" against ", result.Seats.Select(seat => $"{seat.Personality} (player {seat.Player}, start {seat.Start})")));
        output.WriteLine(
            $"  ended after {Clock(result.Ticks)} of game time, "
            + (result.Ended == MatchEnd.TimeLimit
                ? "at the time limit, with no winner"
                : result.Winner is { } winner
                    ? $"by conquest: only {result.Seats[winner].Personality} (player {winner}) had a Capitol or an Outpost left"
                    : "by conquest, with no winner: the last players lost their last Capitol or Outpost in the same tick")
            + (result.FirstHitTick < 0 ? "; nothing was hit" : $"; the first hit landed at {Clock(result.FirstHitTick)}"));
        var perTick = 1000 * result.TickSeconds / result.Ticks;
        var wall = result.TickSeconds + result.ThinkSeconds;
        output.WriteLine(Invariant(
            $"  speed: {result.Ticks:N0} ticks in {wall:0.0} s, {result.GameSeconds / wall:0} times the speed of play; a tick took {perTick:0.000} ms on average and {1000 * result.WorstTickSeconds:0.0} ms at worst; the players thought for {result.ThinkSeconds:0.0} s in all"));
        output.WriteLine(Invariant($"  commands: {result.CommandsGiven:N0} given, {result.CommandsDropped:N0} dropped; state hash {result.StateHash:x16}"));
        foreach (var seat in result.Seats)
        {
            var epochs = seat.EpochTicks.Count == 0
                ? ""
                : $" ({string.Join(", ", seat.EpochTicks.Select((tick, index) => $"epoch {seat.Epoch - seat.EpochTicks.Count + index + 1} at {Clock(tick)}"))})";
            var units = seat.UnitsByChassis.Count == 0 ? "" : $" ({string.Join(", ", seat.UnitsByChassis.Select(entry => $"{entry.Count} {entry.Chassis}"))})";
            output.WriteLine(Invariant($"  {seat.Personality}, player {seat.Player}:"));
            output.WriteLine(Invariant($"    epoch {seat.Epoch}{epochs}; tiers {string.Join(", ", seat.Tiers)}"));
            var paid = Enumerable.Range(0, tracks.Count).Select(track => seat.ResearchMaterials[track] + seat.ResearchEnergy[track]).ToList();
            output.WriteLine(
                Invariant($"    research paid for: {paid.Sum():N0} ({string.Join(", ", paid.Select((amount, track) => Invariant($"{tracks[track].Id} {amount:N0}")))})")
                + (seat.EveryTrackAtCapTick < 0 ? "" : $"; every track at the epoch cap at {Clock(seat.EveryTrackAtCapTick)}"));
            output.WriteLine(
                $"    first attack order: {(seat.FirstAttackOrderTick < 0 ? "none" : Clock(seat.FirstAttackOrderTick))}"
                + (seat.TicksAtSupplyCap == 0 ? "" : $"; at its supply cap for {Clock(seat.TicksAtSupplyCap)}")
                + (seat.TicksAboveSupplyCap == 0 ? "" : Invariant($"; above its supply cap for {Clock(seat.TicksAboveSupplyCap)}, by {seat.MostAboveSupplyCap} at most"))
                + (seat.OutTick < 0 ? "" : $"; out at {Clock(seat.OutTick)}"));
            output.WriteLine(Invariant($"    supply {seat.SupplyUsed} of {seat.SupplyCap}; {seat.Workers} workers; {seat.Units} fighting units worth {seat.ArmyValue:N0}{units}"));
            output.WriteLine(Invariant($"    structures: {string.Join(", ", seat.Structures.Select(entry => $"{entry.Count} {entry.Structure}"))}"));
            output.WriteLine(Invariant($"    lost {seat.UnitsLost} units and {seat.StructuresLost} structures; stockpile {seat.Materials:N0} Materials and {seat.Energy:N0} Energy"));
        }
    }

    /// <summary>The last line of the report: who won, from the winner of each match, none for a match without one.</summary>
    public static string Verdict(IReadOnlyList<string?> winners)
    {
        ArgumentNullException.ThrowIfNull(winners);

        if (winners.All(winner => winner is null))
        {
            return "ai-match: no winner. A computer player of this build builds, researches and trains, and does not attack.";
        }

        if (winners.Count == 1)
        {
            return $"ai-match: {winners[0]} won.";
        }

        return winners[0] == winners[1]
            ? $"ai-match: {winners[0]} won both ways round."
            : $"ai-match: the result changed with the seats: {winners[0] ?? "no winner"} in the first match, {winners[1] ?? "no winner"} in the second.";
    }

    private static string Clock(int ticks)
    {
        var seconds = ticks / SimInfo.TicksPerSecond;
        return Invariant($"{seconds / 60}:{seconds % 60:00}");
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
