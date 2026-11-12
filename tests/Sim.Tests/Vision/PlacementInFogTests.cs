using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Construction;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Vision;

namespace Sim.Tests.Vision;

/// <summary>
/// The two rules of placement that the fog of war brings (design 5.3 and 5.4; design 8.4, rows
/// 23 and 29): a Capitol or an Outpost goes only on explored ground, and an order is judged,
/// when it is given, with nothing but what its player may know. The truth is judged when the
/// worker arrives, with a full refund.
/// </summary>
/// <remarks>
/// The matches are on the design's data, where an Outpost sees 8 tiles and holds a territory of
/// 12, and nobody has explored anything but what a test's units and structures see.
/// </remarks>
public class PlacementInFogTests
{
    private const int CliffX = 31;
    private const int CliffY = 11;

    /// <summary>Open ground of 60 by 24 tiles with one cliff tile, at (31, 11).</summary>
    private static MapDef Ground() => TestMaps.FromRows(
        [.. Enumerable.Range(0, 24).Select(row => row == CliffY ? new string('.', CliffX) + "#" + new string('.', 60 - CliffX - 1) : new string('.', 60))]);

    /// <summary>
    /// Player 0 has a finished Outpost on the tiles (20, 10) to (22, 12), so its center is the
    /// point (21.5, 11.5), and both players have 2,000 of each resource.
    /// </summary>
    private static Match WithAnOutpost()
    {
        var match = FogMatches.Create(Ground());
        match.Place(0, "outpost", 20, 10);
        match.Stockpiles.Add(0, 2000, 2000);
        match.Stockpiles.Add(1, 2000, 2000);
        return match;
    }

    private static EntityId WorkerAt(Match match, int player, int x, int y)
    {
        var worker = match.WorkerAt(player, x, y);
        match.Vision.Refresh();
        return worker;
    }

    /// <summary>Puts a unit somewhere else at once and works sight out again.</summary>
    private static void Put(Match match, EntityId unit, int x, int y)
    {
        match.Bodies.Get(unit).Position = MapGrid.TileCenter(x, y);
        match.RefreshDerivedState();
        match.Vision.Refresh();
    }

    private static long MaterialsOf(Match match, int player) => match.Stockpiles.Materials(player) / 1000;

    /// <summary>Design 5.3: Capitols and Outposts can be placed only on explored ground.</summary>
    [Theory]
    [InlineData("outpost")]
    [InlineData("capitol")]
    public void ACapitolOrAnOutpostIsRefusedOnGroundThatIsNotExplored(string structure)
    {
        var match = WithAnOutpost();
        var worker = WorkerAt(match, 0, 21, 14);
        var def = match.Def(structure);
        var view = new PlayerView(match, 0);
        var before = MaterialsOf(match, 0);

        // The Outpost sees 8 tiles from (21.5, 11.5): the tile (29, 11) is visible and (30, 11) is not.
        Assert.Equal(FogState.Visible, view.FogAt(29, 11));
        Assert.Equal(FogState.Unexplored, view.FogAt(30, 11));

        // Far away, and with one tile of the footprint in the unexplored: the corner (29, 10) is further than 8.
        Assert.Equal(FogState.Unexplored, view.FogAt(29, 10));
        foreach (var (x, y) in new[] { (50, 4), (30 - def.Footprint, 10) })
        {
            Assert.Equal(BuildProblem.NotExplored, view.CheckSite(def, x, y));
            Assert.Equal(BuildProblem.NotExplored, match.Construction.CheckSiteAsKnown(0, def, x, y));
            match.OrderBuild(0, structure, x, y, worker);
            match.Simulation.Tick();

            Assert.Equal([(worker, BuildProblem.NotExplored)], match.Problems(SimEventKind.BuildRefused));
            Assert.Equal(0, match.BuildOrders.Count);
            Assert.Equal(before, MaterialsOf(match, 0));
        }

        // The true state has nothing against either site: it is the player who may not know.
        Assert.Equal(BuildProblem.None, match.Construction.CheckSite(0, def, 50, 4));

        // One column nearer, every tile is explored, and the order is taken.
        var nearX = 29 - def.Footprint;
        Assert.Equal(BuildProblem.None, view.CheckSite(def, nearX, 10));
        match.OrderBuild(0, structure, nearX, 10, worker);
        match.Simulation.Tick();
        Assert.True(match.BuildOrders.Contains(worker));
        Assert.Equal(before - def.Materials, MaterialsOf(match, 0));
    }

