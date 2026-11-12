using Sim.Combat;
using Sim.Core;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// The blast of an Artillery shell (design 2.4; design 8.4, rows 15 and 34): who is inside it,
/// what each takes, where it lands, and what it reports.
/// </summary>
/// <remarks>
/// A Ballista fires one shell of 48 every 4 seconds, with a blast radius of 1.5 tiles, at 10 tiles
/// per second. In most of these tests a wall stands between the Ballista and the enemies, so
/// that those who would fight back cannot come at it and stay where the test put them. A shell
/// does not mind the wall.
/// </remarks>
public class BlastTests
{
    private const long WorkerHitPoints = 50_000;
    private const long Shell = 48_000;

    /// <summary>24 by 12, with a wall from top to bottom in column 5. The Ballista stands left of it.</summary>
    private static Match Field(params (int X, int Y)[] moreWalls)
    {
        var rows = new char[12][];
        for (var y = 0; y < rows.Length; y++)
        {
            rows[y] = new string('.', 24).ToCharArray();
            rows[y][5] = '#';
        }

        foreach (var (x, y) in moreWalls)
        {
            rows[y][x] = '#';
        }

        return Create(TestMaps.FromRows([.. rows.Select(row => new string(row))]));
    }

    /// <summary>Runs until a shell lands, and returns its event. The events of that tick stay on the list.</summary>
    private static SimEvent RunToBlast(Match match)
    {
        for (var tick = 0; tick < 400; tick++)
        {
            match.Run(1);
            var blasts = match.EventsOf(SimEventKind.Blast);
            if (blasts.Length > 0)
            {
                return Assert.Single(blasts);
            }
        }

        throw new Xunit.Sdk.XunitException("No shell landed in 400 ticks.");
    }

    [Fact]
    public void AShellDamagesEveryEnemyWhoseCenterIsWithinItsRadiusInFullAndNoneBeyond()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var target = match.SpawnWorker(1, "9.5", "5.5");
        var beside = match.SpawnWorker(1, "10.5", "5.5");
        var onTheEdge = match.SpawnWorker(1, "11", "5.5");
        var insideDiagonally = match.SpawnWorker(1, "10.56", "6.56");
        var pastTheEdge = match.SpawnWorker(1, "11.0001", "5.5");
        var outsideDiagonally = match.SpawnWorker(1, "10.57", "6.57");
        var onTheEdgeOfTheSquare = match.SpawnWorker(1, "8", "4");
        match.OrderAttack(0, target, ballista);

