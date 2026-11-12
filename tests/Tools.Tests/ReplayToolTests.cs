using System.Text.RegularExpressions;
using Sim.Core;
using Sim.Data;
using Sim.Serialization;
using Tools.Replay;

namespace Tools.Tests;

/// <summary>
/// Runs <c>replay</c> the way the command line and CI do: on the recorded replays of
/// <c>tests/golden</c>, which must reproduce, and on copies with something changed, which must
/// fail and say where.
/// </summary>
/// <remarks>
/// These tests are about the tool. What a kind of scripted order must give and what the golden
/// replay recorded for it must reach is in a file for each script, under <c>ReplayScripts/</c>,
/// so that a piece that adds a kind of order or a golden replay edits nothing here.
/// </remarks>
public class ReplayToolTests
{
    /// <summary>The first two replays that were recorded, which the tests below change copies of.</summary>
    private const string Skirmish = "ridge-skirmish.replay";
    private const string Crossing = "switchback-crossing.replay";

    /// <summary>Every golden replay there is, in the order of the names, which is the order of the tool's report.</summary>
    private static readonly string[] GoldenNames =
    [
        .. Directory.EnumerateFiles(Repository.GoldenReplaysFolder, "*" + ReplayTool.FileEnding)
            .Select(file => Path.GetRelativePath(Repository.GoldenReplaysFolder, file))
            .Order(StringComparer.Ordinal),
    ];

    private static GameData GoldenData { get; } = Load(Repository.GoldenDataFolder);

    /// <summary>The check CI runs. It fails when a change to the rules changes a recorded match.</summary>
    [Fact]
    public void TheGoldenReplaysReproduce()
    {
        var (exitCode, output, errors) = Run("verify", Repository.GoldenReplaysFolder, "--data", Repository.GoldenDataFolder);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        var lines = Lines(output);
        Assert.Contains(Skirmish, GoldenNames);
        Assert.Contains(Crossing, GoldenNames);
        Assert.Equal(GoldenNames.Length + 1, lines.Length);
        for (var i = 0; i < GoldenNames.Length; i++)
        {
            Assert.Matches($"^ok      {GoldenNames[i]}  map=[a-z]+ players=[1-8] ticks=[0-9]+ commands=[0-9]+ hashes=[0-9]+ final=[0-9a-f]{{16}}$", lines[i]);
        }

        Assert.Equal($"replay: {GoldenNames.Length} replays reproduced.", lines[^1]);
    }

