using Sim.Core;
using Sim.Map;
using Sim.Serialization;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using Sim.Vision;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Vision;

/// <summary>
/// The fog of war is state. A snapshot taken between two times sight is worked out holds the
/// picture of the last time, which nothing could work out again from where the units stand now,
/// and a restored match has that picture and goes on as the match the snapshot came from.
/// </summary>
/// <remarks>
/// The grid is not a component store, so the comparison of every value of every component after a
/// restore does not reach it. <see cref="VisionAssert.SamePicture"/> is its comparison.
/// </remarks>
public class VisionSnapshotTests
{
    private const int LastTick = 260;

    /// <summary>Units of both players that walk the whole time, one of them fast, and an attack order whose target is lost from sight.</summary>
    private static Match Build()
    {
        var match = Create(TestMaps.Open(48, 24));
        match.Spawn(0, Hoplite, "2.5", "5.5");
        match.SpawnWorker(0, "14.5", "8.5");
        match.SpawnWorker(1, "16.5", "5.5");
        match.Spawn(0, Hippeus, "4.5", "18.5");
        match.Spawn(1, Ballista, "40.5", "12.5");
        match.Spawn(1, Hippeus, "44.5", "20.5");
        return match;
    }

    private static (int Tick, Command Command)[] Script()
    {
        EntityId hoplite = new(1), spotter = new(2), enemyWorker = new(3), rider = new(4), ballista = new(5), enemyRider = new(6);
        return
        [
            (0, AttackCommand(0, enemyWorker, hoplite)),
            (0, MoveCommand(1, TestMatches.Point("46.5", "2.5"), enemyWorker)),
            (0, MoveCommand(0, TestMatches.Point("44.5", "3.5"), rider)),
            (1, MoveCommand(1, TestMatches.Point("3.5", "21.5"), enemyRider)),
            (2, AttackMoveCommand(1, TestMatches.Point("20.5", "12.5"), ballista)),
            (90, MoveCommand(0, TestMatches.Point("30.5", "20.5"), spotter)),
            (150, MoveCommand(0, TestMatches.Point("5.5", "12.5"), rider)),
        ];
    }

    private static Match RunTo(int tick)
    {
        var match = Build();
        foreach (var (at, command) in Script())
        {
            match.Simulation.Commands.Enqueue(at, command);
        }

        match.Run(tick);
        return match;
    }

    /// <summary>
    /// Sight is worked out in ticks 0, 4, 8 and so on, so after 1 tick the picture is fresh and
    /// after 2, 3 and 4 it is one, two and three ticks old; 26 is just after the attack order's
    /// target was lost; the others are further on.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(94)]
    [InlineData(151)]
    [InlineData(203)]
    public void ASnapshotTakenBetweenTwoTimesSightIsWorkedOutRestoresTheSamePicture(int snapshotTick)
    {
        var source = RunTo(snapshotTick);
        var writer = new ByteWriter();
        Snapshot.Write(source.World, writer);
        var snapshot = writer.ToArray();

        // Into a match that has not run, and into one that has run to the end and has another picture.
        var fresh = Build();
        var used = RunTo(LastTick);
        Assert.NotEqual(source.Vision.Grid.CountExplored(0), used.Vision.Grid.CountExplored(0));
        foreach (var target in new[] { fresh, used })
        {
            var revision = target.Vision.Grid.Revision;
            Snapshot.Restore(target, new ByteReader(snapshot));

            VisionAssert.SamePicture(source.Vision.Grid, target.Vision.Grid);
            Assert.NotEqual(revision, target.Vision.Grid.Revision);
            for (var id = 1; id <= 6; id++)
            {
                for (var player = 0; player < 2; player++)
                {
                    Assert.Equal(source.Vision.Sees(player, new EntityId(id)), target.Vision.Sees(player, new EntityId(id)));
                }
            }
        }

        SnapshotAssert.RestoresAndContinues(Build, Script(), snapshotTick, LastTick);
    }

    /// <summary>
    /// What makes the grid state and not something a restore could work out: between two times
    /// sight is worked out, the picture is not the one the units' positions give.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(27)]
    [InlineData(151)]
    public void BetweenTwoTimesThePictureIsNotTheOneThePositionsGive(int tick)
    {
        var match = RunTo(tick);
        var held = new byte[match.Map.Width * match.Map.Height];
        var worked = new byte[held.Length];
        match.Vision.Grid.CopyStates(0, held);
        var hash = match.World.ComputeHash();

        match.Vision.Refresh();

        match.Vision.Grid.CopyStates(0, worked);
        Assert.NotEqual(held, worked);
        Assert.NotEqual(hash, match.World.ComputeHash());
    }

    [Fact]
    public void TheRunHasTheStatesTheSnapshotsAreTakenIn()
    {
        EntityId hoplite = new(1), enemyWorker = new(3);

        var early = RunTo(7);
        Assert.Equal(Sim.Combat.UnitOrder.Attack, early.UnitOf(hoplite).Order);
        Assert.True(early.Vision.Sees(0, enemyWorker));

        var lost = RunTo(26);
        Assert.False(lost.Vision.Sees(0, enemyWorker));
        Assert.Equal(Sim.Combat.UnitOrder.Move, lost.UnitOf(hoplite).Order);
        Assert.Equal(Sim.Combat.UnitOrder.Attack, RunTo(23).UnitOf(hoplite).Order);

        // Each player has explored ground it no longer sees, and both have seen units of the other.
        var late = RunTo(203);
        for (var player = 0; player < 2; player++)
        {
            Assert.True(late.Vision.Grid.CountExplored(player) > late.Vision.Grid.CountVisible(player) + 100);
            Assert.True(late.Vision.Grid.CountVisible(player) > 0);
        }
    }
}

