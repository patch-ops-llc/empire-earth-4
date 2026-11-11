using System.Globalization;
using System.Runtime.InteropServices;
using Sim.Core;
using Sim.Data;
using Sim.Serialization;
using Sim.Setup;

namespace Tools.Replay;

/// <summary>
/// Runs recorded matches again without the game and says whether each gives the states it
/// recorded. CI runs it on <c>tests/golden/replays</c> for every pull request, and on four systems
/// whose reports must be the same. It also brings recorded replays up to date after a change that
/// was meant to change the match, and records the replay of a match that nobody plays.
/// </summary>
public sealed class ReplayTool : ITool
{
    public const string DefaultFolder = "game/data";

    /// <summary>The ending of a replay file's name. A folder is searched for files that have it.</summary>
    public const string FileEnding = ".replay";

    /// <summary>The game version in the replays that <c>record</c> writes: no build played them.</summary>
    public const string ScriptedGameVersion = "scripted";

    private const string Usage =
        """
        Usage:
          replay verify <file or folder>... [--data <folder>]
          replay update <file or folder>... [--data <folder>]
          replay record <file> --map <id> [--data <folder>] [--starts <list>] [--seed <number>] [--ticks <number>] [--orders <number>]

        verify  Runs each replay and compares the state with every hash it recorded.
        update  Runs the commands of each replay with the rules and the data as they are now and
                writes the file again with the hashes of that run.
        record  Plays a match with scripted orders and writes its replay. --starts lists the start
                of each player, default 0,1. --orders is the seed of the orders.
        --data  The data folder, default game/data.
        """;

    public string Name => "replay";

    public string Summary =>
        "Run recorded matches again and check their state hashes; update or record a replay. "
        + $"Arguments: verify|update <file or folder>... | record <file> --map <id> [--starts <list>] [--seed <n>] [--ticks <n>] [--orders <n>]; all take [--data <folder>, default {DefaultFolder}].";

    public int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        if (!Arguments.TryParse(arguments, out var parsed, out var problem))
        {
            errors.WriteLine($"replay: {problem}");
            errors.WriteLine(Usage);
            return ExitCodes.Usage;
        }

        if (!Directory.Exists(parsed.DataFolder))
        {
            errors.WriteLine($"replay: there is no folder '{parsed.DataFolder}'.");
            return ExitCodes.Usage;
        }

        var loaded = DataLoader.Load(new DiskDataFiles(parsed.DataFolder));
        if (!loaded.IsValid)
        {
            errors.WriteLine($"replay: the data in {parsed.DataFolder} is not valid. Run datacheck on it.");
            return ExitCodes.Failed;
        }

        if (parsed.Action == "record")
        {
            return Record(parsed, loaded.Data, output, errors);
        }

        if (!TryFindFiles(parsed.Paths, out var files, out problem))
        {
            errors.WriteLine($"replay: {problem}");
            return ExitCodes.Usage;
        }