    [Fact]
    public void GroundThatWasExploredAndIsNotVisibleNowIsExplored()
    {
        var match = WithAnOutpost();
        var worker = WorkerAt(match, 0, 50, 5);
        Put(match, worker, 21, 14);
        var view = new PlayerView(match, 0);
        Assert.Equal(FogState.Explored, view.FogAt(50, 5));

        match.OrderBuild(0, "outpost", 49, 4, worker);
        match.Simulation.Tick();

        Assert.True(match.BuildOrders.Contains(worker));
        Assert.Empty(match.EventsOf(SimEventKind.BuildRefused));
    }

    /// <summary>Only Capitols and Outposts: any other structure goes inside the player's territory, explored or not.</summary>
    [Fact]
    public void AStructureThatNeedsTerritoryMayBeOrderedOnGroundInsideItThatIsNotExplored()
    {
        var match = WithAnOutpost();
        var worker = WorkerAt(match, 0, 21, 14);
        var generator = match.Def("generator");
        var view = new PlayerView(match, 0);

        // 12 tiles of territory and 8 of sight: the tiles (30, 14) to (31, 15) are the player's and unexplored.
        Assert.Equal(FogState.Unexplored, view.FogAt(30, 14));
        Assert.Equal(BuildProblem.None, view.CheckSite(generator, 30, 14));
        Assert.True(view.CanBuildOnTile(generator, 30, 14));

        match.OrderBuild(0, "generator", 30, 14, worker);
        var tick = match.RunUntilOrderEnds(worker);

        Assert.True(tick > 20);
        Assert.NotEqual(EntityId.None, match.StructureAt(30, 14));
        Assert.Empty(match.EventsOf(SimEventKind.BuildOrderEnded));
    }

    /// <summary>
    /// Design 8.4, row 29: the check made when the order is given uses only what the player may
    /// know. A cliff the player has not explored is not known, so the order is taken, and the
    /// worker finds the cliff on arrival (row 23).
    /// </summary>
    [Fact]
    public void GroundThePlayerHasNotExploredIsNoReasonToRefuseAnOrder()
    {
        var match = WithAnOutpost();
        var worker = WorkerAt(match, 0, 21, 14);
        var generator = match.Def("generator");
        var view = new PlayerView(match, 0);
        var before = MaterialsOf(match, 0);
        Assert.Equal(FogState.Unexplored, view.FogAt(CliffX, CliffY));

        // The truth is that the cliff is in the way. The player does not know it.
        Assert.Equal(BuildProblem.SiteNotFree, match.Construction.CheckSite(0, generator, 30, 10));
        Assert.Equal(BuildProblem.None, view.CheckSite(generator, 30, 10));
        Assert.True(view.CanBuildOnTile(generator, CliffX, CliffY));
        Assert.False(match.Vision.KnowsBlocked(0, new TileSquare(30, 10, 2)));

        match.OrderBuild(0, "generator", 30, 10, worker);
        match.Simulation.Tick();
        Assert.True(match.BuildOrders.Contains(worker));
        Assert.Equal(before - generator.Materials, MaterialsOf(match, 0));

        // On arrival the true state is judged, and the whole price comes back.
        match.RunUntilOrderEnds(worker);
        var ended = Assert.Single(match.EventsOf(SimEventKind.BuildOrderEnded));
        Assert.Equal((worker, BuildProblem.SiteNotFree), (ended.Entity, (BuildProblem)ended.Amount));
        Assert.Equal(EntityId.None, match.StructureAt(30, 10));
        Assert.Equal(before, MaterialsOf(match, 0));

        // The worker has seen the cliff by now, and the same order is refused when it is given.
        Assert.NotEqual(FogState.Unexplored, view.FogAt(CliffX, CliffY));
        Assert.True(match.Vision.KnowsBlocked(0, new TileSquare(30, 10, 2)));
        Assert.False(view.CanBuildOnTile(generator, CliffX, CliffY));
        match.OrderBuild(0, "generator", 30, 10, worker);
        match.Simulation.Tick();
        Assert.Equal([(worker, BuildProblem.SiteNotFree)], match.Problems(SimEventKind.BuildRefused));
    }

