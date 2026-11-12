using Sim.Core;
using Sim.Research;

namespace Net.Tests;

/// <summary>
/// The development tier (<see cref="CommandType.DebugSetTier"/>) in a match between machines. It
/// is a command, so the host hands it to every peer for the same tick: both raise the track in
/// that tick, stay on the same state hash, and write it into their replays.
/// </summary>
public class DebugSetTierMatchTests
{
    private static readonly LinkConditions Internet = new(TimeSpan.FromMilliseconds(45), TimeSpan.FromMilliseconds(30));

    [Fact]
    public void BothPeersRaiseTheTrackInTheSameTickAndTheirReplaysHoldTheCommand()
    {
        // The repository's data decides whether the command is carried out, for every peer alike.
        var switchedOn = TestData.Data.Rules.Debug.Spawn;
        var track = TestData.Data.TrackRoles.Logistics;
        var match = new RealMatch(clients: 2, conditions: Internet, options: new HostOptions());
        match.Start();
        match.Run(seconds: 1);
        var finished = match.Clients.Select(client => new List<(int Tick, int Player, int Tier)>()).ToArray();
        foreach (var client in match.Clients)
        {
            client.Player.Session.TickCompleted += _ => finished[client.Seat].AddRange(TiersFinished(client));
        }

        Assert.True(match.Clients[0].Player.Session.Submit(DebugSetTierHandler.CommandFor(0, track, 3)));
        match.Run(seconds: 0.5);
        Assert.True(match.Clients[1].Player.Session.Submit(DebugSetTierHandler.CommandFor(1, track, 2)));
        Assert.True(match.Clients[0].Move(40, 30));
        match.Run(seconds: 2);
        var finalTick = match.Finish();

        var (first, second) = (match.Clients[0], match.Clients[1]);
        Assert.Equal(first.Hash, second.Hash);
        Assert.All(match.Clients, client =>
        {
            Assert.Equal(finalTick, client.Tick);
            Assert.Equal(switchedOn ? [3, 2] : [1, 1], new[] { client.Match.Research.State.Tier(0, track), client.Match.Research.State.Tier(1, track) });
            Assert.Equal(switchedOn ? 0 : 2, client.Match.Simulation.DroppedCommandCount);

            var replay = client.ReadReplay();
            Assert.True(replay.IsComplete);
            Assert.Equal(
                [(0, track, 3), (1, track, 2)],
                replay.Commands
                    .Where(entry => entry.Command.Type == CommandType.DebugSetTier)
                    .Select(entry => (entry.Command.Player, entry.Command.ContentId, entry.Command.TargetX.FloorToInt())));
            var again = replay.Run(TestData.Data);
            Assert.True(again.Reproduced);
            Assert.Equal(client.Hash, again.FinalHash);
        });

        // Every peer saw each tier finish in the same tick as the other.
        Assert.Equal(finished[0], finished[1]);
        Assert.Equal(switchedOn ? [(0, 2), (0, 3), (1, 2)] : [], finished[0].Select(entry => (entry.Player, entry.Tier)));
    }

    private static IEnumerable<(int Tick, int Player, int Tier)> TiersFinished(RealClient client)
    {
        foreach (var listed in client.Match.Simulation.Events)
        {
            if (listed.Kind == SimEventKind.ResearchFinished && ResearchEvent.TryRead(listed, out var research))
            {
                yield return (research.Tick, research.Player, research.Tier);
            }
        }
    }
}
