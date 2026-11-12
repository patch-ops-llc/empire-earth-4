using Sim.Combat;
using Sim.Core;
using Sim.Economy;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>How a unit picks its own target, and how far it goes for it (design 2.4, "Picking targets").</summary>
public class TargetPickingTests
{
    private const long WorkerHitPoints = 50_000;

    private static Match Field() => Create(TestMaps.Open(30, 12));

    [Fact]
    public void AUnitLooksOnTheTicksWhereTheTickNumberPlusItsIdIsAMultipleOfFour()
    {
        var match = Field();
        var units = new EntityId[4];
        var workers = new EntityId[4];
        for (var i = 0; i < 4; i++)
        {
            units[i] = match.Spawn(0, Hoplite, $"{(4 * i) + 2}.5", "2.5");
        }

        for (var i = 0; i < 4; i++)
        {
            workers[i] = match.SpawnWorker(1, $"{(4 * i) + 2}.5", "3.5");
        }

        // IDs 1 to 4 look in ticks 3, 2, 1 and 0, and each strikes in the tick it finds its target.
        for (var tick = 0; tick < 4; tick++)
        {
            match.Run(1);
            for (var i = 0; i < 4; i++)
            {
                var looked = 3 - i <= tick;
                Assert.Equal(looked ? workers[i] : EntityId.None, match.UnitOf(units[i]).Target);
                Assert.Equal(looked ? WorkerHitPoints - 8_000 : WorkerHitPoints, match.HitPointsOf(workers[i]));
            }
        }
    }