    /// <summary>
    /// The report is compared between systems and with what other folders give, so it names no
    /// folder and has nothing in it that differs from one machine to the next.
    /// </summary>
    [Fact]
    public void TheReportDoesNotDependOnWhereTheFilesAre()
    {
        using var copy = Repository.CopyOf(Repository.GoldenReplaysFolder);
        using var data = Repository.CopyOf(Repository.GoldenDataFolder);

        var here = Run("verify", Repository.GoldenReplaysFolder, "--data", Repository.GoldenDataFolder);
        var there = Run("verify", "--data", data.Path, copy.Path + Path.DirectorySeparatorChar);
        var oneByOne = Run(["verify", .. GoldenNames.Select(name => Path.Combine(copy.Path, name)), "--data", data.Path]);

        Assert.Equal(here.Output, there.Output);
        Assert.Equal(here.Output, oneByOne.Output);
        Assert.DoesNotContain(copy.Path, there.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFrozenDataIsValidAndHasEveryFileTheLoaderNeeds()
    {
        var (exitCode, output, errors) = ToolOutput(["datacheck", Repository.GoldenDataFolder]);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Contains("is valid.", output, StringComparison.Ordinal);
        Assert.Equal(6, GoldenData.Tracks.Count);
        Assert.Equal(GoldenData.TierCount - 1, GoldenData.Research.Count);
        Assert.NotEmpty(GoldenData.Accents);
        Assert.Equal(["ridge", "switchback"], GoldenData.Maps.Select(map => map.Id).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Golden replays run on the frozen data and not on the game's, so that a change to
    /// <c>game/data</c> never changes them. Today the two differ at least by the map made for
    /// the tests.
    /// </summary>
    [Fact]
    public void TheGoldenReplaysNameTheFrozenDataAndNotTheGameData()
    {
        foreach (var name in GoldenNames)
        {
            Assert.Equal(GoldenData.ContentHash, ReadGolden(name).Description.ContentHash);
        }

        Assert.NotEqual(GoldenData.ContentHash, Load(Repository.DataFolder).ContentHash);
        var (exitCode, output, errors) = Run("verify", Repository.GoldenReplaysFolder, "--data", Repository.DataFolder);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.All(Lines(output), line => Assert.Matches("^FAILED  [a-z-]+\\.replay  The match was made with other data: its content hash is [0-9a-f]{64} and this data has [0-9a-f]{64}\\.$", line));
        Assert.Equal(GoldenNames.Length, Lines(output).Length);
        Assert.Contains($"replay: {GoldenNames.Length} of {GoldenNames.Length} replays did not reproduce.", errors, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every golden replay is a whole recording that <c>replay record</c> made. What each must
    /// reach is listed by the tests of the script it was recorded for.
    /// </summary>
    [Fact]
    public void TheGoldenReplaysAreWholeAndScripted()
    {
        foreach (var name in GoldenNames)
        {
            var replay = ReadGolden(name);
            Assert.True(replay.IsComplete, name);
            Assert.Equal(ReplayRecorder.DefaultHashInterval, replay.HashInterval);
            Assert.Equal(ReplayTool.ScriptedGameVersion, replay.Description.GameVersion);
        }
    }

    /// <summary>One changed hash in one file fails the run and names the tick. The other files are still reported.</summary>
    [Fact]
    public void AReplayWithAChangedHashFails()
    {
        using var copy = Repository.CopyOf(Repository.GoldenReplaysFolder);
        var path = Path.Combine(copy.Path, Crossing);
        var bytes = File.ReadAllBytes(path);
        var recorded = ReadGolden(Crossing).Hashes[7];
        var at = bytes.AsSpan().IndexOf(BitConverter.GetBytes(recorded.Hash));
        bytes[at] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        var (exitCode, output, errors) = Run("verify", copy.Path, "--data", Repository.GoldenDataFolder);

        Assert.Equal(ExitCodes.Failed, exitCode);
        var lines = Lines(output);
        Assert.Equal(GoldenNames.Length, lines.Length);
        for (var i = 0; i < GoldenNames.Length; i++)
        {
            if (GoldenNames[i] != Crossing)
            {
                Assert.StartsWith($"ok      {GoldenNames[i]}  ", lines[i], StringComparison.Ordinal);
                continue;
            }

            Assert.Matches(
                $"^FAILED  switchback-crossing.replay  map=switchback players=2 ticks=140 commands=[0-9]+ hashes=8 final={recorded.Hash:x16}  "
                + $"The state differs from the recording after 140 ticks: recorded {recorded.Hash ^ 1:x16}, computed {recorded.Hash:x16}\\.$",
                lines[i]);
        }

        Assert.Equal($"replay: 1 of {GoldenNames.Length} replays did not reproduce.", errors.Trim());
    }

    /// <summary>A changed command changes the match: the first hash after it differs.</summary>
    [Fact]
    public void AReplayWithAChangedCommandFails()
    {
        using var copy = new TemporaryFolder();
        var replay = ReadGolden(Skirmish);
        var bytes = File.ReadAllBytes(Path.Combine(Repository.GoldenReplaysFolder, Skirmish));
        var first = replay.Commands.First(entry => entry.Command.Type == CommandType.Move && entry.Command.Entities.Length == 1);
        var encoded = new ByteWriter();
        CommandEncoding.Write(encoded, first.Command);
        var at = bytes.AsSpan().IndexOf(encoded.WrittenSpan);
        var changed = new Command(CommandType.Move, first.Command.Player, first.Command.Entities, first.Command.TargetY, first.Command.TargetX);
        encoded.Clear();
        CommandEncoding.Write(encoded, changed);
        encoded.WrittenSpan.CopyTo(bytes.AsSpan(at));
        File.WriteAllBytes(Path.Combine(copy.Path, "changed.replay"), bytes);

        var (exitCode, output, _) = Run("verify", copy.Path, "--data", Repository.GoldenDataFolder);

        var due = ((first.Tick / 20) + 1) * 20;
        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Matches($"^FAILED  changed.replay  .* The state differs from the recording after {due} ticks: ", output);
    }

    [Fact]
    public void AReplayCutShortIsReportedAsPartialAndFails()
    {
        using var copy = new TemporaryFolder();
        var bytes = File.ReadAllBytes(Path.Combine(Repository.GoldenReplaysFolder, Skirmish));
        File.WriteAllBytes(Path.Combine(copy.Path, "cut.replay"), bytes[..^5]);
        File.WriteAllBytes(Path.Combine(copy.Path, "stopped.replay"), bytes[..^13]);

        var (exitCode, output, errors) = Run("verify", copy.Path, "--data", Repository.GoldenDataFolder);

        Assert.Equal(ExitCodes.Failed, exitCode);
        var lines = Lines(output);
        Assert.Matches($"^PARTIAL cut.replay  map=ridge players=2 ticks=3600 .* The file is damaged or cut short at byte {bytes.Length - 13}\\. What came before it reproduced\\.$", lines[0]);
        Assert.Matches("^PARTIAL stopped.replay  map=ridge players=2 ticks=3600 .* The recording stops without an end record\\. What it has reproduced\\.$", lines[1]);
        Assert.Equal("replay: 2 of 2 replays did not reproduce.", errors.Trim());
    }

    [Fact]
    public void AFileThatIsNoReplayFails()
    {
        using var copy = new TemporaryFolder();
        File.WriteAllText(Path.Combine(copy.Path, "text.replay"), "This is not a replay.");
        File.WriteAllBytes(Path.Combine(copy.Path, "empty.replay"), []);

        var (exitCode, output, errors) = Run("verify", copy.Path, "--data", Repository.GoldenDataFolder);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal(
            ["FAILED  empty.replay  The data ends too soon: 4 bytes are needed at offset 0 and 0 are left.", "FAILED  text.replay  The data is not a replay: it does not start with RPLY."],
            Lines(output));
        Assert.Equal("replay: 2 of 2 replays did not reproduce.", errors.Trim());
    }

    /// <summary>Replays in folders below the one named are found, and listed in the order of their names.</summary>
    [Fact]
    public void ReplaysAreFoundInSubfoldersAndListedByName()
    {
        using var copy = new TemporaryFolder();
        var source = Path.Combine(Repository.GoldenReplaysFolder, Skirmish);
        // No two names differ only in case: Windows and macOS would take them for one file.
        foreach (var name in (string[])["b.replay", "a/z.replay", "a/b/c.replay", "Y.replay", "notes.txt"])
        {
            var target = Path.Combine(copy.Path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }

        var (exitCode, output, _) = Run("verify", copy.Path, "--data", Repository.GoldenDataFolder);

        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Equal(["Y.replay", "a/b/c.replay", "a/z.replay", "b.replay"], Lines(output)[..^1].Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]));
        Assert.Equal("replay: 4 replays reproduced.", Lines(output)[^1]);
    }

    public static TheoryData<string[], string> WrongArguments => new()
    {
        { [], "say what to do: verify, update or record." },
        { ["play", "x"], "unknown action 'play'." },
        { ["verify"], "verify needs a file or a folder." },
        { ["update", "--data", "game/data"], "update needs a file or a folder." },
        { ["verify", "x", "--date", "y"], "unknown option '--date'." },
        { ["verify", "x", "--data"], "--data needs a value." },
        { ["verify", "x", "--map", "ridge"], "unknown option '--map'." },
        { ["update", "x", "--ticks", "5"], "unknown option '--ticks'." },
        { ["record", "--map", "ridge"], "record needs one file to write and --map." },
        { ["record", "a.replay"], "record needs one file to write and --map." },
        { ["record", "a.replay", "b.replay", "--map", "ridge"], "record needs one file to write and --map." },
        { ["record", "a.replay", "--map", "ridge", "--ticks", "-5"], "--ticks cannot be '-5'." },
        { ["record", "a.replay", "--map", "ridge", "--ticks", "many"], "--ticks cannot be 'many'." },
        { ["record", "a.replay", "--map", "ridge", "--seed", "0x10"], "--seed cannot be '0x10'." },
        { ["record", "a.replay", "--map", "ridge", "--orders", "1.5"], "--orders cannot be '1.5'." },
        { ["record", "a.replay", "--map", "ridge", "--starts", "0;1"], "--starts cannot be '0;1'." },
        { ["record", "a.replay", "--map", "ridge", "--starts", "0,,1"], "--starts cannot be '0,,1'." },
        { ["record", "a.replay", "--map", "ridge", "--script", "dancing"], "--script cannot be 'dancing'." },
        { ["record", "a.replay", "--map", "ridge", "--script", "movement,dancing"], "--script cannot be 'movement,dancing'." },
        { ["record", "a.replay", "--map", "ridge", "--script", "movement,movement"], "--script cannot be 'movement,movement'." },
        { ["record", "a.replay", "--map", "ridge", "--script", "movement,"], "--script cannot be 'movement,'." },
        { ["record", "a.replay", "--map", "ridge", "--script", ""], "--script cannot be ''." },
        { ["record", "a.replay", "--map", "ridge", "--script"], "--script needs a value." },
        { ["verify", "x", "--script", "movement"], "unknown option '--script'." },
    };

    [Theory]
    [MemberData(nameof(WrongArguments))]
    public void WrongArgumentsGetTheUsage(string[] arguments, string message)
    {
        var (exitCode, output, errors) = Run(arguments);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.StartsWith($"replay: {message}", errors, StringComparison.Ordinal);
        Assert.Contains("replay verify <file or folder>...", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatIsNotThereIsNamed()
    {
        using var empty = new TemporaryFolder();
        var missing = Path.Combine(empty.Path, "missing");

        var noFile = Run("verify", missing, "--data", Repository.GoldenDataFolder);
        var noReplays = Run("verify", empty.Path, "--data", Repository.GoldenDataFolder);
        var noData = Run("verify", Repository.GoldenReplaysFolder, "--data", missing);

        Assert.Equal((ExitCodes.Usage, "", $"replay: there is no file or folder '{missing}'."), (noFile.ExitCode, noFile.Output, noFile.Errors.Trim()));
        Assert.Equal((ExitCodes.Usage, "", $"replay: the folder '{empty.Path}' has no file that ends in .replay."), (noReplays.ExitCode, noReplays.Output, noReplays.Errors.Trim()));
        Assert.Equal((ExitCodes.Usage, "", $"replay: there is no folder '{missing}'."), (noData.ExitCode, noData.Output, noData.Errors.Trim()));
    }

    [Fact]
    public void DataThatIsNotValidFails()
    {
        using var data = Repository.CopyOf(Repository.GoldenDataFolder);
        data.Edit("rules.jsonc", "\"workers\": 6", "\"workers\": \"six\"");

        var (exitCode, output, errors) = Run("verify", Repository.GoldenReplaysFolder, "--data", data.Path);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal("", output);
        Assert.Equal($"replay: the data in {data.Path} is not valid. Run datacheck on it.", errors.Trim());
    }

    [Fact]
    public void ARecordedMatchVerifies()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "new", "match.replay");

        var recorded = Run("record", path, "--map", "ridge", "--data", Repository.GoldenDataFolder, "--ticks", "450", "--seed", "18446744073709551615", "--orders", "4", "--starts", "1,-,0");
        var verified = Run("verify", path, "--data", Repository.GoldenDataFolder);

        Assert.Equal("", recorded.Errors);
        Assert.Equal(ExitCodes.Ok, recorded.ExitCode);
        Assert.Matches("^recorded   match.replay  map=ridge players=3 ticks=450 commands=[0-9]+ final=[0-9a-f]{16}$", recorded.Output.Trim());
        Assert.Equal(ExitCodes.Ok, verified.ExitCode);
        Assert.Matches("^ok      match.replay  map=ridge players=3 ticks=450 commands=[0-9]+ hashes=24 final=[0-9a-f]{16}$", Lines(verified.Output)[0]);
        Assert.Equal(recorded.Output.Trim().Split("final=")[1], Lines(verified.Output)[0].Split("final=")[1]);

        var replay = Sim.Serialization.Replay.Read(new ByteReader(File.ReadAllBytes(path)));
        Assert.Equal(ulong.MaxValue, replay.Description.Seed);
        Assert.Equal([1, -1, 0], replay.Description.Players.Select(player => player.Start));
        Assert.Equal(GoldenData.ContentHash, replay.Description.ContentHash);
        Assert.True(replay.Commands.Count > 5);
    }

    /// <summary>The scripted orders come from their seed alone: recording twice gives the same file, and another seed another.</summary>
    [Fact]
    public void RecordingIsRepeatable()
    {
        using var folder = new TemporaryFolder();
        string[] common = ["--map", "switchback", "--data", Repository.GoldenDataFolder, "--ticks", "300"];
        string File(string name) => Path.Combine(folder.Path, name);

        Assert.Equal(ExitCodes.Ok, Run(["record", File("a.replay"), .. common]).ExitCode);
        Assert.Equal(ExitCodes.Ok, Run(["record", File("b.replay"), .. common, "--orders", "1", "--seed", "1", "--starts", "0,1"]).ExitCode);
        Assert.Equal(ExitCodes.Ok, Run(["record", File("c.replay"), .. common, "--orders", "2"]).ExitCode);
        Assert.Equal(ExitCodes.Ok, Run(["record", File("d.replay"), .. common, "--seed", "2"]).ExitCode);

        var a = System.IO.File.ReadAllBytes(File("a.replay"));
        Assert.Equal(a, System.IO.File.ReadAllBytes(File("b.replay")));
        Assert.NotEqual(a, System.IO.File.ReadAllBytes(File("c.replay")));
        Assert.NotEqual(a, System.IO.File.ReadAllBytes(File("d.replay")));
    }

    /// <summary>
    /// The usage lists the scripts there are, which the tool finds itself, in the order they give
    /// their orders in a tick: the order of their names.
    /// </summary>
    [Fact]
    public void TheUsageNamesTheScripts()
    {
        var names = ScriptNames();

        Assert.Contains("movement", names);
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.All(names, name => Assert.Matches("^[a-z]+(-[a-z]+)*$", name));
    }

    /// <summary>
    /// <c>--script</c> names the kinds of order of a recording. Without it every script gives its
    /// orders, and the order the names are listed in changes nothing.
    /// </summary>
    [Fact]
    public void WithoutScriptEveryScriptIsUsedAndTheNamesAreInAnyOrder()
    {
        using var folder = new TemporaryFolder();
        var names = ScriptNames();
        var path = Path.Combine(folder.Path, "match.replay");
        byte[] Recorded(params string[] script)
        {
            Assert.Equal(ExitCodes.Ok, Run(["record", path, "--map", "switchback", "--data", Repository.GoldenDataFolder, "--ticks", "900", "--orders", "6", .. script]).ExitCode);
            return File.ReadAllBytes(path);
        }

        var withoutScript = Recorded();

        Assert.Equal(withoutScript, Recorded("--script", string.Join(',', names)));
        Assert.Equal(withoutScript, Recorded("--script", string.Join(',', names.Reverse())));
    }

    /// <summary>Each script records a match on its own, without the orders of the others.</summary>
    [Fact]
    public void EveryScriptRecordsAMatchOnItsOwn()
    {
        using var folder = new TemporaryFolder();
        foreach (var name in ScriptNames())
        {
            var path = Path.Combine(folder.Path, name + ".replay");

            var recorded = Run("record", path, "--map", "ridge", "--data", Repository.GoldenDataFolder, "--ticks", "900", "--script", name);

            Assert.Equal((ExitCodes.Ok, ""), (recorded.ExitCode, recorded.Errors));
        }

        var verified = Run("verify", folder.Path, "--data", Repository.GoldenDataFolder);
        Assert.Equal((ExitCodes.Ok, ""), (verified.ExitCode, verified.Errors));
    }

    [Theory]
    [InlineData("nowhere", "0,1", "The data has no map 'nowhere'.")]
    [InlineData("ridge", "0,2", "Player 1 has start 2, but map 'ridge' has 2 starts.")]
    [InlineData("ridge", "0,0", "Players 0 and 1 share start 0.")]
    [InlineData("ridge", "0,1,-,-,-,-,-,-,-", "A match has 1 to 8 players, not 9.")]
    public void AMatchThatCannotBeSetUpIsNotRecorded(string map, string starts, string message)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "match.replay");

        var (exitCode, output, errors) = Run("record", path, "--map", map, "--starts", starts, "--data", Repository.GoldenDataFolder);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.Equal($"replay: {message}", errors.Trim());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void UpdatingReplaysThatReproduceChangesNothing()
    {
        using var copy = Repository.CopyOf(Repository.GoldenReplaysFolder);

        var (exitCode, output, errors) = Run("update", copy.Path, "--data", Repository.GoldenDataFolder);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Equal(GoldenNames.Select(name => $"unchanged  {name}"), Lines(output));
        foreach (var name in GoldenNames)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(Repository.GoldenReplaysFolder, name)), File.ReadAllBytes(Path.Combine(copy.Path, name)));
        }
    }

    /// <summary>
    /// The path a deliberate change to the rules or the frozen data takes: the replays stop
    /// reproducing, <c>update</c> runs their commands again and stores the new hashes, and they
    /// reproduce with the same commands as before.
    /// </summary>
    [Fact]
    public void UpdatingAfterAChangeToTheDataStoresTheNewHashes()
    {
        using var replays = Repository.CopyOf(Repository.GoldenReplaysFolder);
        using var data = Repository.CopyOf(Repository.GoldenDataFolder);
        data.Edit("rules.jsonc", "\"workers\": 6", "\"workers\": 5");
        var before = ReadGolden(Crossing);

        var stale = Run("verify", replays.Path, "--data", data.Path);
        var update = Run("update", replays.Path, "--data", data.Path);
        var fresh = Run("verify", replays.Path, "--data", data.Path);
        var again = Run("update", replays.Path, "--data", data.Path);

        Assert.Equal(ExitCodes.Failed, stale.ExitCode);
        Assert.Contains("other data", stale.Output, StringComparison.Ordinal);
        Assert.Equal((ExitCodes.Ok, ""), (update.ExitCode, update.Errors));
        Assert.Equal(GoldenNames.Select(name => $"updated    {name}"), Lines(update.Output));
        Assert.Equal((ExitCodes.Ok, ""), (fresh.ExitCode, fresh.Errors));
        Assert.Equal(GoldenNames.Select(name => $"unchanged  {name}"), Lines(again.Output));

        var after = Sim.Serialization.Replay.Read(new ByteReader(File.ReadAllBytes(Path.Combine(replays.Path, Crossing))));
        Assert.Equal(Load(data.Path).ContentHash, after.Description.ContentHash);
        Assert.NotEqual(before.Description.ContentHash, after.Description.ContentHash);
        Assert.Equal(before.Description.Seed, after.Description.Seed);
        Assert.Equal(before.Description.GameVersion, after.Description.GameVersion);
        Assert.Equal(before.Commands.Count, after.Commands.Count);
        Assert.Equal(before.TickCount, after.TickCount);
        Assert.NotEqual(before.End, after.End);
        Assert.NotEqual(before.Hashes[0], after.Hashes[0]);
    }

    [Fact]
    public void UpdatingPutsAChangedHashRightAndFinishesACutFile()
    {
        using var copy = new TemporaryFolder();
        var golden = File.ReadAllBytes(Path.Combine(Repository.GoldenReplaysFolder, Skirmish));
        var changed = (byte[])golden.Clone();
        changed[^3] ^= 0x40;
        File.WriteAllBytes(Path.Combine(copy.Path, "changed.replay"), changed);
        File.WriteAllBytes(Path.Combine(copy.Path, "cut.replay"), golden[..^5]);

        var (exitCode, output, _) = Run("update", copy.Path, "--data", Repository.GoldenDataFolder);

        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Equal(["updated    changed.replay", "updated    cut.replay"], Lines(output));
        Assert.Equal(golden, File.ReadAllBytes(Path.Combine(copy.Path, "changed.replay")));
        Assert.Equal(ExitCodes.Ok, Run("verify", copy.Path, "--data", Repository.GoldenDataFolder).ExitCode);
    }

    [Fact]
    public void UpdatingAFileThatIsNoReplayFailsAndLeavesIt()
    {
        using var copy = Repository.CopyOf(Repository.GoldenReplaysFolder);
        var path = Path.Combine(copy.Path, "text.replay");
        File.WriteAllText(path, "This is not a replay.");

        var (exitCode, output, errors) = Run("update", copy.Path, "--data", Repository.GoldenDataFolder);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal("replay: text.replay: The data is not a replay: it does not start with RPLY.", errors.Trim());
        Assert.Equal(GoldenNames.Length, Lines(output).Length);
        Assert.Equal("This is not a replay.", File.ReadAllText(path));
    }

    [Fact]
    public void TheToolIsListed()
    {
        var (exitCode, output, _) = ToolOutput(["--help"]);

        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Matches("(?m)^ *replay +Run recorded matches again", output);
        Assert.Equal("game/data", ReplayTool.DefaultFolder);
    }

    /// <summary>The names of the scripts, as the usage lists them.</summary>
    private static string[] ScriptNames()
    {
        var usage = Run().Errors;
        var list = Regex.Match(usage, @"default all of them: ([^.\s]+)\.");
        Assert.True(list.Success, usage);
        return list.Groups[1].Value.Split(',');
    }

    private static Sim.Serialization.Replay ReadGolden(string name) =>
        Sim.Serialization.Replay.Read(new ByteReader(File.ReadAllBytes(Path.Combine(Repository.GoldenReplaysFolder, name))));

    private static GameData Load(string folder)
    {
        var result = DataLoader.Load(new DiskDataFiles(folder));
        Assert.True(result.IsValid, $"The data in {folder} is not valid.");
        return result.Data;
    }

    private static string[] Lines(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static (int ExitCode, string Output, string Errors) Run(params string[] arguments) => ToolOutput(["replay", .. arguments]);

    private static (int ExitCode, string Output, string Errors) ToolOutput(string[] arguments)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var exitCode = ToolRunner.Run(arguments, output, errors);
        return (exitCode, output.ToString(), errors.ToString());
    }
}
