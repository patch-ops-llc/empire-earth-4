using System.IO.Compression;
using System.Text;
using Sim.Core;
using Sim.Numerics;
using Sim.Serialization;

namespace Net.Tests;

/// <summary>
/// What a peer keeps for finding the cause when the machines of a match stop agreeing: the
/// <see cref="DesyncWatch"/> that follows the match and the <see cref="DesyncBundle"/> it makes.
/// </summary>
public class DesyncBundleTests
{
    private static readonly KeyValuePair<string, string>[] Machine =
    [
        new("operating_system", "TestOS 1.0"),
        new("architecture", "X64"),
    ];

    /// <summary>
    /// Two headless sessions of the real game, forced apart: one field of one unit is changed on
    /// one machine, as a fault in the simulation would. Both stop, and each has a bundle with the
    /// last state they agreed on, the replay, and hashes that name the tick and the store.
    /// </summary>
    [Fact]
    public void AForcedDesyncLeavesEachSideABundleThatNamesTheTickAndTheStore()
    {
        var match = new WatchedMatch();
        match.Start();
        Assert.True(match.Clients[0].Move(40, 30));
        match.Run(seconds: 1.7);
        var faultAfter = match.Clients[1].Tick;
        Assert.InRange(faultAfter, 21, 38);
        Nudge(match.Clients[1]);

        match.UntilStopped();
        var hashesBefore = match.Clients.Select(client => client.Hash).ToArray();
        var bundles = match.Bundles();

        // Both sides stopped for the same reason at the same tick, and making the bundle changed nothing.
        var end = match.Host.EndInfo!;
        Assert.Equal(SessionEndReason.Desync, end.Reason);
        Assert.Equal(40, end.DesyncTick);
        Assert.Equal(hashesBefore, match.Clients.Select(client => client.Hash));
        Assert.All(match.Clients, client => Assert.Equal(SessionEndReason.Desync, client.Player.Session.EndInfo!.Reason));

        for (var seat = 0; seat < 2; seat++)
        {
            var bundle = bundles[seat];
            Assert.Equal(seat, bundle.Player);
            Assert.Equal(2, bundle.PlayerCount);
            Assert.Equal(20, bundle.HashInterval);
            Assert.Equal(40, bundle.DesyncTick);
            Assert.Equal(end.FinalTick, bundle.FinalTick);
            Assert.Equal(TestData.GameVersion, bundle.GameVersion);
            Assert.Equal(TestData.Data.ContentHash, bundle.ContentHash);
            Assert.Equal("ridge", bundle.MapId);
            Assert.Equal(7UL, bundle.Seed);
            Assert.Equal(Machine, bundle.Machine);

            // The parts are the world and the stores the match registered, whatever they are.
            Assert.Equal(StoreHasher.PartNames(match.Clients[seat].Match.World), bundle.Parts);

            // The agreed state is from the last comparison both passed, a second before the one that failed.
            Assert.Equal(20, bundle.AgreedTick);
            Assert.Equal(Enumerable.Range(20, end.FinalTick - 20 + 1), bundle.Hashes.Select(hashes => hashes.Tick));

            // The replay is whole, and its hashes are those of the parts taken together.
            var replay = Replay.Read(new ByteReader(bundle.Replay));
            Assert.Equal(end.FinalTick, replay.End!.Value.Tick);

            // The final state is the state the match stopped in.
            Assert.Equal(match.Clients[seat].Hash, Restore(bundle, bundle.FinalSnapshot).ComputeHash());
        }

        // The agreed state is the same on both sides, and is the state after 20 ticks.
        var agreed = bundles.Select(bundle => Restore(bundle, bundle.AgreedSnapshot!)).ToArray();
        Assert.Equal(20, agreed[0].Tick);
        Assert.Equal(agreed[0].ComputeHash(), agreed[1].ComputeHash());
        Assert.Equal(bundles[0].AgreedSnapshot, bundles[1].AgreedSnapshot);

        // The hashes are the same up to the fault and name the first tick after it and the store.
        var firstDifference = bundles[0].Hashes.Zip(bundles[1].Hashes).First(pair => !pair.First.Hashes.SequenceEqual(pair.Second.Hashes));
        Assert.Equal(faultAfter + 1, firstDifference.First.Tick);
        Assert.Equal(["bodies"], DifferingParts(bundles[0], bundles[1], faultAfter + 1));

        // The machine that stayed right repeats its own run. The other does not, from the fault on.
        Assert.Equal(new SecondRun(SecondRunOutcome.Repeated, From: 20, To: end.FinalTick), bundles[0].SecondRun);
        Assert.Equal(new SecondRun(SecondRunOutcome.Parted, From: 20, To: faultAfter + 1, PartedAt: faultAfter + 1, Detail: "bodies"), bundles[1].SecondRun);

        // Each has the state its match had in every tick the fault may have been in: the machine
        // that went wrong too, whose own second run could not give them.
        Assert.All(bundles, bundle =>
        {
            Assert.Equal(Enumerable.Range(21, 20), bundle.States.Select(state => state.Tick));
            var recorded = Replay.Read(new ByteReader(bundle.Replay)).Hashes.ToDictionary(hash => hash.Tick, hash => hash.Hash);
            Assert.All(bundle.States, state => Assert.Equal(recorded[state.Tick], Restore(bundle, state.Snapshot).ComputeHash()));
        });
        Assert.Equal(bundles[0].StateAt(faultAfter)!.Snapshot, bundles[1].StateAt(faultAfter)!.Snapshot);
        Assert.NotEqual(bundles[0].StateAt(faultAfter + 1)!.Snapshot, bundles[1].StateAt(faultAfter + 1)!.Snapshot);
    }

