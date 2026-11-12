using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Vision;
using Sim.Vision;
using Sim.Walls;

namespace Sim.Tests.Walls;

/// <summary>
/// A wall order under the fog of war (design 2.5 and 5.4): the run is judged, when it is ordered,
/// with nothing but what its player may know, as the site of any build order is. The truth is
/// judged tile by tile as the worker comes to each, and a tile that cannot be laid is skipped
/// with its price back.
/// </summary>
/// <remarks>
/// The match is that of <see cref="PlacementInFogTests"/>: open ground of 60 by 24 tiles with one
/// cliff tile at (31, 11), and a finished Outpost of player 0 on the tiles (20, 10) to (22, 12).
/// The Outpost sees 8 tiles from (21.5, 11.5) and holds a territory of 12, so the cliff is on
/// the player's ground and not explored.
/// </remarks>
public class WallOrdersInFogTests
{
    private const int CliffX = 31;
    private const int CliffY = 11;

    private static Match WithAnOutpost()
    {
        string[] rows = [.. Enumerable.Range(0, 24).Select(row => row == CliffY ? new string('.', CliffX) + "#" + new string('.', 60 - CliffX - 1) : new string('.', 60))];
        var match = FogMatches.Create(TestMaps.FromRows(rows));
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

    private static long MaterialsOf(Match match, int player) => match.Stockpiles.Materials(player) / 1000;

    [Fact]
    public void ARunOverGroundItsPlayerHasNotExploredIsPaidForAndTheTileSkippedWhenTheWorkerComesToIt()
    {
        var match = WithAnOutpost();
        var worker = WorkerAt(match, 0, 21, 14);
        var wall = match.Walls.Wall!;
        PlayerView[] views = [new(match, 0), new(match, 1)];
        Assert.Equal(FogState.Visible, views[0].FogAt(29, 11));
        Assert.Equal(FogState.Unexplored, views[0].FogAt(30, 11));
        Assert.Equal(FogState.Unexplored, views[0].FogAt(CliffX, CliffY));
        Assert.Equal(BuildProblem.SiteNotFree, match.Construction.CheckSite(0, wall, CliffX, CliffY));

        // The run and its price as the interface shows them: the cliff is not known, so its tile counts.
        var tiles = new List<(int X, int Y)>();
        Assert.Equal(BuildProblem.None, views[0].WallRun(29, 11, 32, 11, tiles));
        Assert.Equal([(29, 11), (30, 11), (31, 11), (32, 11)], tiles);

        var before = MaterialsOf(match, 0);
        match.OrderWall(0, (29, 11), (32, 11), worker);
        match.Simulation.Tick();
        Assert.Empty(match.EventsOf(SimEventKind.BuildRefused));
        Assert.Equal(before - (4 * wall.Materials), MaterialsOf(match, 0));

        // The tiles still to come are their owner's to know.
        var plans = new List<WallPlan>();
        views[0].CollectWallPlans(plans);
        Assert.Equal(tiles, plans.Select(plan => (plan.X, plan.Y)));
        views[1].CollectWallPlans(plans);
        Assert.Empty(plans);

        var skipped = new List<SimEvent>();
        while (match.Walls.Has(worker))
        {
            Assert.True(match.World.Tick < 4000, "The wall order never ended.");
            match.Simulation.Tick();
            foreach (var reported in match.EventsOf(SimEventKind.WallTileSkipped))
            {
                skipped.Add(reported);
                Assert.True(views[0].Knows(in reported));
                Assert.False(views[1].Knows(in reported));
            }
        }

        // The worker found the cliff, the tile was given back, and the run went on behind it.
        var skip = Assert.Single(skipped);
        Assert.Equal((0, worker, BuildProblem.SiteNotFree), (skip.Player, skip.Entity, (BuildProblem)skip.Amount));
        Assert.Equal([(29, 11), (30, 11), (32, 11)], match.WallOf(0));
        Assert.Equal(before - (3 * wall.Materials), MaterialsOf(match, 0));
        views[0].CollectWallPlans(plans);
        Assert.Empty(plans);
    }

    [Fact]
    public void ATileItsPlayerKnowsCannotBeLaidIsLeftOutOfTheRunAndNeverPaidFor()
    {
        // The worker stands 4 tiles from the cliff and sees it.
        var match = WithAnOutpost();
        var worker = WorkerAt(match, 0, 27, 11);
        var wall = match.Walls.Wall!;
        var view = new PlayerView(match, 0);
        Assert.Equal(FogState.Visible, view.FogAt(CliffX, CliffY));

        var tiles = new List<(int X, int Y)>();
        Assert.Equal(BuildProblem.None, view.WallRun(29, 11, 32, 11, tiles));
        Assert.Equal([(29, 11), (30, 11), (32, 11)], tiles);

        var before = MaterialsOf(match, 0);
        match.OrderWall(0, (29, 11), (32, 11), worker);
        match.Simulation.Tick();
        Assert.Equal(before - (3 * wall.Materials), MaterialsOf(match, 0));

        match.RunUntilWallOrderEnds(worker);
        Assert.Equal([(29, 11), (30, 11), (32, 11)], match.WallOf(0));
        Assert.Equal(before - (3 * wall.Materials), MaterialsOf(match, 0));
    }

    /// <summary>A run that begins on a tile the player knows is taken says so, and one that begins in the unexplored does not.</summary>
    [Fact]
    public void ARunIsRefusedOnlyForWhatItsPlayerKnows()
    {
        var match = WithAnOutpost();
        var seeing = new PlayerView(match, 0);
        var tiles = new List<(int X, int Y)>();

        // The cliff alone, unexplored: nothing is known against it.
        Assert.Equal(BuildProblem.None, seeing.WallRun(CliffX, CliffY, CliffX, CliffY, tiles));
        Assert.Equal([(CliffX, CliffY)], tiles);

        // The Outpost's own tiles are known to be taken.
        tiles.Clear();
        Assert.Equal(BuildProblem.SiteNotFree, seeing.WallRun(20, 10, 22, 10, tiles));
        Assert.Empty(tiles);

        // Once the cliff has been seen it is known too.
        WorkerAt(match, 0, 27, 11);
        Assert.Equal(BuildProblem.SiteNotFree, seeing.WallRun(CliffX, CliffY, CliffX, CliffY, tiles));
        Assert.Empty(tiles);
    }
}
