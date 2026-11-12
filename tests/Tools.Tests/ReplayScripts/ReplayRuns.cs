using Sim.Data;
using Sim.Serialization;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// What the tests of the scripts of <c>replay record</c> share: the golden replays with their
/// data, and a match recorded for a test. A piece that adds a script uses this and has no need to
/// edit it.
/// </summary>
internal static class ReplayRuns
{
    /// <summary>The frozen data the golden replays run on.</summary>
    public static GameData GoldenData { get; } = Load(Repository.GoldenDataFolder);

    public static Sim.Serialization.Replay ReadGolden(string name) =>
        Sim.Serialization.Replay.Read(new ByteReader(File.ReadAllBytes(Path.Combine(Repository.GoldenReplaysFolder, name))));

    /// <summary>
    /// Records a match on the frozen data with <c>replay record</c> and reads its replay.
    /// The arguments are those of the tool after the file and without <c>--data</c>.
    /// </summary>
    public static Sim.Serialization.Replay Record(params string[] arguments)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "recorded.replay");
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = ToolRunner.Run(["replay", "record", path, "--data", Repository.GoldenDataFolder, .. arguments], output, errors);

        Assert.Equal((ExitCodes.Ok, ""), (exitCode, errors.ToString()));
        return Sim.Serialization.Replay.Read(new ByteReader(File.ReadAllBytes(path)));
    }

    /// <summary>
    /// The match of a replay on the frozen data, before its first tick, with every command of the
    /// replay scheduled. A test runs its ticks and looks at what they do.
    /// </summary>
    public static Match Start(Sim.Serialization.Replay replay)
    {
        var match = replay.Description.CreateMatch(GoldenData);
        foreach (var (tick, command) in replay.Commands)
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        return match;
    }

    private static GameData Load(string folder)
    {
        var result = DataLoader.Load(new DiskDataFiles(folder));
        Assert.True(result.IsValid, $"The data in {folder} is not valid.");
        return result.Data;
    }
}