    /// <summary>
    /// Design 8.4, row 29: a refusal would tell the player what stands in the fog. A structure
    /// the player has never seen is no reason to refuse; the worker finds it on arrival.
    /// </summary>
    [Fact]
    public void AStructureThePlayerHasNeverSeenIsNoReasonToRefuseAnOrder()
    {
        var match = WithAnOutpost();
        var worker = WorkerAt(match, 0, 45, 5);
        Put(match, worker, 21, 14);
        var enemy = match.Place(1, "generator", 45, 5);
        var outpost = match.Def("outpost");
        var view = new PlayerView(match, 0);
        var before = MaterialsOf(match, 0);
        Assert.Equal(FogState.Explored, view.FogAt(45, 5));
        Assert.False(view.KnowsStructure(enemy));

        Assert.Equal(BuildProblem.SiteNotFree, match.Construction.CheckSite(0, outpost, 44, 4));
        Assert.Equal(BuildProblem.None, view.CheckSite(outpost, 44, 4));

        match.OrderBuild(0, "outpost", 44, 4, worker);
        match.Simulation.Tick();
        Assert.True(match.BuildOrders.Contains(worker));
        Assert.Empty(match.EventsOf(SimEventKind.BuildRefused));
        Assert.Equal(before - outpost.Materials, MaterialsOf(match, 0));

        match.RunUntilOrderEnds(worker);
        var ended = Assert.Single(match.EventsOf(SimEventKind.BuildOrderEnded));
        Assert.Equal((worker, BuildProblem.SiteNotFree), (ended.Entity, (BuildProblem)ended.Amount));
        Assert.Equal(before, MaterialsOf(match, 0));

        // Now the player sees it, and the same order is refused when it is given.
        Assert.True(view.Sees(enemy));
        match.OrderBuild(0, "outpost", 44, 4, worker);
        match.Simulation.Tick();
        Assert.Equal([(worker, BuildProblem.SiteNotFree)], match.Problems(SimEventKind.BuildRefused));
    }

    /// <summary>
    /// What the player may know includes what it remembers: a remembered structure is in the
    /// way of an order, also when it is gone, until the player sees its tiles without it.
    /// </summary>
    [Fact]
    public void ARememberedStructureIsInTheWayUntilThePlayerSeesThatItIsGone()
    {
        var match = WithAnOutpost();
        var enemy = match.Place(1, "generator", 45, 5);
        var worker = WorkerAt(match, 0, 45, 8);
        Put(match, worker, 21, 14);
        var outpost = match.Def("outpost");
        var view = new PlayerView(match, 0);
        Assert.True(view.TryGetStructure(enemy, out var seen));
        Assert.True(seen.IsRemembered);

        match.Demolish(enemy);
        match.Run(4);
        Assert.Equal(BuildProblem.None, match.Construction.CheckSite(0, outpost, 44, 4));
        Assert.Equal(BuildProblem.SiteNotFree, view.CheckSite(outpost, 44, 4));
        Assert.False(view.CanBuildOnTile(outpost, 46, 6));
        Assert.True(view.CanBuildOnTile(outpost, 47, 6));

        match.OrderBuild(0, "outpost", 44, 4, worker);
        match.Simulation.Tick();
        Assert.Equal([(worker, BuildProblem.SiteNotFree)], match.Problems(SimEventKind.BuildRefused));

        // Beside what it remembers, the order is taken.
        match.OrderBuild(0, "outpost", 47, 4, worker);
        match.Simulation.Tick();
        Assert.True(match.BuildOrders.Contains(worker));
        match.OrderCancelBuild(0, worker);
        match.Simulation.Tick();

        // The worker looks again, the memory is cleared, and the site is free.
        Put(match, worker, 45, 8);
        Assert.False(view.KnowsStructure(enemy));
        Assert.Equal(BuildProblem.None, view.CheckSite(outpost, 44, 4));
        match.OrderBuild(0, "outpost", 44, 4, worker);
        match.Simulation.Tick();
        Assert.True(match.BuildOrders.Contains(worker));
    }

