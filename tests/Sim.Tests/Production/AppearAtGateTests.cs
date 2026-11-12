using Sim.Construction;
using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Setup;
using Sim.Tests.Walls;

namespace Sim.Tests.Production;

/// <summary>
/// Where production meets gates (design 5.4, "Where a unit appears", and design 2.5): a unit
/// appears on a tile beside its structure that it "can stand on", and a finished gate is a wall
/// tile its owner's units can walk through. So the owner's own finished gate is a tile a new unit
/// may appear on, and nobody else's is.
/// </summary>
/// <remarks>
/// The Barracks of player 0 stands on tiles (10, 10) to (12, 12) of an open field, so its center
/// is (11.5, 11.5), and the ring round it is the 16 tiles on the edge of the box from (9, 9) to
/// (13, 13): the 12 that share a side with the Barracks and the four corners. With the whole ring
/// closed, the nearest tile a unit can stand on is (11, 8), outside the ring.
/// </remarks>
public class AppearAtGateTests
{
    private const string Gate = "gate";
    private const string Barracks = "barracks";

    private static readonly (int X, int Y) Low = (9, 9);
    private static readonly (int X, int Y) High = (13, 13);
    private static readonly (int X, int Y) Outside = (11, 8);

    private static int Chassis(Match match, string id) => match.Data.Chassis.Single(chassis => chassis.Id == id).Index;

    /// <summary>
    /// Player 0's Barracks with wall of <paramref name="wallOwner"/> on every tile of the ring
    /// but those in <paramref name="open"/>, which are left as they are.
    /// </summary>
    private static Match Ringed(out EntityId barracks, int wallOwner, params (int X, int Y)[] open)
    {
        var match = SiegeMatches.Field();
        barracks = match.PlaceStructure(0, Barracks, 10, 10);
        match.MakeRich(0);
        for (var y = Low.Y; y <= High.Y; y++)
        {
            for (var x = Low.X; x <= High.X; x++)
            {
                if ((x == Low.X || x == High.X || y == Low.Y || y == High.Y) && !open.Contains((x, y)))
                {
                    match.WallTile(wallOwner, x, y);
                }
            }
        }

        return match;
    }

    private static EntityId[] Train(Match match, EntityId structure, int unit)
    {
        match.OrderTrain(0, structure, unit);
        match.RunUntilAppeared();
        return match.Appeared();
    }