    [Fact]
    public void AUnitPicksTheNearestEnemy()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "10.5", "5.5");
        var far = match.SpawnWorker(1, "13.5", "5.5");
        var near = match.SpawnWorker(1, "10.5", "7.5");
        match.SpawnWorker(0, "10.5", "4.5");
        match.Run(4);

        Assert.Equal(near, match.UnitOf(peltast).Target);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(far));
    }

    [Fact]
    public void AmongEquallyNearEnemiesTheLowestIdIsPicked()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "10.5", "5.5");
        var first = match.SpawnWorker(1, "13.5", "5.5");
        match.SpawnWorker(1, "10.5", "8.5");
        match.SpawnWorker(1, "7.5", "5.5");
        match.Run(4);

        Assert.Equal(first, match.UnitOf(peltast).Target);
    }

    [Fact]
    public void AcquisitionRangeIsTheLargerOfSightAndWeaponRange()
    {
        var match = Field();

        // Sight 8 against range 5, sight 8 against range 9, and melee with sight 8 and 10.
        Assert.Equal(Fixed.FromInt(8), match.Combat.ProfileOf(0, Chassis(Peltast)).Acquisition);
        Assert.Equal(Fixed.FromInt(9), match.Combat.ProfileOf(0, Chassis(Ballista)).Acquisition);
        Assert.Equal(Fixed.FromInt(8), match.Combat.ProfileOf(0, Chassis(Hoplite)).Acquisition);
        Assert.Equal(Fixed.FromInt(10), match.Combat.ProfileOf(0, Chassis(Hippeus)).Acquisition);

        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");
        var atTheEdge = match.SpawnWorker(1, "10.5", "5.5");
        match.Run(4);
        Assert.Equal(atTheEdge, match.UnitOf(peltast).Target);

        var other = Field();
        var blind = other.Spawn(0, Peltast, "2.5", "5.5");
        other.SpawnWorker(1, "10.5625", "5.5");
        other.Run(8);
        Assert.Equal(EntityId.None, other.UnitOf(blind).Target);
        Assert.Equal(TestMatches.Point("2.5", "5.5"), other.PositionOf(blind));
    }

    [Fact]
    public void AnIdleUnitMovesOnlyAsFarAsItMustAndWalksBackToItsPost()
    {
        var match = Field();
        var post = TestMatches.Point("2.5", "5.5");
        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "9.5", "5.5");
        match.SetHitPoints(worker, 12_000);

        // 7 tiles off, with a range of 5: it walks until the worker is in range and no further.
        for (var tick = 0; match.HitPointsOf(worker) == 12_000; tick++)
        {
            Assert.InRange(tick, 0, 60);
            match.Run(1);
        }

        var stood = match.PositionOf(peltast);
        var distance = FixedVector2.Distance(stood, match.PositionOf(worker));
        Assert.InRange(distance.Raw, (Fixed.FromInt(5) - match.Blueprint(0, Peltast).SpeedPerTick).Raw, Fixed.FromInt(5).Raw);
        Assert.Equal(post, match.UnitOf(peltast).Anchor);
        Assert.True(match.UnitOf(peltast).Engaged);

        // The second shot kills. With nothing left to pick, it goes home.
        match.Run(10);
        Assert.Equal(stood, match.PositionOf(peltast));
        match.Run(60);
        Assert.False(match.IsAlive(worker));
        Assert.Equal(post, match.PositionOf(peltast));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(peltast).Order);
        Assert.False(match.UnitOf(peltast).Engaged);
        Assert.Equal(MoveState.Idle, match.Movers.Get(peltast).State);
    }

    [Fact]
    public void AnIdleUnitNeverGoesFurtherFromItsPostThanItsAcquisitionRange()
    {
        var match = Field();
        var post = TestMatches.Point("2.5", "5.5");
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "8.5", "5.5");
        match.Run(6);
        Assert.Equal(worker, match.UnitOf(hoplite).Target);

        // The worker runs, faster than the Guard walks. The Guard follows to the edge of its circle
        // at most, gives the worker up, and returns.
        match.OrderMove(1, TestMatches.Point("28.5", "5.5"), worker);
        var limit = FixedVector2.SquareRaw(Fixed.FromInt(8));
        var furthest = 0L;
        for (var tick = 0; tick < 300; tick++)
        {
            match.Run(1);
            furthest = Math.Max(furthest, FixedVector2.DistanceSquaredRaw(match.PositionOf(hoplite), post));
        }

        Assert.InRange(furthest, FixedVector2.SquareRaw(Fixed.FromInt(2)), limit);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(worker));
        Assert.Equal(post, match.PositionOf(hoplite));
        Assert.Equal(EntityId.None, match.UnitOf(hoplite).Target);
        Assert.Equal(post, match.UnitOf(hoplite).Anchor);
    }

    [Fact]
    public void AnIdleUnitLeavesATargetItWouldNeedAWayRoundBlockedGroundFor()
    {
        MapDefAndUnits(out var match, out var hoplite, out var worker);
        match.Run(40);

        // The worker is in sight and on ground the Guard can reach, but only round the wall.
        Assert.Equal(EntityId.None, match.UnitOf(hoplite).Target);
        Assert.Equal(TestMatches.Point("3.5", "4.5"), match.PositionOf(hoplite));
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(worker));
    }

    [Fact]
    public void AUnitOnAnAttackMoveTakesTheWayRound()
    {
        MapDefAndUnits(out var match, out var hoplite, out var worker);
        match.SetHitPoints(worker, 8_000);
        match.OrderAttackMove(0, TestMatches.Point("3.5", "0.5"), hoplite);
        match.Run(200);

        Assert.False(match.IsAlive(worker));
        Assert.Equal(TestMatches.Point("3.5", "0.5"), match.PositionOf(hoplite));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
    }

    [Fact]
    public void AMeleeUnitPicksOnlyTargetsOnGroundItCanReach()
    {
        var match = Create(TestMaps.FromRows(
            ".....#....",
            ".....#....",
            ".....#....",
            ".....#....",
            ".....#...."));
        var hoplite = match.Spawn(0, Hoplite, "4.5", "2.5");
        var peltast = match.Spawn(0, Peltast, "4.5", "1.5");
        var worker = match.SpawnWorker(1, "6.5", "2.5");
        match.OrderAttackMove(0, TestMatches.Point("4.5", "4.5"), hoplite);
        match.Run(60);

        Assert.Equal(TestMatches.Point("4.5", "4.5"), match.PositionOf(hoplite));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(EntityId.None, match.UnitOf(hoplite).Target);

        // Shots are not blocked, so the ranged unit beside it picks the worker and hits it.
        Assert.Equal(worker, match.UnitOf(peltast).Target);
        Assert.True(match.HitPointsOf(worker) < WorkerHitPoints);
    }

    [Fact]
    public void AUnitWhoseWaitIsOverAndWhoseTargetIsOutOfReachPicksAgainAtOnce()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var far = match.SpawnWorker(1, "9.5", "5.5");
        match.OrderAttackMove(0, TestMatches.Point("28.5", "5.5"), hoplite);
        match.Run(10);
        Assert.Equal(far, match.UnitOf(hoplite).Target);

        // A nearer enemy appears beside it. Tick 10 is not its turn to look, and it turns on it all the same.
        var position = match.PositionOf(hoplite);
        var near = match.SpawnWorker(1, new FixedVector2(position.X + Fixed.One, position.Y));
        match.Run(1);
        Assert.Equal(near, match.UnitOf(hoplite).Target);
        match.Run(2);
        Assert.Equal(WorkerHitPoints - 8_000, match.HitPointsOf(near));
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(far));
    }

    [Fact]
    public void ATargetInsideTheMinimumRangeIsNotPicked()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "10.5", "5.5");
        var close = match.SpawnWorker(1, "12.5", "5.5");
        var further = match.SpawnWorker(1, "10.5", "9.5");
        match.Run(4);

        Assert.Equal(further, match.UnitOf(ballista).Target);
        Assert.Equal(TestMatches.Point("10.5", "5.5"), match.PositionOf(ballista));
        match.Run(20);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(close));
        Assert.Equal(WorkerHitPoints - 48_000, match.HitPointsOf(further));
    }

    [Fact]
    public void AUnitHitFromBeyondItsAcquisitionRangeDoesNothing()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.25", "5.5");
        var ballista = match.Spawn(1, Ballista, "10.5", "5.5");
        match.Run(100);

        // The two are 8.25 tiles apart. The Ballista sees 8 tiles and so sees the tile the Guard
        // stands on, whose center is 8 tiles away, and it reaches 9. The Guard sees 8, and the
        // center of the Ballista's tile is further than that.
        Assert.True(match.Vision.Sees(1, hoplite));
        Assert.False(match.Vision.Sees(0, ballista));
        Assert.Equal(hoplite, match.UnitOf(ballista).Target);
        Assert.True(match.HitPointsOf(hoplite) < 130_000);
        Assert.Equal(EntityId.None, match.UnitOf(hoplite).Target);
        Assert.Equal(TestMatches.Point("2.25", "5.5"), match.PositionOf(hoplite));
    }

    [Fact]
    public void WorkersNeverActOnTheirOwn()
    {
        var match = Field();
        var worker = match.SpawnWorker(0, "5.5", "5.5");
        var hoplite = match.Spawn(1, Hoplite, "8.5", "5.5");

        // It neither fights nor flees: it stands until it is dead.
        for (var tick = 0; match.IsAlive(worker); tick++)
        {
            Assert.InRange(tick, 0, 200);
            Assert.Equal(TestMatches.Point("5.5", "5.5"), match.PositionOf(worker));
            match.Run(1);
        }

        Assert.Equal(130_000, match.HitPointsOf(hoplite));
    }

    /// <summary>A Guard and an enemy worker 4 tiles apart with a wall between them that ends two tiles above.</summary>
    private static void MapDefAndUnits(out Match match, out EntityId hoplite, out EntityId worker)
    {
        match = Create(TestMaps.FromRows(
            "..........",
            "..........",
            ".....#....",
            ".....#....",
            ".....#....",
            ".....#....",
            ".....#....",
            ".....#...."));
        hoplite = match.Spawn(0, Hoplite, "3.5", "4.5");
        worker = match.SpawnWorker(1, "7.5", "4.5");
    }
}