    /// <summary>
    /// A machine that no longer has the states of those ticks as the match ran, because it was
    /// far behind or its states are large, has them from its second run instead, for as long as
    /// that run gave the hashes of the first.
    /// </summary>
    [Theory]
    [InlineData(0, 1L << 30)]
    [InlineData(64, 1L)]
    public void StatesThatAreNoLongerKeptComeFromTheSecondRun(int liveStatesKept, long liveStateBytes)
    {
        var options = new DesyncWatchOptions { LiveStatesKept = liveStatesKept, LiveStateBytes = liveStateBytes };
        var match = new WatchedMatch(watch: _ => options);
        match.Start();
        Assert.True(match.Clients[0].Move(40, 30));
        match.Run(seconds: 1.7);
        var faultAfter = match.Clients[1].Tick;
        Nudge(match.Clients[1]);
        match.UntilStopped();

        var bundles = match.Bundles();

        // The machine that stayed right has them all. The other has them up to the fault.
        Assert.Equal(Enumerable.Range(21, 20), bundles[0].States.Select(state => state.Tick));
        Assert.Equal(Enumerable.Range(21, faultAfter - 20), bundles[1].States.Select(state => state.Tick));
        var recorded = Replay.Read(new ByteReader(bundles[0].Replay)).Hashes.ToDictionary(hash => hash.Tick, hash => hash.Hash);
        Assert.All(bundles[0].States, state => Assert.Equal(recorded[state.Tick], Restore(bundles[0], state.Snapshot).ComputeHash()));
        Assert.All(bundles[1].States, state => Assert.Equal(recorded[state.Tick], Restore(bundles[1], state.Snapshot).ComputeHash()));
    }