    [Theory]
    [InlineData(13, 11)]
    [InlineData(11, 9)]
    [InlineData(9, 10)]
    [InlineData(12, 13)]
    public void AProducerWithOnlyItsOwnersGateBesideItPutsTheNewUnitThere(int gateX, int gateY)
    {
        var match = Ringed(out var barracks, 0, (gateX, gateY));
        match.PlaceStructure(0, Gate, gateX, gateY);
        Assert.False(match.Grid.IsPassable(gateX, gateY));

        var unit = Assert.Single(Train(match, barracks, Chassis(match, SiegeMatches.Hoplite)));

        Assert.Equal(MapGrid.TileCenter(gateX, gateY), match.PositionOf(unit));
        Assert.Equal(MapGrid.TileCenter(gateX, gateY), match.TrainEvents().Single(train => train.Kind == SimEventKind.UnitTrained).Position);
        Assert.Equal(0, match.Bodies.Get(unit).Owner);

        // With no rally point it stays where it appeared, as beside any structure.
        match.Simulation.Run(100);
        Assert.Equal(MapGrid.TileCenter(gateX, gateY), match.PositionOf(unit));
        Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State);
    }

    [Fact]
    public void AUnitThatAppearsInTheGateWalksOutToTheRallyPoint()
    {
        var match = Ringed(out var barracks, 0, (13, 11));
        match.PlaceStructure(0, Gate, 13, 11);
        match.OrderRally(0, TestMatches.Point("24.5", "4.5"), barracks);
        match.Simulation.Tick();

        var unit = Assert.Single(Train(match, barracks, Chassis(match, SiegeMatches.Hoplite)));
        Assert.Equal((13, 11), match.TileOn(unit));

        match.Simulation.Run(400);
        Assert.Equal(TestMatches.Point("24.5", "4.5"), match.PositionOf(unit));
    }

    [Fact]
    public void BothUnitsOfAPairAppearInTheGateWhenItIsTheOnlyTile()
    {
        var match = Ringed(out var barracks, 0, (13, 11));
        match.PlaceStructure(0, Gate, 13, 11);
        match.Research.SetTiers(0, [2, 2, 3, 5, 5, 3]);

        var pair = Train(match, barracks, Chassis(match, SiegeMatches.Peltast));

        Assert.Equal(2, pair.Length);
        Assert.Equal([MapGrid.TileCenter(13, 11), MapGrid.TileCenter(13, 11)], match.TrainEvents().Select(train => train.Position));
    }

    /// <summary>The gate is one of the side tiles like any other: it is ranked with them, by the same ties.</summary>
    [Fact]
    public void AGateAmongFreeSideTilesIsRankedLikeThem()
    {
        var match = SiegeMatches.Field();
        var barracks = match.PlaceStructure(0, Barracks, 10, 10);
        match.MakeRich(0);
        match.Research.SetTiers(0, [2, 2, 3, 5, 5, 3]);

        // (11, 9), (9, 11), (13, 11) and (11, 13) are equally near the center: the lower y, then the lower x.
        match.PlaceStructure(0, Gate, 11, 9);

        Train(match, barracks, Chassis(match, SiegeMatches.Peltast));

        Assert.Equal([MapGrid.TileCenter(11, 9), MapGrid.TileCenter(9, 11)], match.TrainEvents().Select(train => train.Position));
    }

    [Fact]
    public void AnEnemysGateBesideTheProducerIsNoTileToAppearOn()
    {
        var match = Ringed(out var barracks, 1, (13, 11));
        match.PlaceStructure(1, Gate, 13, 11);

        var unit = Assert.Single(Train(match, barracks, Chassis(match, SiegeMatches.Hoplite)));

        Assert.Equal(Outside, match.TileOn(unit));
    }

    [Fact]
    public void AGateThatIsStillBuildingItselfIsNoTileToAppearOnAndIsOneOnceItIsFinished()
    {
        var match = Ringed(out var barracks, 0, (13, 11));
        var hoplite = Chassis(match, SiegeMatches.Hoplite);

        // The gate is laid in the gap one tick before the Hoplite is due, so it is a foundation,
        // a wall tile to everyone, in the tick the Hoplite appears.
        match.OrderTrain(0, barracks, hoplite);
        do
        {
            match.Simulation.Tick();
            Assert.Empty(match.Appeared());
        }
        while (match.ProducerOf(barracks).Progress < match.ProducerOf(barracks).BuildTicks - 1);

        var gate = match.StructureFactory.Lay(0, match.Data.Gate!, 13, 11, new Foundation { Built = 0, Materials = 40 });
        match.RefreshDerivedState();
        match.Simulation.Tick();
        var first = Assert.Single(match.Appeared());
        Assert.False(match.Construction.IsFinished(gate));
        Assert.Equal(Outside, match.TileOn(first));

        while (!match.Grid.For(0).IsPassable(13, 11))
        {
            match.Simulation.Tick();
            Assert.True(match.World.Tick < 1000, "The gate is not finished after 1,000 ticks.");
        }

        var second = Assert.Single(Train(match, barracks, hoplite));
        Assert.Equal((13, 11), match.TileOn(second));
    }

    /// <summary>
    /// Design 5.4: "If none can be stood on, the nearest tile to the structure's center that the
    /// unit can stand on is used." The owner's gate at a corner of the ring shares no side with
    /// the Barracks, and is nearer its center than any tile outside the ring.
    /// </summary>
    [Fact]
    public void WithNoSideTileToStandOnTheNearestTileMayBeTheOwnersGate()
    {
        var match = Ringed(out var barracks, 0, (13, 13));
        match.PlaceStructure(0, Gate, 13, 13);

        var unit = Assert.Single(Train(match, barracks, Chassis(match, SiegeMatches.Hoplite)));

        Assert.Equal((13, 13), match.TileOn(unit));
    }

    [Fact]
    public void AWorkerAppearsInItsOwnersGateBesideTheCapitol()
    {
        // The Capitol on tiles (10, 10) to (13, 13), ringed from (9, 9) to (14, 14) with a gate on its east side.
        var match = SiegeMatches.Field();
        var capitol = match.PlaceStructure(0, SiegeMatches.Capitol, 10, 10);
        match.MakeRich(0);
        for (var y = 9; y <= 14; y++)
        {
            for (var x = 9; x <= 14; x++)
            {
                if ((x == 9 || x == 14 || y == 9 || y == 14) && (x, y) != (14, 12))
                {
                    match.WallTile(0, x, y);
                }
            }
        }

        match.PlaceStructure(0, Gate, 14, 12);

        var worker = Assert.Single(Train(match, capitol, ProductionMatches.AWorker));

        Assert.True(match.Workers.Contains(worker));
        Assert.Equal((14, 12), match.TileOn(worker));
    }
}
