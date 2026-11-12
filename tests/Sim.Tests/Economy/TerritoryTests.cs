using Sim.Economy;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Economy.EconomyMatches;

namespace Sim.Tests.Economy;

/// <summary>Design 2.5: a tile is inside a territory when its center is within the radius of the footprint's center.</summary>
public class TerritoryTests
{
    [Theory]
    [InlineData(3, 4, true)] // Its center is exactly 5 tiles from the center: 3 across and 4 down.
    [InlineData(4, 3, true)]
    [InlineData(5, 0, true)]
    [InlineData(4, 4, false)]
    [InlineData(6, 0, false)]
    [InlineData(-5, 0, true)]
    [InlineData(0, -6, false)]
    public void ATileIsInsideWhenItsCenterIsWithinTheRadiusTheExactDistanceIncluded(int x, int y, bool inside)
    {
        var territory = Territory.Around(owner: 0, x: 0, y: 0, footprint: 1, radius: 5);

        Assert.Equal(inside, territory.Contains(x, y));
    }

    /// <summary>The center of a footprint of 4 by 4 is a corner of four tiles, half a tile from each of their centers.</summary>
    [Fact]
    public void TheRadiusIsMeasuredFromTheCenterOfTheFootprint()
    {
        var capitol = Territory.Around(owner: 0, x: 10, y: 46, footprint: 4, radius: 16);

        Assert.Equal((24, 96), (capitol.CenterX2, capitol.CenterY2));
        Assert.True(capitol.Contains(27, 47));
        Assert.False(capitol.Contains(28, 47));
        Assert.True(capitol.Contains(-4, 48));
        Assert.False(capitol.Contains(-5, 48));

        // 11.5 across and 11.5 down is 16.26 tiles; 11.5 and 10.5 is 15.57.
        Assert.False(capitol.Contains(23, 59));
        Assert.True(capitol.Contains(23, 58));
    }

    /// <summary>
    /// The map checker accepts a main deposit only wholly inside a starting territory and a natural
    /// or contested one only outside every one. The match must see the same.
    /// </summary>
    [Fact]
    public void TheStartingTerritoriesHoldTheMainDepositsAsTheMapCheckerSaid()
    {
        var data = TestMatches.Data;
        var map = data.Maps[0];
        var match = MatchSetup.Create(data, map, 1, [new(0), new(1)]);

        Assert.Equal(2, match.Territories.Count);
        Assert.Equal(map.Deposits.Count, match.WorkSites.Count);
        Assert.Equal(12 + 2 + map.Deposits.Count, match.World.Entities.Count);
        for (var i = 0; i < map.Deposits.Count; i++)
        {
            var site = match.WorkSites.ValueAt(i);
            Assert.Equal((WorkSiteKind.Deposit, WorkSite.NoOwner, map.Deposits[i].X, map.Deposits[i].Y, 2, 0L), (site.Kind, site.Owner, site.X, site.Y, site.Size, site.Delivered));

            var holders = Enumerable.Range(0, 2).Where(player => Territory.Holds(match.Territories, match.World.Entities, player, site.X, site.Y, site.Size)).ToArray();
            if (map.Deposits[i].Kind == Sim.Data.DepositKind.Main)
            {
                Assert.Equal([site.X < map.Width / 2 ? 0 : 1], holders);
            }
            else
            {
                Assert.Empty(holders);
            }
        }
    }

    [Fact]
    public void ATerritoryEndsWithItsEntity()
    {
        var match = Create(TestMaps.Open(16, 16));
        var outpost = match.PlaceTerritory(1, 4, 4, 3, 12);
        Assert.True(Territory.Holds(match.Territories, match.World.Entities, 1, 8, 8, 2));

        match.World.Entities.Destroy(outpost);
        Assert.False(Territory.Holds(match.Territories, match.World.Entities, 1, 8, 8, 2));
        match.Simulation.Tick();
        Assert.Equal(0, match.Territories.Count);

        Assert.Throws<ArgumentOutOfRangeException>(() => match.PlaceTerritory(2, 4, 4, 3, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => match.PlaceTerritory(0, 4, 4, 0, 12));
    }
}
