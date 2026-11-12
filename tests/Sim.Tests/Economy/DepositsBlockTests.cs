using Sim.Economy;
using Sim.Map;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Economy.EconomyMatches;

namespace Sim.Tests.Economy;

/// <summary>Design 4.1: a deposit blocks its four tiles for movement.</summary>
public class DepositsBlockTests
{
    [Fact]
    public void NoUnitCanStandOnADeposit()
    {
        var grid = new MapGrid(OneDeposit, depositSize: 2);

        foreach (var (x, y) in ((int, int)[])[(10, 10), (11, 10), (10, 11), (11, 11)])
        {
            Assert.False(grid.IsPassable(x, y));
            Assert.Equal(0, grid.RegionOf(x, y));
        }

        foreach (var (x, y) in ((int, int)[])[(9, 10), (12, 11), (10, 9), (11, 12), (9, 9), (12, 12)])
        {
            Assert.True(grid.IsPassable(x, y));
        }

        Assert.False(grid.IsSegmentClear(MapGrid.TileCenter(8, 10), MapGrid.TileCenter(13, 10)));
    }

    [Fact]
    public void AGridOfAMapWithDepositsMustBeToldHowLargeADepositIs()
    {
        Assert.Throws<ArgumentException>(() => new MapGrid(OneDeposit));
        Assert.Throws<ArgumentException>(() => new MapGrid(OneDeposit, depositSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapGrid(OneDeposit, depositSize: -1));
        Assert.True(new MapGrid(TestMaps.Open(4, 4)).IsPassable(2, 2));

        // One that sticks out over the edge of the map blocks the tiles that are on it.
        var atTheEdge = new MapGrid(TestMaps.Open(4, 4).WithDeposits((3, 3)), depositSize: 2);
        Assert.False(atTheEdge.IsPassable(3, 3));
        Assert.True(atTheEdge.IsPassable(2, 3));
    }

    [Fact]
    public void AUnitWalksAroundADeposit()
    {
        var match = WithOneDeposit(out _);
        var unit = match.WorkerAt(0, 6, 10);

        match.OrderMove(0, MapGrid.TileCenter(15, 11), unit);
        for (var tick = 0; tick < 200; tick++)
        {
            match.Simulation.Tick();
            Assert.DoesNotContain(match.StandsOn(unit), ((int, int)[])[(10, 10), (11, 10), (10, 11), (11, 11)]);
        }

        Assert.Equal(MapGrid.TileCenter(15, 11), match.PositionOf(unit));
    }

    /// <summary>A move order onto a deposit ends on the nearest tile beside it.</summary>
    [Fact]
    public void AnOrderOntoADepositEndsBesideIt()
    {
        var match = WithOneDeposit(out _);
        var unit = match.WorkerAt(0, 6, 10);

        match.OrderMove(0, TestMatches.Point("10.25", "10.5"), unit);
        match.Simulation.Run(100);

        Assert.Equal(MapGrid.TileCenter(9, 10), match.PositionOf(unit));
    }

    [Fact]
    public void AGeneratorCannotStandOnADepositOffTheMapOrOnAnotherWorkSite()
    {
        var match = WithOneDeposit(out _);
        var generator = match.PlaceGenerator(1, 4, 4);

        Assert.Equal((WorkSiteKind.Generator, 1, 4, 4, 2), (match.WorkSites.Get(generator).Kind, match.WorkSites.Get(generator).Owner, match.WorkSites.Get(generator).X, match.WorkSites.Get(generator).Y, match.WorkSites.Get(generator).Size));
        Assert.Throws<ArgumentException>(() => match.PlaceGenerator(0, 9, 9));
        Assert.Throws<ArgumentException>(() => match.PlaceGenerator(0, 23, 5));
        Assert.Throws<ArgumentException>(() => match.PlaceGenerator(0, 5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => match.PlaceGenerator(2, 14, 14));
        Assert.Equal(2, match.WorkSites.Count);
    }
}
