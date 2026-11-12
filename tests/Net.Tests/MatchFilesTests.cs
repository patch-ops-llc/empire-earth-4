using Sim.Serialization;

namespace Net.Tests;

/// <summary>What a peer writes to disk: the replay of every match, and its state when a match stops on a desync.</summary>
public class MatchFilesTests
{
    [Fact]
    public void TheReplayIsInTheStreamWhileTheMatchRuns()
    {
        var match = new RealMatch(options: new HostOptions());
        match.Start();
        var client = match.Clients[0];
        var atStart = client.ReplayFile.Length;
        Assert.True(atStart > 0);

        Assert.True(client.Move(40, 30));
        match.Run(seconds: 3.2);

        // What a game that failed here would leave behind: a replay of the whole seconds so far.
        Assert.True(client.ReplayFile.Length > atStart);
        Assert.False(client.Recording!.IsComplete);
        var partial = Replay.Read(new ByteReader(client.ReplayFile.ToArray()));
        Assert.False(partial.IsComplete);
        Assert.InRange(partial.TickCount, 60, client.Tick);
        Assert.Single(partial.Commands);
        Assert.True(partial.Run(TestData.Data).Reproduced);
    }

    [Fact]
    public void AClientThatLeavesHasAReplayOfTheMatchUpToThere()
    {
        var match = new RealMatch(options: new HostOptions());
        match.Start();
        match.Run(seconds: 1.3);
        var leaver = match.Clients[1];

        leaver.Player.Leave();

        Assert.True(leaver.Recording!.IsComplete);
        var replay = leaver.ReadReplay();
        Assert.Equal(leaver.Tick, replay.End!.Value.Tick);
        Assert.True(replay.Run(TestData.Data).Reproduced);
    }

    [Fact]
    public void AStreamThatFailsEndsTheRecordingAndNotTheMatch()
    {
        var link = new LoopbackNetwork().Connect();
        var host = new HostSession(new HostOptions { PlayerCount = 1, CommandDelay = 1, Match = TestData.Description(players: 1) });
        host.Attach(0, link.HostEnd);
        var client = new MatchClient(link.ClientEnd, TestData.Data, TestData.GameVersion, readyWhenBuilt: true);
        var file = new FailingStream { WritesLeft = 3 };
        MatchRecording? recording = null;
        client.MatchBuilt += () => recording = new MatchRecording(client.Session, client.Description!, client.Match!, file);

        host.Advance(TimeSpan.Zero);
        client.Advance(TimeSpan.Zero);
        host.Advance(TimeSpan.Zero);
        host.Start();
        for (var i = 0; i < 600; i++)
        {
            host.Advance(TestMatch.Frame);
            client.Advance(TestMatch.Frame);
        }

        Assert.Equal("The disk is full.", recording!.Failure);
        Assert.False(recording.IsComplete);
        Assert.Equal(SessionPhase.Running, client.Session.Phase);
        Assert.InRange(client.Match!.World.Tick, 195, 205);

        client.Leave();
        Assert.False(recording.IsComplete);
    }

    /// <summary>
    /// What the game leaves on a desync: each peer's replay with a hash for every tick, and a
    /// snapshot of the state it stopped in. Together they say when the peers parted and how.
    /// </summary>
    [Fact]
    public void WithAHashEveryTickTheReplaysOfTwoPeersShowTheTickTheyParted()
    {
        var match = new RealMatch(options: new HostOptions(), replayHashInterval: 1);
        match.Start();
        Assert.True(match.Clients[0].Move(40, 30));
        match.Run(seconds: 1.7);
        var partedAfter = match.Clients[1].Tick;
        RealMatch.Corrupt(match.Clients[1]);
        match.Until(() => match.Clients.All(client => client.Player.Session.Phase == SessionPhase.Stopped), "the desync is found");

        var replays = match.Clients.Select(client => client.ReadReplay()).ToArray();
        var finalTick = match.Host.EndInfo!.FinalTick;
        Assert.All(replays, replay =>
        {
            Assert.Equal(1, replay.HashInterval);
            Assert.Equal(finalTick, replay.End!.Value.Tick);
            Assert.Equal(Enumerable.Range(0, finalTick + 1), replay.Hashes.Select(hash => hash.Tick));
        });

        // The session compares hashes once a second, so it names a later tick than the replays do.
        var firstDifference = replays[0].Hashes.Zip(replays[1].Hashes).First(pair => pair.First.Hash != pair.Second.Hash).First.Tick;
        Assert.Equal(partedAfter + 1, firstDifference);
        Assert.True(match.Host.EndInfo.DesyncTick > firstDifference);

        // The peer that stayed right reproduces from its replay. The other does not, from that tick.
        Assert.True(replays[0].Run(TestData.Data).Reproduced);
        Assert.Equal(firstDifference, replays[1].Run(TestData.Data).Mismatch!.Value.Tick);

        // Each peer's snapshot goes back into a match built from the description in its replay.
        var worlds = match.Clients.Zip(replays).Select(pair =>
        {
            var writer = new ByteWriter();
            Snapshot.Write(pair.First.Match.World, writer);
            var restored = pair.Second.Description.CreateMatch(TestData.Data);
            Snapshot.Restore(restored, new ByteReader(writer.ToArray()));
            Assert.Equal(pair.First.Hash, restored.World.ComputeHash());
            return restored.World;
        }).ToArray();
        Assert.Equal(worlds[0].Tick, worlds[1].Tick);
        Assert.Equal(worlds[0].Entities.Count + 1, worlds[1].Entities.Count);
    }

    private sealed class FailingStream : MemoryStream
    {
        public int WritesLeft { get; set; }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (WritesLeft == 0)
            {
                throw new IOException("The disk is full.");
            }

            WritesLeft--;
            base.Write(buffer);
        }
    }
}