        return parsed.Action == "verify" ? Verify(files, loaded.Data, output, errors) : Update(files, loaded.Data, output, errors);
    }

    private static int Verify(List<ReplayFile> files, GameData data, TextWriter output, TextWriter errors)
    {
        var failed = 0;
        foreach (var file in files)
        {
            var outcome = VerifyOne(file, data);
            output.WriteLine(outcome.Line);
            failed += outcome.Reproduced ? 0 : 1;
        }

        if (failed > 0)
        {
            errors.WriteLine($"replay: {failed} of {Count(files.Count)} did not reproduce.");
            return ExitCodes.Failed;
        }

        output.WriteLine($"replay: {Count(files.Count)} reproduced.");
        return ExitCodes.Ok;
    }

    private static (bool Reproduced, string Line) VerifyOne(ReplayFile file, GameData data)
    {
        Sim.Serialization.Replay replay;
        ReplayResult result;
        try
        {
            replay = Sim.Serialization.Replay.Read(new ByteReader(File.ReadAllBytes(file.Path)), keepWhatIsWhole: true);
            result = replay.Run(data);
        }
        catch (InvalidDataException refused)
        {
            return (false, $"FAILED  {file.Name}  {refused.Message}");
        }

        var description = replay.Description;
        var facts = string.Create(
            CultureInfo.InvariantCulture,
            $"{file.Name}  map={description.MapId} players={description.Players.Count} ticks={result.TicksRun} commands={replay.Commands.Count} hashes={result.HashesChecked} final={result.FinalHash:x16}");
        if (result.Mismatch is { } mismatch)
        {
            return (false, string.Create(
                CultureInfo.InvariantCulture,
                $"FAILED  {facts}  The state differs from the recording after {mismatch.Tick} ticks: recorded {mismatch.Recorded:x16}, computed {mismatch.Computed:x16}."));
        }

        if (replay.DamagedAt is { } position)
        {
            return (false, string.Create(CultureInfo.InvariantCulture, $"PARTIAL {facts}  The file is damaged or cut short at byte {position}. What came before it reproduced."));
        }

        return replay.IsComplete
            ? (true, $"ok      {facts}")
            : (false, $"PARTIAL {facts}  The recording stops without an end record. What it has reproduced.");
    }

    private static int Update(List<ReplayFile> files, GameData data, TextWriter output, TextWriter errors)
    {
        var failed = 0;
        foreach (var file in files)
        {
            try
            {
                var old = File.ReadAllBytes(file.Path);
                var replay = Sim.Serialization.Replay.Read(new ByteReader(old), keepWhatIsWhole: true);
                var description = replay.Description.WithContentHash(data.ContentHash);
                var writer = new ByteWriter();
                replay.RecordAgain(description.CreateMatch(data), description, writer);
                if (writer.WrittenSpan.SequenceEqual(old))
                {
                    output.WriteLine($"unchanged  {file.Name}");
                    continue;
                }

                File.WriteAllBytes(file.Path, writer.ToArray());
                output.WriteLine($"updated    {file.Name}");
            }
            catch (InvalidDataException refused)
            {
                failed++;
                errors.WriteLine($"replay: {file.Name}: {refused.Message}");
            }
        }

        return failed > 0 ? ExitCodes.Failed : ExitCodes.Ok;
    }

    private static int Record(Arguments arguments, GameData data, TextWriter output, TextWriter errors)
    {
        Match match;
        MatchDescription description;
        try
        {
            description = new MatchDescription(ScriptedGameVersion, data.ContentHash, arguments.Map, arguments.Seed, arguments.Starts);
            match = description.CreateMatch(data);
        }
        catch (Exception refused) when (refused is ArgumentException or InvalidDataException)
        {
            errors.WriteLine($"replay: {refused.Message}");
            return ExitCodes.Usage;
        }

        var writer = new ByteWriter();
        var recorder = new ReplayRecorder(writer, description, match.World);
        var orders = new ScriptedOrders(match, arguments.OrdersSeed);
        var commands = new List<Command>();
        var commandCount = 0;
        for (var tick = 0; tick < arguments.Ticks; tick++)
        {
            commands.Clear();
            orders.AddOrdersFor(tick, commands);
            recorder.RecordCommands(tick, CollectionsMarshal.AsSpan(commands));
            foreach (var command in commands)
            {
                match.Simulation.Commands.Enqueue(tick, command);
            }

            commandCount += commands.Count;
            match.Simulation.Tick();
            recorder.AfterTick();
        }

        recorder.Finish();
        var path = arguments.Paths[0];
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder)
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllBytes(path, writer.ToArray());
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"recorded   {Path.GetFileName(path)}  map={description.MapId} players={description.Players.Count} ticks={arguments.Ticks} commands={commandCount} final={match.World.ComputeHash():x16}"));
        return ExitCodes.Ok;
    }

    /// <summary>The files named on the command line, and every replay in the folders named there, each folder's in the order of their names.</summary>
    private static bool TryFindFiles(List<string> paths, out List<ReplayFile> files, out string problem)
    {
        files = [];
        problem = "";
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                files.Add(new ReplayFile(path, Path.GetFileName(path)));
            }
            else if (Directory.Exists(path))
            {
                var found = Directory.EnumerateFiles(path, "*" + FileEnding, SearchOption.AllDirectories)
                    .Select(file => new ReplayFile(file, Path.GetRelativePath(path, file).Replace(Path.DirectorySeparatorChar, '/')))
                    .OrderBy(file => file.Name, StringComparer.Ordinal)
                    .ToList();
                if (found.Count == 0)
                {
                    problem = $"the folder '{path}' has no file that ends in {FileEnding}.";
                    return false;
                }

                files.AddRange(found);
            }
            else
            {
                problem = $"there is no file or folder '{path}'.";
                return false;
            }
        }

        return true;
    }

    private static string Count(int replays) => replays == 1 ? "1 replay" : string.Create(CultureInfo.InvariantCulture, $"{replays} replays");

    /// <param name="Path">Where the file is.</param>
    /// <param name="Name">What the report calls it: the same on every system, so that reports can be compared.</param>
    private sealed record ReplayFile(string Path, string Name);

    private sealed class Arguments
    {
        public string Action { get; private set; } = "";

        public List<string> Paths { get; } = [];

        public string DataFolder { get; private set; } = DefaultFolder;

        public string Map { get; private set; } = "";

        public List<PlayerSetup> Starts { get; private set; } = [new(0), new(1)];

        public ulong Seed { get; private set; } = 1;

        public int Ticks { get; private set; } = 60 * Sim.SimInfo.TicksPerSecond;

        public ulong OrdersSeed { get; private set; } = 1;

        public static bool TryParse(IReadOnlyList<string> arguments, out Arguments parsed, out string problem)
        {
            parsed = new Arguments();
            problem = "";
            if (arguments.Count == 0 || arguments[0] is not ("verify" or "update" or "record"))
            {
                problem = arguments.Count == 0 ? "say what to do: verify, update or record." : $"unknown action '{arguments[0]}'.";
                return false;
            }

            parsed.Action = arguments[0];
            var recording = parsed.Action == "record";
            for (var i = 1; i < arguments.Count; i++)
            {
                var argument = arguments[i];
                if (!argument.StartsWith("--", StringComparison.Ordinal))
                {
                    parsed.Paths.Add(argument);
                    continue;
                }

                if (argument is not "--data" && !(recording && argument is ("--map" or "--starts" or "--seed" or "--ticks" or "--orders")))
                {
                    problem = $"unknown option '{argument}'.";
                    return false;
                }

                if (++i == arguments.Count)
                {
                    problem = $"{argument} needs a value.";
                    return false;
                }

                var value = arguments[i];
                var understood = parsed.TrySet(argument, value);
                if (!understood)
                {
                    problem = $"{argument} cannot be '{value}'.";
                    return false;
                }
            }

            if (recording && (parsed.Paths.Count != 1 || parsed.Map.Length == 0))
            {
                problem = "record needs one file to write and --map.";
                return false;
            }

            if (parsed.Paths.Count == 0)
            {
                problem = $"{parsed.Action} needs a file or a folder.";
                return false;
            }

            return true;
        }

        private bool TrySet(string option, string value)
        {
            switch (option)
            {
                case "--data":
                    DataFolder = value;
                    return true;
                case "--map":
                    Map = value;
                    return true;
                case "--seed" when ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seed):
                    Seed = seed;
                    return true;
                case "--orders" when ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seed):
                    OrdersSeed = seed;
                    return true;
                case "--ticks" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks):
                    Ticks = ticks;
                    return true;
                case "--starts" when TryParseStarts(value, out var starts):
                    Starts = starts;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>A list such as <c>0,1</c>: the start of each player, or <c>-</c> for a player without one.</summary>
        private static bool TryParseStarts(string text, out List<PlayerSetup> starts)
        {
            starts = [];
            foreach (var part in text.Split(','))
            {
                if (part == "-")
                {
                    starts.Add(PlayerSetup.WithoutStart);
                }
                else if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var start))
                {
                    starts.Add(new PlayerSetup(start));
                }
                else
                {
                    return false;
                }
            }

            return true;
        }
    }
}
