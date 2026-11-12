using Sim.Core;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Tech;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Perks;

/// <summary>
/// Charge and splash together (design 2.3): Charge "applies to all damage of one attack", and the
/// splash of a hit is a share "of its damage", so the splash of a charged attack is a share of the
/// charged damage, for a melee hit and for a shot alike.
/// </summary>
/// <remarks>
/// A chassis of epoch 1 has Overgunned II, splash, with Firepower at tier 3, and Rapid II, Charge,
/// with Mobility at tier 3. A Hoplite then hits for 8 x 1.25^2 = 12.5 and a Peltast for
/// 6 x 1.25^2 = 9.375. A worker has 50 hit points, takes a hit as it is, and stays where it stands.
/// </remarks>
public class ChargeIntoSplashTests
{
    private const long Worker = 50_000;

    private static readonly int[] OvergunnedAndRapid = DesignData.Mix(3, 1, 3, 1, 1, 1);

    private static Match Field()
    {
        var match = Create(TestMaps.Open(30, 12));
        match.SetTiers(0, OvergunnedAndRapid);
        return match;
    }

    /// <summary>Runs ticks until an attack of the unit is reported, and returns the events of that tick.</summary>
    private static SimEvent[] RunUntilItAttacks(Match match, EntityId unit, int limit = 400)
    {
        for (var tick = 0; tick < limit; tick++)
        {
            match.Run(1);
            if (match.Simulation.Events.Any(reported =>
                (reported.Kind is SimEventKind.MeleeHit or SimEventKind.ShotFired) && reported.Other == unit))
            {
                return [.. match.Simulation.Events];
            }
        }

        throw new InvalidOperationException($"No attack in {limit} ticks.");
    }

    [Theory]
    [InlineData(Hoplite)]
    [InlineData(Peltast)]
    [InlineData(Hippeus)]
    public void AChassisCanCarryBothPerks(string chassis)
    {
        var match = Field();

        var profile = match.Combat.ProfileOf(0, Chassis(chassis));
        Assert.Equal((Fixed.FromInt(4), new Fraction(3, 2)), (profile.Charge!.Distance, profile.Charge.Multiplier));
        Assert.Equal((new Fraction(3, 10), Fixed.FromInt(1)), (profile.SplashShare, profile.SplashRadius));
    }

    [Fact]
    public void TheSplashOfAChargedMeleeHitIsAShareOfTheChargedDamage()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var target = match.SpawnWorker(1, "8.5", "5.5");
        var beside = match.SpawnWorker(1, "9.5", "5.5");
        var above = match.SpawnWorker(1, "8.5", "4.5");
        var outOfReach = match.SpawnWorker(1, "10.5", "5.5");
        match.OrderAttack(0, target, hoplite);

        var events = RunUntilItAttacks(match, hoplite);

        // It walked more than 4 tiles: 12.5 with half again is 18.75, and 30% of that is 5.625.
        Assert.Equal(
            [SimEventKind.ChargedAttack, SimEventKind.MeleeHit, SimEventKind.Splash, SimEventKind.SplashHit, SimEventKind.SplashHit],
            events.Select(reported => reported.Kind));
        Assert.Equal((target, 18_750L), (events[1].Entity, events[1].Amount));
        Assert.Equal([(beside, 5_625L), (above, 5_625L)], events.Skip(3).Select(reported => (reported.Entity, reported.Amount)));
        Assert.Equal(Worker - 18_750, match.HitPointsOf(target));
        Assert.Equal((Worker - 5_625, Worker - 5_625), (match.HitPointsOf(beside), match.HitPointsOf(above)));
        Assert.Equal(Worker, match.HitPointsOf(outOfReach));

