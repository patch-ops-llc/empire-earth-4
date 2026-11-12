using Sim.Combat;
using Sim.Core;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.SiegeMatches;
using static Sim.Tests.Walls.WalledMatches;

namespace Sim.Tests.Walls;

/// <summary>
/// The wall rule (design 2.5 and 5.3; design 8.4, rows 3 and 41): a unit ordered to a place it
/// cannot reach attacks the nearest enemy wall tile in its way, the first on the way there
/// through enemy walls, and goes on with its order once that tile is destroyed.
/// </summary>
/// <remarks>
/// The camp of these tests is a ring of player 1's wall on the edge of the box from tile (14, 8)
/// to tile (22, 16), so the ground inside is tiles 15 to 21 by 9 to 15 and its middle is
/// (18.5, 12.5). Units of player 0 come from the west along y = 12.5, so the tile in their way
/// is (14, 12).
/// </remarks>
public class WallRuleTests
{
    private static readonly (int X, int Y) Low = (14, 8);
    private static readonly (int X, int Y) High = (22, 16);

    private static Match WithCamp(int wallHitPoints = 24)
    {
        var match = Field();
        match.Ring(1, Low, High);
        match.WeakenWalls(wallHitPoints);
        return match;
    }

    [Theory]
    [InlineData(Hoplite, SimEventKind.MeleeHit)]
    [InlineData(Peltast, SimEventKind.ShotHit)]
    public void AUnitOnAMoveOrderBreaksInAtTheTileInItsWayAndGoesOn(string chassis, SimEventKind hit)
    {
        var match = WithCamp();
        var unit = match.Spawn(0, chassis, "5.5", "12.5");
        var inWay = match.WallOn(14, 12);
        var walls = match.WallHitPoints();
        match.OrderMove(0, Center(18, 12), unit);

        // It has its wall with the order, and keeps the order while it breaks in.
        match.Run(1);
        Assert.Equal((UnitOrder.Move, inWay), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));
        Assert.Equal(Center(18, 12), match.UnitOf(unit).Goal);

        var hits = new List<SimEvent>();
        while (match.IsAlive(inWay))
        {
            Assert.True(match.World.Tick < 600, "The wall tile still stands.");
            match.Run(1);
            hits.AddRange(match.EventsOf(hit));
            Assert.Equal(UnitOrder.Move, match.UnitOf(unit).Order);
            Assert.False(match.IsInside(unit, Low, High));
        }

        // Only that tile was attacked, and by this unit.
        Assert.NotEmpty(hits);
        Assert.All(hits, reported => Assert.Equal(inWay, reported.Entity));
        Assert.Equal([inWay], match.Damaged(walls));

