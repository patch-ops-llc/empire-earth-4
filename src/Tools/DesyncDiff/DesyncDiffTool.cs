using System.Globalization;
using Sim.Data;
using Sim.Serialization;

namespace Tools.DesyncDiff;

/// <summary>
/// Compares what two peers left on disk when their match stopped on a desync, and says when the
/// two parted and which fields of the state differ. Each peer has a pair of files with the same
/// name: the replay of the match, which the game records with a state hash for every tick, and a
/// snapshot of the state the match stopped in. A tester sends both pairs.
/// </summary>
public sealed class DesyncDiffTool : ITool
{
    public const string DefaultFolder = "game/data";

    public const int DefaultLimit = 40;

    /// <summary>The ending of a replay file's name.</summary>
    public const string ReplayEnding = ".replay";

    /// <summary>The ending of the name of the snapshot beside it.</summary>
    public const string SnapshotEnding = ".snapshot";

    private const string Usage =
        """
        Usage:
          desync-diff <file> <file> [--data <folder>] [--limit <number>]

        Each file is one peer's replay (.replay) or snapshot (.snapshot) of the same match; the
        other file of the pair is read from beside it. Reports the tick at which the recorded
        state hashes of the two peers part, whether this machine's own run of each replay gives
        the hashes that peer recorded, and the fields of the two final states that differ, by
        store, entity and field name.
        --data   The data folder the match was played with, default game/data.
        --limit  The most differences to list, default 40. 0 lists all.
        """;

    public string Name => "desync-diff";

    public string Summary =>
        "Compare the replays and snapshots two peers wrote when their match stopped on a desync: when they parted, and which fields differ. "
        + $"Arguments: <file> <file> [--data <folder>, default {DefaultFolder}] [--limit <n>, default {DefaultLimit}].";

    public int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        if (!TryParse(arguments, out var paths, out var dataFolder, out var limit, out var problem))
        {
            errors.WriteLine($"desync-diff: {problem}");
            errors.WriteLine(Usage);
            return ExitCodes.Usage;
        }

        if (!Directory.Exists(dataFolder))
        {
            errors.WriteLine($"desync-diff: there is no folder '{dataFolder}'.");
            return ExitCodes.Usage;
        }

        var loaded = DataLoader.Load(new DiskDataFiles(dataFolder));
        if (!loaded.IsValid)
        {
            errors.WriteLine($"desync-diff: the data in {dataFolder} is not valid. Run datacheck on it.");
            return ExitCodes.Failed;
        }

        var sides = new Side[2];
        for (var i = 0; i < 2; i++)
        {
            if (!TryLoad(paths[i], loaded.Data, out sides[i], out problem))
            {
                errors.WriteLine($"desync-diff: {paths[i]}: {problem}");
                return ExitCodes.Failed;
            }
        }

