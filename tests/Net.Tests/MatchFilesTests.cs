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

    [Fact]
    public void ADesyncSnapshotBringsTheStateBack()
    {
        var match = new RealMatch(options: new HostOptions());
        match.Start();
        Assert.True(match.Clients[0].Move(40, 30));
        match.Run(seconds: 4);
        var client = match.Clients[1];
        var writer = new ByteWriter();

        DesyncSnapshot.Write(writer, client.Seat, 60, match.Description, client.Match.World);
        var file = DesyncSnapshot.Read(writer.ToArray());
        var restored = file.Restore(TestData.Data);

        Assert.Equal(1, file.Player);
        Assert.Equal(60, file.DesyncTick);
        Assert.Equal(match.Description.Seed, file.Description.Seed);
        Assert.Equal(client.Tick, restored.World.Tick);
        Assert.Equal(client.Hash, restored.World.ComputeHash());

        // The restored match goes on as the live one does.
        Assert.True(match.Clients[0].Move(70, 70));
        var tick = client.Tick;
        match.Run(seconds: 1);
        foreach (var (commandTick, command) in client.Scheduled.Where(entry => entry.Tick >= tick))
        {
            restored.Simulation.Commands.Enqueue(commandTick, command);
        }

        restored.Simulation.Run(client.Tick - tick);
        Assert.Equal(client.Hash, restored.World.ComputeHash());
    }

    [Fact]
    public void TwoPeersThatPartedLeaveSnapshotsThatDiffer()
    {
        var match = new RealMatch(options: new HostOptions());
        match.Start();
        match.Run(seconds: 1);
        RealMatch.Corrupt(match.Clients[1]);
        match.Until(() => match.Clients.All(client => client.Player.Session.Phase == SessionPhase.Stopped), "the desync is found");

        var files = match.Clients.Select(client =>
        {
            var writer = new ByteWriter();
            DesyncSnapshot.Write(writer, client.Seat, client.Player.Session.EndInfo!.DesyncTick, match.Description, client.Match.World);
            return DesyncSnapshot.Read(writer.ToArray());
        }).ToArray();

        Assert.Equal(files[0].DesyncTick, files[1].DesyncTick);
        var worlds = files.Select(file => file.Restore(TestData.Data).World).ToArray();
        Assert.Equal(worlds[0].Tick, worlds[1].Tick);
        Assert.Equal(worlds[0].Entities.Count + 1, worlds[1].Entities.Count);
    }

    [Fact]
    public void DataThatIsNotADesyncSnapshotIsRejected()
    {
        var match = new RealMatch(options: new HostOptions());
        match.Start();
        var writer = new ByteWriter();
        DesyncSnapshot.Write(writer, 0, -1, match.Description, match.Clients[0].Match.World);
        var good = writer.ToArray();

        Assert.Throws<InvalidDataException>(() => DesyncSnapshot.Read(Array.Empty<byte>()));
        Assert.Throws<InvalidDataException>(() => DesyncSnapshot.Read("RPLY and then some more bytes"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => DesyncSnapshot.Read(good.AsMemory(0, 20)));

        var otherVersion = (byte[])good.Clone();
        otherVersion[4] = 2;
        Assert.Throws<InvalidDataException>(() => DesyncSnapshot.Read(otherVersion));

        // The state is checked when it is restored: cut short, or with bytes left over.
        Assert.Throws<InvalidDataException>(() => DesyncSnapshot.Read(good.AsMemory(0, good.Length - 3)).Restore(TestData.Data));
        Assert.Throws<InvalidDataException>(() => DesyncSnapshot.Read((byte[])[.. good, 0]).Restore(TestData.Data));
        Assert.Throws<InvalidDataException>(() => DesyncSnapshot.Read(good).Restore(TestData.ChangedData()));
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
