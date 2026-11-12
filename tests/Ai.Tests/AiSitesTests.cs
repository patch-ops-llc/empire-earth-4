using Sim.Construction;
using Sim.Economy;

namespace Ai.Tests;

/// <summary>
/// Where the computer player puts its structures: the nearest site with open ground on every
/// side, so that it never closes a way through its base or covers the places around a deposit.
/// </summary>
public class AiSitesTests
{
    [Theory]
    [InlineData("generator")]
    [InlineData("barracks")]
    [InlineData("academy")]
    public void ASiteHasOpenGroundOnEverySide(string structure)
    {
        var fixture = new AiFixture();
        var sites = new AiSites(fixture.Sight);
        var def = AiFixture.Structure(structure);
        var (homeX, homeY, radius) = fixture.Home;

        Assert.True(sites.TryFind(def, homeX, homeY, radius, homeX, homeY, alsoNeeds: null, out var x, out var y));

        Assert.Equal(BuildProblem.None, fixture.Sight.CheckSite(def, x, y));
        for (var tileY = y - AiSites.Margin; tileY < y + def.Footprint + AiSites.Margin; tileY++)
        {
            for (var tileX = x - AiSites.Margin; tileX < x + def.Footprint + AiSites.Margin; tileX++)
            {
                Assert.True(fixture.Match.Grid.IsPassable(tileX, tileY), $"Tile ({tileX}, {tileY}) beside the site at ({x}, {y}) is not open.");
            }
        }

        // The match agrees: the structure can stand there.
        Assert.True(fixture.Match.StructureFactory.HasRoomFor(def, x, y));
    }

    [Fact]
    public void ASiteBesideAStructureIsNoSite()
    {
        var fixture = new AiFixture();
        var sites = new AiSites(fixture.Sight);
        var generator = AiFixture.Structure("generator");
        var capitol = fixture.Sight.Structures.Single();

        // Touching the Capitol, and one tile away from it: units could not pass.
        Assert.Equal(BuildProblem.None, fixture.Sight.CheckSite(generator, capitol.X + capitol.Size + 1, capitol.Y));
        Assert.False(sites.IsGood(generator, capitol.X + capitol.Size, capitol.Y));
        Assert.False(sites.IsGood(generator, capitol.X + capitol.Size + 1, capitol.Y));
        Assert.False(sites.IsGood(generator, capitol.X - generator.Footprint - 1, capitol.Y));
        Assert.False(sites.IsGood(generator, capitol.X, capitol.Y + capitol.Size + 1));
    }

    [Fact]
    public void ASiteBesideADepositIsNoSite()
    {
        var fixture = new AiFixture();
        var sites = new AiSites(fixture.Sight);
        var generator = AiFixture.Structure("generator");
        var deposit = fixture.Sight.Sites.First(site => site.Kind == WorkSiteKind.Deposit && site.IsHeld);

        // The tiles around a deposit are where its workers stand.
        for (var gap = 0; gap < AiSites.Margin; gap++)
        {
            Assert.False(sites.IsGood(generator, deposit.X + deposit.Size + gap, deposit.Y));
            Assert.False(sites.IsGood(generator, deposit.X, deposit.Y - generator.Footprint - gap));
        }
    }

    [Fact]
    public void TheSiteIsOfTheNearestRingThatHasOne()
    {
        var fixture = new AiFixture();
        var sites = new AiSites(fixture.Sight);
        var def = AiFixture.Structure("barracks");
        var (homeX, homeY, radius) = fixture.Home;
        var originX = homeX - (def.Footprint / 2);
        var originY = homeY - (def.Footprint / 2);

        Assert.True(sites.TryFind(def, homeX, homeY, radius, homeX, homeY, alsoNeeds: null, out var x, out var y));

        // The Capitol stands on the tile searched around, so the site is some rings out.
        var ring = Math.Max(Math.Abs(x - originX), Math.Abs(y - originY));
        Assert.True(ring > 0);
        for (var offsetY = -(ring - 1); offsetY < ring; offsetY++)
        {
            for (var offsetX = -(ring - 1); offsetX < ring; offsetX++)
            {
                Assert.False(sites.IsGood(def, originX + offsetX, originY + offsetY));
            }
        }
    }

