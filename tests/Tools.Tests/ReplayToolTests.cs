using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Movement;
using Sim.Research;
using Sim.Serialization;
using Tools.Replay;

namespace Tools.Tests;

/// <summary>
/// Runs <c>replay</c> the way the command line and CI do: on the recorded replays of
/// <c>tests/golden</c>, which must reproduce, and on copies with something changed, which must
/// fail and say where.
/// </summary>
public class ReplayToolTests
{
    private static readonly string[] GoldenNames =
        ["ridge-skirmish.replay", "switchback-crossing.replay", "switchback-research.replay", "switchback-three-players.replay", "switchback-workers.replay"];

    private static GameData GoldenData { get; } = Load(Repository.GoldenDataFolder);

    /// <summary>The check CI runs. It fails when a change to the rules changes a recorded match.</summary>
    [Fact]
    public void TheGoldenReplaysReproduce()
    {
        var (exitCode, output, errors) = Run("verify", Repository.GoldenReplaysFolder, "--data", Repository.GoldenDataFolder);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        var lines = Lines(output);
        Assert.Equal(GoldenNames.Length + 1, lines.Length);
        for (var i = 0; i < GoldenNames.Length; i++)
        {
            Assert.Matches($"^ok      {GoldenNames[i]}  map=[a-z]+ players=[23] ticks=[0-9]+ commands=[0-9]+ hashes=[0-9]+ final=[0-9a-f]{{16}}$", lines[i]);
        }

        Assert.Equal("replay: 5 replays reproduced.", lines[^1]);
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
        Assert.Equal(5, Lines(output).Length);
        Assert.Contains("replay: 5 of 5 replays did not reproduce.", errors, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the recorded matches are for: between them they must reach the rules that a change
    /// could break. A milestone that adds rules adds a replay that reaches them, and a line here.
    /// </summary>
    [Fact]
    public void TheGoldenReplaysReachTheRulesTheyGuard()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in GoldenNames)
        {
            var replay = ReadGolden(name);
            Assert.True(replay.IsComplete);
            Assert.Equal(ReplayRecorder.DefaultHashInterval, replay.HashInterval);
            Assert.Equal(ReplayTool.ScriptedGameVersion, replay.Description.GameVersion);
            seen.Add($"map {replay.Description.MapId}");
            seen.Add($"{replay.Description.Players.Count} players");
            if (replay.Description.Players.Any(player => !player.HasStart))
            {
                seen.Add("a player without a start");
            }

            if (replay.Description.Players[0].Start > replay.Description.Players[^1].Start)
            {
                seen.Add("starts swapped");
            }

            var match = replay.Description.CreateMatch(GoldenData);
            seen.UnionWith(KindsOfCommands(replay, match));
            foreach (var (tick, command) in replay.Commands)
            {
                match.Simulation.Commands.Enqueue(tick, command);
            }

            match.Research.Reported += reported => seen.Add(reported.Kind switch
            {
                ResearchEventKind.Started => "a research started and paid for",
                ResearchEventKind.Cancelled => "a research cancelled and refunded",
                ResearchEventKind.Finished => "a research finished and the tier raised",
                _ => $"research event {reported.Kind}",
            });
            var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
            var waited = new int[match.Movers.Count];
            var workers = new Worker[match.Workers.Count];
            long[] materials = [.. Enumerable.Range(0, match.Players.Count).Select(match.Stockpiles.Materials)];
            for (var tick = 0; tick < replay.TickCount; tick++)
            {
                seen.UnionWith(ResearchOrdersRefused(match, due[tick]));
                match.Simulation.Tick();
                seen.UnionWith(WhatTheWorkersDid(match, workers, materials));
                for (var i = 0; i < match.Movers.Count; i++)
                {
                    var state = match.Movers.ValueAt(i).State;
                    waited[i] = state == MoveState.Planning ? waited[i] + 1 : 0;
                    seen.Add(state switch
                    {
                        MoveState.Idle => "a unit standing",
                        MoveState.Moving => "a unit walking straight",
                        MoveState.Following => "a unit walking a route",
                        _ => waited[i] > 1 ? "a unit waiting more than a tick for a route" : "a unit waiting for a route",
                    });
                }
            }
        }

        string[] expected =
        [
            "map ridge",
            "map switchback",
            "2 players",
            "3 players",
            "a player without a start",
            "starts swapped",
            .. EveryKindOfCommand,
            "a unit standing",
            "a unit walking straight",
            "a unit walking a route",
            "a unit waiting for a route",
            "a unit waiting more than a tick for a route",
            "a research started and paid for",
            "a research cancelled and refunded",
            "a research finished and the tier raised",
            "a research refused: its track is in research already",
            "a research refused: no free slot",
            "a research refused: the stockpile does not cover it",
            "a research refused: the player has no slot",
            "a cancel refused: the track is not in research",
            "a worker walking to its place",
            "a worker at work",
            "a worker waiting for a place",
            "a worker given a place it waited for",
            "a worker taken off its work by another order",
            "a payment",
            "a payment skipped at a deposit outside its owner's territory",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The generator behind <c>replay record</c> gives every kind of command it is meant to, so
    /// that a match recorded with it reaches the rules: here in one long match on each map. The
    /// rarest kinds come about once in 6,000 ticks, so a shorter match can miss one.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptedOrdersHaveEveryKindOfCommand(string map)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "long.replay");

        var (exitCode, _, errors) = Run("record", path, "--map", map, "--data", Repository.GoldenDataFolder, "--ticks", "30000", "--orders", "33");

        Assert.Equal((ExitCodes.Ok, ""), (exitCode, errors));
        var replay = Sim.Serialization.Replay.Read(new ByteReader(File.ReadAllBytes(path)));
        var seen = KindsOfCommands(replay, replay.Description.CreateMatch(GoldenData));
        Assert.Equal(EveryKindOfCommand.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(GoldenData).Reproduced);
    }

    /// <summary>One changed hash in one file fails the run and names the tick. The other files are still reported.</summary>
    [Fact]
    public void AReplayWithAChangedHashFails()
    {
        using var copy = Repository.CopyOf(Repository.GoldenReplaysFolder);
        var path = Path.Combine(copy.Path, GoldenNames[1]);
        var bytes = File.ReadAllBytes(path);
        var recorded = ReadGolden(GoldenNames[1]).Hashes[7];
        var at = bytes.AsSpan().IndexOf(BitConverter.GetBytes(recorded.Hash));
        bytes[at] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        var (exitCode, output, errors) = Run("verify", copy.Path, "--data", Repository.GoldenDataFolder);

        Assert.Equal(ExitCodes.Failed, exitCode);
        var lines = Lines(output);
        Assert.Equal(5, lines.Length);
        Assert.StartsWith("ok      ridge-skirmish.replay", lines[0], StringComparison.Ordinal);
        Assert.Matches(
            $"^FAILED  switchback-crossing.replay  map=switchback players=2 ticks=140 commands=[0-9]+ hashes=8 final={recorded.Hash:x16}  "
            + $"The state differs from the recording after 140 ticks: recorded {recorded.Hash ^ 1:x16}, computed {recorded.Hash:x16}\\.$",
            lines[1]);
        Assert.StartsWith("ok      switchback-research.replay", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("ok      switchback-three-players.replay", lines[3], StringComparison.Ordinal);
        Assert.StartsWith("ok      switchback-workers.replay", lines[4], StringComparison.Ordinal);
        Assert.Equal("replay: 1 of 5 replays did not reproduce.", errors.Trim());
    }

    /// <summary>A changed command changes the match: the first hash after it differs.</summary>
    [Fact]
    public void AReplayWithAChangedCommandFails()
    {
        using var copy = new TemporaryFolder();
        var replay = ReadGolden(GoldenNames[0]);
        var bytes = File.ReadAllBytes(Path.Combine(Repository.GoldenReplaysFolder, GoldenNames[0]));
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
        var bytes = File.ReadAllBytes(Path.Combine(Repository.GoldenReplaysFolder, GoldenNames[0]));
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
        var source = Path.Combine(Repository.GoldenReplaysFolder, GoldenNames[2]);
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
        var before = ReadGolden(GoldenNames[1]);

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

        var after = Sim.Serialization.Replay.Read(new ByteReader(File.ReadAllBytes(Path.Combine(replays.Path, GoldenNames[1]))));
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
        var golden = File.ReadAllBytes(Path.Combine(Repository.GoldenReplaysFolder, GoldenNames[2]));
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
        Assert.Equal(5, Lines(output).Length);
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

    private static readonly string[] EveryKindOfCommand =
    [
        "a move",
        "a command of a kind without a handler",
        "a command from nobody in the match",
        "an order for one unit",
        "an order for a group",
        "an order for each unit of every player in one tick",
        "a target off the map",
        "a target as far off the map as a number goes",
        "an order for units that are not the player's",
        "an order for an entity that never existed",
        "a research order",
        "a cancel of a research",
        "a research order for a track that does not exist",
        "a research order from nobody in the match",
        "a gather order",
        "a gather order for a deposit the player holds",
        "a gather order for a deposit the player does not hold",
        "gather orders from every player for one deposit in one tick",
        "a gather order at something that is no work site",
    ];

    /// <summary>What the commands of a replay are, in the words of <see cref="EveryKindOfCommand"/>.</summary>
    private static HashSet<string> KindsOfCommands(Sim.Serialization.Replay replay, Sim.Setup.Match match)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var players = match.Players.Count;
        var unitsWithAnOrderOfTheirOwn = new HashSet<EntityId>();
        var tickOfThose = -1;
        foreach (var (tick, command) in replay.Commands)
        {
            seen.Add(command.Type switch
            {
                CommandType.Move => "a move",
                CommandType.Gather => "a gather order",
                CommandType.StartResearch => "a research order",
                CommandType.CancelResearch => "a cancel of a research",
                _ => "a command of a kind without a handler",
            });
            if (command.Type is CommandType.StartResearch or CommandType.CancelResearch)
            {
                if (command.Player >= players)
                {
                    seen.Add("a research order from nobody in the match");
                }
                else if (command.ContentId < 0 || command.ContentId >= match.Data.Tracks.Count)
                {
                    seen.Add("a research order for a track that does not exist");
                }

                continue;
            }

            if (command.Player >= players)
            {
                seen.Add("a command from nobody in the match");
            }

            if (command.Type is not (CommandType.Move or CommandType.Gather) || command.Player >= players)
            {
                continue;
            }

            if (command.Type == CommandType.Gather)
            {
                if (!match.WorkSites.TryGet(command.TargetEntity, out var site))
                {
                    seen.Add("a gather order at something that is no work site");
                    continue;
                }

                // The territories of a scripted match are those of the start and do not change.
                seen.Add(Territory.Holds(match.Territories, match.World.Entities, command.Player, site.X, site.Y, site.Size)
                    ? "a gather order for a deposit the player holds"
                    : "a gather order for a deposit the player does not hold");
                var others = replay.Commands.Count(other =>
                    other.Tick == tick && other.Command.Type == CommandType.Gather && other.Command.TargetEntity == command.TargetEntity && other.Command.Player != command.Player);
                if (others == players - 1)
                {
                    seen.Add("gather orders from every player for one deposit in one tick");
                }
            }

            seen.Add(command.Entities.Length == 1 ? "an order for one unit" : "an order for a group");
            if (command.Type == CommandType.Move && !match.Grid.Contains(new Sim.Numerics.FixedVector2(command.TargetX, command.TargetY)))
            {
                seen.Add(command.TargetX == Sim.Numerics.Fixed.MaxValue ? "a target as far off the map as a number goes" : "a target off the map");
            }

            foreach (var entity in command.Entities)
            {
                if (!match.Bodies.TryGet(entity, out var body))
                {
                    seen.Add("an order for an entity that never existed");
                }
                else if (body.Owner != command.Player)
                {
                    seen.Add("an order for units that are not the player's");
                }
            }

            if (command.Type == CommandType.Move && command.Entities.Length == 1)
            {
                if (tick != tickOfThose)
                {
                    unitsWithAnOrderOfTheirOwn.Clear();
                    tickOfThose = tick;
                }

                unitsWithAnOrderOfTheirOwn.Add(command.Entities[0]);
                if (unitsWithAnOrderOfTheirOwn.Count == match.Bodies.Count)
                {
                    seen.Add("an order for each unit of every player in one tick");
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// What the tick that has just run did to the workers, in the words of the list of
    /// <see cref="TheGoldenReplaysReachTheRulesTheyGuard"/>. The two arrays hold what the workers
    /// and the stockpiles of Materials were before it, and are brought up to date.
    /// </summary>
    private static HashSet<string> WhatTheWorkersDid(Sim.Setup.Match match, Worker[] before, long[] materialsBefore)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < before.Length; i++)
        {
            var worker = match.Workers.ValueAt(i);
            var owner = match.Bodies.Get(match.Workers.EntityAt(i)).Owner;
            switch (worker.State)
            {
                case WorkerState.Walking:
                    seen.Add(before[i].State == WorkerState.Waiting ? "a worker given a place it waited for" : "a worker walking to its place");
                    break;
                case WorkerState.Working:
                    seen.Add("a worker at work");
                    var site = match.WorkSites.Get(worker.Site);
                    if (before[i].Timer == Sim.SimInfo.TicksPerSecond - 1
                        && worker.Timer == 0
                        && !Territory.Holds(match.Territories, match.World.Entities, owner, site.X, site.Y, site.Size))
                    {
                        seen.Add("a payment skipped at a deposit outside its owner's territory");
                    }

                    break;
                case WorkerState.Waiting:
                    seen.Add("a worker waiting for a place");
                    break;
                case WorkerState.Idle when before[i].State == WorkerState.Working:
                    seen.Add("a worker taken off its work by another order");
                    break;
                default:
                    break;
            }

            before[i] = worker;
        }

        for (var player = 0; player < materialsBefore.Length; player++)
        {
            var materials = match.Stockpiles.Materials(player);
            if (materials > materialsBefore[player])
            {
                seen.Add("a payment");
            }

            materialsBefore[player] = materials;
        }

        return seen;
    }

    /// <summary>
    /// Why the research orders due in the tick that runs next will be dropped, for those that
    /// will. An order is judged only when it is its player's one research order of the tick, so
    /// that the state it is judged against here is the state it runs in.
    /// </summary>
    private static IEnumerable<string> ResearchOrdersRefused(Sim.Setup.Match match, IEnumerable<Command> due)
    {
        var orders = due.Where(command => command.Type is CommandType.StartResearch or CommandType.CancelResearch).ToList();
        var tracks = match.Data.Tracks.Count;
        foreach (var order in orders)
        {
            var alone = orders.Count(other => other.Player == order.Player) == 1;
            if (!alone || order.Player >= match.Players.Count || order.ContentId < 0 || order.ContentId >= tracks)
            {
                continue;
            }

            if (order.Type == CommandType.CancelResearch)
            {
                if (match.Research.State.SlotResearching(order.Player, order.ContentId) < 0)
                {
                    yield return "a cancel refused: the track is not in research";
                }

                continue;
            }

            var check = match.Research.CheckStart(order.Player, order.ContentId);
            if (check == ResearchStartCheck.NoFreeSlot && !match.Players[order.Player].HasStart)
            {
                yield return "a research refused: the player has no slot";
            }
            else if (check != ResearchStartCheck.Allowed)
            {
                yield return check switch
                {
                    ResearchStartCheck.InProgress => "a research refused: its track is in research already",
                    ResearchStartCheck.NoFreeSlot => "a research refused: no free slot",
                    ResearchStartCheck.TooExpensive => "a research refused: the stockpile does not cover it",
                    _ => $"a research refused: {check}",
                };
            }
        }
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