        return Compare(sides[0], sides[1], limit, output, errors);
    }

    private static int Compare(Side first, Side second, int limit, TextWriter output, TextWriter errors)
    {
        output.WriteLine(Describe("A", first));
        output.WriteLine(Describe("B", second));

        if (!first.DescriptionBytes.AsSpan().SequenceEqual(second.DescriptionBytes))
        {
            errors.WriteLine("desync-diff: the two are not from the same match: the match descriptions in their replays differ.");
            return ExitCodes.Failed;
        }

        var parted = WriteWhereTheyParted(first, second, output);
        output.WriteLine(DescribeRun("A", first));
        output.WriteLine(DescribeRun("B", second));

        if (first.Tick != second.Tick)
        {
            output.WriteLine(Invariant($"The snapshots are from different ticks, {first.Tick} and {second.Tick}, so most of what follows is the time between them."));
        }

        var differences = Differences(first.Fields, second.Fields);
        if (differences.Count == 0)
        {
            output.WriteLine("The two snapshots are the same in every field.");
            return parted ? ExitCodes.Failed : ExitCodes.Ok;
        }

        var shown = limit == 0 ? differences.Count : Math.Min(limit, differences.Count);
        output.WriteLine(shown == differences.Count
            ? Invariant($"{differences.Count} {(differences.Count == 1 ? "difference" : "differences")} between the snapshots, A | B:")
            : Invariant($"{differences.Count} differences between the snapshots. The first {shown}, A | B:"));

        var width = differences.Take(shown).Max(difference => difference.Where.Length);
        foreach (var difference in differences.Take(shown))
        {
            output.WriteLine($"  {difference.Where.PadRight(width)}  {difference.Text}");
        }

        return ExitCodes.Failed;
    }

    private static string Describe(string label, Side side)
    {
        var description = side.Replay.Description;
        return Invariant(
            $"{label}: {side.Name}  map={description.MapId} players={description.Players.Count} seed={description.Seed} game={description.GameVersion} recorded_ticks={side.Replay.TickCount} snapshot_tick={side.Tick} state_hash={side.Hash:x16}");
    }

    /// <summary>What this machine gets when it runs the peer's commands itself: a third opinion on which of the two went wrong.</summary>
    private static string DescribeRun(string label, Side side) => side.Run.Mismatch is { } mismatch
        ? Invariant($"Run here, {label}'s replay gives another state than {label} recorded, first after {mismatch.Tick} ticks.")
        : Invariant($"Run here, {label}'s replay gives the states {label} recorded, all {side.Run.HashesChecked} of them.");

    /// <summary>Compares the state hashes the two recorded on the way. Returns true if any differ.</summary>
    private static bool WriteWhereTheyParted(Side first, Side second, TextWriter output)
    {
        var others = second.Replay.Hashes.ToDictionary(hash => hash.Tick, hash => hash.Hash);
        var compared = 0;
        var lastSame = -1;
        foreach (var hash in first.Replay.Hashes)
        {
            if (!others.TryGetValue(hash.Tick, out var other))
            {
                continue;
            }

            compared++;
            if (hash.Hash != other)
            {
                output.WriteLine(lastSame < 0
                    ? Invariant($"The recorded state hashes differ from the first one compared, after {hash.Tick} ticks: {hash.Hash:x16} | {other:x16}.")
                    : Invariant($"The recorded state hashes are the same after {lastSame} ticks and differ after {hash.Tick}: {hash.Hash:x16} | {other:x16}."));
                return true;
            }

            lastSame = hash.Tick;
        }

        output.WriteLine(compared == 0
            ? "The two replays recorded no state hash for the same tick, so they do not say when the peers parted."
            : Invariant($"The recorded state hashes are the same, all {compared} that both have, up to {lastSame} ticks."));
        return false;
    }

    /// <summary>
    /// What differs, store by store in the order of the walk: the fields with different values,
    /// then what only the second state has. Fields are matched by store and entity, so one entity
    /// more on one side is one line and does not put every field after it out of step.
    /// </summary>
    private static List<Difference> Differences(List<StateField> first, List<StateField> second)
    {
        var differences = new List<Difference>();
        var others = Group(second);
        var seen = new HashSet<(string, string)>();

        foreach (var (key, fields) in Group(first))
        {
            seen.Add(key);
            if (!others.TryGetValue(key, out var otherFields))
            {
                differences.Add(new Difference(key.Section, key.Item, "only in A"));
                continue;
            }

            for (var i = 0; i < Math.Min(fields.Count, otherFields.Count); i++)
            {
                if (fields[i].Name != otherFields[i].Name)
                {
                    differences.Add(new Difference(key.Section, key.Item, $"field {i} is {fields[i].Name} | {otherFields[i].Name}: the two walk different fields"));
                    break;
                }

                if (fields[i].Value != otherFields[i].Value)
                {
                    differences.Add(new Difference(key.Section, key.Item, $"{fields[i].Name}: {fields[i].Value} | {otherFields[i].Value}"));
                }
            }

            if (fields.Count != otherFields.Count)
            {
                differences.Add(new Difference(key.Section, key.Item, Invariant($"{fields.Count} fields | {otherFields.Count} fields")));
            }
        }

        foreach (var (key, _) in others)
        {
            if (!seen.Contains(key))
            {
                differences.Add(new Difference(key.Section, key.Item, "only in B"));
            }
        }

        // Keep each store's lines together. The sort is stable, so the order within a store stays.
        var sections = first.Concat(second).Select(field => field.Section).Distinct().ToList();
        return [.. differences.OrderBy(difference => sections.IndexOf(difference.Section))];
    }

    /// <summary>The fields of each store's each entity, the groups in walk order.</summary>
    private static OrderedDictionary<(string Section, string Item), List<StateField>> Group(List<StateField> fields)
    {
        var groups = new OrderedDictionary<(string Section, string Item), List<StateField>>();
        foreach (var field in fields)
        {
            var key = (field.Section, field.Item);
            if (!groups.TryGetValue(key, out var group))
            {
                groups.Add(key, group = []);
            }

            group.Add(field);
        }

        return groups;
    }

    /// <summary>
    /// Reads one peer's pair of files. Whatever restores a snapshot builds the match with
    /// <c>MatchSetup.Create</c>, here through the description in the replay, and brings what the
    /// match works out from its state in line afterwards, which <c>Snapshot.Restore</c> does.
    /// </summary>
    private static bool TryLoad(string path, GameData data, out Side side, out string problem)
    {
        side = default!;
        problem = "";

        var stem = path;
        foreach (var ending in (string[])[ReplayEnding, SnapshotEnding])
        {
            if (path.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
            {
                stem = path[..^ending.Length];
            }
        }

        foreach (var ending in (string[])[ReplayEnding, SnapshotEnding])
        {
            if (!File.Exists(stem + ending))
            {
                problem = $"there is no file '{Path.GetFileName(stem + ending)}' beside it. A peer's replay and its snapshot have the same name and belong together.";
                return false;
            }
        }

        var file = ReplayEnding;
        try
        {
            var replay = Sim.Serialization.Replay.Read(new ByteReader(File.ReadAllBytes(stem + ReplayEnding)), keepWhatIsWhole: true);
            if (replay.Description.FindProblem(data) is { } unfit)
            {
                problem = unfit;
                return false;
            }

            var run = replay.Run(data);

            file = SnapshotEnding;
            var match = replay.Description.CreateMatch(data);
            var reader = new ByteReader(File.ReadAllBytes(stem + SnapshotEnding));
            Snapshot.Restore(match, reader);
            reader.ExpectEnd();

            var description = new ByteWriter();
            replay.Description.Write(description);
            side = new Side(Path.GetFileName(stem), replay, run, description.ToArray(), match.World.Tick, match.World.ComputeHash(), StateListing.Of(match.World));
            return true;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            problem = $"{Path.GetFileName(stem + file)}: {refused.Message}";
            return false;
        }
    }

    private static bool TryParse(IReadOnlyList<string> arguments, out List<string> paths, out string dataFolder, out int limit, out string problem)
    {
        paths = [];
        dataFolder = DefaultFolder;
        limit = DefaultLimit;
        problem = "";

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (argument is "--data" or "--limit")
            {
                if (i + 1 >= arguments.Count)
                {
                    problem = $"{argument} needs a value.";
                    return false;
                }

                var value = arguments[++i];
                if (argument == "--data")
                {
                    dataFolder = value;
                }
                else if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out limit))
                {
                    problem = $"--limit takes a whole number, 0 or more, not '{value}'.";
                    return false;
                }
            }
            else if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                problem = $"unknown option '{argument}'.";
                return false;
            }
            else
            {
                paths.Add(argument);
            }
        }

        if (paths.Count != 2)
        {
            problem = Invariant($"it compares two peers, and {paths.Count} files were given.");
            return false;
        }

        return true;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private readonly record struct Difference(string Section, string Item, string Text)
    {
        /// <summary>The store, and the entity when the field belongs to one.</summary>
        public string Where => Item.Length == 0 ? Section : $"{Section} {Item}";
    }

    private sealed record Side(
        string Name,
        Sim.Serialization.Replay Replay,
        ReplayResult Run,
        byte[] DescriptionBytes,
        int Tick,
        ulong Hash,
        List<StateField> Fields);
}
