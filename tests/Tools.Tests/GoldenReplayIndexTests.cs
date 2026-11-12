using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Serialization;

namespace Tools.Tests;

/// <summary>
/// The spatial index is not state, so a match restored from a snapshot makes its own from the
/// state it was given. A match that has run on must be getting the same answers from the index
/// its ticks made, in every tick, or a peer that joins again would see another match than the
/// others. This holds every recorded match to that, on whatever replays the folder has.
/// </summary>
public class GoldenReplayIndexTests
{
    /// <summary>
    /// After every tick of every golden replay the index is what making it new would give: it
    /// counts and lists the living bodies and nothing else, each where it stands now.
    /// </summary>
    /// <remarks>
    /// It fails for a rule that adds or moves a body after the index of the tick was made and
    /// does not make the index again (<c>SpatialIndex.Rebuild</c>), and for an index that still
    /// counts or lists what was destroyed.
    /// </remarks>
    [Fact]
    public void AfterEveryTickOfEveryGoldenReplayTheIndexIsWhatARestoredMatchWouldMake()
    {
        var data = DataLoader.Load(new DiskDataFiles(Repository.GoldenDataFolder));
        Assert.True(data.IsValid);
        var paths = Directory.GetFiles(Repository.GoldenReplaysFolder, "*.replay");
        Assert.NotEmpty(paths);
        var living = new List<EntityId>();
        var found = new List<EntityId>();
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            var replay = Sim.Serialization.Replay.Read(new ByteReader(File.ReadAllBytes(path)));
            var match = replay.Description.CreateMatch(data.Data);
            foreach (var (tick, command) in replay.Commands)
            {
                match.Simulation.Commands.Enqueue(tick, command);
            }

            var everywhere = new FixedVector2(Fixed.FromInt(match.Grid.Width + 1), Fixed.FromInt(match.Grid.Height + 1));
            for (var tick = 0; tick < replay.TickCount; tick++)
            {
                match.Simulation.Tick();

                living.Clear();
                for (var i = 0; i < match.Bodies.Count; i++)
                {
                    living.Add(match.Bodies.EntityAt(i));
                }

                Assert.True(
                    living.Count == match.SpatialIndex.Count,
                    $"{name}: after tick {tick} the index counts {match.SpatialIndex.Count} entities and the match has {living.Count} bodies.");
                match.SpatialIndex.QueryBox(new FixedVector2(-Fixed.One, -Fixed.One), everywhere, found);
                Assert.True(
                    living.SequenceEqual(found),
                    $"{name}: after tick {tick} the index lists [{string.Join(", ", found)}] and the match has the bodies [{string.Join(", ", living)}].");
                foreach (var entity in living)
                {
                    var position = match.Bodies.Get(entity).Position;
                    match.SpatialIndex.QueryBox(position, position, found);
                    Assert.True(
                        found.Contains(entity),
                        $"{name}: after tick {tick} the index does not have {entity} at {position}, where it stands.");
                }
            }
        }
    }
}