    /// <summary>Within a ring the tile to prefer decides, which is how a site toward home is chosen.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1000, 0)]
    [InlineData(0, 1000)]
    [InlineData(1000, 1000)]
    public void OfTheSitesOfOneRingTheNearestToThePreferredTileIsTaken(int preferX, int preferY)
    {
        var fixture = new AiFixture();
        var sites = new AiSites(fixture.Sight);
        var def = AiFixture.Structure("barracks");
        var (homeX, homeY, radius) = fixture.Home;
        var originX = homeX - (def.Footprint / 2);
        var originY = homeY - (def.Footprint / 2);

        Assert.True(sites.TryFind(def, homeX, homeY, radius, preferX, preferY, alsoNeeds: null, out var x, out var y));

        var ring = Math.Max(Math.Abs(x - originX), Math.Abs(y - originY));
        var found = AiSites.DistanceSquared(x, y, def.Footprint, preferX, preferY);
        var others = 0;
        for (var offsetY = -ring; offsetY <= ring; offsetY++)
        {
            for (var offsetX = -ring; offsetX <= ring; offsetX++)
            {
                var candidateX = originX + offsetX;
                var candidateY = originY + offsetY;
                if (Math.Max(Math.Abs(offsetX), Math.Abs(offsetY)) == ring && sites.IsGood(def, candidateX, candidateY))
                {
                    others++;
                    Assert.True(AiSites.DistanceSquared(candidateX, candidateY, def.Footprint, preferX, preferY) >= found);
                }
            }
        }

        // The ring had a choice to make.
        Assert.True(others > 1);
    }

    [Fact]
    public void AFurtherRuleOfTheCallerNarrowsTheSearch()
    {
        var fixture = new AiFixture();
        var sites = new AiSites(fixture.Sight);
        var def = AiFixture.Structure("barracks");
        var (homeX, homeY, radius) = fixture.Home;
        Assert.True(sites.TryFind(def, homeX, homeY, radius, homeX, homeY, alsoNeeds: null, out var x, out var y));

        Assert.True(sites.TryFind(def, homeX, homeY, radius, homeX, homeY, (siteX, siteY) => siteX != x || siteY != y, out var otherX, out var otherY));
        Assert.NotEqual((x, y), (otherX, otherY));
        Assert.True(sites.IsGood(def, otherX, otherY));

        Assert.False(sites.TryFind(def, homeX, homeY, radius, homeX, homeY, (_, _) => false, out _, out _));
    }

    [Fact]
    public void ItFindsNoSiteWhereThereIsNone()
    {
        var fixture = new AiFixture();
        var sites = new AiSites(fixture.Sight);
        var (homeX, homeY, _) = fixture.Home;

        // Two rings around the middle of the Capitol are the Capitol and the ground kept open beside it.
        Assert.False(sites.TryFind(AiFixture.Structure("barracks"), homeX, homeY, rings: 2, homeX, homeY, alsoNeeds: null, out _, out _));
    }

    [Fact]
    public void ASiteKeepsClearOfAStructureThatIsOrderedAndNotYetLaid()
    {
        var fixture = new AiFixture();
        var sites = new AiSites(fixture.Sight);
        var def = AiFixture.Structure("barracks");
        var (homeX, homeY, radius) = fixture.Home;
        Assert.True(sites.TryFind(def, homeX, homeY, radius, homeX, homeY, alsoNeeds: null, out var x, out var y));

        // The order blocks no tile yet, so the match would take a second structure right beside it.
        fixture.Sight.BuildOrders.Add(new OwnBuildOrder(def.Index, x, y, def.Footprint));

        Assert.False(sites.IsGood(def, x, y));
        Assert.False(sites.IsGood(def, x + def.Footprint, y));
        Assert.False(sites.IsGood(def, x + def.Footprint + AiSites.Margin - 1, y));
        Assert.False(sites.IsGood(def, x, y - def.Footprint - AiSites.Margin + 1));

        Assert.True(sites.TryFind(def, homeX, homeY, radius, homeX, homeY, alsoNeeds: null, out var nextX, out var nextY));
        var apartX = Math.Max(nextX - (x + def.Footprint), x - (nextX + def.Footprint));
        var apartY = Math.Max(nextY - (y + def.Footprint), y - (nextY + def.Footprint));
        Assert.True(Math.Max(apartX, apartY) >= AiSites.Margin, $"({nextX}, {nextY}) is too near ({x}, {y}).");
    }

