using Sim.Core;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.SiegeMatches;
using static Sim.Tests.Walls.WalledMatches;

namespace Sim.Tests.Walls;

/// <summary>
/// Walls keep units out (design 2.5; design 8.4, rows 4, 15 and 22): a wall tile blocks
/// everyone, tiles that touch only at their corners close a line, a melee hit does not cross
/// one, and a shot does.
/// </summary>
public class WallRingTests
{
    private static readonly (int X, int Y) Low = (14, 8);
    private static readonly (int X, int Y) High = (22, 16);

    /// <summary>Eight units round the camp, two on each side.</summary>
    private static EntityId[] Around(Match match, int player, Func<Match, int, string, string, EntityId> spawn) =>
    [
        spawn(match, player, "5.5", "11.5"), spawn(match, player, "5.5", "13.5"),
        spawn(match, player, "29.5", "11.5"), spawn(match, player, "29.5", "13.5"),
        spawn(match, player, "17.5", "2.5"), spawn(match, player, "19.5", "2.5"),
        spawn(match, player, "17.5", "21.5"), spawn(match, player, "19.5", "21.5"),
    ];

    [Fact]
    public void AClosedRingWithItsCornersKeepsTheUnitsOfItsOwnerAndOfOthersOut()
    {
        // Workers of the other player, who have no attack, and units of the owner, who do not
        // attack their own walls: all are ordered to the middle and press on the ring.
        var match = Field();
        match.Ring(1, Low, High);
        var walls = match.WallHitPoints();
        var workers = Around(match, 0, (m, player, x, y) => m.SpawnWorker(player, x, y));
        var own = Around(match, 1, (m, player, x, y) => m.Spawn(player, Hoplite, x, y));
        match.OrderMove(0, Center(18, 12), workers);
        match.OrderMove(1, Center(18, 12), own);

        for (var tick = 0; tick < 600; tick++)
        {
            match.Run(1);
            Assert.All(workers.Concat(own), unit => Assert.False(match.IsInside(unit, Low, High)));
        }

        // They all press toward the one tile nearest the place, and none has a wall in its way.
        Assert.All(own, unit => Assert.Equal(EntityId.None, match.UnitOf(unit).WallInWay));
        Assert.Empty(match.Damaged(walls));
        Assert.Equal(0, match.Breaching.SearchCount);
        Assert.NotEqual(match.Grid.RegionOf(18, 12), match.Grid.RegionOf(5, 12));
    }

    [Fact]
    public void ARingWhoseTilesTouchOnlyAtTheirCornersKeepsUnitsOut()
    {
        // The tiles 5 steps from (18, 12): a diamond, every joint of it a corner.
        var match = Field();
        match.Diamond(1, (18, 12), 5);
        var walls = match.WallHitPoints();
        Assert.Equal(20, walls.Count);
        Assert.NotEqual(match.Grid.RegionOf(18, 12), match.Grid.RegionOf(5, 12));

        var workers = Around(match, 0, (m, player, x, y) => m.SpawnWorker(player, x, y));
        var own = Around(match, 1, (m, player, x, y) => m.Spawn(player, Hoplite, x, y));
        match.OrderMove(0, Center(18, 12), workers);
        match.OrderMove(1, Center(18, 12), own);

        for (var tick = 0; tick < 600; tick++)
        {
            match.Run(1);
            Assert.All(workers.Concat(own), unit =>
            {
                var (x, y) = match.TileOf(unit);
                Assert.True(Math.Abs(x - 18) + Math.Abs(y - 12) > 5, $"A unit stands on tile ({x}, {y}).");
            });
        }

        Assert.Empty(match.Damaged(walls));
        Assert.Equal(0, match.Breaching.SearchCount);
    }

    [Fact]
    public void AUnitBreaksIntoARingOfCornersThroughATileAndNotBetweenTwo()
    {
        var match = Field();
        match.Diamond(1, (18, 12), 5);
        match.WeakenWalls(24);
        var walls = match.WallHitPoints();
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        match.OrderMove(0, Center(18, 12), unit);

        match.Run(1);
        var inWay = match.UnitOf(unit).WallInWay;
        Assert.Equal(match.WallOn(13, 12), inWay);
        while (match.IsAlive(inWay))
        {
            Assert.True(match.World.Tick < 600, "The wall tile still stands.");
            match.Run(1);
            var (x, y) = match.TileOf(unit);
            Assert.True(Math.Abs(x - 18) + Math.Abs(y - 12) > 5, $"The unit stands on tile ({x}, {y}).");
        }

        match.Run(200);
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Equal([inWay], match.Damaged(walls));
    }

    /// <summary>
    /// Design 8.4, row 4. A line of wall from the north edge to the south, each tile touching the
    /// next at a corner: tile (4 + y, y) for every y. Two enemies stand on its two sides at the
    /// joint of (15, 11) and (16, 12), which is the point (16, 12), well within melee reach of
    /// each other.
    /// </summary>
    [Theory]
    [InlineData("15.8", "12.2", "16.2", "11.8")]
    [InlineData("15.7", "12.2", "16.2", "11.9")]
    [InlineData("15.9", "12.3", "16.4", "11.9")]
    public void AMeleeHitDoesNotCrossAWallNorTheCornerWhereTwoTilesTouch(string x, string y, string enemyX, string enemyY)
    {
        // Without the wall the two fight.
        var open = Field();
        var first = open.Spawn(0, Hoplite, x, y);
        var second = open.Spawn(1, Hoplite, enemyX, enemyY);
        Assert.Contains(open.RunAndCollect(60), reported => reported.Kind == SimEventKind.MeleeHit);

        var match = Field();
        for (var row = 0; row < 24; row++)
        {
            match.WallTile(1, 4 + row, row);
        }

        var walls = match.WallHitPoints();
        first = match.Spawn(0, Hoplite, x, y);
        second = match.Spawn(1, Hoplite, enemyX, enemyY);
        Assert.NotEqual(match.Grid.RegionOf(match.PositionOf(first)), match.Grid.RegionOf(match.PositionOf(second)));
        var hitPoints = (match.HitPointsOf(first), match.HitPointsOf(second));

        var events = match.RunAndCollect(200);

        Assert.DoesNotContain(events, reported => reported.Kind == SimEventKind.MeleeHit);
        Assert.Equal(hitPoints, (match.HitPointsOf(first), match.HitPointsOf(second)));
        Assert.Empty(match.Damaged(walls));
    }

    /// <summary>Design 8.4, row 15: nothing stops a shot. A unit that shoots hits an enemy on the far side of a wall.</summary>
    [Fact]
    public void AShotCrossesAWall()
    {
        var match = Field();
        match.Ring(1, Low, High);
        var walls = match.WallHitPoints();
        var peltast = match.Spawn(0, Peltast, "12.5", "12.5");
        var inside = match.SpawnWorker(1, "16.5", "12.5");

        var events = match.RunAndCollect(400);

        Assert.Contains(events, reported => reported.Kind == SimEventKind.ShotHit && reported.Entity == inside);
        Assert.False(match.IsAlive(inside));
        Assert.Equal(TestMatches.Point("12.5", "12.5"), match.PositionOf(peltast));
        Assert.Empty(match.Damaged(walls));
    }
}