    [Fact]
    public void ABundleReadBackIsTheBundleThatWasWritten()
    {
        var match = new WatchedMatch();
        match.Start();
        match.Run(seconds: 1.7);
        Nudge(match.Clients[1]);
        match.UntilStopped();

        foreach (var bundle in match.Bundles())
        {
            var file = bundle.ToArray();
            Assert.True(DesyncBundle.LooksLikeOne(file));
            var read = DesyncBundle.Read(file);

            Assert.Equal(bundle.Player, read.Player);
            Assert.Equal(bundle.PlayerCount, read.PlayerCount);
            Assert.Equal(bundle.GameVersion, read.GameVersion);
            Assert.Equal(bundle.ContentHash, read.ContentHash);
            Assert.Equal(bundle.MapId, read.MapId);
            Assert.Equal(bundle.Seed, read.Seed);
            Assert.Equal(bundle.HashInterval, read.HashInterval);
            Assert.Equal(bundle.DesyncTick, read.DesyncTick);
            Assert.Equal(bundle.FinalTick, read.FinalTick);
            Assert.Equal(bundle.AgreedTick, read.AgreedTick);
            Assert.Equal(bundle.Replay, read.Replay);
            Assert.Equal(bundle.FinalSnapshot, read.FinalSnapshot);
            Assert.Equal(bundle.AgreedSnapshot, read.AgreedSnapshot);
            Assert.Equal(bundle.Parts, read.Parts);
            Assert.Equal(bundle.Hashes.Select(hashes => hashes.Tick), read.Hashes.Select(hashes => hashes.Tick));
            Assert.All(bundle.Hashes.Zip(read.Hashes), pair => Assert.Equal(pair.First.Hashes, pair.Second.Hashes));
            Assert.Equal(bundle.SecondRun, read.SecondRun);
            Assert.Equal(bundle.States.Select(state => state.Tick), read.States.Select(state => state.Tick));
            Assert.All(bundle.States.Zip(read.States), pair => Assert.Equal(pair.First.Snapshot, pair.Second.Snapshot));
            Assert.Equal(bundle.Machine, read.Machine);
            Assert.Equal("TestOS 1.0", read.MachineValue("operating_system"));
            Assert.Null(read.MachineValue("no such key"));

            // Anyone can open the file, and the first thing in it says in words what it is.
            using var archive = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
            Assert.Equal("about.txt", archive.Entries[0].FullName);
            using var about = new StreamReader(archive.Entries[0].Open());
            Assert.StartsWith("This match was stopped because the players' games no longer agreed", about.ReadLine(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WatchingAMatchChangesNothingInIt()
    {
        ulong[] Play(bool watched)
        {
            var match = watched ? new WatchedMatch() : new WatchedMatch(watch: _ => null);
            match.Start();
            Assert.True(match.Clients[0].Move(40, 30));
            Assert.True(match.Clients[1].Move(20, 34));
            match.Run(seconds: 4);
            match.Finish();
            Assert.Equal(SessionEndReason.HostEnded, match.Host.EndInfo!.Reason);
            return [.. match.Clients[0].ReadReplay().Hashes.Select(hash => hash.Hash)];
        }

        var hashes = Play(watched: true);

        Assert.True(hashes.Length > 60);
        Assert.Equal(Play(watched: false), hashes);
    }

    [Fact]
    public void TheHashesOfThePartsAreThoseOfTheMatchAsItRan()
    {
        var match = new WatchedMatch();
        match.Start();
        Assert.True(match.Clients[0].Move(40, 30));
        var seen = new Dictionary<int, ulong[]>();
        var client = match.Clients[0];
        client.Player.Session.TickCompleted += _ => seen[client.Tick] = new StoreHasher().Compute(client.Match.World);
        match.Run(seconds: 2.5);
        match.Finish();

        var bundle = match.Watches[0]!.CreateBundle(client.Player.Session.EndInfo!, match.Description, client.ReplayFile.ToArray());

        // Not a desync, so nothing is known to have been agreed on and all that was kept is listed.
        Assert.Equal(-1, bundle.DesyncTick);
        Assert.Equal(-1, bundle.AgreedTick);
        Assert.Null(bundle.AgreedSnapshot);
        Assert.Equal(SecondRunOutcome.NotRun, bundle.SecondRun.Outcome);
        Assert.Equal(Enumerable.Range(0, client.Tick + 1), bundle.Hashes.Select(hashes => hashes.Tick));
        Assert.All(bundle.Hashes.Skip(1), hashes => Assert.Equal(seen[hashes.Tick], hashes.Hashes));
        Assert.Empty(bundle.Machine);
    }

    [Fact]
    public void GamesThatDifferFromTheStartHaveNoAgreedState()
    {
        var match = new WatchedMatch();
        match.Clients[1].Player.MatchBuilt += () => Nudge(match.Clients[1]);
        match.Until(() => match.Clients.All(client => client.Player.Match is not null) && match.Host.CanStart, "the matches are built");
        match.Host.Start();
        match.UntilStopped();

        Assert.Equal(0, match.Host.EndInfo!.DesyncTick);
        foreach (var bundle in match.Bundles())
        {
            Assert.Equal(-1, bundle.AgreedTick);
            Assert.Null(bundle.AgreedSnapshot);
            Assert.Equal(SecondRunOutcome.NotRun, bundle.SecondRun.Outcome);
            Assert.Contains("first comparison", bundle.SecondRun.Detail, StringComparison.Ordinal);
            Assert.Equal(0, bundle.Hashes[0].Tick);

            // The state each began with is there, which is where two such games differ.
            Assert.Equal([0], bundle.States.Select(state => state.Tick));

            var read = DesyncBundle.Read(bundle.ToArray());
            Assert.Null(read.AgreedSnapshot);
            Assert.Equal(bundle.SecondRun, read.SecondRun);
        }

        // The watch took its first hashes when the match was built, before the fault was put in here.
        Assert.Equal([], DifferingParts(match.Bundles()[0], match.Bundles()[1], 0));
        Assert.Equal(["bodies"], DifferingParts(match.Bundles()[0], match.Bundles()[1], 1));
    }

    /// <summary>What is kept goes round: old states and hashes make room, and the commands still needed stay.</summary>
    [Fact]
    public void ALongMatchKeepsOnlyItsLastStretchAndStillRunsItAgain()
    {
        var options = new DesyncWatchOptions { SnapshotsKept = 3, HashTicksKept = 70 };
        var match = new WatchedMatch(watch: _ => options);
        match.Start();
        Assert.True(match.Clients[0].Move(40, 30));
        match.Run(seconds: 9);
        Assert.True(match.Clients[0].Move(22, 30));
        Assert.True(match.Clients[1].Move(30, 22));
        match.Run(seconds: 1.4);
        var faultAfter = match.Clients[1].Tick;
        Nudge(match.Clients[1]);
        match.UntilStopped();

        var desyncTick = match.Host.EndInfo!.DesyncTick;
        Assert.InRange(desyncTick, 200, 240);
        var bundles = match.Bundles();
        Assert.All(bundles, bundle =>
        {
            Assert.Equal(desyncTick - 20, bundle.AgreedTick);
            Assert.Equal(bundle.AgreedTick, bundle.Hashes[0].Tick);
            Assert.Equal(bundle.FinalTick, bundle.Hashes[^1].Tick);
        });
        Assert.Equal(SecondRunOutcome.Repeated, bundles[0].SecondRun.Outcome);
        Assert.Equal(faultAfter + 1, bundles[1].SecondRun.PartedAt);
    }

    [Fact]
    public void AMachineThatKeptNoStateFromBeforeTheFaultSaysSo()
    {
        var options = new DesyncWatchOptions { SnapshotsKept = 1, HashTicksKept = 5, MaxStates = 0 };
        var match = new WatchedMatch(watch: _ => options);
        match.Start();
        match.Run(seconds: 1.7);
        Nudge(match.Clients[1]);
        match.UntilStopped();

        var bundle = match.Bundles()[0];

        Assert.Equal(-1, bundle.AgreedTick);
        Assert.Equal(SecondRunOutcome.NotRun, bundle.SecondRun.Outcome);
        Assert.Contains("no state from before", bundle.SecondRun.Detail, StringComparison.Ordinal);
        Assert.Equal(Enumerable.Range(bundle.FinalTick - 4, 5), bundle.Hashes.Select(hashes => hashes.Tick));
        Assert.Equal(bundle.FinalTick, DesyncBundle.Read(bundle.ToArray()).FinalTick);
    }

    [Fact]
    public void ASecondRunThatFailsDoesNotCostTheBundle()
    {
        var match = new WatchedMatch();
        match.Start();
        match.Run(seconds: 1.7);
        Nudge(match.Clients[1]);
        match.UntilStopped();
        var client = match.Clients[0];

        // A description of another match than the one played: the agreed state does not fit it.
        var other = TestData.Description(players: 3);
        var bundle = match.Watches[0]!.CreateBundle(client.Player.Session.EndInfo!, other, client.ReplayFile.ToArray(), Machine);

        Assert.Equal(SecondRunOutcome.NotRun, bundle.SecondRun.Outcome);
        Assert.StartsWith("it failed: ", bundle.SecondRun.Detail, StringComparison.Ordinal);
        Assert.Equal(Enumerable.Range(21, 20), bundle.States.Select(state => state.Tick));
        Assert.Equal(20, bundle.AgreedTick);
        Assert.NotNull(bundle.AgreedSnapshot);
        Assert.Equal(bundle.SecondRun, DesyncBundle.Read(bundle.ToArray()).SecondRun);
    }

    [Fact]
    public void AFileThatIsNotABundleIsRefused()
    {
        var match = new WatchedMatch();
        match.Start();
        match.Run(seconds: 1.7);
        Nudge(match.Clients[1]);
        match.UntilStopped();
        var bundle = match.Bundles()[0];
        var file = bundle.ToArray();

        void Refused(byte[] data, string because)
        {
            var refused = Assert.Throws<InvalidDataException>(() => DesyncBundle.Read(data));
            Assert.Contains(because, refused.Message, StringComparison.Ordinal);
        }

        Refused([], "not a desync bundle");
        Refused(Encoding.UTF8.GetBytes("This is not an archive at all, only some words."), "not a desync bundle");
        Refused(bundle.Replay, "not a desync bundle");
        Refused(file[..(file.Length / 2)], "not a desync bundle");
        Assert.False(DesyncBundle.LooksLikeOne(bundle.Replay));
        Assert.False(DesyncBundle.LooksLikeOne(bundle.FinalSnapshot));
        Assert.False(DesyncBundle.LooksLikeOne([]));

        Refused(Zip(("words.txt", "nothing")), "it has no about.txt");
        Refused(Rewrite(file, "match.replay", null), "it has no match.replay");
        Refused(Rewrite(file, "final.snapshot", null), "it has no final.snapshot");
        Refused(Rewrite(file, "hashes.txt", null), "it has no hashes.txt");
        Refused(Rewrite(file, "agreed.snapshot", null), "has no agreed.snapshot");
        Refused(Rewrite(file, "about.txt", about => about.Replace("format: 1\n", "format: 2\n", StringComparison.Ordinal)), "layout version 2");
        Refused(Rewrite(file, "about.txt", about => about.Replace("final_tick: ", "final_tick: -", StringComparison.Ordinal)), "final_tick");
        Refused(Rewrite(file, "about.txt", about => about.Replace("hash_interval: 20", "hash_interval: 0", StringComparison.Ordinal)), "hash_interval");
        Refused(Rewrite(file, "about.txt", about => about.Replace("seed: 7", "seed: seven", StringComparison.Ordinal)), "seed");
        Refused(Rewrite(file, "about.txt", about => about.Replace("second_run: repeated", "second_run: perhaps", StringComparison.Ordinal)), "not an outcome");
        Refused(Rewrite(file, "about.txt", about => about.Replace("desync_tick: 40\n", "", StringComparison.Ordinal)), "no line 'desync_tick'");
        Refused(Rewrite(file, "about.txt", about => about.Replace("parts: world, ", "parts: ", StringComparison.Ordinal)), "names of the parts");
        Refused(Rewrite(file, "hashes.txt", hashes => hashes.Replace("\n21\t", "\n19\t", StringComparison.Ordinal)), "ascending order");
        Refused(Rewrite(file, "hashes.txt", hashes => hashes + "99\tnot-a-hash\n"), "one hash for each part");
        Refused(Rewrite(file, "hashes.txt", hashes => hashes[..^2] + "g\n"), "where a hash should be");
        Refused(Rewrite(file, "about.txt", _ => "\ufffe\uffff"), "format");
        Refused(Rewrite(file, "states/tick-21.snapshot", null, rename: "states/tick-x.snapshot"), "names no tick");

        // A file that unpacks to more than a bundle can is not unpacked to the end.
        var tooMuch = Assert.Throws<InvalidDataException>(() => DesyncBundle.Read(new MemoryStream(file), maxUnpackedBytes: 1000));
        Assert.Contains("more than a bundle can", tooMuch.Message, StringComparison.Ordinal);
        Assert.Equal(bundle.FinalTick, DesyncBundle.Read(new MemoryStream(file)).FinalTick);
    }

    [Fact]
    public void ABundleDoesNotWriteWhatItCouldNotReadBack()
    {
        DesyncBundle Bundle(string part = "bodies", string key = "operating_system", int hashes = 2) => new()
        {
            Player = 0,
            HashInterval = 20,
            DesyncTick = 40,
            FinalTick = 43,
            Replay = [],
            FinalSnapshot = [1, 2, 3],
            Parts = ["world", part],
            Hashes = [new PartHashes(43, [.. Enumerable.Repeat(5UL, hashes)])],
            Machine = [new(key, "a value\nover two lines")],
        };

        var read = DesyncBundle.Read(Bundle().ToArray());
        Assert.Equal("a value over two lines", read.MachineValue("operating_system"));
        Assert.Equal(new PartHashes(43, [5, 5]).Hashes, read.HashesAt(43)!.Value.Hashes);
        Assert.Null(read.HashesAt(42));
        Assert.Null(read.StateAt(43));
        Assert.Equal(SecondRunOutcome.NotRun, read.SecondRun.Outcome);

        Assert.Throws<ArgumentException>(() => Bundle(part: "two words").ToArray());
        Assert.Throws<ArgumentException>(() => Bundle(part: "a,b").ToArray());
        Assert.Throws<ArgumentException>(() => Bundle(part: "").ToArray());
        Assert.Throws<ArgumentException>(() => Bundle(key: "final_tick").ToArray());
        Assert.Throws<ArgumentException>(() => Bundle(key: "two words").ToArray());
        Assert.Throws<ArgumentException>(() => Bundle(key: "a:b").ToArray());
        Assert.Throws<ArgumentException>(() => Bundle(hashes: 3).ToArray());
    }

    [Fact]
    public void OptionsThatKeepNothingAreRefused()
    {
        var match = new WatchedMatch(watch: _ => null);
        match.Until(() => match.Clients[0].Player.Match is not null, "the match is built");
        var client = match.Clients[0].Player;

        Assert.Throws<ArgumentOutOfRangeException>(() => new DesyncWatch(client.Session, client.Match!, new DesyncWatchOptions { SnapshotInterval = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DesyncWatch(client.Session, client.Match!, new DesyncWatchOptions { SnapshotsKept = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DesyncWatch(client.Session, client.Match!, new DesyncWatchOptions { HashTicksKept = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DesyncWatch(client.Session, client.Match!, new DesyncWatchOptions { MaxStates = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DesyncWatch(client.Session, client.Match!, new DesyncWatchOptions { LiveStatesKept = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DesyncWatch(client.Session, client.Match!, new DesyncWatchOptions { LiveStateBytes = -1 }));
    }

    /// <summary>Moves one of the player's own units by the smallest step there is, on that player's machine only.</summary>
    private static void Nudge(RealClient client)
    {
        var unit = client.Units()[0];
        ref var body = ref client.Match.Bodies.Get(unit);
        body.Position += new FixedVector2(Fixed.Epsilon, Fixed.Zero);
    }

    private static World Restore(DesyncBundle bundle, byte[] snapshot)
    {
        var match = Replay.Read(new ByteReader(bundle.Replay)).Description.CreateMatch(TestData.Data);
        var reader = new ByteReader(snapshot);
        Snapshot.Restore(match, reader);
        reader.ExpectEnd();
        return match.World;
    }

    private static string[] DifferingParts(DesyncBundle first, DesyncBundle second, int tick)
    {
        var a = first.HashesAt(tick)!.Value.Hashes;
        var b = second.HashesAt(tick)!.Value.Hashes;
        return [.. first.Parts.Where((_, part) => a[part] != b[part])];
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return stream.ToArray();
    }

    /// <summary>The archive with one entry's text changed, or with the entry left out or renamed.</summary>
    private static byte[] Rewrite(byte[] file, string name, Func<string, string>? change, string? rename = null)
    {
        using var source = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        using var stream = new MemoryStream();
        var found = false;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var bytes = new MemoryStream();
                using (var from = entry.Open())
                {
                    from.CopyTo(bytes);
                }

                var content = bytes.ToArray();
                var target = entry.FullName;
                if (entry.FullName == name)
                {
                    found = true;
                    if (change is null && rename is null)
                    {
                        continue;
                    }

                    target = rename ?? target;
                    if (change is not null)
                    {
                        var text = Encoding.UTF8.GetString(content);
                        var changed = change(text);
                        Assert.NotEqual(text, changed);
                        content = Encoding.UTF8.GetBytes(changed);
                    }
                }

                using var to = archive.CreateEntry(target).Open();
                to.Write(content);
            }
        }

        Assert.True(found, $"The bundle has no entry {name}.");
        return stream.ToArray();
    }

    /// <summary>A <see cref="RealMatch"/> whose clients each have a <see cref="DesyncWatch"/>, as the game gives them.</summary>
    private sealed class WatchedMatch
    {
        private readonly RealMatch _match;

        /// <param name="watch">The options of each seat's watch. Left out: the defaults. Returning null with this given: no watch.</param>
        public WatchedMatch(Func<int, DesyncWatchOptions?>? watch = null)
        {
            // As the game runs a match: hashes compared once a second, and a replay with one for every tick.
            _match = new RealMatch(options: new HostOptions(), replayHashInterval: 1);
            Watches = new DesyncWatch?[_match.Clients.Count];
            foreach (var client in _match.Clients)
            {
                var player = client.Player;
                var seat = client.Seat;
                player.MatchBuilt += () =>
                {
                    if (watch is null || watch(seat) is not null)
                    {
                        Watches[seat] = new DesyncWatch(player.Session, player.Match!, watch?.Invoke(seat));
                    }
                };
            }
        }

        public DesyncWatch?[] Watches { get; }

        public List<RealClient> Clients => _match.Clients;

        public HostSession Host => _match.Host;

        public Sim.Serialization.MatchDescription Description => _match.Description;

        public void Start() => _match.Start();

        public void Run(double seconds) => _match.Run(seconds);

        public void Until(Func<bool> condition, string what) => _match.Until(condition, what);

        public int Finish() => _match.Finish();

        public void UntilStopped() =>
            _match.Until(() => Clients.All(client => client.Player.Session.Phase == SessionPhase.Stopped), "the desync is found");

        /// <summary>Each client's bundle, made as the game makes it when the session has stopped.</summary>
        public DesyncBundle[] Bundles() =>
        [
            .. Clients.Select(client => Watches[client.Seat]!.CreateBundle(
                client.Player.Session.EndInfo!,
                client.Player.Description!,
                client.ReplayFile.ToArray(),
                Machine)),
        ];
    }
}