    [Fact]
    public void ARefusedSiteIsLeftAloneUntilItIsForgotten()
    {
        var fixture = new AiFixture();
        var sites = new AiSites(fixture.Sight);
        var def = AiFixture.Structure("barracks");
        var other = AiFixture.Structure("works");
        var (homeX, homeY, radius) = fixture.Home;
        Assert.True(sites.TryFind(def, homeX, homeY, radius, homeX, homeY, alsoNeeds: null, out var x, out var y));

        sites.Refuse(def.Index, x, y, untilTick: 100);

        Assert.False(sites.IsGood(def, x, y));
        Assert.True(sites.IsGood(other, x, y));
        Assert.True(sites.TryFind(def, homeX, homeY, radius, homeX, homeY, alsoNeeds: null, out var nextX, out var nextY));
        Assert.NotEqual((x, y), (nextX, nextY));

        sites.Forget(tick: 99);
        Assert.False(sites.IsGood(def, x, y));

        sites.Forget(tick: 100);
        Assert.True(sites.IsGood(def, x, y));
        Assert.True(sites.TryFind(def, homeX, homeY, radius, homeX, homeY, alsoNeeds: null, out nextX, out nextY));
        Assert.Equal((x, y), (nextX, nextY));
    }

    [Fact]
    public void ARefusedSiteIsKnownFromNearIt()
    {
        var sites = new AiSites(new AiFixture().Sight);
        sites.Refuse(type: 3, x: 10, y: 10, untilTick: 100);

        Assert.True(sites.WasRefusedNear(type: 3, x: 10, y: 10, within: 0));
        Assert.False(sites.WasRefusedNear(type: 3, x: 11, y: 10, within: 0));
        Assert.True(sites.WasRefusedNear(type: 3, x: 13, y: 7, within: 3));
        Assert.False(sites.WasRefusedNear(type: 3, x: 14, y: 10, within: 3));
        Assert.False(sites.WasRefusedNear(type: 4, x: 10, y: 10, within: 3));
    }