/// <summary>
/// What a player remembers of structures is state too. A snapshot holds each memory as it was
/// written when the player last saw the structure, which nothing could work out again from the
/// match as it stands, and a restored match remembers the same and goes on the same.
/// </summary>
public class StructureMemorySnapshotTests
{
    private const int LastTick = 420;

    private static readonly EntityId Outpost = new(1);
    private static readonly EntityId Foundation = new(2);
    private static readonly EntityId Rider = new(3);

    /// <summary>
    /// A finished Outpost of player 1 and a Generator of its that is building itself, both in
    /// sight of a rider of player 0.
    /// </summary>
    private static Match Build()
    {
        var match = FogMatches.Create(TestMaps.Open(60, 24));
        Assert.Equal(Outpost, match.Place(1, "outpost", 40, 10));
        Assert.Equal(Foundation, match.Lay(1, "generator", 38, 4));
        Assert.Equal(Rider, match.Spawn(0, Hippeus, "31.5", "11.5"));
        match.RefreshDerivedState();
        return match;
    }

    /// <summary>The rider leaves, the Generator is cancelled while nobody looks, and the rider comes back.</summary>
    private static (int Tick, Command Command)[] Script() =>
    [
        (2, MoveCommand(0, TestMatches.Point("3.5", "11.5"), Rider)),
        (150, new Command(CommandType.CancelBuild, 1, [Foundation])),
        (200, MoveCommand(0, TestMatches.Point("31.5", "11.5"), Rider)),
    ];

    private static Match RunTo(int tick)
    {
        var match = Build();
        foreach (var (at, command) in Script())
        {
            match.Simulation.Commands.Enqueue(at, command);
        }

        match.Run(tick);
        return match;
    }

    [Fact]
    public void TheMatchOfTheseTestsRemembersAndForgets()
    {
        var view = new PlayerView(RunTo(1), 0);
        Assert.True(view.TryGetStructure(Foundation, out var seen));
        Assert.False(seen.IsRemembered);

        var away = RunTo(140);
        view = new PlayerView(away, 0);
        Assert.True(view.TryGetStructure(Outpost, out seen) && seen.IsRemembered);
        Assert.True(view.TryGetStructure(Foundation, out seen) && seen.IsRemembered);
        Assert.False(seen.IsFinished);
        var built = seen.Built;
        Assert.InRange(built, 4, 139);

        // Cancelled, and remembered as it was.
        var gone = RunTo(190);
        view = new PlayerView(gone, 0);
        Assert.False(gone.World.Entities.IsAlive(Foundation));
        Assert.True(view.TryGetStructure(Foundation, out seen) && seen.IsRemembered);
        Assert.Equal(built, seen.Built);

        // Seen again without it, and with the Outpost still there.
        view = new PlayerView(RunTo(LastTick), 0);
        Assert.False(view.TryGetStructure(Foundation, out _));
        Assert.True(view.TryGetStructure(Outpost, out seen));
        Assert.False(seen.IsRemembered);
    }

    /// <summary>
    /// Sight is worked out in ticks 0, 4, 8 and so on. The snapshots are taken while the rider
    /// sees both structures, in the ticks round the one in which it loses them, while it
    /// remembers them, after the Generator is cancelled, and after it has seen the tiles again.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(40)]
    [InlineData(41)]
    [InlineData(42)]
    [InlineData(43)]
    [InlineData(140)]
    [InlineData(151)]
    [InlineData(190)]
    [InlineData(330)]
    [InlineData(LastTick)]
    public void ASnapshotRestoresWhatEachPlayerRemembers(int snapshotTick)
    {
        var source = RunTo(snapshotTick);
        var writer = new ByteWriter();
        Snapshot.Write(source.World, writer);
        var snapshot = writer.ToArray();

        // Into a match that has not run, and into one that has run to the end and remembers something else.
        foreach (var target in new[] { Build(), RunTo(LastTick) })
        {
            Snapshot.Restore(target, new ByteReader(snapshot));

            VisionAssert.SamePicture(source.Vision.Grid, target.Vision.Grid);
            var expected = new List<SeenStructure>();
            var actual = new List<SeenStructure>();
            for (var player = 0; player < 2; player++)
            {
                Assert.Equal(source.Vision.Memory.Count(player), target.Vision.Memory.Count(player));
                new PlayerView(source, player).CollectStructures(expected);
                new PlayerView(target, player).CollectStructures(actual);
                Assert.Equal(expected, actual);
                Assert.Equal(source.Vision.KnowsBlocked(player, new TileSquare(38, 4, 2)), target.Vision.KnowsBlocked(player, new TileSquare(38, 4, 2)));
            }
        }

        SnapshotAssert.RestoresAndContinues(Build, Script(), snapshotTick, LastTick);
    }
}
