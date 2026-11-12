using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Production;
using Sim.Setup;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Production;

/// <summary>
/// Design 8.4, row 32, and design 5.4: a unit appears on the side tile nearest the rally point,
/// ties to the lower y and then the lower x, with full hit points, and gets a move order to the
/// rally point if one is set.
/// </summary>
public class AppearTests
{
    private static readonly int Hoplite = Chassis("hoplite");
    private static readonly int Peltast = Chassis("peltast");
    private static readonly int[] SwarmMix = [2, 2, 3, 5, 5, 3];

    /// <summary>A Barracks on the tiles (10, 10) to (12, 12) of open ground: its center is (11.5, 11.5).</summary>
    private static Match WithBarracks(out EntityId barracks, MapDef? map = null, bool pairs = false)
    {
        var match = Create(map ?? TestMaps.Open(24, 24));
        if (pairs)
        {
            match.Research.SetTiers(0, SwarmMix);
        }

        barracks = match.Place(0, "barracks", 10, 10);
        match.MakeRich(0);
        return match;
    }

    private static EntityId[] Train(Match match, EntityId structure, int unit)
    {
        match.OrderTrain(0, structure, unit);
        match.RunUntilAppeared();
        return match.Appeared();
    }

    [Fact]
    public void WithNoRallyPointAUnitAppearsBesideTheStructureAndStays()
    {
        var match = WithBarracks(out var barracks);

        var unit = Assert.Single(Train(match, barracks, Hoplite));

        // The four middle tiles of the sides are equally near the center: the lower y wins.
        Assert.Equal(MapGrid.TileCenter(11, 9), match.PositionOf(unit));
        Assert.Equal(MapGrid.TileCenter(11, 9), match.TrainEvents()[0].Position);
        Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State);
        Assert.Equal(0, match.Bodies.Get(unit).Owner);
        Assert.False(match.ProducerOf(barracks).RallySet);