    /// <summary>From the middle of a footprint to the middle of a tile, in half tiles, so no number is ever halved.</summary>
    [Theory]
    [InlineData(0, 0, 1, 0, 0, 0)]
    [InlineData(0, 0, 2, 0, 0, 2)] // Half a tile each way.
    [InlineData(0, 0, 2, 1, 1, 2)]
    [InlineData(4, 6, 3, 5, 7, 0)] // The middle tile of three by three.
    [InlineData(4, 6, 3, 8, 7, 36)] // Three tiles.
    [InlineData(10, 10, 1, 7, 14, 100)] // Three and four tiles.
    public void DistancesAreMeasuredFromTheMiddleOfTheFootprint(int x, int y, int size, int tileX, int tileY, long expected)
    {
        Assert.Equal(expected, AiSites.DistanceSquared(x, y, size, tileX, tileY));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AnOutpostGoesWhereItsTerritoryHoldsTheWholeDeposit(int players)
    {
        var fixture = new AiFixture(players);
        fixture.ExploreAll();
        var construction = new AiConstruction(fixture.Sight);
        var outpost = AiFixture.Structure("outpost");
        var (homeX, homeY, _) = fixture.Home;
        var claimed = 0;

        foreach (var deposit in fixture.Sight.Sites.Where(site => site.Kind == WorkSiteKind.Deposit && !site.IsHeld))
        {
            if (!construction.TryFindSiteAt(deposit, outpost, homeX, homeY, out var x, out var y))
            {
                continue;
            }

            claimed++;
            Assert.True(construction.Sites.IsGood(outpost, x, y));
            var territory = Territory.Around(AiFixture.Player, x, y, outpost.Footprint, outpost.TerritoryRadius!.Value);
            for (var tileY = deposit.Y; tileY < deposit.Y + deposit.Size; tileY++)
            {
                for (var tileX = deposit.X; tileX < deposit.X + deposit.Size; tileX++)
                {
                    Assert.True(territory.Contains(tileX, tileY));
                }
            }

            Assert.False(fixture.Sight.IsTooCloseToKnownEnemyTerritory(outpost, x, y));

            // It stands beside the deposit, as near as the open ground around both allows.
            var apart = Math.Max(
                Math.Max(x - (deposit.X + deposit.Size), deposit.X - (x + outpost.Footprint)),
                Math.Max(y - (deposit.Y + deposit.Size), deposit.Y - (y + outpost.Footprint)));
            Assert.InRange(apart, AiSites.Margin, AiSites.Margin + 3);
        }

        Assert.True(claimed > 0);
    }

    /// <summary>
    /// With every site near the deposit taken from it, what is left is far out, where a
    /// territory can miss a corner of the deposit. It takes one that holds it all, or none.
    /// </summary>
    [Fact]
    public void AnOutpostIsNeverPutWhereItsTerritoryWouldMissATileOfTheDeposit()
    {
        var fixture = new AiFixture();
        fixture.ExploreAll();
        var outpost = AiFixture.Structure("outpost");
        var radius = outpost.TerritoryRadius!.Value;
        var (homeX, homeY, _) = fixture.Home;
        var checkedDeposits = 0;

        foreach (var deposit in fixture.Sight.Sites.Where(site => site.Kind == WorkSiteKind.Deposit && !site.IsHeld))
        {
            var construction = new AiConstruction(fixture.Sight);
            var originX = deposit.X + (deposit.Size / 2) - (outpost.Footprint / 2);
            var originY = deposit.Y + (deposit.Size / 2) - (outpost.Footprint / 2);
            var near = radius - 2;
            for (var offsetY = -near; offsetY <= near; offsetY++)
            {
                for (var offsetX = -near; offsetX <= near; offsetX++)
                {
                    construction.Sites.Refuse(outpost.Index, originX + offsetX, originY + offsetY, untilTick: 1000);
                }
            }

            if (construction.TryFindSiteAt(deposit, outpost, homeX, homeY, out var x, out var y))
            {
                var territory = Territory.Around(AiFixture.Player, x, y, outpost.Footprint, radius);
                Assert.True(territory.Contains(deposit.X, deposit.Y));
                Assert.True(territory.Contains(deposit.X + deposit.Size - 1, deposit.Y));
                Assert.True(territory.Contains(deposit.X, deposit.Y + deposit.Size - 1));
                Assert.True(territory.Contains(deposit.X + deposit.Size - 1, deposit.Y + deposit.Size - 1));
            }

            checkedDeposits++;
        }

        Assert.True(checkedDeposits > 0);
    }

    [Fact]
    public void NoOutpostGoesWhereItsTerritoryWouldReachIntoAnotherPlayers()
    {
        var fixture = new AiFixture(players: 2);
        fixture.ExploreAll();
        var construction = new AiConstruction(fixture.Sight);
        var outpost = AiFixture.Structure("outpost");
        var (homeX, homeY, _) = fixture.Home;

        // The deposits the other player holds from the start.
        var theirs = fixture.Sight.Sites.Where(site => site.Kind == WorkSiteKind.Deposit && fixture.Sight.IsClaimedByAnother(site.X, site.Y, site.Size)).ToList();

        Assert.NotEmpty(theirs);
        Assert.All(theirs, deposit =>
        {
            Assert.False(deposit.IsHeld);
            Assert.False(construction.TryFindSiteAt(deposit, outpost, homeX, homeY, out _, out _));
        });
    }

    [Fact]
    public void AStructureWithoutTerritoryClaimsNothing()
    {
        var fixture = new AiFixture();
        fixture.ExploreAll();
        var deposit = fixture.Sight.Sites.First(site => site.Kind == WorkSiteKind.Deposit && !site.IsHeld);

        Assert.False(new AiConstruction(fixture.Sight).TryFindSiteAt(deposit, AiFixture.Structure("barracks"), 0, 0, out _, out _));
    }
}
