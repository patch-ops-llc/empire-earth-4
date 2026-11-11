using Net;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Setup;
using Tools.DesyncDiff;

namespace Tools.Tests;

/// <summary>
/// Runs <c>desync-diff</c> the way the command line does, on the files two peers leave when
/// their match stops on a desync: each a replay with a hash for every tick, and a snapshot.
/// </summary>
public class DesyncDiffToolTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private static GameData Data { get; } = DataLoader.Load(new DiskDataFiles(Repository.DataFolder)).Data!;

    /// <summary>
    /// The whole path once: two peers play over the session, one goes wrong, the host finds the
    /// hashes differ and stops both at one tick, each has its replay with a hash for every tick
    /// and writes a snapshot, and the tool says when they parted and what is different.
    /// </summary>
    [Fact]
    public void ADesyncProvokedOnPurposeLeavesTwoSnapshotsTheToolTellsApart()
    {
        using var folder = new TemporaryFolder();
        var host = new MatchHost(Data, Description());
        var network = new LoopbackNetwork(seed: 2);
        var link = network.Connect(new LinkConditions(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(20)));
        Assert.True(host.TryAttach(link.HostEnd));
        var guest = new MatchClient(link.ClientEnd, Data, "test", readyWhenBuilt: true);
        using var hostReplay = File.Create(Path.Combine(folder.Path, "host.replay"));
        using var guestReplay = File.Create(Path.Combine(folder.Path, "guest.replay"));
        _ = new MatchRecording(host.Local.Session, host.Description, host.Local.Match!, hostReplay, hashInterval: 1);
        guest.MatchBuilt += () => _ = new MatchRecording(guest.Session, guest.Description!, guest.Match!, guestReplay, hashInterval: 1);

        void Play(double seconds)
        {
            for (var passed = TimeSpan.Zero; passed < TimeSpan.FromSeconds(seconds); passed += Frame)
            {
                network.Advance(Frame);
                host.Advance(Frame);
                guest.Advance(Frame);
            }
        }

        Play(0.5);
        host.Start();
        Play(1);
        Assert.True(host.Local.Session.Submit(new Command(CommandType.Move, 0, UnitsOf(host.Local.Match!, 0), Fixed.FromInt(40), Fixed.FromInt(30))));
        Play(1);

        // One more unit on the guest's machine than on the host's.
        var parted = guest.Match!.World.Tick;
        var extra = guest.Match.SpawnUnit(1, guest.Match.Bodies.ValueAt(0).Position, Fixed.One / 8);
        Play(3);

        Assert.Equal(SessionEndReason.Desync, host.Local.Session.EndInfo?.Reason);
        Assert.Equal(SessionEndReason.Desync, guest.Session.EndInfo?.Reason);
        hostReplay.Dispose();
        guestReplay.Dispose();
        WriteSnapshot(folder, "host", host.Local.Match!);
        WriteSnapshot(folder, "guest", guest.Match);

        var (exitCode, output, errors) = Run(Path.Combine(folder.Path, "host.snapshot"), Path.Combine(folder.Path, "guest.replay"));

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Failed, exitCode);
        var lines = Lines(output);
        var tick = host.Local.Match!.World.Tick;
        Assert.Matches($"^A: host  map=ridge players=2 seed=77 game=test recorded_ticks={tick} snapshot_tick={tick} state_hash=[0-9a-f]{{16}}$", lines[0]);
        Assert.Matches($"^B: guest  map=ridge players=2 seed=77 game=test recorded_ticks={tick} snapshot_tick={tick} state_hash=[0-9a-f]{{16}}$", lines[1]);
        Assert.Matches($"^The recorded state hashes are the same after {parted} ticks and differ after {parted + 1}: [0-9a-f]{{16}} \\| [0-9a-f]{{16}}\\.$", lines[2]);
        Assert.Matches("^Run here, A's replay gives the states A recorded, all [0-9]+ of them\\.$", lines[3]);
        Assert.Equal($"Run here, B's replay gives another state than B recorded, first after {parted + 1} ticks.", lines[4]);
        Assert.Equal("7 differences between the snapshots, A | B:", lines[5]);
        var id = extra.Value;
        Assert.Equal(
            [
                $"  world  _nextId: {id} | {id + 1}",
                $"  world  _count: {id - 1} | {id}",
                $"  world entity {id}  only in B",
                $"  bodies  _count: {id - 1} | {id}",
                $"  bodies entity {id}  only in B",
                $"  movers  _count: {id - 1} | {id}",
                $"  movers entity {id}  only in B",
            ],
            lines[6..].Select(line => System.Text.RegularExpressions.Regex.Replace(line, " {2,}", "  ")));

        // The lines are set in columns.
        Assert.Single(lines[6..].Select(line => line.IndexOf("only in B", StringComparison.Ordinal)).Where(column => column >= 0).Distinct());
    }

    [Fact]
    public void AFieldThatDiffersIsNamedWithItsStoreAndItsEntity()
    {
        using var folder = new TemporaryFolder();
        var first = Record(folder, "a", ticks: 40);
        EntityId unit = default;
        var second = Record(folder, "b", ticks: 40, beforeTick: (match, tick) =>
        {
            if (tick == 12)
            {
                // A unit that is somewhere else on one machine, and one that walks at another speed.
                unit = UnitsOf(match, 1)[2];
                ref var body = ref match.Bodies.Get(unit);
                body.Position = new FixedVector2(body.Position.X + Fixed.Half, body.Position.Y);
            }
        });

        var (exitCode, output, errors) = Run(first, second);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Failed, exitCode);
        var lines = Lines(output);
        Assert.StartsWith("The recorded state hashes are the same after 12 ticks and differ after 13: ", lines[2]);
        Assert.Equal("1 difference between the snapshots, A | B:", lines[5]);
        Assert.Matches($"^  bodies entity {unit.Value}  Position\\.X: [0-9.]+ \\(raw [0-9]+\\) \\| [0-9.]+ \\(raw [0-9]+\\)$", lines[6]);
        Assert.Equal(7, lines.Length);
    }

    [Fact]
    public void TheListIsCutAtTheLimit()
    {
        using var folder = new TemporaryFolder();
        var order = new Command(CommandType.Move, 1, UnitsOf(Description().CreateMatch(Data), 1), Fixed.FromInt(50), Fixed.FromInt(50));
        var first = Record(folder, "a", ticks: 40);
        var second = Record(folder, "b", ticks: 40, commands: [(3, order)]);

        var cut = Lines(Run(first, second, "--limit", "3").Output);
        var all = Lines(Run(first, second, "--limit", "0").Output);

        Assert.Matches("^[0-9]+ differences between the snapshots. The first 3, A \\| B:$", cut[5]);
        Assert.Equal(9, cut.Length);
        Assert.Matches("^[0-9]+ differences between the snapshots, A \\| B:$", all[5]);
        Assert.True(all.Length > 20);
        Assert.Contains(all, line => line.Contains("Destination.X: ", StringComparison.Ordinal));

        // Each ran the commands it recorded, so both reproduce here: neither machine went wrong, they were told different things.
        Assert.StartsWith("Run here, A's replay gives the states A recorded", cut[3]);
        Assert.StartsWith("Run here, B's replay gives the states B recorded", cut[4]);
    }

    [Fact]
    public void TwoPeersThatAgreeAreReportedAsTheSame()
    {
        using var folder = new TemporaryFolder();
        var first = Record(folder, "a", ticks: 25);
        var second = Record(folder, "b", ticks: 25);

        var (exitCode, output, errors) = Run(first, second);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        var lines = Lines(output);
        Assert.Equal("The recorded state hashes are the same, all 26 that both have, up to 25 ticks.", lines[2]);
        Assert.Equal("The two snapshots are the same in every field.", lines[^1]);
    }

    [Fact]
    public void SnapshotsFromDifferentTicksAreComparedWithAWarning()
    {
        using var folder = new TemporaryFolder();
        var early = Record(folder, "early", ticks: 0);
        var late = Record(folder, "late", ticks: 1);

        var (exitCode, output, _) = Run(early, late);

        Assert.Equal(ExitCodes.Failed, exitCode);
        var lines = Lines(output);
        Assert.Equal("The recorded state hashes are the same, all 1 that both have, up to 0 ticks.", lines[2]);
        Assert.StartsWith("The snapshots are from different ticks, 0 and 1,", lines[5]);
        Assert.Equal("1 difference between the snapshots, A | B:", lines[6]);
        Assert.Equal("  world  Tick: 0 | 1", lines[7]);
    }

    [Fact]
    public void FilesFromDifferentMatchesAreNotCompared()
    {
        using var folder = new TemporaryFolder();
        var first = Record(folder, "a", ticks: 5);
        var second = Record(folder, "b", ticks: 5, description: Description(seed: 78));

        var (exitCode, _, errors) = Run(first, second);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Contains("not from the same match", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatCannotBeLoadedIsNamedWithTheReason()
    {
        using var folder = new TemporaryFolder();
        var good = Record(folder, "good", ticks: 5);
        folder.Write("notes.replay", "not a replay");
        folder.Write("notes.snapshot", "not a snapshot");
        File.Copy(good, Path.Combine(folder.Path, "alone.replay"));
        File.Copy(good, Path.Combine(folder.Path, "cut.replay"));
        File.WriteAllBytes(Path.Combine(folder.Path, "cut.snapshot"), File.ReadAllBytes(Path.ChangeExtension(good, ".snapshot"))[..^5]);
        File.Copy(good, Path.Combine(folder.Path, "longer.replay"));
        File.WriteAllBytes(Path.Combine(folder.Path, "longer.snapshot"), [.. File.ReadAllBytes(Path.ChangeExtension(good, ".snapshot")), 0]);
        using var otherData = Repository.CopyOfData();
        otherData.Edit("rules.jsonc", "\"damage\": \"1.25\"", "\"damage\": \"1.3\"");

        var notAReplay = Run(good, Path.Combine(folder.Path, "notes.replay"));
        var alone = Run(Path.Combine(folder.Path, "alone.replay"), good);
        var cut = Run(Path.Combine(folder.Path, "cut.snapshot"), good);
        var longer = Run(good, Path.Combine(folder.Path, "longer"));
        var missing = Run(good, Path.Combine(folder.Path, "nowhere.replay"));
        var other = Run(good, good, "--data", otherData.Path);

        Assert.All((int[])[notAReplay.ExitCode, alone.ExitCode, cut.ExitCode, longer.ExitCode, missing.ExitCode, other.ExitCode], code => Assert.Equal(ExitCodes.Failed, code));
        Assert.Contains("notes.replay: ", notAReplay.Errors, StringComparison.Ordinal);
        Assert.Contains("there is no file 'alone.snapshot' beside it", alone.Errors, StringComparison.Ordinal);
        Assert.Contains("cut.snapshot: ", cut.Errors, StringComparison.Ordinal);
        Assert.Contains("longer.snapshot: ", longer.Errors, StringComparison.Ordinal);
        Assert.Contains("there is no file 'nowhere.replay' beside it", missing.Errors, StringComparison.Ordinal);
        Assert.Contains("The match was made with other data", other.Errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData()]
    [InlineData("one.replay")]
    [InlineData("one.replay", "two.replay", "three.replay")]
    [InlineData("one.replay", "two.replay", "--limit")]
    [InlineData("one.replay", "two.replay", "--limit", "-1")]
    [InlineData("one.replay", "two.replay", "--limit", "many")]
    [InlineData("one.replay", "two.replay", "--verbose")]
    [InlineData("one.replay", "two.replay", "--data", "no-such-folder")]
    public void ArgumentsItCannotUseAreAUsageError(params string[] arguments)
    {
        var (exitCode, output, errors) = Run(arguments);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.StartsWith("desync-diff: ", errors);
    }

    [Fact]
    public void TheToolIsListed()
    {
        var tool = Assert.Single(ToolRunner.FindTools(), tool => tool.Name == "desync-diff");

        Assert.IsType<DesyncDiffTool>(tool);
        Assert.Equal("game/data", DesyncDiffTool.DefaultFolder);
    }

    /// <summary>The listing is the walk the hash uses: a field the hash covers cannot be missing from the comparison.</summary>
    [Fact]
    public void TheListingHasEveryFieldOfTheWalkInOrder()
    {
        var match = Description().CreateMatch(Data);
        match.Simulation.Run(5);

        var fields = StateListing.Of(match.World);

        Assert.Equal(new StateField("world", "", "Tick", "5"), fields[0]);
        Assert.Equal(["world", "bodies", "movers"], fields.Select(field => field.Section).Distinct());
        Assert.Equal(12, fields.Count(field => field.Section == "bodies" && field.Name == "Owner"));
        Assert.Equal(12, fields.Where(field => field.Section == "movers" && field.Name == "Speed").Select(field => field.Item).Distinct().Count());
        Assert.All(fields.Where(field => field.Name is "Owner" or "Speed"), field => Assert.StartsWith("entity ", field.Item));
        Assert.All(fields.Where(field => field.Name is "_count" or "_nextId" or "Tick"), field => Assert.Equal("", field.Item));
    }

    private static MatchDescription Description(ulong seed = 77) =>
        new("test", Data.ContentHash, "ridge", seed, [new PlayerSetup(0), new PlayerSetup(1)]);

    private static EntityId[] UnitsOf(Match match, int player)
    {
        var units = new List<EntityId>();
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            if (match.Bodies.ValueAt(i).Owner == player)
            {
                units.Add(match.Bodies.EntityAt(i));
            }
        }

        return [.. units];
    }

    /// <summary>
    /// Plays a match alone and leaves the pair of files a peer leaves: its replay, with a hash for
    /// every tick, and a snapshot of where it stopped. Returns the path of the replay.
    /// </summary>
    private static string Record(
        TemporaryFolder folder,
        string name,
        int ticks,
        Action<Match, int>? beforeTick = null,
        MatchDescription? description = null,
        (int Tick, Command Command)[]? commands = null)
    {
        description ??= Description();
        var match = description.CreateMatch(Data);
        var writer = new ByteWriter();
        var recorder = new ReplayRecorder(writer, description, match.World, hashInterval: 1);
        for (var tick = 0; tick < ticks; tick++)
        {
            var due = (commands ?? []).Where(entry => entry.Tick == tick).Select(entry => entry.Command).ToArray();
            foreach (var command in due)
            {
                match.Simulation.Commands.Enqueue(tick, command);
            }

            recorder.RecordCommands(tick, due);
            beforeTick?.Invoke(match, tick);
            match.Simulation.Tick();
            recorder.AfterTick();
        }

        recorder.Finish();
        var path = Path.Combine(folder.Path, name + ".replay");
        File.WriteAllBytes(path, writer.ToArray());
        WriteSnapshot(folder, name, match);
        return path;
    }

    private static void WriteSnapshot(TemporaryFolder folder, string name, Match match)
    {
        var writer = new ByteWriter();
        Snapshot.Write(match.World, writer);
        File.WriteAllBytes(Path.Combine(folder.Path, name + ".snapshot"), writer.ToArray());
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();

    private static (int ExitCode, string Output, string Errors) Run(params string[] arguments)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        string[] data = arguments.Contains("--data") ? [] : ["--data", Repository.DataFolder];
        var exitCode = ToolRunner.Run(["desync-diff", .. arguments, .. data], output, errors);
        return (exitCode, output.ToString(), errors.ToString());
    }
}