        // 7 tiles at 10 tiles per second: fired in tick 0, it lands in tick 14.
        match.Run(14);
        Assert.Equal(1, match.Shots.Count);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(target));
        match.Run(1);
        Assert.Equal(0, match.Shots.Count);

        // No falloff: the full shell at the center, beside it, and at exactly the radius.
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(target));
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(beside));
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(onTheEdge));
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(insideDiagonally));

        // A hard edge, and a circle: nothing a hair outside, and nothing in the corner of the square round it.
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(pastTheEdge));
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(outsideDiagonally));
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(onTheEdgeOfTheSquare));
        Assert.Equal(Squared("1.5"), match.DistanceSquared(target, onTheEdge));
    }

    [Fact]
    public void EachEnemyInTheBlastTakesTheShellWithItsOwnCounterMultiplier()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var hoplite = match.Spawn(1, Hoplite, "9.5", "5.5");
        var knight = match.Spawn(1, "knight", "10.5", "5.5");
        var peltast = match.Spawn(1, Peltast, "10.5", "6.5");
        var worker = match.SpawnWorker(1, "9.5", "4.5");
        var before = new[] { hoplite, knight, peltast, worker }.ToDictionary(unit => unit, unit => match.HitPointsOf(unit));
        match.OrderAttack(0, hoplite, ballista);
        RunToBlast(match);

        // Nobody has moved: the wall keeps the melee units away, and the Ballista is further off than the Peltast looks.
        Assert.Equal(TestMatches.Point("10.5", "6.5"), match.PositionOf(peltast));
        Assert.Equal(TestMatches.Point("10.5", "5.5"), match.PositionOf(knight));

        // Artillery counts half against Cavalry/Armor (design 2.4): one shell, 48 to the others and 24 to the Knight.
        Assert.Equal(Shell, before[hoplite] - match.HitPointsOf(hoplite));
        Assert.Equal(Shell / 2, before[knight] - match.HitPointsOf(knight));
        Assert.Equal(Shell, before[peltast] - match.HitPointsOf(peltast));
        Assert.Equal(Shell, before[worker] - match.HitPointsOf(worker));
    }

    [Fact]
    public void TheKnightTakesHalfAlsoWhenItIsTheTarget()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var knight = match.Spawn(1, "knight", "9.5", "5.5");
        var hoplite = match.Spawn(1, Hoplite, "10.5", "5.5");
        var knightBefore = match.HitPointsOf(knight);
        var hopliteBefore = match.HitPointsOf(hoplite);
        match.OrderAttack(0, knight, ballista);
        RunToBlast(match);

        // The multiplier is each one's own, not the target's.
        Assert.Equal(Shell / 2, knightBefore - match.HitPointsOf(knight));
        Assert.Equal(Shell, hopliteBefore - match.HitPointsOf(hoplite));
    }

    [Fact]
    public void ABlastNeverTouchesItsOwnersSide()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var target = match.SpawnWorker(1, "9.5", "5.5");
        var ownWorker = match.SpawnWorker(0, "9.5", "5.5");
        var ownHoplite = match.Spawn(0, Hoplite, "10.5", "5.5");
        var hopliteBefore = match.HitPointsOf(ownHoplite);
        match.SetHitPoints(target, 1_000_000);
        match.OrderAttack(0, target, ballista);
        var blast = RunToBlast(match);

        // The player's own Guard stands a tile from the target, and its own worker on the same spot.
        Assert.Equal(match.PositionOf(target), blast.Position);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(ownWorker));
        Assert.Equal(hopliteBefore, match.HitPointsOf(ownHoplite));
        Assert.Equal(target, Assert.Single(match.EventsOf(SimEventKind.ShotHit)).Entity);
    }

    [Fact]
    public void ABlastReachesOnlyWhatTheFirersLineCanHit()
    {
        var match = Field();
        var air = CombatMatches.Data.Chassis.First(chassis => chassis.Line == Line("air")).Id;
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var target = match.SpawnWorker(1, "9.5", "5.5");
        var flyer = match.Spawn(1, air, "9.5", "5.5");
        var flyerBefore = match.HitPointsOf(flyer);
        match.OrderAttack(0, target, ballista);
        RunToBlast(match);

        // Artillery cannot hit Air, so its blast passes a flyer by, even one at its center.
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(target));
        Assert.Equal(flyerBefore, match.HitPointsOf(flyer));
        Assert.Equal(target, Assert.Single(match.EventsOf(SimEventKind.ShotHit)).Entity);
    }

    [Fact]
    public void NothingBlocksABlast()
    {
        // The wall of the field is between the Ballista and its target, and another piece of wall
        // between the target and the worker beside it.
        var match = Field((10, 4), (10, 5), (10, 6));
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var target = match.SpawnWorker(1, "9.5", "5.5");
        var behindTheWall = match.SpawnWorker(1, "11", "5.5");
        match.OrderAttack(0, target, ballista);
        RunToBlast(match);

        Assert.False(match.Grid.IsSegmentClear(match.PositionOf(target), match.PositionOf(behindTheWall)));
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(target));
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(behindTheWall));
    }

    [Fact]
    public void TheBlastIsCenteredWhereTheTargetIsOnImpact()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "2.5", "2.5");
        var target = match.SpawnWorker(1, "11.5", "2.5");
        var whereItWas = match.SpawnWorker(1, "12.5", "2.5");
        var whereItGoes = match.SpawnWorker(1, "11.5", "6.5");
        var start = match.PositionOf(target);
        match.OrderAttack(0, target, ballista);
        match.Run(1);
        Assert.Equal(1, match.Shots.Count);

        // The target walks off while the shell is in the air, and the shell turns after it.
        match.OrderMove(1, TestMatches.Point("11.5", "9.5"), target);
        var blast = RunToBlast(match);

        Assert.Equal(match.PositionOf(target), blast.Position);
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(target));

        // The worker beside where the target stood when the shell was fired is out of the blast by now.
        Assert.True(FixedVector2.DistanceSquaredRaw(start, match.PositionOf(whereItWas)) <= Squared("1.5"));
        Assert.True(match.DistanceSquared(target, whereItWas) > Squared("1.5"));
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(whereItWas));

        // The one the target has walked up to was out of it then and is in it now.
        Assert.True(FixedVector2.DistanceSquaredRaw(start, match.PositionOf(whereItGoes)) > Squared("1.5"));
        Assert.True(match.DistanceSquared(target, whereItGoes) <= Squared("1.5"));
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(whereItGoes));
    }

    [Fact]
    public void AShellWhoseTargetHasDiedLandsOnItsLastPosition()
    {
        var match = Field();
        var near = match.Spawn(0, Ballista, "3.5", "5.5");
        var far = match.Spawn(0, Ballista, "1.5", "5.5");
        var target = match.SpawnWorker(1, "9.5", "5.5");
        var beside = match.SpawnWorker(1, "10.5", "5.5");
        var away = match.SpawnWorker(1, "9.5", "8.5");
        var where = match.PositionOf(target);
        match.SetHitPoints(target, Shell);
        match.SetHitPoints(beside, 200_000);
        match.OrderAttack(0, target, near, far);

        // Both fire in tick 0. The nearer shell lands first and kills the target.
        var first = RunToBlast(match);
        Assert.Equal(where, first.Position);
        Assert.Equal(200_000 - Shell, match.HitPointsOf(beside));
        Assert.False(match.IsAlive(target));
        Assert.Equal(1, match.Shots.Count);
        Assert.Equal(target, match.Shots.ValueAt(0).Target);
        Assert.Equal(where, match.Shots.ValueAt(0).Aim);

        // The other is not lost. It flies on to where the target last stood and lands there.
        var lost = 0;
        SimEvent second = default;
        for (var tick = 0; tick < 20 && second.Kind == SimEventKind.None; tick++)
        {
            match.Run(1);
            lost += match.EventsOf(SimEventKind.ShotLost).Length;
            second = match.EventsOf(SimEventKind.Blast).FirstOrDefault();
        }

        Assert.Equal(0, lost);
        Assert.Equal(where, second.Position);
        Assert.Equal(target, second.Other);
        Assert.Equal(0, match.Shots.Count);
        Assert.Equal(200_000 - (2 * Shell), match.HitPointsOf(beside));
        Assert.Equal(beside, Assert.Single(match.EventsOf(SimEventKind.ShotHit)).Entity);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(away));
    }

    [Fact]
    public void AShellThatLandsWhereNobodyIsLeftDamagesNothing()
    {
        var match = Field();
        var near = match.Spawn(0, Ballista, "3.5", "5.5");
        var far = match.Spawn(0, Ballista, "1.5", "5.5");
        var target = match.SpawnWorker(1, "9.5", "5.5");
        var away = match.SpawnWorker(1, "9.5", "8.5");
        match.SetHitPoints(target, Shell);
        match.OrderAttack(0, target, near, far);
        RunToBlast(match);
        Assert.False(match.IsAlive(target));

        var blast = RunToBlast(match);

        Assert.Equal(TestMatches.Point("9.5", "5.5"), blast.Position);
        Assert.Empty(match.EventsOf(SimEventKind.ShotHit));
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(away));
        Assert.Equal(0, match.Shots.Count);
    }

    [Fact]
    public void AShellStillLandsWhenItsAttackerHasDied()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var target = match.SpawnWorker(1, "9.5", "5.5");
        var beside = match.SpawnWorker(1, "10.5", "5.5");
        match.OrderAttack(0, target, ballista);
        match.Run(1);
        match.SetHitPoints(ballista, 0);
        match.Run(1);
        Assert.False(match.IsAlive(ballista));

        var blast = RunToBlast(match);

        Assert.Equal(0, blast.Player);
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(target));
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(beside));
    }

    [Fact]
    public void TheRadiusIsTheOneTheFirerHadWhenItFired()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var target = match.SpawnWorker(1, "9.5", "5.5");
        var twoTilesOff = match.SpawnWorker(1, "11.5", "5.5");
        var pastTheWiderEdge = match.SpawnWorker(1, "9.5", "7.7501");
        match.SetHitPoints(target, 1_000_000);
        match.SetHitPoints(twoTilesOff, 1_000_000);
        Assert.Equal("1.5", match.Blueprint(0, Ballista).BlastRadius.ToString());
        match.OrderAttack(0, target, ballista);
        match.Run(1);
        Assert.Equal(Fraction.Parse("1.5").ToFixed(), match.Shots.ValueAt(0).Blast);

        // The firer's Firepower rises while the shell is in the air, which gives its blueprint the
        // blast radius bonus (design 2.3). The shell keeps the radius it was fired with.
        match.SetTiers(0, 3, 1, 1, 1, 1, 1);
        Assert.Equal("2.25", match.Blueprint(0, Ballista).BlastRadius.ToString());
        var blast = RunToBlast(match);
        Assert.Equal(Fraction.Parse("1.5").ToFixed().Raw, blast.Amount);
        Assert.Equal(1_000_000, match.HitPointsOf(twoTilesOff));

        // The next shell has the wider blast, and more damage with the tiers: it reaches the worker
        // two tiles off, and still nothing past its own edge.
        var targetBefore = match.HitPointsOf(target);
        blast = RunToBlast(match);
        Assert.Equal(Fraction.Parse("2.25").ToFixed().Raw, blast.Amount);
        var damage = targetBefore - match.HitPointsOf(target);
        Assert.True(damage > Shell);
        Assert.Equal(1_000_000 - damage, match.HitPointsOf(twoTilesOff));
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(pastTheWiderEdge));
    }

    [Fact]
    public void AnEnemyInsideTheMinimumRangeIsNotFiredAtAndABlastBesideItStillReachesIt()
    {
        var match = Create(TestMaps.Open(24, 12));
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var close = match.SpawnWorker(1, "4.5", "5.5");
        var atTheMinimum = match.SpawnWorker(1, "5.5", "5.5");

        // Left to itself the Ballista picks the nearest enemy it can fire at, which is not the nearest enemy.
        var blast = RunToBlast(match);

        Assert.Equal(atTheMinimum, blast.Other);
        Assert.Equal(atTheMinimum, match.UnitOf(ballista).Target);
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(atTheMinimum));

        // The one inside the minimum range was never a target. It stands a tile from the one that was.
        Assert.Equal(WorkerHitPoints - Shell, match.HitPointsOf(close));
    }

    [Fact]
    public void AShellReportsItsBlastAndThenAHitForEachEnemyReachedInTheOrderOfTheirIds()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var third = match.SpawnWorker(1, "10.5", "5.5");
        var knight = match.Spawn(1, "knight", "9.5", "6.5");
        var target = match.SpawnWorker(1, "9.5", "5.5");
        match.SpawnWorker(1, "13.5", "5.5");
        match.OrderAttack(0, target, ballista);
        match.Run(1);
        var shell = match.Shots.EntityAt(0);
        var blast = RunToBlast(match);

        Assert.Equal(new SimEvent(SimEventKind.Blast, match.World.Tick - 1, 0, shell, target, match.PositionOf(target), 98_304), blast);
        Assert.Equal(
            [
                new SimEvent(SimEventKind.ShotHit, blast.Tick, 0, third, shell, match.PositionOf(third), Shell),
                new SimEvent(SimEventKind.ShotHit, blast.Tick, 0, knight, shell, match.PositionOf(knight), Shell / 2),
                new SimEvent(SimEventKind.ShotHit, blast.Tick, 0, target, shell, match.PositionOf(target), Shell),
            ],
            match.EventsOf(SimEventKind.ShotHit));
        Assert.Equal(
            [SimEventKind.Blast, SimEventKind.ShotHit, SimEventKind.ShotHit, SimEventKind.ShotHit],
            match.Simulation.Events.Select(reported => reported.Kind));
    }

    [Fact]
    public void AShotWithoutABlastHitsItsTargetAloneAndIsLostWithIt()
    {
        var match = Field();
        var near = match.Spawn(0, Peltast, "8.5", "2.5");
        var far = match.Spawn(0, Peltast, "7.5", "2.5");
        var target = match.SpawnWorker(1, "11.5", "2.5");
        var beside = match.SpawnWorker(1, "11.5", "2.5");
        match.SetHitPoints(target, 6_000);
        match.OrderAttack(0, target, near, far);
        var lost = 0;
        for (var tick = 0; tick < 10; tick++)
        {
            match.Run(1);
            Assert.Empty(match.EventsOf(SimEventKind.Blast));
            lost += match.EventsOf(SimEventKind.ShotLost).Length;
        }

        Assert.False(match.IsAlive(target));
        Assert.Equal(1, lost);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(beside));
    }

    /// <summary>
    /// Two shells at one target, which the first kills: a snapshot before they are fired, with
    /// both in the air, with one in the air and its target gone, and after both have landed.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(16)]
    [InlineData(40)]
    [InlineData(90)]
    public void AMatchWithShellsInTheAirRestoresAndContinuesFromASnapshot(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(
            () =>
            {
                var match = Field();
                match.Spawn(0, Ballista, "3.5", "5.5");
                match.Spawn(0, Ballista, "1.5", "5.5");
                match.SetHitPoints(match.SpawnWorker(1, "9.5", "5.5"), Shell);
                match.Spawn(1, "knight", "10.5", "5.5");
                match.Spawn(1, Hoplite, "9.5", "6.5");
                match.RefreshDerivedState();
                return match;
            },
            [(0, AttackCommand(0, new EntityId(3), new EntityId(1), new EntityId(2)))],
            snapshotTick,
            lastTick: 200);
}
