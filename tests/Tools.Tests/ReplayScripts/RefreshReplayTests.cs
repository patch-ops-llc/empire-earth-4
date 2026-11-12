using Sim.Core;
using Sim.Data;
using Sim.Research;
using Sim.Serialization;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The golden replay <c>switchback-refresh.replay</c>: what that recorded match must reach. It
/// is there for the live refresh (design 2.3): when a research finishes, the units and workers
/// on the map follow it.
/// </summary>
/// <remarks>
/// This reads the replay and the frozen data itself and shares nothing with the other tests of
/// the golden replays, so it stays as it is however those are arranged.
/// </remarks>
public class RefreshReplayTests
{
    private const string GoldenReplay = "switchback-refresh.replay";

    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var loaded = DataLoader.Load(new DiskDataFiles(Repository.GoldenDataFolder));
        Assert.True(loaded.IsValid, $"The data in {Repository.GoldenDataFolder} is not valid.");
        var data = loaded.Data;
        var replay = Sim.Serialization.Replay.Read(
            new ByteReader(File.ReadAllBytes(Path.Combine(Repository.GoldenReplaysFolder, GoldenReplay))));
        Assert.True(replay.IsComplete);
        var match = replay.Description.CreateMatch(data);
        foreach (var (tick, command) in replay.Commands)
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var maximums = new Dictionary<EntityId, long>();
        var finished = new List<ResearchEvent>();
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            maximums.Clear();
            for (var i = 0; i < match.Workers.Count; i++)
            {
                var worker = match.Workers.EntityAt(i);
                maximums[worker] = match.Health.Get(worker).Maximum;
            }

            match.Simulation.Tick();

            finished.Clear();
            foreach (var simEvent in match.Simulation.Events)
            {
                if (simEvent.Kind == SimEventKind.ResearchFinished && ResearchEvent.TryRead(simEvent, out var research))
                {
                    finished.Add(research);
                }
            }

            var raised = new int[match.Players.Count];
            foreach (var (worker, before) in maximums)
            {
                var health = match.Health.Get(worker);
                if (health.Maximum == before)
                {
                    continue;
                }

                var owner = match.Bodies.Get(worker).Owner;
                Assert.True(health.Maximum > before, $"The hit points of {worker} fell in tick {tick}.");
                Assert.Contains(finished, research => research.Player == owner && research.Track == data.TrackRoles.Protection);

                // Nothing in this match harms a worker: it was at its maximum and still is.
                Assert.Equal(health.Maximum, health.HitPoints);
                raised[owner]++;
            }

            foreach (var research in finished)
            {
                var workers = maximums.Keys.Count(worker => match.Bodies.Get(worker).Owner == research.Player);
                Assert.NotEqual(0, workers);
                if (research.Track == data.TrackRoles.Protection)
                {
                    Assert.Equal(workers, raised[research.Player]);
                    seen.Add("a worker's hit points raised by a finished research");
                }
                else if (raised[research.Player] == 0)
                {
                    seen.Add("a research finished that leaves the workers' hit points as they are");
                }
            }
        }

        string[] expected =
        [
            "a worker's hit points raised by a finished research",
            "a research finished that leaves the workers' hit points as they are",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }
}
