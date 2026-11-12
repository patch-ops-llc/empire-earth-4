using Sim.Combat;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>Attacks of design 2.4: the interval, the tick an attack is made in, shots, and what a hit takes off.</summary>
public class AttackTests
{
    private const long WorkerHitPoints = 50_000;

    private static Match Field() => Create(TestMaps.Open(24, 12));

    [Fact]
    public void AMeleeUnitAttacksAsSoonAsItsTargetIsInReachAndThenWaitsItsInterval()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.OrderAttack(0, worker, hoplite);

        // 8 damage per second at an interval of 1 second is 8 a hit, landing in the tick it is made.
        match.Run(1);
        Assert.Equal(WorkerHitPoints - 8_000, match.HitPointsOf(worker));

        match.Run(19);
        Assert.Equal(WorkerHitPoints - 8_000, match.HitPointsOf(worker));

        match.Run(1);
        Assert.Equal(WorkerHitPoints - 16_000, match.HitPointsOf(worker));
        Assert.Equal(0, match.Shots.Count);
    }

    [Fact]
    public void EachLineKeepsItsOwnInterval()
    {
        var match = Field();
        var hippeus = match.Spawn(0, Hippeus, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "7", "5.5");
        match.OrderAttack(0, worker, hippeus);

        // 10 damage per second at an interval of 1.5 seconds: 15 a hit, every 30 ticks.
        match.Run(30);
        Assert.Equal(WorkerHitPoints - 15_000, match.HitPointsOf(worker));
        match.Run(1);
        Assert.Equal(WorkerHitPoints - 30_000, match.HitPointsOf(worker));
    }

    [Fact]
    public void AUnitDoesNotAttackInATickInWhichItWalksAndMayWalkInTheTickAfterAnAttack()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "8.5", "5.5");
        match.OrderAttack(0, worker, hoplite);

        var reach = Squared("1.25");
        var cameIntoReach = -1;
        var firstHit = -1;
        for (var tick = 0; tick < 80 && firstHit < 0; tick++)
        {
            var before = match.PositionOf(hoplite);
            var hitPoints = match.HitPointsOf(worker);
            match.Run(1);
            var moved = match.PositionOf(hoplite) != before;
            if (cameIntoReach < 0 && match.DistanceSquared(hoplite, worker) <= reach)
            {
                cameIntoReach = tick;
                Assert.True(moved);
            }

            if (match.HitPointsOf(worker) < hitPoints)
            {
                firstHit = tick;
                Assert.False(moved);
            }
        }

        // It walked into reach in one tick, was stopped there, and struck in the next: no turn or set-up time.
        Assert.True(cameIntoReach > 0);
        Assert.Equal(cameIntoReach + 1, firstHit);

        // The target walks off. The unit is after it again in the very next tick: no recovery time.
        match.OrderMove(1, TestMatches.Point("20.5", "5.5"), worker);
        var stood = match.PositionOf(hoplite);
        match.Run(1);
        Assert.Equal(stood, match.PositionOf(hoplite));
        match.Run(1);
        Assert.NotEqual(stood, match.PositionOf(hoplite));
    }

    [Fact]
    public void ARangedUnitFiresAShotThatFliesAtItsSpeedAndLandsOnArrival()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "7.5", "5.5");
        match.OrderAttack(0, worker, peltast);

        // Exactly at range 5: the shot is fired in tick 0 and flies 1 tile a tick from tick 1.
        match.Run(1);
        Assert.Equal(1, match.Shots.Count);
        var shot = match.Shots.EntityAt(0);
        Assert.False(match.Bodies.Contains(shot));
        Assert.Equal(TestMatches.Point("2.5", "5.5"), match.Shots.Get(shot).Position);
        Assert.Equal(match.PositionOf(peltast), match.Shots.Get(shot).Position);

        match.Run(4);
        Assert.Equal(TestMatches.Point("6.5", "5.5"), match.Shots.Get(shot).Position);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(worker));

        match.Run(1);
        Assert.Equal(WorkerHitPoints - 6_000, match.HitPointsOf(worker));
        Assert.Equal(0, match.Shots.Count);
        Assert.False(match.IsAlive(shot));

        // The next shot leaves one interval after the first.
        match.Run(14);
        Assert.Equal(0, match.Shots.Count);
        match.Run(1);
        Assert.Equal(1, match.Shots.Count);
    }

    [Fact]
    public void AShotIsInTheAirForTheDistanceRoundedUpToWholeTicks()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "6.75", "5.5");
        match.OrderAttack(0, worker, peltast);

        // 4.25 tiles at 1 tile a tick: fired in tick 0, lands in tick 5.
        match.Run(5);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(worker));
        match.Run(1);
        Assert.Equal(WorkerHitPoints - 6_000, match.HitPointsOf(worker));
    }

    [Fact]
    public void AnArtilleryShotFliesAtHalfTheSpeed()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "7.5", "5.5");
        match.OrderAttack(0, worker, ballista);

        // 5 tiles at 10 tiles per second: 10 ticks. 12 damage per second every 4 seconds is 48 a shell.
        match.Run(10);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(worker));
        match.Run(1);
        Assert.Equal(WorkerHitPoints - 48_000, match.HitPointsOf(worker));
    }

    [Fact]
    public void AShotFollowsItsTargetAndHitsIt()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "7.5", "5.5");
        match.OrderAttack(0, worker, peltast);
        match.Run(1);
        var shot = match.Shots.EntityAt(0);

        // The target walks off sideways, out of range. The shot turns after it and still lands.
        match.OrderMove(1, TestMatches.Point("7.5", "10.5"), worker);
        match.Run(3);
        Assert.True(match.Shots.Get(shot).Position.Y > TestMatches.Point("0", "5.5").Y);

        match.Run(4);
        Assert.False(match.IsAlive(shot));
        Assert.Equal(WorkerHitPoints - 6_000, match.HitPointsOf(worker));
    }

    [Fact]
    public void AShotWhoseTargetDiesFirstIsLost()
    {
        var match = Field();
        var near = match.Spawn(0, Peltast, "4.5", "5.5");
        var far = match.Spawn(0, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "7.5", "5.5");
        var bystander = match.SpawnWorker(1, "7.5", "6.5");
        match.SetHitPoints(worker, 6_000);
        match.OrderAttack(0, worker, near, far);

        // Both fire in tick 0. The nearer shot lands in tick 3 and kills; the other has nothing left to hit.
        match.Run(3);
        Assert.Equal(2, match.Shots.Count);
        match.Run(1);
        Assert.False(match.IsAlive(worker));
        Assert.Equal(1, match.Shots.Count);
        match.Run(1);
        Assert.Equal(0, match.Shots.Count);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(bystander));
    }

    [Fact]
    public void AShotStillLandsWhenItsAttackerHasDied()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "7.5", "5.5");
        match.OrderAttack(0, worker, peltast);
        match.Run(1);

        match.SetHitPoints(peltast, 0);
        match.Run(1);
        Assert.False(match.IsAlive(peltast));
        Assert.Equal(1, match.Shots.Count);

        match.Run(4);
        Assert.Equal(WorkerHitPoints - 6_000, match.HitPointsOf(worker));
    }

    [Fact]
    public void TheAttackersSideIsFixedWhenTheShotIsFired()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "7.5", "5.5");
        match.OrderAttack(0, worker, peltast);
        match.Run(1);

        var shot = match.Shots.EntityAt(0);
        Assert.Equal(6, match.Shots.Get(shot).Damage);
        Assert.Equal(1, match.Shots.Get(shot).Firepower);
        Assert.Equal(Line("line_infantry"), match.Shots.Get(shot).Line);
        Assert.Equal(0, match.Shots.Get(shot).Owner);

        // The attacker's Firepower rises while the shot is in the air. The shot keeps what it was
        // fired with; the next one has the new values.
        match.SetTiers(0, 3, 1, 1, 1, 1, 1);
        match.Run(5);
        Assert.Equal(WorkerHitPoints - 6_000, match.HitPointsOf(worker));

        match.Run(15);
        var next = match.Shots.EntityAt(0);
        Assert.Equal(match.Blueprint(0, Peltast).DamagePerHit, match.Shots.Get(next).Damage);
        Assert.True(match.Shots.Get(next).Damage > 6);
        Assert.Equal(3, match.Shots.Get(next).Firepower);
    }

    [Fact]
    public void TheTargetsSideIsAppliedOnImpactWithTheCounterMultiplierOfTheTwoLines()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "2.5", "2.5");
        var hoplite = match.Spawn(0, Hoplite, "2.5", "8.5");
        var hippeus = match.Spawn(0, Hippeus, "12.5", "8.5");
        var ballista = match.Spawn(0, Ballista, "12.5", "2.5");
        var guard = match.Spawn(1, Hoplite, "5.5", "2.5");
        var horse = match.Spawn(1, Hippeus, "3.5", "8.5");
        var archer = match.Spawn(1, Peltast, "13.5", "8.5");
        var rider = match.Spawn(1, Hippeus, "17.5", "2.5");
        match.OrderAttack(0, guard, peltast);
        match.OrderAttack(0, horse, hoplite);
        match.OrderAttack(0, archer, hippeus);
        match.OrderAttack(0, rider, ballista);
        match.Run(1);

        // Guard against Cavalry/Armor: 8 x 1.5. Cavalry/Armor against Line infantry: 15 x 1.25.
        Assert.Equal(160_000 - 12_000, match.HitPointsOf(horse));
        Assert.Equal(60_000 - 18_750, match.HitPointsOf(archer));

        // Line infantry against Guard: 6 x 1.25. Artillery against Cavalry/Armor: 48 x 0.5. Whatever
        // the targets do meanwhile, the shots find them.
        match.Run(40);
        Assert.InRange(match.HitPointsOf(guard), 1, 130_000 - 7_500);
        Assert.Equal(0, (130_000 - match.HitPointsOf(guard)) % 7_500);
        Assert.InRange(match.HitPointsOf(rider), 1, 160_000 - 24_000);
        Assert.Equal(0, (160_000 - match.HitPointsOf(rider)) % 24_000);
    }

    [Fact]
    public void DamageIsRoundedOnceAfterEveryFactorIsMultipliedIn()
    {
        var match = Field();
        match.SetTiers(0, 4, 1, 1, 1, 1, 1);
        var attackerSide = match.Blueprint(0, "auxiliary").DamagePerHit;
        var lineInfantry = Line("line_infantry");
        var guard = Line("guard");

        // 6 x 1.25^3 = 11.71875, times 1.25 against a Guard: 14.6484375, so 14.648. Rounding the
        // attacker's side first would give 11.719 x 1.25 = 14.64875, so 14.649.
        Assert.Equal(new Fraction(1171875, 100000), attackerSide);
        Assert.Equal(14_648, match.Combat.DamageOf(attackerSide, lineInfantry, guard));
        Assert.Equal(11_719, match.Combat.DamageOf(attackerSide, lineInfantry, CombatRules.NoLine));

        var auxiliary = match.Spawn(0, "auxiliary", "2.5", "5.5");
        var hoplite = match.Spawn(1, Hoplite, "6.5", "5.5");
        match.OrderAttack(0, hoplite, auxiliary);
        match.Run(10);
        Assert.Equal(130_000 - 14_648, match.HitPointsOf(hoplite));
    }

    [Fact]
    public void TheMeleeAttackRateBonusGivesExactly23AttacksInTheTimeOf20()
    {
        var match = Field();
        match.SetTiers(0, 2, 1, 1, 1, 1, 1);
        var blueprint = match.Blueprint(0, Hoplite);
        Assert.NotNull(blueprint.Perks.AttackRateBonus);
        Assert.Equal(new Fraction(23, 20), match.Combat.ProfileOf(0, Chassis(Hoplite)).Rate);

        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        const long plenty = 10_000_000;
        match.SetHitPoints(worker, plenty);
        match.OrderAttack(0, worker, hoplite);

        // The bonus changes how often it hits, not how hard: 8 x 1.25 for Firepower 2.
        var hit = match.Combat.DamageOf(blueprint.DamagePerHit, blueprint.Line.Index, CombatRules.NoLine);
        Assert.Equal(10_000, hit);

        // 20 intervals of 20 ticks hold 23 attacks, and the next 20 another 23: nothing is lost to rounding.
        match.Run(400);
        Assert.Equal(23, (plenty - match.HitPointsOf(worker)) / hit);
        match.Run(400);
        Assert.Equal(46, (plenty - match.HitPointsOf(worker)) / hit);
    }

    [Fact]
    public void AReadyUnitWithoutATargetKeepsNoSurplus()
    {
        var match = Field();
        match.SetTiers(0, 2, 1, 1, 1, 1, 1);
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.OrderAttack(0, worker, hoplite);
        match.Run(1);
        Assert.Equal(20, match.UnitOf(hoplite).Wait);

        // It is sent away, and a move order never fights. After 18 ticks it has counted 20.7
        // against an interval of 20; with nothing to hit, the 0.7 is dropped.
        match.OrderMove(0, TestMatches.Point("20.5", "5.5"), hoplite);
        match.Run(18);
        Assert.Equal(Fraction.Zero, match.UnitOf(hoplite).Wait);

        // So its next attack starts a full interval. With the surplus kept it would be 19.3.
        match.OrderAttack(0, worker, hoplite);
        var hitPoints = match.HitPointsOf(worker);
        for (var tick = 0; tick < 40 && match.HitPointsOf(worker) == hitPoints; tick++)
        {
            match.Run(1);
        }

        Assert.True(match.HitPointsOf(worker) < hitPoints);
        Assert.Equal(20, match.UnitOf(hoplite).Wait);
    }

    [Fact]
    public void AnAttackInsideTheMinimumRangeIsNotMadeAndTheUnitDoesNotStepBack()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var close = match.SpawnWorker(1, "5.25", "5.5");
        match.OrderAttack(0, close, ballista);
        match.Run(100);

        Assert.Equal(0, match.Shots.Count);
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(close));
        Assert.Equal(TestMatches.Point("2.5", "5.5"), match.PositionOf(ballista));
        Assert.Equal(UnitOrder.Attack, match.UnitOf(ballista).Order);
    }

    [Fact]
    public void ATargetAtExactlyTheMinimumRangeCanBeFiredAt()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "5.5", "5.5");
        match.OrderAttack(0, worker, ballista);
        match.Run(1);
        Assert.Equal(1, match.Shots.Count);
    }

    [Fact]
    public void ThereIsNoFriendlyFire()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var friend = match.Spawn(0, Peltast, "6.5", "5.5");
        match.OrderAttack(0, friend, hoplite);
        match.Run(40);

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(60_000, match.HitPointsOf(friend));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(default, match.UnitOf(hoplite).Target);
    }
}
