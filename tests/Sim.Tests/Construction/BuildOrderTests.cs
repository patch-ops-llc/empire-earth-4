using Sim.Construction;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Tests.Structures;
using static Sim.Tests.Construction.ConstructionMatches;

namespace Sim.Tests.Construction;

/// <summary>
/// The build order when it is given (design 5.4; design 8.4, rows 27 and 29): it is charged at
/// once, one worker takes it, and every rule of placement that the player may know is checked.
/// </summary>
public class BuildOrderTests
{
    [Fact]
    public void AnOrderIsChargedWhenItIsGivenAndOneWorkerWalksToTheSite()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 2);
        match.OrderBuild(0, "generator", 10, 10, worker);

        match.Simulation.Tick();

        Assert.Equal((900, 1000), match.StockOf(0));
        Assert.Equal(
            new BuildOrder { Type = Generator, X = 10, Y = 10, Size = 2, Materials = 100, Energy = 0 },
            match.BuildOrders.Get(worker));
        Assert.NotEqual(MoveState.Idle, match.MoveStateOf(worker));
        Assert.Equal(EntityId.None, match.StructureAt(10, 10));
        Assert.True(match.Grid.IsPassable(10, 10));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Empty(match.EventsOf(SimEventKind.BuildRefused));
    }

    /// <summary>The tile that holds the command's point is the lowest tile of the footprint, wherever in the tile the point is.</summary>
    [Theory]
    [InlineData("10", "10")]
    [InlineData("10.99", "10.01")]
    public void TheTileOfThePointIsTheLowestTileOfTheFootprint(string x, string y)
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 2);
        var point = TestMatches.Point(x, y);
        match.Simulation.Commands.Enqueue(0, new Command(CommandType.Build, 0, [worker], point.X, point.Y, contentId: Generator));

        match.Simulation.Tick();

        Assert.Equal(new TileSquare(10, 10, 2), match.BuildOrders.Get(worker).Tiles);
    }

    public static TheoryData<string, int, int, int, BuildProblem> OrdersThatAreRefused() => new()
    {
        // Named by nothing that a build order places.
        { "barracks", 10, 10, 0, BuildProblem.NoStructure },
        { "wall", 10, 10, 0, BuildProblem.NoStructure },
        // Its tier is not reached.
        { "works", 9, 9, 0, BuildProblem.Locked },
        // Off the map, half off it, on the Capitol, and on the cliff at (9, 14).
        { "generator", -3, 10, 0, BuildProblem.SiteNotFree },
        { "outpost", 38, 10, 0, BuildProblem.SiteNotFree },
        { "generator", 7, 11, 0, BuildProblem.SiteNotFree },
        { "generator", 8, 13, 0, BuildProblem.SiteNotFree },
        // The tile (13, 10) is half a tile too far from the Capitol's center.
        { "generator", 12, 10, 0, BuildProblem.OutsideTerritory },
        // Player 1 has no territory, and needs none for an Outpost, but has no worker in the list.
        { "generator", 10, 10, 1, BuildProblem.OutsideTerritory },
        { "outpost", 30, 10, 1, BuildProblem.NoWorker },
    };

    [Theory]
    [MemberData(nameof(OrdersThatAreRefused))]
    public void AnOrderThatBreaksARuleIsRefusedAndItsPlayerIsToldWhy(string structure, int x, int y, int player, BuildProblem why)
    {
        var match = Create(TestMaps.FromRows([.. Enumerable.Range(0, 24).Select(row => row == 14 ? "........." + "#" + new string('.', 30) : new string('.', 40))]));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 1000, 1000);
        match.Stockpiles.Add(1, 1000, 1000);
        var worker = match.WorkerAt(0, 2, 2);
        var before = match.World.ComputeHash();
        var index = match.Data.Rules.Structures.ToList().IndexOf(structure);
        var point = MapGrid.TileCenter(Math.Max(x, 0), y);
        var command = new Command(CommandType.Build, player, [worker], x < 0 ? Fixed(x) : point.X, point.Y, contentId: index);
        match.Simulation.Commands.Enqueue(0, command);

        match.Simulation.Tick();

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal([(worker, why)], match.Problems(SimEventKind.BuildRefused));
        Assert.Equal(player, match.EventsOf(SimEventKind.BuildRefused)[0].Player);
        Assert.Equal((0, 1000L), (match.BuildOrders.Count, match.StockOf(player).Materials));

        // Nothing but the tick number changed.
        var untouched = Create(match.Map);
        untouched.PlaceStructure(0, "capitol", 6, 10);
        untouched.Stockpiles.Add(0, 1000, 1000);
        untouched.Stockpiles.Add(1, 1000, 1000);
        untouched.WorkerAt(0, 2, 2);
        Assert.Equal(before, untouched.World.ComputeHash());
        untouched.Simulation.Tick();
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
    }

    [Fact]
    public void AnOrderTheStockpileDoesNotCoverIsRefused()
    {
        var match = Create(TestMaps.Open(40, 24));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 199, 100);
        var worker = match.WorkerAt(0, 2, 2);

        match.OrderBuild(0, "academy", 10, 10, worker);
        match.Simulation.Tick();
        Assert.Equal([(worker, BuildProblem.CannotAfford)], match.Problems(SimEventKind.BuildRefused));
        Assert.Equal((199, 100), match.StockOf(0));

        // One Materials more and it is paid, to the last of both resources.
        match.Stockpiles.Add(0, 1, 0);
        match.OrderBuild(0, "academy", 10, 10, worker);
        match.Simulation.Tick();
        Assert.Equal((0, 0), match.StockOf(0));
        Assert.True(match.BuildOrders.Contains(worker));
    }

    [Fact]
    public void AnOrderFromNobodyInTheMatchIsDroppedAndNobodyIsTold()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 2);
        match.Simulation.Commands.Enqueue(0, match.Build(2, "generator", 10, 10, worker));
        match.Simulation.Commands.Enqueue(0, match.Build(-1, "generator", 10, 10, worker));

        match.Simulation.Tick();

        Assert.Equal(2, match.Simulation.DroppedCommandCount);
        Assert.Empty(match.Simulation.Events);
    }

    /// <summary>Design 5.4: Capitols and Outposts are how territory grows, so they need none.</summary>
    [Fact]
    public void ACapitolAndAnOutpostMayBeOrderedOutsideTerritoryAndOtherStructuresMayNot()
    {
        var match = WithACapitol();
        var first = match.WorkerAt(0, 2, 2);
        var second = match.WorkerAt(0, 3, 2);
        var third = match.WorkerAt(0, 4, 2);

        match.OrderBuild(0, "outpost", 30, 4, first);
        match.OrderBuild(0, "capitol", 30, 16, second);
        match.OrderBuild(0, "academy", 30, 10, third);
        match.Simulation.Tick();

        Assert.Equal([first, second], Enumerable.Range(0, match.BuildOrders.Count).Select(match.BuildOrders.EntityAt));
        Assert.Equal([(third, BuildProblem.OutsideTerritory)], match.Problems(SimEventKind.BuildRefused));
        Assert.Equal((350, 1000), match.StockOf(0));
    }

    /// <summary>
    /// The enemy's territory is no reason to refuse an order when it is given (design 5.4): the
    /// overlap rule is judged once, when the worker arrives.
    /// </summary>
    [Fact]
    public void AnOutpostTooNearEnemyTerritoryIsOrderedAllTheSame()
    {
        var match = WithACapitol();
        match.Stockpiles.Add(1, 1000, 0);
        var worker = match.WorkerAt(1, 20, 10);

        // Its center would be 6.5 tiles from the Capitol's, and the radii are 6 and 5.
        match.OrderBuild(1, "outpost", 12, 10, worker);
        match.Simulation.Tick();

        Assert.True(match.BuildOrders.Contains(worker));
        Assert.True(match.Construction.IsTooCloseToEnemyTerritory(1, match.Def("outpost"), 12, 10));
        Assert.Equal(BuildProblem.None, match.Construction.CheckSite(1, match.Def("outpost"), 12, 10));
    }

    [Fact]
    public void TheNearestListedWorkerWithoutABuildOrderTakesTheOrder()
    {
        var match = WithACapitol();
        var far = match.WorkerAt(0, 2, 10);
        var near = match.WorkerAt(0, 8, 8);
        var nearer = match.WorkerAt(0, 8, 9);
        var enemy = match.WorkerAt(1, 10, 9);
        var unit = match.SpawnUnit(0, MapGrid.TileCenter(10, 9), StructureMatches.QuarterTile);
        EntityId[] listed = [enemy, unit, new EntityId(999), far, near, nearer];

        // The nearest takes the first. It has an order now, so the next nearest takes the second.
        match.OrderBuild(0, "generator", 10, 10, listed);
        match.OrderBuild(0, "generator", 10, 12, listed);
        match.OrderBuild(0, "generator", 10, 7, listed);
        match.Simulation.Tick();

        Assert.Equal(new TileSquare(10, 10, 2), match.BuildOrders.Get(nearer).Tiles);
        Assert.Equal(new TileSquare(10, 12, 2), match.BuildOrders.Get(near).Tiles);
        Assert.Equal(new TileSquare(10, 7, 2), match.BuildOrders.Get(far).Tiles);
        Assert.Equal(3, match.BuildOrders.Count);
        Assert.Equal((700, 1000), match.StockOf(0));

        // All three have one, so the nearest of them gives its order up for the fourth.
        match.OrderBuild(0, "academy", 3, 6, listed);
        match.Simulation.Tick();

        Assert.Equal(new TileSquare(3, 6, 2), match.BuildOrders.Get(far).Tiles);
        Assert.Equal([(far, BuildProblem.OtherOrder)], match.Problems(SimEventKind.BuildOrderEnded));
        Assert.Equal((600, 900), match.StockOf(0));
    }

    /// <summary>Of two workers equally near, the one listed first goes.</summary>
    [Fact]
    public void OfTwoWorkersEquallyNearTheOneListedFirstGoes()
    {
        var match = WithACapitol();
        var left = match.WorkerAt(0, 8, 10);
        var above = match.WorkerAt(0, 10, 8);

        match.OrderBuild(0, "generator", 10, 10, above, left);
        match.Simulation.Tick();

        Assert.True(match.BuildOrders.Contains(above));
        Assert.False(match.BuildOrders.Contains(left));
    }

    /// <summary>
    /// A new order for a worker that has one counts what the old one gives back, so a player
    /// with nothing left can still move a site.
    /// </summary>
    [Fact]
    public void AnOrderThatReplacesAnotherIsPaidWithWhatTheOtherGivesBack()
    {
        var match = Create(TestMaps.Open(40, 24));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 250, 100);
        var worker = match.WorkerAt(0, 2, 2);
        match.OrderBuild(0, "outpost", 20, 10, worker);
        match.Simulation.Tick();
        Assert.Equal((0, 100), match.StockOf(0));

        match.OrderBuild(0, "academy", 10, 10, worker);
        match.Simulation.Tick();

        Assert.Equal((50, 0), match.StockOf(0));
        Assert.Equal(new TileSquare(10, 10, 2), match.BuildOrders.Get(worker).Tiles);

        // An order that is refused leaves the one the worker has.
        match.OrderBuild(0, "capitol", 20, 10, worker);
        match.OrderBuild(0, "generator", 30, 10, worker);
        match.Simulation.Tick();

        Assert.Equal(
            [(worker, BuildProblem.CannotAfford), (worker, BuildProblem.OutsideTerritory)],
            match.Problems(SimEventKind.BuildRefused));
        Assert.Equal(new TileSquare(10, 10, 2), match.BuildOrders.Get(worker).Tiles);
        Assert.Equal((50, 0), match.StockOf(0));
    }

    /// <summary>Design 2.5: a third Academy is refused; Academies under construction and orders on their way count.</summary>
    [Fact]
    public void AThirdAcademyIsRefusedCountingThoseUnderWay()
    {
        var match = WithACapitol();
        var first = match.WorkerAt(0, 9, 8);
        var second = match.WorkerAt(0, 2, 2);
        var third = match.WorkerAt(0, 3, 2);
        match.OrderBuild(0, "academy", 9, 9, first);
        match.Simulation.Tick();
        Assert.NotEqual(EntityId.None, match.StructureAt(9, 9));
        Assert.False(match.Construction.IsFinished(match.StructureAt(9, 9)));

        // One is building itself and one is on its way.
        match.OrderBuild(0, "academy", 9, 12, second);
        match.OrderBuild(0, "academy", 4, 9, third);
        match.Simulation.Tick();

        Assert.Equal([(third, BuildProblem.LimitReached)], match.Problems(SimEventKind.BuildRefused));
        Assert.Equal(2, match.Construction.CountOf(0, match.Def("academy").Index));
        Assert.Equal(BuildProblem.LimitReached, match.Construction.CheckStructure(0, match.Def("academy").Index));
        Assert.Equal((600, 800), match.StockOf(0));

        // The worker on its way may be sent to another site: its own order does not count against it.
        match.OrderBuild(0, "academy", 4, 9, second);
        match.Simulation.Tick();
        Assert.Equal(new TileSquare(4, 9, 2), match.BuildOrders.Get(second).Tiles);

        // The other player has none.
        match.PlaceStructure(1, "capitol", 30, 10);
        match.Stockpiles.Add(1, 1000, 1000);
        var theirs = match.WorkerAt(1, 30, 8);
        match.OrderBuild(1, "academy", 30, 13, theirs);
        match.Simulation.Tick();
        Assert.True(match.BuildOrders.Contains(theirs));
    }

    /// <summary>Design 2.5: structures may touch each other.</summary>
    [Fact]
    public void StructuresMayTouch()
    {
        var match = WithACapitol();
        var first = match.WorkerAt(0, 8, 9);
        var second = match.WorkerAt(0, 9, 9);

        // Beside the Capitol, and beside each other.
        match.OrderBuild(0, "generator", 8, 10, first);
        match.OrderBuild(0, "generator", 10, 10, second);
        match.Simulation.Tick();

        Assert.NotEqual(EntityId.None, match.StructureAt(8, 10));
        Assert.NotEqual(EntityId.None, match.StructureAt(10, 10));
    }

    /// <summary>Design 2.5: a structure with no tier in its name is there from the start, and one with a tier when the tier is reached.</summary>
    [Fact]
    public void AStructureIsUnlockedByItsTier()
    {
        var match = WithACapitol();
        var works = match.Def("works").Index;
        Assert.Equal(BuildProblem.Locked, match.Construction.CheckStructure(0, works));
        Assert.Equal(BuildProblem.None, match.Construction.CheckStructure(0, Generator));
        Assert.Equal(BuildProblem.None, match.Construction.CheckStructure(0, match.Def("outpost").Index));
        Assert.Equal(BuildProblem.NoStructure, match.Construction.CheckStructure(0, match.Def("wall").Index));
        Assert.Equal(BuildProblem.CannotAfford, match.Construction.CheckStructure(1, Generator));

        var later = WithACapitol(DataStartingAtTier(2));
        var worker = later.WorkerAt(0, 2, 2);
        later.OrderBuild(0, "works", 9, 9, worker);
        later.Simulation.Tick();

        Assert.Equal(BuildProblem.None, later.Construction.CheckStructure(0, works));
        Assert.True(later.BuildOrders.Contains(worker));
    }

    /// <summary>What a placement preview asks tile by tile is what the order asks of the footprint.</summary>
    [Fact]
    public void TheTilesOfASiteAreJudgedAsTheSiteIs()
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((10, 12)));
        match.PlaceStructure(0, "capitol", 6, 10);
        var generator = match.Def("generator");
        var outpost = match.Def("outpost");

        for (var y = -1; y <= match.Grid.Height; y++)
        {
            for (var x = -1; x <= match.Grid.Width; x++)
            {
                foreach (var structure in new[] { generator, outpost })
                {
                    var tiles = Enumerable.Range(0, structure.Footprint * structure.Footprint)
                        .All(i => match.Construction.CanBuildOnTile(0, structure, x + (i % structure.Footprint), y + (i / structure.Footprint)));
                    Assert.Equal(tiles, match.Construction.CheckSite(0, structure, x, y) == BuildProblem.None);
                }
            }
        }

        // The Capitol, the deposit, the ground outside the territory, and the ground off the map.
        Assert.False(match.Construction.CanBuildOnTile(0, generator, 7, 11));
        Assert.False(match.Construction.CanBuildOnTile(0, generator, 11, 13));
        Assert.True(match.Construction.CanBuildOnTile(0, generator, 12, 10));
        Assert.False(match.Construction.CanBuildOnTile(0, generator, 13, 10));
        Assert.True(match.Construction.CanBuildOnTile(0, outpost, 13, 10));
        Assert.False(match.Construction.CanBuildOnTile(0, outpost, 40, 10));
        Assert.False(match.Construction.CanBuildOnTile(1, generator, 10, 10));
    }

    /// <summary>A worker that stands at work keeps its work site for the build order and gives up its place (design 4.1).</summary>
    [Fact]
    public void AWorkerAtWorkStepsAwayAndKeepsItsWorkSite()
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((2, 10)));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 1000, 1000);
        var deposit = match.SiteAt(2, 10);
        var worker = match.WorkerAt(0, 4, 9);
        match.OrderGather(0, deposit, worker);
        match.RunUntilWorking(worker);

        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Tick();

        Assert.Equal(new Worker { State = WorkerState.Away, Site = deposit }, match.WorkerOf(worker));
        Assert.Equal(0, match.Work.HoldersOf(match.World, deposit));
    }

    private static Sim.Numerics.Fixed Fixed(int tiles) => Sim.Numerics.Fixed.FromInt(tiles);
}
