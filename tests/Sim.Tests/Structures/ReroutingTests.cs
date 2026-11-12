using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Structures;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Structures.StructureMatches;

namespace Sim.Tests.Structures;

/// <summary>Units that are on their way when the tiles change get a way that fits the new tiles.</summary>
public class ReroutingTests
{
    /// <summary>A wall across the map with two gaps, each two tiles wide: at x 4 and 5 and at x 14 and 15.</summary>
    private static MapDef TwoGaps => TestMaps.FromRows(
        "....................",
        "....................",
        "####..########..####",
        "####..########..####",
        "....................",
        "....................");

    /// <summary>Plan 15b: a unit on a route through the new footprint finds another.</summary>
    [Fact]
    public void AUnitOnARouteThroughANewFootprintFindsAnother()
    {
        var match = Create(TwoGaps);
        var unit = match.SpawnUnit(0, MapGrid.TileCenter(1, 0), QuarterTile);
        var target = MapGrid.TileCenter(1, 5);
        match.OrderMove(0, target, unit);
        match.Simulation.Run(4);
        Assert.Equal(MoveState.Following, match.MoveStateOf(unit));

        // The near gap closes while the unit is on its way to it.
        match.PlaceStructure(1, "generator", 4, 2);

        Assert.Equal(MoveState.Planning, match.MoveStateOf(unit));
        Assert.Equal(target, match.Movers.Get(unit).Destination);
        var ticks = match.RunUntilIdle(unit);
        Assert.Equal(target, match.PositionOf(unit));

        // Through the far gap: over to x 14 and back, far longer than the walk through the near one.
        Assert.InRange(ticks, 4 * 24, 4 * 40);
    }

    [Fact]
    public void AUnitWalkingAStraightLineThroughANewFootprintGoesAroundIt()
    {
        var match = Create(TestMaps.Open(24, 8));
        var crossing = match.SpawnUnit(0, MapGrid.TileCenter(2, 4), QuarterTile);
        var clear = match.SpawnUnit(0, MapGrid.TileCenter(2, 1), QuarterTile);
        match.OrderMove(0, MapGrid.TileCenter(20, 4), crossing);
        match.OrderMove(0, MapGrid.TileCenter(20, 1), clear);
        match.Simulation.Run(4);
        Assert.Equal((MoveState.Moving, MoveState.Moving), (match.MoveStateOf(crossing), match.MoveStateOf(clear)));
        var untouched = match.Movers.Get(clear);

        match.PlaceStructure(1, "generator", 10, 3);

        // The line of one unit is cut and it plans a route; the other's is as clear as it was.
        Assert.Equal(MoveState.Planning, match.MoveStateOf(crossing));
        Assert.Equal(untouched, match.Movers.Get(clear));
        match.RunUntilIdle(crossing);
        Assert.Equal(MapGrid.TileCenter(20, 4), match.PositionOf(crossing));
        match.RunUntilIdle(clear, limit: 1);
        Assert.Equal(MapGrid.TileCenter(20, 1), match.PositionOf(clear));
    }

    [Fact]
    public void AUnitWhoseDestinationIsCutOffGoesToTheNearestTileItCanStillReach()
    {
        var match = Create(TwoGaps);
        var unit = match.SpawnUnit(0, MapGrid.TileCenter(1, 0), QuarterTile);
        match.OrderMove(0, MapGrid.TileCenter(1, 5), unit);
        match.Simulation.Run(4);

        match.PlaceStructure(1, "generator", 4, 2);
        match.PlaceStructure(1, "generator", 14, 2);

        Assert.Equal(2, match.Grid.RegionCount);
        match.RunUntilIdle(unit);
        Assert.Equal(MapGrid.TileCenter(1, 1), match.PositionOf(unit));
    }

    [Fact]
    public void AUnitOnARouteTakesTheShorterWayThatOpensWhenAStructureFalls()
    {
        var match = Create(TwoGaps);
        var plug = match.PlaceStructure(1, "generator", 4, 2);
        var unit = match.SpawnUnit(0, MapGrid.TileCenter(1, 0), QuarterTile);
        var target = MapGrid.TileCenter(1, 5);
        match.OrderMove(0, target, unit);
        match.Simulation.Run(4);
        Assert.Equal(MoveState.Following, match.MoveStateOf(unit));

        match.Raze(plug);
        var ticks = match.RunUntilIdle(unit);

        Assert.Equal(target, match.PositionOf(unit));
        Assert.InRange(ticks, 4 * 6, 4 * 14);
    }

    [Fact]
    public void UnitsGoingToTheSamePlaceShareARouteAgainAndTheOthersKeepTheirOwnDestinations()
    {
        var match = Create(TwoGaps);
        var first = match.SpawnUnit(0, MapGrid.TileCenter(1, 0), QuarterTile);
        var second = match.SpawnUnit(0, MapGrid.TileCenter(2, 0), QuarterTile);
        var other = match.SpawnUnit(1, MapGrid.TileCenter(3, 0), QuarterTile);
        match.OrderMove(0, MapGrid.TileCenter(1, 5), first, second);
        match.OrderMove(1, MapGrid.TileCenter(8, 5), other);
        match.Simulation.Run(4);
        var searches = match.Routes.SearchCount;

        match.PlaceStructure(1, "generator", 4, 2);
        match.Simulation.Tick();

        // The cache was emptied, so both routes are searched again: one for the two units of
        // player 0, who walk to the same point, and one for the unit of player 1.
        Assert.Equal(searches + 2, match.Routes.SearchCount);
        Assert.Equal(match.Movers.Get(first).RouteStart, match.Movers.Get(second).RouteStart);

        match.RunUntilIdle(first);
        match.RunUntilIdle(other);
        match.Simulation.Run(40);
        Assert.Equal(MapGrid.TileCenter(8, 5), match.PositionOf(other));
        Assert.Equal((1, 5), match.TileOf(first));
        Assert.Equal(MoveState.Idle, match.MoveStateOf(second));
    }

    /// <summary>What lays a structure where units stand moves them off first; one left there stops.</summary>
    [Fact]
    public void AUnitLeftStandingUnderANewStructureStops()
    {
        var match = Create(TestMaps.Open(24, 8));
        var unit = match.SpawnUnit(0, MapGrid.TileCenter(10, 3), QuarterTile);
        match.OrderMove(0, MapGrid.TileCenter(20, 3), unit);
        match.Simulation.Tick();

        match.PlaceStructure(1, "generator", 10, 3);

        Assert.Equal(MoveState.Idle, match.MoveStateOf(unit));
        match.Simulation.Run(10);
        Assert.Equal(MoveState.Idle, match.MoveStateOf(unit));
    }
}