        // Then it goes on to the place it was ordered to, and is idle there.
        match.Run(200);
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Equal((UnitOrder.Idle, EntityId.None), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));
        Assert.True(match.Stands(unit));
        Assert.Equal([inWay], match.Damaged(walls));
    }

    [Fact]
    public void AWorkerHasNoAttackAndGoesToTheNearestTileItCanReach()
    {
        var match = WithCamp();
        var worker = match.SpawnWorker(0, "5.5", "12.5");
        var walls = match.WallHitPoints();
        match.OrderMove(0, Center(18, 12), worker);

        match.Run(400);

        // Four tiles outside the ring are equally near the place; the one nearest the worker wins.
        Assert.Equal(Center(13, 12), match.PositionOf(worker));
        Assert.True(match.Stands(worker));
        Assert.Empty(match.Damaged(walls));
        Assert.Equal(0, match.Breaching.SearchCount);
    }

    [Fact]
    public void OnesOwnWallsAreNotInTheWay()
    {
        var match = Field();
        match.Ring(0, Low, High);
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var walls = match.WallHitPoints();
        match.OrderMove(0, Center(18, 12), unit);

        match.Run(1);
        Assert.Equal(EntityId.None, match.UnitOf(unit).WallInWay);
        match.Run(400);

        // Ground closed off by the player's own walls is a place it cannot reach, and no more.
        Assert.Equal(Center(13, 12), match.PositionOf(unit));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
        Assert.Empty(match.Damaged(walls));
        Assert.Equal(0, match.Breaching.SearchCount);
    }

    [Fact]
    public void AnOrderOntoABuildingInsideTheWallsLeadsThroughThem()
    {
        // The place is on a Barracks that covers tiles 17 to 19 by 11 to 13. With the walls out of
        // the way the unit would go to the nearest tile beside it, and that is where it goes.
        var match = WithCamp();
        match.PlaceStructure(1, Barracks, 17, 11);
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var inWay = match.WallOn(14, 12);
        match.OrderMove(0, Center(18, 12), unit);

        match.Run(1);
        Assert.Equal(inWay, match.UnitOf(unit).WallInWay);
        Assert.Equal(Center(16, 12), match.UnitOf(unit).Goal);

        match.Run(600);
        Assert.False(match.IsAlive(inWay));
        Assert.Equal(Center(16, 12), match.PositionOf(unit));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    [Fact]
    public void AnAttackMoveBreaksInAndThenFightsWhatIsInside()
    {
        var match = WithCamp();
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var inside = match.SpawnWorker(1, "20.5", "12.5");
        var full = match.HitPointsOf(inside);
        var inWay = match.WallOn(14, 12);
        var walls = match.WallHitPoints();
        match.OrderAttackMove(0, Center(18, 12), unit);

        match.Run(1);
        Assert.Equal((UnitOrder.AttackMove, inWay), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));
        while (match.IsAlive(inWay))
        {
            Assert.True(match.World.Tick < 600, "The wall tile still stands.");
            match.Run(1);

            // A melee unit picks nothing on ground it cannot reach, so it has only the wall.
            Assert.Equal((UnitOrder.AttackMove, EntityId.None), (match.UnitOf(unit).Order, match.UnitOf(unit).Target));
            Assert.Equal(full, match.HitPointsOf(inside));
        }

        match.Run(400);
        Assert.False(match.IsAlive(inside));
        Assert.Equal([inWay], match.Damaged(walls));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
        Assert.True(match.IsInside(unit, Low, High));
    }

    [Fact]
    public void AUnitBreakingInOnAnAttackMoveTurnsOnAnEnemyAndComesBackToTheWall()
    {
        var match = WithCamp(wallHitPoints: 60);
        var unit = match.Spawn(0, Hoplite, "12.5", "12.5");
        var inWay = match.WallOn(14, 12);
        match.OrderAttackMove(0, Center(18, 12), unit);
        match.Run(60);
        var afterFirstHits = match.HitPointsOf(inWay);
        Assert.True(afterFirstHits < 60_000);

        // An enemy on its own side of the wall, within its sight: it leaves the wall for it.
        var passerBy = match.SpawnWorker(1, "10.5", "9.5");
        var events = match.RunAndCollect(200);
        Assert.False(match.IsAlive(passerBy));
        var death = events.FindIndex(reported => reported.Kind == SimEventKind.MeleeHit && reported.Entity == passerBy);
        Assert.True(death >= 0);

        // Then it goes back to the same tile and breaks it.
        Assert.Equal(inWay, match.UnitOf(unit).WallInWay);
        match.Run(1200);
        Assert.False(match.IsAlive(inWay));
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    [Fact]
    public void AnAttackOrderBreaksTheWallBetweenAMeleeUnitAndItsTarget()
    {
        var match = WithCamp();
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var target = match.SpawnWorker(1, "18.5", "12.5");
        var inWay = match.WallOn(14, 12);
        var walls = match.WallHitPoints();
        match.OrderAttack(0, target, unit);

        match.Run(1);
        var ordered = match.UnitOf(unit);
        Assert.Equal((UnitOrder.Attack, target, inWay), (ordered.Order, ordered.Target, ordered.WallInWay));
        while (match.IsAlive(inWay))
        {
            Assert.True(match.World.Tick < 600, "The wall tile still stands.");
            match.Run(1);
            Assert.Equal((UnitOrder.Attack, target), (match.UnitOf(unit).Order, match.UnitOf(unit).Target));
        }

        // The order was never swapped for the wall: with the tile gone it goes after its target.
        match.Run(400);
        Assert.False(match.IsAlive(target));
        Assert.Equal([inWay], match.Damaged(walls));
        Assert.Equal((UnitOrder.Idle, EntityId.None), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));
    }

    [Fact]
    public void AUnitThatCanShootItsTargetOverTheWallLeavesTheWallAlone()
    {
        // The target stands 3 tiles from the nearest tile outside, and a Peltast shoots 5.
        var match = WithCamp();
        var unit = match.Spawn(0, Peltast, "5.5", "12.5");
        var target = match.SpawnWorker(1, "16.5", "12.5");
        var walls = match.WallHitPoints();
        match.OrderAttack(0, target, unit);

        for (var tick = 0; tick < 400 && match.IsAlive(target); tick++)
        {
            match.Run(1);
            Assert.Equal(EntityId.None, match.UnitOf(unit).WallInWay);
        }

        Assert.False(match.IsAlive(target));
        Assert.Empty(match.Damaged(walls));
        Assert.False(match.IsInside(unit, Low, High));
        Assert.Equal(0, match.Breaching.SearchCount);
    }

    [Fact]
    public void AUnitThatCannotShootFarEnoughBreaksTheWallFirst()
    {
        // A camp so wide that its middle is 8 tiles from the nearest tile outside.
        var match = Field();
        match.Ring(1, (10, 4), (26, 20));
        match.WeakenWalls(24);
        var unit = match.Spawn(0, Peltast, "3.5", "12.5");
        var target = match.SpawnWorker(1, "18.5", "12.5");
        var inWay = match.WallOn(10, 12);
        var walls = match.WallHitPoints();
        match.OrderAttack(0, target, unit);

        match.Run(1);
        Assert.Equal(inWay, match.UnitOf(unit).WallInWay);
        match.Run(800);

        Assert.False(match.IsAlive(inWay));
        Assert.False(match.IsAlive(target));
        Assert.Equal([inWay], match.Damaged(walls));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    [Fact]
    public void OnAnAttackMoveATargetBehindTheWallsDoesNotHoldAUnitThatShoots()
    {
        // The unit passes north of a wide camp. It sees the worker inside, 6 tiles from the
        // nearest tile outside, and shoots 5: it cannot bring it into range, and its own place
        // is not behind the walls.
        var match = Field();
        match.Ring(1, (10, 4), (26, 20));
        var unit = match.Spawn(0, Peltast, "3.5", "2.5");
        var inside = match.SpawnWorker(1, "18.5", "9.5");
        var full = match.HitPointsOf(inside);
        var walls = match.WallHitPoints();
        match.OrderAttackMove(0, Center(29, 2), unit);

        var picked = false;
        for (var tick = 0; tick < 500; tick++)
        {
            match.Run(1);
            picked |= match.UnitOf(unit).Target == inside;
            Assert.Equal(EntityId.None, match.UnitOf(unit).WallInWay);
        }

        Assert.True(picked);
        Assert.Equal(Center(29, 2), match.PositionOf(unit));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
        Assert.Equal(full, match.HitPointsOf(inside));
        Assert.Empty(match.Damaged(walls));
    }

    [Fact]
    public void OnAnAttackMoveIntoTheWallsAUnitThatShootsBreaksInAndShootsWhatItFinds()
    {
        var match = Field();
        match.Ring(1, (10, 4), (26, 20));
        match.WeakenWalls(24);
        var unit = match.Spawn(0, Peltast, "3.5", "12.5");
        var inside = match.SpawnWorker(1, "20.5", "12.5");
        var inWay = match.WallOn(10, 12);
        var walls = match.WallHitPoints();
        match.OrderAttackMove(0, Center(18, 12), unit);

        match.Run(1);
        Assert.Equal(inWay, match.UnitOf(unit).WallInWay);
        match.Run(900);

        Assert.Equal([inWay], match.Damaged(walls));
        Assert.False(match.IsAlive(inside));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    [Fact]
    public void AGroupOrderedTogetherBreaksInAtOneTile()
    {
        var match = WithCamp(wallHitPoints: 100);
        var units = new List<EntityId>();
        foreach (var y in new[] { "11.5", "12.5", "13.5" })
        {
            units.Add(match.Spawn(0, Hoplite, "5.5", y));
            units.Add(match.Spawn(0, Hoplite, "6.5", y));
        }

        var walls = match.WallHitPoints();
        match.OrderMove(0, Center(18, 12), [.. units]);
        match.Run(1);

        // One search for the six of them: each can walk straight to the first.
        var inWay = match.UnitOf(units[0]).WallInWay;
        Assert.NotEqual(EntityId.None, inWay);
        Assert.All(units, unit => Assert.Equal(inWay, match.UnitOf(unit).WallInWay));
        Assert.Equal(1, match.Breaching.SearchCount);

        match.Run(1500);
        Assert.Equal([inWay], match.Damaged(walls));
        Assert.All(units, unit =>
        {
            Assert.True(match.IsInside(unit, Low, High));
            Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
        });
    }

    [Fact]
    public void WhenTheWayOpensElsewhereTheUnitGoesThroughThere()
    {
        var match = WithCamp(wallHitPoints: 400);
        var unit = match.Spawn(0, Hoplite, "12.5", "12.5");
        var inWay = match.WallOn(14, 12);
        match.OrderMove(0, Center(18, 12), unit);
        match.Run(100);
        var left = match.HitPointsOf(inWay);
        Assert.InRange(left, 1, 399_999);

        // Another tile of the ring falls, to someone else.
        match.SetHitPoints(match.WallOn(14, 9), 0);
        match.Run(1 + match.Combat.LookInterval);
        Assert.Equal((UnitOrder.Move, EntityId.None), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));
        Assert.False(match.Stands(unit));

        match.Run(300);
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Equal(left, match.HitPointsOf(inWay));
    }

    [Fact]
    public void BehindTheFirstWallTheNextOneIsBrokenToo()
    {
        var match = WithCamp();
        match.Ring(1, (16, 10), (20, 14));
        match.WeakenWalls(24);
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var outer = match.WallOn(14, 12);
        var inner = match.WallOn(16, 12);
        var walls = match.WallHitPoints();
        match.OrderMove(0, Center(18, 12), unit);

        match.Run(1);
        Assert.Equal(outer, match.UnitOf(unit).WallInWay);
        while (match.IsAlive(outer))
        {
            Assert.True(match.World.Tick < 600, "The outer tile still stands.");
            match.Run(1);
        }

        // In the tick after the tile is gone the unit has the next one, and still its order.
        match.Run(1);
        Assert.Equal((UnitOrder.Move, inner), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));

        match.Run(600);
        Assert.Equal([outer, inner], match.Damaged(walls));
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    [Fact]
    public void AWallLaidAcrossTheWayOfAUnitThatIsWalkingIsBroken()
    {
        // A cliff from edge to edge with one gap, at (16, 12).
        var rows = Enumerable.Range(0, 24).Select(y => y == 12 ? new string('.', 32) : new string('.', 16) + "#" + new string('.', 15));
        var match = Create(TestMaps.FromRows([.. rows]));
        var unit = match.Spawn(0, Hoplite, "3.5", "12.5");
        match.OrderMove(0, Center(25, 12), unit);
        match.Run(20);
        Assert.Equal(EntityId.None, match.UnitOf(unit).WallInWay);

        // Player 1 closes the gap while the unit is on its way.
        var gap = match.WallTile(1, 16, 12);
        match.WeakenWalls(24);
        match.Run(match.Combat.LookInterval);
        Assert.Equal((UnitOrder.Move, gap), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));

        match.Run(600);
        Assert.False(match.IsAlive(gap));
        Assert.Equal(Center(25, 12), match.PositionOf(unit));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    [Fact]
    public void UnitsThatCannotShareAWallFindTheirsWithinTheLimitOfAnOrderAndTheRestSoonAfter()
    {
        // Ten units in stalls: a cliff stands between each two, so none walks straight to another.
        var rows = Enumerable.Range(0, 24).Select(y => y % 2 == 1 ? "..###" + new string('.', 35) : new string('.', 40));
        var match = Create(TestMaps.FromRows([.. rows]));
        match.Ring(1, (20, 6), (30, 16));
        var units = Enumerable.Range(0, 10).Select(i => match.Spawn(0, Hoplite, "3.5", $"{(2 * i) + 2}.5")).ToArray();
        match.Simulation.Commands.Enqueue(0, CombatMatches.MoveCommand(0, Center(25, 11), units));

        // The order runs its eight searches. The attacks of the same tick may find more.
        match.Run(1);
        Assert.InRange(match.Breaching.SearchCount, WallRule.MaxSearchesPerOrder, 10);
        match.Run(match.Combat.LookInterval);

        Assert.Equal(10, match.Breaching.SearchCount);
        Assert.All(units, unit => Assert.NotEqual(EntityId.None, match.UnitOf(unit).WallInWay));
    }
}