        match.Simulation.Run(100);
        Assert.Equal(MapGrid.TileCenter(11, 9), match.PositionOf(unit));
    }

    [Fact]
    public void AUnitAppearsWithFullHitPoints()
    {
        var match = WithBarracks(out var barracks);
        var capitol = match.Place(0, "capitol", 2, 2);

        var hoplite = Assert.Single(Train(match, barracks, Hoplite));
        var worker = Assert.Single(Train(match, capitol, AWorker));

        Assert.Equal((130_000, 130_000), (match.Health.Get(hoplite).HitPoints, match.Health.Get(hoplite).Maximum));
        Assert.Equal(match.Health.Get(worker).Maximum, match.Health.Get(worker).HitPoints);
        Assert.True(match.Health.Get(worker).HitPoints > 0);
        Assert.True(match.Workers.Contains(worker));
        Assert.False(match.Combatants.Contains(worker));
        Assert.Equal(WorkerState.Idle, match.Workers.Get(worker).State);
    }

    [Fact]
    public void TheSecondUnitOfAPairAppearsOnTheNextTile()
    {
        var match = WithBarracks(out var barracks, pairs: true);

        var pair = Train(match, barracks, Peltast);

        // (11, 9), (9, 11), (13, 11) and (11, 13) are equally near: the lower y, then the lower x.
        Assert.Equal([MapGrid.TileCenter(11, 9), MapGrid.TileCenter(9, 11)], pair.Select(match.PositionOf));
        Assert.True(pair[0].Value < pair[1].Value);
    }

    [Theory]
    [InlineData("20.5", "11.5", 13, 11, 13, 10)] // Due east: the middle of the east side, then the two beside it tie and the lower y wins.
    [InlineData("2.5", "11.5", 9, 11, 9, 10)]
    [InlineData("11.5", "20.5", 11, 13, 10, 13)] // Due south: the tiles beside the middle have the same y, and the lower x wins.
    [InlineData("11.5", "2.5", 11, 9, 10, 9)]
    [InlineData("20.5", "2.5", 12, 9, 13, 10)] // To the north-east (12, 9) and (13, 10) are equally near: the lower y wins.
    [InlineData("2.5", "20.5", 9, 12, 10, 13)]
    [InlineData("2.5", "2.5", 10, 9, 9, 10)]
    [InlineData("20.5", "20.5", 13, 12, 12, 13)]
    [InlineData("11.5", "11.5", 11, 9, 9, 11)] // On the structure itself.
    [InlineData("13.25", "10.75", 13, 10, 13, 11)] // On a side tile.
    [InlineData("23.999", "0", 13, 10, 12, 9)] // The corner of the map: no tie.
    public void AUnitAppearsOnTheSideTileNearestTheRallyPoint(string x, string y, int firstX, int firstY, int secondX, int secondY)
    {
        var match = WithBarracks(out var barracks, pairs: true);
        match.OrderRally(0, TestMatches.Point(x, y), barracks);
        match.Simulation.Tick();

        Train(match, barracks, Peltast);

        Assert.Equal(
            [MapGrid.TileCenter(firstX, firstY), MapGrid.TileCenter(secondX, secondY)],
            match.TrainEvents().Select(train => train.Position));
    }

    [Fact]
    public void AUnitGetsAMoveOrderToTheRallyPointAndTakesItsFirstStepInTheSameTick()
    {
        var match = WithBarracks(out var barracks);
        var rally = TestMatches.Point("20.25", "11.75");
        match.OrderRally(0, rally, barracks);
        match.Simulation.Tick();
        Assert.True(match.ProducerOf(barracks).RallySet);
        Assert.Equal(rally, match.ProducerOf(barracks).RallyPoint);

        var unit = Assert.Single(Train(match, barracks, Hoplite));

        var start = MapGrid.TileCenter(13, 11);
        var speed = match.Research.Tech(0).Blueprints[Hoplite].SpeedPerTick;
        Assert.Equal(start, match.TrainEvents()[0].Position);
        Assert.Equal(MoveState.Moving, match.Movers.Get(unit).State);
        Assert.Equal(rally, match.Movers.Get(unit).Destination);
        Assert.InRange(FixedVector2.Distance(start, match.PositionOf(unit)).Raw - speed.Raw, -2, 2);
        Assert.True(match.PositionOf(unit).X > start.X);

        for (var ticks = 0; match.Movers.Get(unit).State != MoveState.Idle; ticks++)
        {
            Assert.True(ticks < 600);
            match.Simulation.Tick();
        }

        Assert.Equal(rally, match.PositionOf(unit));
    }

    /// <summary>
    /// Production runs before the routes are found, so a unit whose way to the rally point has to
    /// be searched for also takes its first step in the tick it appears.
    /// </summary>
    [Fact]
    public void AUnitWhoseWayNeedsARouteTakesItsFirstStepInTheSameTickToo()
    {
        var rows = Enumerable.Repeat(new string('.', 24), 24).ToArray();
        for (var y = 6; y <= 17; y++)
        {
            rows[y] = "...............#........";
        }

        var match = WithBarracks(out var barracks, TestMaps.FromRows(rows));
        var rally = TestMatches.Point("20.5", "11.5");
        match.OrderRally(0, rally, barracks);

        var unit = Assert.Single(Train(match, barracks, Hoplite));

        var start = MapGrid.TileCenter(13, 11);
        Assert.Equal(start, match.TrainEvents()[0].Position);
        Assert.Equal(MoveState.Following, match.Movers.Get(unit).State);
        Assert.NotEqual(start, match.PositionOf(unit));
    }

    [Fact]
    public void AUnitBelongsToThePlayerWhoseStructureTrainedIt()
    {
        var match = Create(TestMaps.Open(24, 24));
        match.Place(0, "barracks", 2, 2);
        var barracks = match.Place(1, "barracks", 10, 10);
        var capitol = match.Place(1, "capitol", 16, 16);
        match.Stockpiles.Add(1, 90, 0);

        match.Simulation.Commands.Enqueue(match.World.Tick, TrainCommand(1, barracks, Peltast));
        match.Simulation.Commands.Enqueue(match.World.Tick, TrainCommand(1, capitol, AWorker));
        match.RunUntilAppeared();

        Assert.Equal(2, match.Appeared().Length);
        Assert.All(match.Appeared(), unit => Assert.Equal(1, match.Bodies.Get(unit).Owner));
        Assert.All(match.TrainEvents(), train => Assert.Equal(1, train.Player));
        Assert.Equal((0, 3), (match.Supply.Used(0), match.Supply.Used(1)));
        Assert.Equal((0, 0), match.Stock(1));
    }

    [Fact]
    public void BothUnitsOfAPairGoToTheRallyPoint()
    {
        var match = WithBarracks(out var barracks, pairs: true);
        var rally = TestMatches.Point("20.5", "11.5");
        match.OrderRally(0, rally, barracks);

        var pair = Train(match, barracks, Peltast);

        Assert.Equal(2, pair.Length);
        Assert.All(pair, unit => Assert.NotEqual(MoveState.Idle, match.Movers.Get(unit).State));
        match.Simulation.Run(400);
        Assert.All(pair, unit => Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State));
        Assert.All(pair, unit => Assert.True(FixedVector2.Distance(rally, match.PositionOf(unit)) < Fixed.FromInt(2)));
    }

    [Fact]
    public void TheRallyPointOfTheMomentTheUnitAppearsCounts()
    {
        var match = WithBarracks(out var barracks);
        match.OrderRally(0, TestMatches.Point("20.5", "11.5"), barracks);
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Run(100);
        match.OrderRally(0, TestMatches.Point("2.5", "11.5"), barracks);
        match.RunUntilAppeared();

        Assert.Equal(MapGrid.TileCenter(9, 11), match.TrainEvents()[0].Position);
        Assert.Equal(TestMatches.Point("2.5", "11.5"), match.Movers.Get(match.Appeared()[0]).Destination);
    }

    [Fact]
    public void OnlyTilesAUnitCanStandOnAreTaken()
    {
        // Cliffs on the north side and on the middle of the west side; the map ends at the south side.
        var match = WithBarracks(out var barracks, TestMaps.FromRows(
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "..........###...........",
            "........................",
            ".........#..............",
            "........................"), pairs: true);

        // (11, 9) is a cliff, (9, 11) is a cliff, (11, 13) is off the map: (13, 11) is left of the four nearest.
        // Then (9, 10) and (13, 10) tie, and (9, 12) and (13, 12).
        Assert.Equal([MapGrid.TileCenter(13, 11), MapGrid.TileCenter(9, 10)], Train(match, barracks, Peltast).Select(match.PositionOf));
    }

    [Fact]
    public void WithOneTileBesideItBothUnitsOfAPairAppearOnIt()
    {
        var match = WithBarracks(out var barracks, TestMaps.FromRows(
            "###############",
            "###############",
            "###############",
            "###############",
            "###############",
            "###############",
            "###############",
            "###############",
            "###############",
            "###############",
            "##########...##",
            "##########....#",
            "##########...##",
            "###############"), pairs: true);

        Train(match, barracks, Peltast);

        // Where they appear, which the events say: units on one tile step apart from the same tick on.
        Assert.Equal([MapGrid.TileCenter(13, 11), MapGrid.TileCenter(13, 11)], match.TrainEvents().Select(train => train.Position));
    }

    [Theory]
    [InlineData(false, 9, 9)] // The four corners are the nearest ground, 2 tiles off on both axes: the lower y, then the lower x.
    [InlineData(true, 11, 8)] // With the corners gone, the tiles 3 off the center in a straight line.
    public void WithNoTileBesideItAUnitAppearsOnTheNearestTileItCanStandOn(bool cornersBlocked, int x, int y)
    {
        var corner = cornersBlocked ? '#' : '.';
        var match = WithBarracks(out var barracks, TestMaps.FromRows(
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            $".........{corner}###{corner}..........",
            ".........#...#..........",
            ".........#...#..........",
            ".........#...#..........",
            $".........{corner}###{corner}..........",
            "........................",
            "........................",
            "........................",
            "........................"), pairs: true);

        // The rally point does not say where, when no side tile is free. Both units of the pair take the one tile.
        match.OrderRally(0, TestMatches.Point("20.5", "13.5"), barracks);

        var pair = Train(match, barracks, Peltast);

        Assert.Equal([MapGrid.TileCenter(x, y), MapGrid.TileCenter(x, y)], match.TrainEvents().Select(train => train.Position));
        Assert.All(pair, unit => Assert.Equal(TestMatches.Point("20.5", "13.5"), match.Movers.Get(unit).Destination));
    }

    [Fact]
    public void TheNearestTileMayBeFarAway()
    {
        var rows = Enumerable.Repeat(new string('#', 24), 24).ToArray();
        rows[10] = rows[11] = rows[12] = "##########...###########";
        rows[2] = "##.#####################";
        rows[20] = "....................####";
        var match = WithBarracks(out var barracks, TestMaps.FromRows(rows));

        // (2, 2) is 9.5 tiles off on both axes, (11, 20) is 9 off on one: the nearer wins, not the lower y.
        Assert.Equal([MapGrid.TileCenter(11, 20)], Train(match, barracks, Hoplite).Select(match.PositionOf));
    }

    /// <summary>The search goes on past the first ring with ground: a tile farther out in a straight line can be nearer than a corner.</summary>
    [Fact]
    public void ATileFartherOutMayBeNearerThanACornerCloserIn()
    {
        var rows = Enumerable.Repeat(new string('#', 24), 24).ToArray();
        rows[10] = rows[11] = rows[12] = "##########...###########";
        rows[8] = "########.###############";
        rows[7] = "###########.############";
        var match = WithBarracks(out var barracks, TestMaps.FromRows(rows));

        // (8, 8) is 3 tiles off on both axes, (11, 7) is 4 off on one.
        Assert.Equal([MapGrid.TileCenter(11, 7)], Train(match, barracks, Hoplite).Select(match.PositionOf));
    }

    [Fact]
    public void AnOrderWhoseUnitHasNowhereToStandWaitsUntilItHas()
    {
        var match = Create(TestMaps.Open(4, 2));
        var capitol = match.Place(0, "capitol", 0, 0);
        var generator = match.Place(0, "generator", 2, 0);
        match.MakeRich(0);

        match.OrderTrain(0, capitol, AWorker);
        match.OrderTrain(0, capitol, AWorker);
        match.Simulation.Run(WorkerTicks + 50);

        Assert.Equal(0, match.Workers.Count);
        Assert.Equal(2, match.OrdersAt(capitol).Length);
        Assert.Equal((WorkerTicks, WorkerTicks), (match.ProducerOf(capitol).BuildTicks, match.ProducerOf(capitol).Progress));
        Assert.Equal(2, match.Supply.Used(0));

        // The Generator falls and leaves ground beside the Capitol.
        match.Destroy(generator);
        match.Simulation.Tick();
        Assert.Empty(match.Appeared());
        match.Simulation.Tick();

        var worker = Assert.Single(match.Appeared());
        Assert.Equal(MapGrid.TileCenter(2, 0), match.PositionOf(worker));
        Assert.Single(match.OrdersAt(capitol));
        Assert.Equal(WorkerTicks, match.RunUntilAppeared());
    }

    [Fact]
    public void ACapitolsRallyPointOnADepositAssignsItsNewWorkers()
    {
        var match = Create(TestMaps.Open(24, 24).WithDeposits((9, 4)));
        var capitol = match.Place(0, "capitol", 4, 4);
        var deposit = match.SiteAt(9, 4);
        match.Stockpiles.Add(0, 100, 0);

        // Any point on the deposit's four tiles.
        match.OrderRally(0, TestMatches.Point("10.75", "5.25"), capitol);
        match.OrderTrain(0, capitol, AWorker);
        match.OrderTrain(0, capitol, AWorker);
        match.RunUntilAppeared();

        var first = Assert.Single(match.Appeared());
        Assert.Equal(MapGrid.TileCenter(6, 5), match.TrainEvents()[0].Position);
        Assert.Equal((WorkerState.Walking, deposit), (match.Workers.Get(first).State, match.Workers.Get(first).Site));
        Assert.NotEqual(MapGrid.TileCenter(6, 5), match.PositionOf(first));

        match.RunUntilAppeared();
        var second = Assert.Single(match.Appeared());
        Assert.Equal((WorkerState.Working, deposit), (match.Workers.Get(first).State, match.Workers.Get(first).Site));
        Assert.Equal(deposit, match.Workers.Get(second).Site);

        // They earn: the stockpile was emptied by the two orders.
        match.Simulation.Run(200);
        Assert.Equal(WorkerState.Working, match.Workers.Get(second).State);
        Assert.True(match.Stockpiles.Materials(0) > 0);
    }

    [Fact]
    public void ACapitolsRallyPointOnTheOwnersGeneratorAssignsItsNewWorkers()
    {
        var match = Create(TestMaps.Open(24, 24));
        var capitol = match.Place(0, "capitol", 4, 4);
        var mine = match.Place(0, "generator", 9, 4);
        var theirs = match.Place(1, "generator", 4, 9);
        var other = match.Place(0, "capitol", 16, 16);
        match.MakeRich(0);

        match.OrderRally(0, TestMatches.Point("9.5", "5.5"), capitol);
        match.OrderRally(0, TestMatches.Point("4.5", "9.5"), other);
        match.OrderTrain(0, capitol, AWorker);
        match.OrderTrain(0, other, AWorker);
        match.RunUntilAppeared();

        var appeared = match.Appeared();
        Assert.Equal(2, appeared.Length);
        Assert.Equal((WorkerState.Walking, mine), (match.Workers.Get(appeared[0]).State, match.Workers.Get(appeared[0]).Site));

        // The other player's Generator is ground like any other: the worker walks to the point and has no work.
        Assert.Equal((WorkerState.Idle, EntityId.None), (match.Workers.Get(appeared[1]).State, match.Workers.Get(appeared[1]).Site));
        Assert.NotEqual(MoveState.Idle, match.Movers.Get(appeared[1]).State);
        Assert.True(match.World.Entities.IsAlive(theirs));
    }

    [Fact]
    public void AWorkerWhoCannotReachTheDepositWalksTowardTheRallyPoint()
    {
        var match = Create(TestMaps.FromRows(
            "....................",
            "....................",
            "............####....",
            "............#..#....",
            "............#..#....",
            "............####....",
            "....................",
            "....................").WithDeposits((13, 3)));
        var capitol = match.Place(0, "capitol", 4, 3);
        match.MakeRich(0);

        var rally = TestMatches.Point("13.5", "3.5");
        match.OrderRally(0, rally, capitol);
        match.OrderTrain(0, capitol, AWorker);
        match.RunUntilAppeared();

        var worker = Assert.Single(match.Appeared());
        Assert.Equal((WorkerState.Idle, EntityId.None), (match.Workers.Get(worker).State, match.Workers.Get(worker).Site));
        Assert.NotEqual(MoveState.Idle, match.Movers.Get(worker).State);
    }

    [Fact]
    public void AUnitThatIsNoWorkerWalksToARallyPointOnADeposit()
    {
        var match = WithBarracks(out var barracks, TestMaps.Open(24, 24).WithDeposits((18, 10)));
        var deposit = match.SiteAt(18, 10);
        match.OrderRally(0, TestMatches.Point("19", "11"), barracks);

        var unit = Assert.Single(Train(match, barracks, Hoplite));

        Assert.False(match.Workers.Contains(unit));
        Assert.NotEqual(MoveState.Idle, match.Movers.Get(unit).State);
        Assert.Equal(0, match.Work.HoldersOf(match.World, deposit));
    }

    [Fact]
    public void OneOrderSetsTheRallyPointOfEveryProductionStructureItLists()
    {
        var match = WithBarracks(out var barracks);
        var capitol = match.Place(0, "capitol", 2, 2);
        var generator = match.Place(0, "generator", 16, 2);
        var theirs = match.Place(1, "barracks", 16, 16);
        var point = TestMatches.Point("7.5", "20.25");

        match.OrderRally(0, point, theirs, barracks, generator, capitol);
        match.Simulation.Tick();

        Assert.Equal((true, point), (match.ProducerOf(barracks).RallySet, match.ProducerOf(barracks).RallyPoint));
        Assert.Equal((true, point), (match.ProducerOf(capitol).RallySet, match.ProducerOf(capitol).RallyPoint));
        Assert.Equal((false, default(FixedVector2)), (match.ProducerOf(theirs).RallySet, match.ProducerOf(theirs).RallyPoint));
        Assert.Equal(
            [
                new TrainEvent(SimEventKind.RallyPointSet, 0, 0, barracks, 0, 0, TrainCheck.Allowed, EntityId.None, point),
                new TrainEvent(SimEventKind.RallyPointSet, 0, 0, capitol, 0, 0, TrainCheck.Allowed, EntityId.None, point),
            ],
            match.TrainEvents());
    }

    [Fact]
    public void ARallyOrderThatSetsNothingIsRefused()
    {
        var match = WithBarracks(out var barracks);
        var generator = match.Place(0, "generator", 16, 2);
        var theirs = match.Place(1, "barracks", 16, 16);
        var unit = match.SpawnWorker(0, MapGrid.TileCenter(2, 2));
        var onMap = TestMatches.Point("5.5", "5.5");
        Command[] refused =
        [
            RallyCommand(0, TestMatches.Point("24", "5"), barracks),
            RallyCommand(0, TestMatches.Point("5", "-0.001"), barracks),
            RallyCommand(0, onMap, theirs, generator, unit, new EntityId(9999)),
            RallyCommand(0, onMap),
            RallyCommand(1, onMap, barracks),
            RallyCommand(5, onMap, barracks),
            RallyCommand(-1, onMap, barracks),
        ];
        foreach (var command in refused)
        {
            match.Simulation.Commands.Enqueue(match.World.Tick, command);
        }

        match.Simulation.Tick();

        Assert.False(match.ProducerOf(barracks).RallySet);
        Assert.False(match.ProducerOf(theirs).RallySet);
        Assert.Empty(match.TrainEvents());
        Assert.Equal(
            refused.Select(command => (SimEventKind.OrderRefused, command.Player)),
            match.Simulation.Events.Select(simEvent => (simEvent.Kind, simEvent.Player)));
    }
}