        // "Then the count restarts": the next hit is plain, and so is its splash, 30% of 12.5.
        events = RunUntilItAttacks(match, hoplite);
        Assert.Equal(
            [SimEventKind.MeleeHit, SimEventKind.Splash, SimEventKind.SplashHit, SimEventKind.SplashHit],
            events.Select(reported => reported.Kind));
        Assert.Equal(12_500, events[0].Amount);
        Assert.All(events.Skip(2), reported => Assert.Equal(3_750, reported.Amount));
        Assert.Equal(Worker - 18_750 - 12_500, match.HitPointsOf(target));
        Assert.Equal((Worker - 9_375, Worker - 9_375), (match.HitPointsOf(beside), match.HitPointsOf(above)));
        Assert.Equal(Worker, match.HitPointsOf(outOfReach));
    }

    /// <summary>
    /// A shot keeps the attacker's side as it was when it was fired (design 2.4), Charge included,
    /// and its splash is a share of what it carries: the splash of a charged shot is charged
    /// when the shot lands, and that of the shot after it is plain.
    /// </summary>
    [Fact]
    public void TheSplashOfAChargedShotIsAShareOfWhatTheShotCarries()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");
        var target = match.SpawnWorker(1, "13.5", "5.5");
        var beside = match.SpawnWorker(1, "14.5", "5.5");
        var outOfReach = match.SpawnWorker(1, "15.5", "5.5");

        // The target is 11 tiles off and a Peltast sees 8: a worker of its own side sees the target
        // for it. It stands within the splash and takes none: there is no friendly fire.
        var spotter = match.SpawnWorker(0, "13.5", "6.5");
        match.OrderAttack(0, target, peltast);

        var events = RunUntilItAttacks(match, peltast);

        // 9.375 with half again is 14.0625, which the shot carries from the moment it is fired.
        var fired = events.Single(reported => reported.Kind == SimEventKind.ShotFired);
        Assert.Single(events, reported => reported.Kind == SimEventKind.ChargedAttack);
        Assert.Equal(new Fraction(225, 16), match.Shots.Get(fired.Entity).Damage);
        Assert.Equal((Worker, Worker), (match.HitPointsOf(target), match.HitPointsOf(beside)));

        var hits = new List<long>();
        var splashes = new List<(EntityId Entity, long Amount)>();
        for (var tick = 0; tick < 30; tick++)
        {
            match.Run(1);
            hits.AddRange(match.EventsOf(SimEventKind.ShotHit).Select(reported => reported.Amount));
            splashes.AddRange(match.EventsOf(SimEventKind.SplashHit).Select(reported => (reported.Entity, reported.Amount)));
            Assert.Empty(match.EventsOf(SimEventKind.ChargedAttack));
        }

        // The charged shot lands with 14.0625 and splashes 30% of it, 4.21875; the next with 9.375
        // and 2.8125. Each is rounded half up once, to thousandths of a hit point.
        Assert.Equal([14_063, 9_375], hits);
        Assert.Equal([(beside, 4_219L), (beside, 2_813L)], splashes);
        Assert.Equal(Worker - 14_063 - 9_375, match.HitPointsOf(target));
        Assert.Equal(Worker - 4_219 - 2_813, match.HitPointsOf(beside));
        Assert.Equal((Worker, Worker), (match.HitPointsOf(outOfReach), match.HitPointsOf(spotter)));
    }

    /// <summary>
    /// What a target takes off area damage is the target's side, and comes after the charge: with
    /// Hardened I a unit takes the splash of a charged hit at three quarters, like any other.
    /// </summary>
    [Fact]
    public void ATargetsOwnSideIsAppliedToTheChargedSplash()
    {
        var match = Create(TestMaps.Open(30, 12), players: 3);
        match.SetTiers(0, OvergunnedAndRapid);
        match.SetTiers(2, DesignData.Mix(1, 2, 1, 1, 1, 1));
        var area = match.Combat.ArmorOf(2, Chassis(Hippeus)).AreaDamage;
        Assert.True(area < Fraction.One);
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var target = match.SpawnWorker(1, "8.5", "5.5");
        var hardened = match.Spawn(2, Hippeus, "8.5", "4.5");
        match.OrderAttack(0, target, hoplite);

        var events = RunUntilItAttacks(match, hoplite);

        // 18.75 x 0.3 = 5.625 before the target's side: a Guard hits Cavalry/Armor for 1.5, and Hardened I takes its part off.
        Assert.Single(events, reported => reported.Kind == SimEventKind.ChargedAttack);
        var splashed = Assert.Single(events, reported => reported.Kind == SimEventKind.SplashHit);
        var expected = (new Fraction(5_625, 1) * new Fraction(3, 2) * area).RoundHalfUp();
        Assert.Equal((hardened, expected), (splashed.Entity, splashed.Amount));
    }
}
