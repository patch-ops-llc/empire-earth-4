using System.Text.RegularExpressions;
using Net;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Setup;
using Match = Sim.Setup.Match;

namespace Tools.Tests;

/// <summary>
/// Runs <c>desync-diff</c> the way the command line does, on the desync bundles two games leave
/// when their match stops because they no longer agree: two headless sessions of the real game,
/// forced apart in one way or another.
/// </summary>
public class DesyncBundleDiffTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private static GameData Data { get; } = DataLoader.Load(new DiskDataFiles(Repository.DataFolder)).Data!;

    /// <summary>
    /// The same data with every research one Material dearer, under the name of the real data: a
    /// build that computes differently and does not know it, as a build for another system might.
    /// </summary>
    private static GameData OtherBuild { get; } = Data with
    {
        Research = [.. Data.Research.Select(tier => tier with { Materials = tier.Materials + 1 })],
    };

    /// <summary>
    /// Two builds that compute differently, each true to itself: what a desync between two
    /// operating systems would be. The report names the tick, the store and the field, at the
    /// first tick that differs, and says what differs between the machines.
    /// </summary>
    [Fact]
    public void TwoBuildsThatComputeDifferentlyAreToldApartDownToTheField()
    {
        using var pair = DesyncedPair.Play(guestData: OtherBuild, research: true);

        var (exitCode, output, errors) = Run(pair.HostBundle, pair.GuestBundle);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Failed, exitCode);
        var lines = Lines(output);
        var end = pair.End;
        var agreed = end.DesyncTick - 20;
        Assert.Equal($"A: host.desync.zip  player 1 of 2  map=ridge seed=77 game=test failed_comparison_tick={end.DesyncTick} final_tick={end.FinalTick} agreed_state_tick={agreed}", lines[0]);
        Assert.Equal($"B: guest.desync.zip  player 2 of 2  map=ridge seed=77 game=test failed_comparison_tick={end.DesyncTick} final_tick={end.FinalTick} agreed_state_tick={agreed}", lines[1]);
        Assert.Equal(
            [
                "The machines, A | B:",
                "  operating_system  HostOS 1 | GuestOS 2   <- differs",
                "  dotnet            10.0 | 10.0",
                "  written_at        host o'clock | guest o'clock",
                $"Both kept the same state after {agreed} ticks, from before the comparison that failed after {end.DesyncTick}.",
            ],
            lines[2..7]);

        // The research is paid for in the tick its command runs, a few ticks after it was given.
        var tick = FirstTickThatDiffers(lines);
        Assert.InRange(tick, pair.FaultAfter + 1, end.DesyncTick);
        Assert.Matches($"^The recorded state hashes are the same after {tick - 1} ticks and differ after {tick}: [0-9a-f]{{16}} \\| [0-9a-f]{{16}}\\.$", lines[7]);
        Assert.Equal($"First tick that differs: {tick}. The states are the same after {tick - 1} ticks and differ after {tick}.", lines[8]);
        Assert.Equal($"First store that differs: research. In that tick 2 of the {pair.Parts} parts of the state differ: research, stockpiles.", lines[9]);
        Assert.Equal($"A's machine ran ticks {agreed} to {end.FinalTick} a second time from the agreed state and got the same states: it repeats itself.", lines[10]);
        Assert.Equal($"B's machine ran ticks {agreed} to {end.FinalTick} a second time from the agreed state and got the same states: it repeats itself.", lines[11]);
        Assert.Equal($"Run here from the agreed state after {agreed} ticks, A's commands give the states that machine had, in all {end.FinalTick - agreed + 1} ticks compared, up to {end.FinalTick}.", lines[12]);
        Assert.Equal($"Run here from the agreed state after {agreed} ticks, B's commands give another state than that machine had, first after {tick} ticks, in: research, stockpiles.", lines[13]);
        Assert.StartsWith("Reading: each machine repeats itself, so the same code gives different results on the two.", lines[14]);
        Assert.Equal("This machine agrees with A.", lines[15]);

        // The fields are those of the first tick that differs, not of the end of the match.
        Assert.Equal($"2 differences after {tick} ticks, the first tick that differs, A | B:", lines[16]);
        Assert.Equal(2, lines[17..].Length);
        Assert.Matches("^  research +slot\\.Materials: [0-9]+ \\| [0-9]+$", lines[17]);
        Assert.Matches("^  stockpiles +\\S+: [0-9]+ \\| [0-9]+$", lines[18]);
    }

    /// <summary>
    /// One machine on which a unit is suddenly somewhere else: a fault that the machine's own
    /// commands do not explain. Its own second run says so, and the report says whom to look at.
    /// </summary>
    [Fact]
    public void AMachineThatDoesNotRepeatItselfIsNamed()
    {
        EntityId unit = default;
        using var pair = DesyncedPair.Play(fault: guest =>
        {
            unit = UnitsOf(guest, 1)[2];
            ref var body = ref guest.Bodies.Get(unit);
            body.Position = new FixedVector2(body.Position.X + Fixed.Half, body.Position.Y);
        });

        var (exitCode, output, errors) = Run(pair.HostBundle, pair.GuestBundle);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Failed, exitCode);
        var lines = Lines(output);
        var tick = pair.FaultAfter + 1;
        var agreed = pair.End.DesyncTick - 20;
        Assert.Contains($"First tick that differs: {tick}. The states are the same after {tick - 1} ticks and differ after {tick}.", lines);
        Assert.Contains($"First store that differs: bodies. In that tick 1 of the {pair.Parts} parts of the state differ: bodies.", lines);
        Assert.Contains(lines, line => line.StartsWith("A's machine ran ticks ", StringComparison.Ordinal) && line.EndsWith("it repeats itself.", StringComparison.Ordinal));
        Assert.Contains($"B's machine ran the match a second time from the agreed state after {agreed} ticks and got another state than the first time, first after {tick} ticks, in: bodies. It does not repeat itself.", lines);
        Assert.Contains(lines, line => line.StartsWith("Reading: B's machine does not repeat its own run.", StringComparison.Ordinal));
        Assert.Contains("This machine agrees with A.", lines);

        // The bundle has the state B's match really had in that tick, so the one field is all that differs.
        Assert.Equal(
            [$"1 difference after {tick} ticks, the first tick that differs, A | B:", $"  bodies entity {unit.Value}  Position.X: "],
            [lines[^2], lines[^1][..lines[^1].IndexOf(": ", StringComparison.Ordinal)] + ": "]);
        Assert.Matches("Position\\.X: [0-9.]+ \\(raw [0-9]+\\) \\| [0-9.]+ \\(raw [0-9]+\\)$", lines[^1]);
    }

    [Fact]
    public void OneBundleIsComparedWithARunOnThisMachine()
    {
        using var pair = DesyncedPair.Play(guestData: OtherBuild, research: true);
        var agreed = pair.End.DesyncTick - 20;

        // The guest's build computes differently from this one, and its bundle alone shows where.
        var guest = Run(pair.GuestBundle);

        Assert.Equal("", guest.Errors);
        Assert.Equal(ExitCodes.Failed, guest.ExitCode);
        var lines = Lines(guest.Output);
        Assert.StartsWith("The bundle: guest.desync.zip  player 2 of 2  map=ridge ", lines[0]);
        Assert.Equal(["The machine it is from:", "  operating_system: GuestOS 2", "  dotnet: 10.0", "  written_at: guest o'clock"], lines[1..5]);
        Assert.StartsWith("The bundle's machine ran ticks ", lines[5]);
        var tick = int.Parse(Regex.Match(lines[6], "first after ([0-9]+) ticks").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"Run here from the agreed state after {agreed} ticks, the bundle's commands give another state than that machine had, first after {tick} ticks, in: research, stockpiles.", lines[6]);
        Assert.Equal($"2 differences after {tick} ticks, the first tick that differs, the bundle | here:", lines[7]);
        Assert.Matches("^  research +slot\\.Materials: [0-9]+ \\| [0-9]+$", lines[8]);
        Assert.Matches("^  stockpiles +\\S+: [0-9]+ \\| [0-9]+$", lines[9]);
        Assert.Equal(10, lines.Length);

        // Run from the first tick, it is the same tick: the two builds differ in nothing before it.
        var whole = Run(pair.GuestBundle, "--from-start");
        Assert.Equal(ExitCodes.Failed, whole.ExitCode);
        Assert.Equal($"Run here from the start, the bundle's commands give another state than that machine had, first after {tick} ticks, in: research, stockpiles.", Lines(whole.Output)[6]);

        // The host's build is this one, so its bundle alone shows nothing, and the report says what is needed.
        var host = Run(pair.HostBundle);
        Assert.Equal("", host.Errors);
        Assert.Equal(ExitCodes.Ok, host.ExitCode);
        Assert.Equal(
            [
                $"Run here from the agreed state after {agreed} ticks, the bundle's commands give the states that machine had, in all {pair.End.FinalTick - agreed + 1} ticks compared, up to {pair.End.FinalTick}.",
                "This machine repeats what that machine did, so it does not show the fault alone. The other player's bundle is needed: give both to compare them.",
            ],
            Lines(host.Output)[^2..]);

        var hostWhole = Run(pair.HostBundle, "--from-start");
        Assert.Equal(ExitCodes.Ok, hostWhole.ExitCode);
        Assert.Contains($"Run here from the start, the bundle's commands give the states that machine had, in all {pair.End.FinalTick + 1} ticks compared, up to {pair.End.FinalTick}.", Lines(hostWhole.Output));
    }

    [Fact]
    public void OneBundleOfAMachineThatDidNotRepeatItselfShowsTheFieldToo()
    {
        EntityId unit = default;
        using var pair = DesyncedPair.Play(fault: guest =>
        {
            unit = UnitsOf(guest, 1)[0];
            ref var body = ref guest.Bodies.Get(unit);
            body.Position = new FixedVector2(body.Position.X, body.Position.Y + Fixed.Half);
        });

        var (exitCode, output, errors) = Run(pair.GuestBundle);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Failed, exitCode);
        var lines = Lines(output);
        var tick = pair.FaultAfter + 1;
        Assert.Contains(lines, line => line.EndsWith($"give another state than that machine had, first after {tick} ticks, in: bodies.", StringComparison.Ordinal));
        Assert.Equal($"1 difference after {tick} ticks, the first tick that differs, the bundle | here:", lines[^2]);
        Assert.Matches($"^  bodies entity {unit.Value}  Position\\.Y: [0-9.]+ \\(raw [0-9]+\\) \\| [0-9.]+ \\(raw [0-9]+\\)$", lines[^1]);
    }

    /// <summary>
    /// Bundles that lack the state of the tick that differs, as those of a machine that was far
    /// behind when the match stopped: the states the match stopped in are compared instead, the
    /// store that differed first in front, and the report says that they are from later.
    /// </summary>
    [Fact]
    public void WithoutTheStateOfTheTickTheFinalStatesAreCompared()
    {
        EntityId unit = default;
        using var pair = DesyncedPair.Play(fault: guest =>
        {
            unit = UnitsOf(guest, 1)[2];
            ref var body = ref guest.Bodies.Get(unit);
            body.Position = new FixedVector2(body.Position.X + Fixed.Half, body.Position.Y);
        });
        WithoutStates(pair.HostBundle);
        WithoutStates(pair.GuestBundle);
        var tick = pair.FaultAfter + 1;
        var later = pair.End.FinalTick - tick;
        var field = $"^  bodies entity {unit.Value} +Position\\.X: [0-9.]+ \\(raw [0-9]+\\) \\| [0-9.]+ \\(raw [0-9]+\\)$";

        // The host's machine repeated itself, and this one repeats it, so the host's state is known. The guest's is not.
        var both = Lines(Run(pair.HostBundle, pair.GuestBundle).Output);
        var heading = Array.FindIndex(both, line => line.StartsWith("The fields after ", StringComparison.Ordinal));
        Assert.Equal(
            $"The fields after {tick} ticks cannot be compared: B's state at that tick is not on file, and this machine's run does not give it. "
            + $"The states the match stopped in are compared, after {pair.End.FinalTick} ticks: later than the first difference, so some of what follows came of it.",
            both[heading]);
        Assert.Matches("^[0-9]+ differences? between the final states, A \\| B:$", both[heading + 1]);
        Assert.StartsWith("  bodies ", both[heading + 2]);
        Assert.Contains(both[(heading + 2)..], line => Regex.IsMatch(line, field));

        var one = Lines(Run(pair.GuestBundle).Output);
        heading = Array.FindIndex(one, line => line.StartsWith("The bundle has no state for the tick that differs", StringComparison.Ordinal));
        Assert.Equal(
            $"The bundle has no state for the tick that differs, so the states the match stopped in are compared, after {pair.End.FinalTick} ticks: {later} ticks later, so some of what follows came of the first difference.",
            one[heading]);
        Assert.Matches("^[0-9]+ differences? between the final states, the bundle \\| here:$", one[heading + 1]);
        Assert.StartsWith("  bodies ", one[heading + 2]);
        Assert.Contains(one[(heading + 2)..], line => Regex.IsMatch(line, field));
    }

    /// <summary>
    /// A store this tool has never heard of, as an open piece of the simulation adds one: the
    /// report names it, because the parts of the state are those the match registered and the
    /// bundle lists, not a list kept here.
    /// </summary>
    [Fact]
    public void AStoreTheToolDoesNotKnowIsNamedAllTheSame()
    {
        Tally? tally = null;
        using var pair = DesyncedPair.Play(
            built: (match, isGuest) =>
            {
                var store = match.World.AddStore(new Tally());
                tally = isGuest ? store : tally;
            },
            fault: _ => tally!.Count++);

        var (exitCode, output, errors) = Run(pair.HostBundle, pair.GuestBundle);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Failed, exitCode);
        var lines = Lines(output);
        var tick = pair.FaultAfter + 1;
        Assert.Contains($"First tick that differs: {tick}. The states are the same after {tick - 1} ticks and differ after {tick}.", lines);
        Assert.Contains($"First store that differs: tally. In that tick 1 of the {pair.Parts} parts of the state differ: tally.", lines);

        // A match built here has no such store, so this machine can neither run the match nor list its fields, and says so.
        Assert.Contains("A's machine did not run the match a second time: a match built again from the description has other stores than the match that was played.", lines);
        Assert.Contains(lines, line => line.StartsWith("A's match was not run here: the agreed state in the bundle does not fit a match built here", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("The final states cannot be compared either", StringComparison.Ordinal));
    }

    [Fact]
    public void TheListIsCutAtTheLimit()
    {
        using var pair = DesyncedPair.Play(fault: guest => guest.SpawnUnit(1, guest.Bodies.ValueAt(0).Position, Fixed.One / 8));

        var cut = Lines(Run(pair.HostBundle, pair.GuestBundle, "--limit", "2").Output);
        var all = Lines(Run(pair.HostBundle, pair.GuestBundle, "--limit", "0").Output);

        Assert.Matches("^[0-9]+ differences after [0-9]+ ticks, the first tick that differs. The first 2, A \\| B:$", cut[^3]);
        Assert.Matches("^[0-9]+ differences after [0-9]+ ticks, the first tick that differs, A \\| B:$", all[cut.Length - 3]);
        Assert.True(all.Length > cut.Length);

        // One unit more: the list of entities differs, which is the world's own, and the stores that hold the unit.
        var store = Assert.Single(all, line => line.StartsWith("First store that differs: ", StringComparison.Ordinal));
        Assert.StartsWith("First store that differs: bodies. ", store);
        Assert.Contains("parts of the state differ: world, bodies, movers", store, StringComparison.Ordinal);
        Assert.EndsWith(" The world's own fields differ too: the tick, the random generator or the list of entities.", store);
    }

    [Fact]
    public void BundlesThatDoNotBelongTogetherAreNotCompared()
    {
        using var pair = DesyncedPair.Play(fault: Nudge);
        using var other = DesyncedPair.Play(fault: Nudge, seed: 78);

        var different = Run(pair.HostBundle, other.GuestBundle);
        Assert.Equal(ExitCodes.Failed, different.ExitCode);
        Assert.Contains("not from the same match", different.Errors, StringComparison.Ordinal);

        // The same file twice compares, finds nothing, and says what is wrong with that.
        var twice = Run(pair.HostBundle, pair.HostBundle);
        Assert.Equal(ExitCodes.Ok, twice.ExitCode);
        Assert.Contains("Both bundles are from player 1. Two players' bundles are needed to compare their games.", Lines(twice.Output));
        Assert.Equal("The two final states are the same in every field.", Lines(twice.Output)[^1]);
    }

    [Fact]
    public void AFileThatCannotBeUsedIsNamedWithTheReason()
    {
        using var pair = DesyncedPair.Play(fault: Nudge);
        var folder = Path.GetDirectoryName(pair.HostBundle)!;
        var cut = Path.Combine(folder, "cut.desync.zip");
        File.WriteAllBytes(cut, File.ReadAllBytes(pair.HostBundle)[..^40]);
        var replay = Path.Combine(folder, "loose.replay");
        File.WriteAllBytes(replay, DesyncBundle.Read(File.ReadAllBytes(pair.HostBundle)).Replay);
        using var otherData = Repository.CopyOfData();
        otherData.Edit("rules.jsonc", "\"damage\": \"1.25\"", "\"damage\": \"1.3\"");

        var damaged = Run(pair.GuestBundle, cut);
        Assert.Equal(ExitCodes.Failed, damaged.ExitCode);
        Assert.StartsWith($"desync-diff: {cut}: The file is not a desync bundle", damaged.Errors);

        var otherBuild = Run(pair.HostBundle, pair.GuestBundle, "--data", otherData.Path);
        Assert.Equal(ExitCodes.Failed, otherBuild.ExitCode);
        Assert.Contains("The match was made with other data", otherBuild.Errors, StringComparison.Ordinal);

        var mixed = Run(pair.HostBundle, replay);
        Assert.Equal(ExitCodes.Usage, mixed.ExitCode);
        Assert.StartsWith("desync-diff: one file is a desync bundle and the other is not.", mixed.Errors);

        var alone = Run(replay);
        Assert.Equal(ExitCodes.Usage, alone.ExitCode);
        Assert.Contains("is not a desync bundle, and without one it compares the files of two peers", alone.Errors, StringComparison.Ordinal);

        var fromStart = Run(replay, replay, "--from-start");
        Assert.Equal(ExitCodes.Usage, fromStart.ExitCode);
        Assert.StartsWith("desync-diff: --from-start goes with desync bundles.", fromStart.Errors);

        var three = Run(pair.HostBundle, pair.GuestBundle, pair.HostBundle);
        Assert.Equal(ExitCodes.Usage, three.ExitCode);
    }

    /// <summary>Writes the bundle again without the states of the ticks the fault may have happened in.</summary>
    private static void WithoutStates(string path)
    {
        var bundle = DesyncBundle.Read(File.ReadAllBytes(path));
        Assert.NotEmpty(bundle.States);
        var stripped = new DesyncBundle
        {
            Player = bundle.Player,
            PlayerCount = bundle.PlayerCount,
            GameVersion = bundle.GameVersion,
            ContentHash = bundle.ContentHash,
            MapId = bundle.MapId,
            Seed = bundle.Seed,
            HashInterval = bundle.HashInterval,
            DesyncTick = bundle.DesyncTick,
            FinalTick = bundle.FinalTick,
            Replay = bundle.Replay,
            FinalSnapshot = bundle.FinalSnapshot,
            AgreedTick = bundle.AgreedTick,
            AgreedSnapshot = bundle.AgreedSnapshot,
            Parts = bundle.Parts,
            Hashes = bundle.Hashes,
            SecondRun = bundle.SecondRun,
            Machine = bundle.Machine,
        };
        File.WriteAllBytes(path, stripped.ToArray());
    }

    private static void Nudge(Match guest)
    {
        ref var body = ref guest.Bodies.Get(UnitsOf(guest, 1)[0]);
        body.Position = new FixedVector2(body.Position.X + Fixed.Epsilon, body.Position.Y);
    }

    private static int FirstTickThatDiffers(string[] lines)
    {
        var line = Assert.Single(lines, line => line.StartsWith("First tick that differs: ", StringComparison.Ordinal));
        return int.Parse(Regex.Match(line, "^First tick that differs: ([0-9]+)\\.").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

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

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();

    private static (int ExitCode, string Output, string Errors) Run(params string[] arguments)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        string[] data = arguments.Contains("--data") ? [] : ["--data", Repository.DataFolder];
        var exitCode = ToolRunner.Run(["desync-diff", .. arguments, .. data], output, errors);
        return (exitCode, output.ToString(), errors.ToString());
    }

    /// <summary>A store with one number in it, which no match of the game has.</summary>
    private sealed class Tally : IStateStore
    {
        public int Count;

        public string Name => "tally";

        public void RemoveEntities(ReadOnlySpan<EntityId> destroyed)
        {
        }

        public void Walk(IStateWalker walker) => Count = walker.Field(Count);
    }

    /// <summary>
    /// A host and a guest that played over the session until the host found that their hashes
    /// differ and stopped both, and the bundle each wrote, as the game writes it.
    /// </summary>
    private sealed class DesyncedPair : IDisposable
    {
        private readonly TemporaryFolder _folder = new();

        private DesyncedPair()
        {
        }

        public string HostBundle => Path.Combine(_folder.Path, "host" + DesyncBundle.FileEnding);

        public string GuestBundle => Path.Combine(_folder.Path, "guest" + DesyncBundle.FileEnding);

        /// <summary>The tick the guest's game had run when the fault was put in.</summary>
        public int FaultAfter { get; private set; }

        public SessionEndInfo End { get; private set; } = null!;

        /// <summary>The number of parts the state of the match has.</summary>
        public int Parts { get; private set; }

        /// <param name="guestData">The game data of the guest's machine. Left out: the host's.</param>
        /// <param name="built">Called with each machine's match when it is built, before anything follows it.</param>
        /// <param name="fault">Called with the guest's match two and a half seconds in, to put a fault in it.</param>
        /// <param name="research">True to have the host's player start a research then, which costs what the build says.</param>
        /// <param name="seed">The seed of the match.</param>
        public static DesyncedPair Play(GameData? guestData = null, Action<Match, bool>? built = null, Action<Match>? fault = null, bool research = false, ulong seed = 77)
        {
            var pair = new DesyncedPair();
            var description = new MatchDescription("test", Data.ContentHash, "ridge", seed, [new PlayerSetup(0), new PlayerSetup(1)]);
            var host = new MatchHost(Data, description);
            var network = new LoopbackNetwork(seed: 2);
            var link = network.Connect(new LinkConditions(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(20)));
            Assert.True(host.TryAttach(link.HostEnd));
            var guest = new MatchClient(link.ClientEnd, guestData ?? Data, "test", readyWhenBuilt: true);

            var replays = new[] { new MemoryStream(), new MemoryStream() };
            var watches = new DesyncWatch?[2];
            void Follow(MatchClient client, int seat)
            {
                built?.Invoke(client.Match!, seat == 1);
                _ = new MatchRecording(client.Session, client.Description!, client.Match!, replays[seat], hashInterval: 1);
                watches[seat] = new DesyncWatch(client.Session, client.Match!);
            }

            Follow(host.Local, 0);
            guest.MatchBuilt += () => Follow(guest, 1);

            void Run(double seconds)
            {
                for (var passed = TimeSpan.Zero; passed < TimeSpan.FromSeconds(seconds); passed += Frame)
                {
                    network.Advance(Frame);
                    host.Advance(Frame);
                    guest.Advance(Frame);
                }
            }

            Run(0.5);
            host.Start();
            Run(1);
            Assert.True(host.Local.Session.Submit(new Command(CommandType.Move, 0, UnitsOf(host.Local.Match!, 0), Fixed.FromInt(40), Fixed.FromInt(30))));
            Run(1.5);
            pair.FaultAfter = guest.Match!.World.Tick;
            fault?.Invoke(guest.Match);
            Assert.True(!research || host.Local.Session.Submit(new Command(CommandType.StartResearch, 0, contentId: 0)));
            Run(4);

            Assert.Equal(SessionEndReason.Desync, host.Local.Session.EndInfo?.Reason);
            Assert.Equal(SessionEndReason.Desync, guest.Session.EndInfo?.Reason);
            pair.End = guest.Session.EndInfo!;
            pair.Parts = StoreHasher.PartCount(guest.Match.World);

            MatchClient[] clients = [host.Local, guest];
            string[] names = ["host", "guest"];
            string[] systems = ["HostOS 1", "GuestOS 2"];
            for (var seat = 0; seat < 2; seat++)
            {
                KeyValuePair<string, string>[] machine =
                [
                    new("operating_system", systems[seat]),
                    new("dotnet", "10.0"),
                    new(DesyncBundle.WrittenAtKey, $"{names[seat]} o'clock"),
                ];
                var bundle = watches[seat]!.CreateBundle(clients[seat].Session.EndInfo!, clients[seat].Description!, replays[seat].ToArray(), machine);
                File.WriteAllBytes(seat == 0 ? pair.HostBundle : pair.GuestBundle, bundle.ToArray());
            }

            return pair;
        }

        public void Dispose() => _folder.Dispose();
    }
}
