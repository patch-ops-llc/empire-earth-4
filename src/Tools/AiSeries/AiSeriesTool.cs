using System.Globalization;
using Sim;
using Sim.Data;

namespace Tools.AiSeries;

/// <summary>
/// A series of headless matches (technical plan, M23; design 5.5, item 7): every pairing of
/// computer players on every map, from both seats and both starts, at several command delays,
/// each to its end by conquest or to the time limit. It prints what the matches come to against
/// the things the design sets targets for: how long a match is, when each epoch comes, when the
/// first attack comes, who wins from where, and what each personality pays for on which track.
/// </summary>
/// <remarks>
/// The tool fails only when the series cannot be played as asked. What the matches show is a
/// finding, never a failure.
/// </remarks>
public sealed class AiSeriesTool : ITool
{
    public const string DefaultFolder = "game/data";

    public const int DefaultMinutes = 30;

    private const string Usage =
        """
        Usage:
          ai-series [--data <folder>] [--maps <list>] [--personalities <list>] [--delays <list>] [--minutes <number>] [--csv <file>] [--samples <file>] [--jobs <number>]

        --data           The data folder, default game/data.
        --maps           The maps, by their IDs in the data, separated by commas. Default: every map.
        --personalities  The personalities, by their IDs in the data, separated by commas. Every
                         one plays every one, itself too, from both seats. Default: every personality.
        --delays         The command delays, in ticks, each 1 to 20, separated by commas, default 2.
                         Seeds are not varied, because nothing in a match is random; the command
                         delay is what makes one match of a pairing differ from the next.
        --minutes        The longest a match may take, in minutes of game time, default 30.
        --csv            A file to write one line per seat of every match to, as comma-separated values.
        --samples        A file to write what each seat had every ten seconds to, as comma-separated values.
        --jobs           The matches to play side by side, default one per processor.
        """;

    public string Name => "ai-series";

    public string Summary =>
        "Play every pairing of computer players on every map, from both seats and both starts, at several command delays, and report lengths, epochs, first attacks, wins and research against the design's targets. "
        + $"Arguments: [--data <folder>, default {DefaultFolder}] [--maps <list>] [--personalities <list>] [--delays <list>, default 2] [--minutes <number>, default {DefaultMinutes}] [--csv <file>] [--samples <file>] [--jobs <number>].";

    public int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        var folder = DefaultFolder;
        var minutes = DefaultMinutes;
        var jobs = Environment.ProcessorCount;
        string[]? mapIds = null;
        string[]? personalityIds = null;
        int[] delays = [AiMatch.HeadlessMatch.CommandDelay];
        string? csv = null;
        string? samples = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case "--data" when i + 1 < arguments.Count:
                    folder = arguments[++i];
                    break;
                case "--maps" when i + 1 < arguments.Count:
                    mapIds = arguments[++i].Split(',');
                    break;
                case "--personalities" when i + 1 < arguments.Count:
                    personalityIds = arguments[++i].Split(',');
                    break;
                case "--delays" when i + 1 < arguments.Count && Numbers(arguments[i + 1], 1, 20) is { } numbers:
                    delays = numbers;
                    i++;
                    break;
                case "--minutes" when i + 1 < arguments.Count && Numbers(arguments[i + 1], 1, 24 * 60) is [var number]:
                    minutes = number;
                    i++;
                    break;
                case "--jobs" when i + 1 < arguments.Count && Numbers(arguments[i + 1], 1, 1024) is [var number]:
                    jobs = number;
                    i++;
                    break;
                case "--csv" when i + 1 < arguments.Count:
                    csv = arguments[++i];
                    break;
                case "--samples" when i + 1 < arguments.Count:
                    samples = arguments[++i];
                    break;
                default:
                    errors.WriteLine($"ai-series: unknown or incomplete argument '{arguments[i]}'.");
                    errors.WriteLine(Usage);
                    return ExitCodes.Usage;
            }
        }

        if (!Directory.Exists(folder))
        {
            errors.WriteLine($"ai-series: there is no folder '{folder}'.");
            return ExitCodes.Usage;
        }

        var loaded = DataLoader.Load(new DiskDataFiles(folder));
        if (!loaded.IsValid)
        {
            errors.WriteLine($"ai-series: the data in {folder} is not valid. Run datacheck on it.");
            return ExitCodes.Failed;
        }

        var data = loaded.Data;
        mapIds ??= [.. data.Maps.Select(map => map.Id)];
        if (mapIds.FirstOrDefault(id => data.Maps.All(map => map.Id != id)) is { } noMap)
        {
            errors.WriteLine($"ai-series: the data in {folder} has no map '{noMap}'. Its maps are: {string.Join(", ", data.Maps.Select(map => map.Id))}.");
            return ExitCodes.Usage;
        }

        personalityIds ??= [.. data.Personalities.Select(personality => personality.Id)];
        if (personalityIds.FirstOrDefault(id => data.PersonalityOf(id) is null) is { } noPersonality)
        {
            errors.WriteLine($"ai-series: the data in {folder} has no personality '{noPersonality}'. Its personalities are: {string.Join(", ", data.Personalities.Select(personality => personality.Id))}.");
            return ExitCodes.Usage;
        }

        if (mapIds.Length == 0 || personalityIds.Length == 0)
        {
            errors.WriteLine($"ai-series: the data in {folder} needs a map and a personality for a match.");
            return ExitCodes.Usage;
        }

        var maps = mapIds.Distinct().Select(id => data.Maps.Single(map => map.Id == id)).ToList();
        var personalities = personalityIds.Distinct().Select(id => data.PersonalityOf(id)!).ToList();
        delays = [.. delays.Distinct()];
        var count = maps.Count * personalities.Count * personalities.Count * 2 * delays.Length;
        output.WriteLine(
            $"ai-series: {count} matches between computer players with the data in {folder}: "
            + $"on {string.Join(", ", maps.Select(map => map.Id))}; between {string.Join(", ", personalities.Select(personality => personality.Id))}, "
            + "every pairing from both seats and with the starts both ways round; "
            + $"at command delays of {string.Join(", ", delays)} ticks; at most {minutes} minutes of game time each.");
        output.WriteLine(
            "Nothing in a match is random, so a seed changes nothing: the matches of a pairing differ by seat, start and command delay alone, "
            + "and a share of wins says how stable a result is, not how likely.");
        output.WriteLine();

        var matches = Series.Play(data, maps, personalities, delays, minutes * 60 * SimInfo.TicksPerSecond, jobs);
        SeriesReport.Write(output, data, matches);

        if (csv is not null)
        {
            using var writer = Open(csv);
            SeriesReport.WriteCsv(writer, data, matches);
            output.WriteLine();
            output.WriteLine($"ai-series: one line per seat of every match is in {csv}.");
        }

        if (samples is not null)
        {
            using var writer = Open(samples);
            SeriesReport.WriteSamples(writer, matches);
            output.WriteLine(csv is null ? Environment.NewLine + $"ai-series: what each seat had every ten seconds is in {samples}." : $"ai-series: what each seat had every ten seconds is in {samples}.");
        }

        return ExitCodes.Ok;
    }

    private static StreamWriter Open(string file)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(file)) is { } directory)
        {
            Directory.CreateDirectory(directory);
        }

        return new StreamWriter(file) { NewLine = "\n" };
    }

    /// <summary>The whole numbers of a list separated by commas, when each is within the bounds; otherwise nothing.</summary>
    private static int[]? Numbers(string list, int least, int most)
    {
        var parts = list.Split(',');
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] < least || numbers[i] > most)
            {
                return null;
            }
        }

        return numbers;
    }
}