    /// <summary>The player's own structures and foundations are known wherever they stand.</summary>
    [Fact]
    public void ThePlayersOwnStructuresAreKnownWhereverTheyStand()
    {
        var match = WithAnOutpost();
        var view = new PlayerView(match, 0);
        var generator = match.Def("generator");

        Assert.Equal(BuildProblem.SiteNotFree, view.CheckSite(generator, 22, 12));
        Assert.Equal(BuildProblem.None, view.CheckSite(generator, 23, 12));

        // A foundation of its own, laid by a fixture where nothing of the player sees.
        match.Lay(0, "generator", 26, 16);
        Assert.Equal(BuildProblem.SiteNotFree, view.CheckSite(generator, 27, 17));
    }

    /// <summary>Off the map is known to everyone, and so is an order outside the player's territory.</summary>
    [Fact]
    public void WhatEveryPlayerKnowsIsJudgedWhenTheOrderIsGiven()
    {
        var match = WithAnOutpost();
        var view = new PlayerView(match, 0);
        var enemy = new PlayerView(match, 1);

        Assert.Equal(BuildProblem.SiteNotFree, view.CheckSite(match.Def("generator"), 59, 10));
        Assert.Equal(BuildProblem.SiteNotFree, view.CheckSite(match.Def("outpost"), 58, 22));
        Assert.Equal(BuildProblem.OutsideTerritory, view.CheckSite(match.Def("generator"), 40, 10));
        Assert.Equal(BuildProblem.OutsideTerritory, enemy.CheckSite(match.Def("generator"), 23, 12));
        Assert.Equal(BuildProblem.NotExplored, enemy.CheckSite(match.Def("outpost"), 40, 10));
    }

    /// <summary>
    /// Design 5.4: the placement preview marks a site as blocked only by enemy Capitols and
    /// Outposts the player can see at that moment.
    /// </summary>
    [Fact]
    public void ThePreviewCountsOnlyEnemyTerritoryThePlayerSeesNow()
    {
        var match = WithAnOutpost();
        var enemy = match.Place(1, "outpost", 45, 10);
        var worker = WorkerAt(match, 0, 21, 14);
        var outpost = match.Def("outpost");
        var view = new PlayerView(match, 0);

        // Two territories of 12 tiles: a center nearer than 24 tiles overlaps. The site at (28, 10) is 17 away.
        Assert.True(match.Construction.IsTooCloseToEnemyTerritory(0, outpost, 28, 10));
        Assert.False(view.IsTooCloseToSeenEnemyTerritory(outpost, 28, 10));

        Put(match, worker, 43, 8);
        Assert.True(view.Sees(enemy));
        Assert.True(view.IsTooCloseToSeenEnemyTerritory(outpost, 28, 10));
        Assert.False(view.IsTooCloseToSeenEnemyTerritory(outpost, 20, 18));

        // Remembered and not seen now: the preview does not count it, and the order is taken all the same.
        Put(match, worker, 21, 14);
        Assert.True(view.TryGetStructure(enemy, out var seen));
        Assert.True(seen.IsRemembered);
        Assert.False(view.IsTooCloseToSeenEnemyTerritory(outpost, 26, 10));
        match.OrderBuild(0, "outpost", 26, 10, worker);
        match.Simulation.Tick();
        Assert.True(match.BuildOrders.Contains(worker));

        // The overlap is judged with the true state on arrival, as before.
        match.RunUntilOrderEnds(worker);
        var ended = Assert.Single(match.EventsOf(SimEventKind.BuildOrderEnded));
        Assert.Equal(BuildProblem.TooCloseToEnemyTerritory, (BuildProblem)ended.Amount);

        // An unfinished Outpost holds no territory, and a structure that holds none is never too close.
        Assert.False(view.IsTooCloseToSeenEnemyTerritory(match.Def("generator"), 28, 10));
    }
}
