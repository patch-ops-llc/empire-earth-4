using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;

namespace Sim.Tests.Serialization;

/// <summary>
/// The snapshot round trip for real matches, built by <see cref="MatchSetup.Create"/>.
/// </summary>
public class MatchSnapshotTests
{
    private const int MazeSize = 32;
    private const int MazeLastTick = 520;
    private const int WorkersLastTick = 400;

    private static readonly PlayerSetup[] TwoPlayers = [new(0), new(1)];

    /// <summary>
    /// The starting workers of two players on the sample map, sent across it, with three turned
    /// round on the way. Snapshots: before any order, with an order waiting, with every worker
    /// walking, just after the turn, with some arrived, and with all at rest.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(40)]
    [InlineData(61)]
    [InlineData(150)]
    [InlineData(WorkersLastTick)]
    public void AMatchWithItsStartingWorkersRestoresAndContinues(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(BuildWorkers, WorkersScript(), snapshotTick, WorkersLastTick);

    /// <summary>
    /// Units that need routes through a maze. Snapshots: before any order, with the orders
    /// waiting, twice with some units waiting for a route while others walk theirs, with every
    /// unit on its route, just after eight were sent elsewhere, with units on routes, on their
    /// last straight line and arrived all at once, and with all at rest.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(61)]
    [InlineData(175)]
    [InlineData(225)]
    [InlineData(465)]
    [InlineData(MazeLastTick)]
    public void AMatchWithUnitsWaitingForAndWalkingRoutesRestoresAndContinues(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(BuildMaze, MazeScript(), snapshotTick, MazeLastTick);

    /// <summary>The runs do what the two tests above say they do, so that those tests mean something.</summary>
    [Fact]
    public void TheRunsHaveUnitsInEveryStateAtTheSnapshots()
    {
        var workers = Run(BuildWorkers(), WorkersScript(), 40);
        Assert.Equal(12, Count(workers, MoveState.Moving) + Count(workers, MoveState.Following));
        workers.Simulation.Run(110);
        Assert.InRange(Count(workers, MoveState.Idle), 1, 11);
        workers.Simulation.Run(WorkersLastTick - 150);
        Assert.Equal(12, Count(workers, MoveState.Idle));

        var maze = Run(BuildMaze(), MazeScript(), 2);
        Assert.Equal((6, 10, 6), (Count(maze, MoveState.Idle), Count(maze, MoveState.Planning), Count(maze, MoveState.Following)));
        Assert.Contains(Enumerable.Range(0, 22), index => maze.Movers.ValueAt(index).RouteNode > 0);
        maze.Simulation.Tick();
        Assert.Equal((10, 12), (Count(maze, MoveState.Planning), Count(maze, MoveState.Following)));
        maze.Simulation.Tick();
        Assert.Equal(22, Count(maze, MoveState.Following));
        maze.Simulation.Run(171);
        Assert.InRange(Count(maze, MoveState.Moving), 1, 21);
        Assert.InRange(Count(maze, MoveState.Following), 1, 21);
        maze.Simulation.Run(50);
        Assert.InRange(Count(maze, MoveState.Idle), 1, 20);
        Assert.InRange(Count(maze, MoveState.Moving), 1, 20);
        Assert.InRange(Count(maze, MoveState.Following), 1, 20);
        maze.Simulation.Run(MazeLastTick - 225);
        Assert.Equal(22, Count(maze, MoveState.Idle));
        Assert.Equal(MapGrid.TileCenter(16, MazeSize - 1), maze.PositionOf(maze.World.Entities[15]));
    }

    /// <summary>
    /// What is worked out from the state is not in a snapshot. The spatial index of a match that a
    /// snapshot was restored into still answers for the old world until
    /// <see cref="Match.RefreshDerivedState"/> has run, and for the restored one afterwards.
    /// </summary>
    [Fact]
    public void ARestoredMatchAnswersForTheRestoredWorldOnceItsDerivedStateIsRefreshed()
    {
        var source = Run(BuildMaze(), MazeScript(), 175);
        var writer = new ByteWriter();
        Snapshot.Write(source.World, writer);
        var expected = NearTheMiddle(source);
        Assert.NotEmpty(expected);

        var target = Run(BuildMaze(), MazeScript(), MazeLastTick);
        var before = NearTheMiddle(target);
        Snapshot.Restore(target.Simulation, new ByteReader(writer.ToArray()));
        Assert.Equal(before, NearTheMiddle(target));
        Assert.NotEqual(expected, before);

        target.RefreshDerivedState();
        Assert.Equal(expected, NearTheMiddle(target));
        Assert.Equal(22, target.SpatialIndex.Count);
    }

    private static List<EntityId> NearTheMiddle(Match match)
    {
        var found = new List<EntityId>();
        match.SpatialIndex.QueryCircle(MapGrid.TileCenter(MazeSize / 2, MazeSize / 2), Fixed.One * 10, found);
        return found;
    }

    private static Match Run(Match match, (int Tick, Command Command)[] script, int ticks)
    {
        foreach (var (tick, command) in script)
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        match.Simulation.Run(ticks);
        return match;
    }

    private static int Count(Match match, MoveState state) =>
        Enumerable.Range(0, match.Movers.Count).Count(index => match.Movers.ValueAt(index).State == state);

    private static EntityId[] UnitsOf(Match match) =>
        [.. Enumerable.Range(0, match.World.Entities.Count).Select(index => match.World.Entities[index])];

    /// <summary>The sample data's map with both starts taken: six workers for each of two players.</summary>
    private static Match BuildWorkers() => MatchSetup.Create(TestMatches.Data, TestMatches.Data.Maps[0], 7, TwoPlayers);

    private static (int Tick, Command Command)[] WorkersScript()
    {
        var units = UnitsOf(BuildWorkers());
        var far = TestMatches.Point("30.5", "12.25");
        var near = TestMatches.Point("6.75", "3.5");
        return
        [
            (2, new Command(CommandType.Move, 0, units.AsSpan(0, 6), far.X, far.Y)),
            (5, new Command(CommandType.Move, 1, units.AsSpan(6, 6), near.X, near.Y)),
            (60, new Command(CommandType.Move, 0, units.AsSpan(0, 3), near.X, near.Y)),
        ];
    }

    /// <summary>
    /// A map of walls with a gap at alternating ends, so that the way from top to bottom runs
    /// back and forth and every route is a long search.
    /// </summary>
    private static MapDef Maze()
    {
        var rows = new string[MazeSize];
        for (var y = 0; y < MazeSize; y++)
        {
            rows[y] = y % 4 != 2 ? new string('.', MazeSize)
                : y % 8 == 2 ? new string('#', MazeSize - 2) + ".."
                : ".." + new string('#', MazeSize - 2);
        }

        return TestMaps.FromRows(rows);
    }

    /// <summary>Sixteen units of player 0 along the top edge and six of player 1 along the bottom.</summary>
    private static Match BuildMaze()
    {
        var match = TestMatches.Create(Maze());
        var speed = Fixed.One / 2;
        for (var i = 0; i < 16; i++)
        {
            match.SpawnUnit(0, MapGrid.TileCenter(2 * i, 0), speed);
        }

        for (var i = 0; i < 6; i++)
        {
            match.SpawnUnit(1, MapGrid.TileCenter(10 + i, MazeSize - 1), speed);
        }

        match.RefreshDerivedState();
        return match;
    }

    private static (int Tick, Command Command)[] MazeScript()
    {
        var units = UnitsOf(BuildMaze());
        var bottom = MapGrid.TileCenter(16, MazeSize - 1);
        var top = TestMatches.Point("20.25", "0.5");
        var middle = TestMatches.Point("5.5", "13.75");
        var script = new List<(int Tick, Command Command)>();

        // One order each, so that each unit needs a route of its own and most have to wait.
        for (var i = 0; i < 16; i++)
        {
            script.Add((1, new Command(CommandType.Move, 0, units.AsSpan(i, 1), bottom.X, bottom.Y)));
        }

        // One order for a group, which shares its routes.
        script.Add((2, new Command(CommandType.Move, 1, units.AsSpan(16, 6), top.X, top.Y)));

        // Units on a route are sent somewhere else.
        script.Add((60, new Command(CommandType.Move, 0, units.AsSpan(0, 8), middle.X, middle.Y)));
        return [.. script];
    }
}
