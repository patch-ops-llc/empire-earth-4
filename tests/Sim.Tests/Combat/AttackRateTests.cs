using Sim.Combat;
using Sim.Core;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// Overgunned I for a melee chassis (design 2.3): 23 attacks in the time of 20. Each tick of
/// waiting counts as 23/20 of a tick toward the attack interval, the surplus at the tick an
/// attack is made carries into the next wait, and a unit that is ready with nothing to attack
/// keeps none.
/// </summary>
/// <remarks>
/// A chassis of epoch 1 has Overgunned I when its owner's Firepower is 2. A Hoplite attacks once
/// a second, every 20 ticks; a Hippeus every 30. <see cref="AttackTests"/> counts the attacks of
/// 400 ticks; these tests look at each tick of them.
/// </remarks>
public class AttackRateTests
{
    private const long Plenty = 100_000_000;

    private static Match Field(int firepower)
    {
        var match = Create(TestMaps.Open(24, 12));
        match.SetTiers(0, firepower, 1, 1, 1, 1, 1);
        return match;
    }

    /// <summary>The ticks, counted from its first attack, in which a unit beside a worker that never dies attacks it.</summary>
    private static List<int> AttackTicks(Match match, string chassis, int attacks)
    {
        var unit = match.Spawn(0, chassis, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.SetHitPoints(worker, Plenty);
        match.OrderAttack(0, worker, unit);
        var ticks = new List<int>();
        var first = -1;
        for (var tick = 0; ticks.Count < attacks && tick < 5_000; tick++)
        {
            match.Run(1);
            var made = match.Simulation.Events.Count(reported =>
                reported.Other == unit && reported.Kind is SimEventKind.MeleeHit or SimEventKind.ShotFired);
            Assert.InRange(made, 0, 1);
            if (made == 1)
            {
                first = first < 0 ? tick : first;
                ticks.Add(tick - first);
            }
        }

        Assert.Equal(attacks, ticks.Count);
        return ticks;
    }

    /// <summary>
    /// The rule in closed form: attack number k is made in the first tick by which the waiting has
    /// counted k whole intervals, which is k x interval x 20/23 ticks, rounded up.
    /// </summary>
    private static int[] Expected(int interval, int attacks) =>
        [.. Enumerable.Range(0, attacks).Select(k => ((k * interval * 20) + 22) / 23)];

    [Theory]
    [InlineData(Hoplite, 20)]
    [InlineData(Hippeus, 30)]
    public void EveryAttackComesInTheFirstTickByWhichTheWaitingHasCountedItsInterval(string chassis, int interval)
    {
        var match = Field(firepower: 2);
        Assert.Equal(interval, match.Combat.ProfileOf(0, Chassis(chassis)).IntervalTicks);
        Assert.Equal(new Fraction(23, 20), match.Combat.ProfileOf(0, Chassis(chassis)).Rate);

        var ticks = AttackTicks(match, chassis, attacks: 70);

        Assert.Equal(Expected(interval, 70), ticks);

        // 23 attacks fill the time of 20 exactly, and so do the next 23, and the 23 after them.
        Assert.Equal(20 * interval, ticks[23]);
        Assert.Equal(40 * interval, ticks[46]);
        Assert.Equal(60 * interval, ticks[69]);
    }

    [Fact]
    public void TheWaitsOfAHopliteAreSeventeenOrEighteenTicksAndAddUpToTheTimeOfTwenty()
    {
        var ticks = AttackTicks(Field(firepower: 2), Hoplite, attacks: 24);

        // 400 / 23 = 17.39 ticks: fourteen waits of 17 ticks and nine of 18 make the 400.
        var waits = ticks.Skip(1).Select((tick, index) => tick - ticks[index]).ToArray();
        Assert.Equal(
            [18, 17, 18, 17, 17, 18, 17, 18, 17, 17, 18, 17, 18, 17, 17, 18, 17, 18, 17, 17, 18, 17, 17],
            waits);
        Assert.Equal(400, waits.Sum());
    }

    [Fact]
    public void TheSurplusOfOneWaitIsCarriedIntoTheNext()
    {
        var match = Field(firepower: 2);
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.SetHitPoints(worker, Plenty);
        match.OrderAttack(0, worker, hoplite);
        match.Run(1);
        Assert.Equal(20, match.UnitOf(hoplite).Wait);

        // Each tick takes 23/20 off: after 17 ticks 0.45 is left, so the attack waits one tick more.
        match.Run(17);
        Assert.Equal(new Fraction(9, 20), match.UnitOf(hoplite).Wait);
        Assert.Equal(Plenty - 10_000, match.HitPointsOf(worker));

        // The 18th tick has counted 20.7. The attack is made, and the 0.7 comes off the next wait.
        match.Run(1);
        Assert.Equal(Plenty - 20_000, match.HitPointsOf(worker));
        Assert.Equal(new Fraction(193, 10), match.UnitOf(hoplite).Wait);

        // So the next attack comes after 17 ticks: 19.3 - 17 x 1.15 = -0.25, which leaves 19.75.
        match.Run(16);
        Assert.Equal(Plenty - 20_000, match.HitPointsOf(worker));
        match.Run(1);
        Assert.Equal(Plenty - 30_000, match.HitPointsOf(worker));
        Assert.Equal(new Fraction(79, 4), match.UnitOf(hoplite).Wait);
    }

    [Theory]
    [InlineData(1, 8_000)]
    [InlineData(2, 10_000)]
    [InlineData(3, 12_500)]
    public void TheBonusChangesHowOftenAUnitHitsAndNotHowHard(int firepower, long damage)
    {
        var match = Field(firepower);
        var blueprint = match.Blueprint(0, Hoplite);
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.SetHitPoints(worker, Plenty);
        match.OrderAttack(0, worker, hoplite);

        match.Run(1);

        // The damage of a hit is the unrounded damage per second times the interval of the data,
        // with the bonus or without: 8, 10 and 12.5.
        var hit = Assert.Single(match.EventsOf(SimEventKind.MeleeHit));
        Assert.Equal(blueprint.DamagePerHit.RoundHalfUp(1000), hit.Amount);
        Assert.Equal(damage, hit.Amount);
        Assert.Equal(20, match.Combat.ProfileOf(0, Chassis(Hoplite)).IntervalTicks);
    }

    [Fact]
    public void WithoutTheAccentEveryTickOfWaitingCountsAsOne()
    {
        var match = Field(firepower: 1);
        Assert.Equal(Fraction.One, match.Combat.ProfileOf(0, Chassis(Hoplite)).Rate);

        var ticks = AttackTicks(match, Hoplite, attacks: 24);

        Assert.Equal([.. Enumerable.Range(0, 24).Select(k => k * 20)], ticks);
    }

    /// <summary>Design 2.3: the rate is for a melee chassis. A ranged chassis gets range from the same accent.</summary>
    [Theory]
    [InlineData(Peltast, 2, 20)]
    [InlineData(Ballista, 2, 80)]

    // The Guard's chassis of epoch 4 attacks at range, so it takes the range and not the rate.
    [InlineData("grenadier", 5, 20)]
    public void ARangedChassisWithTheAccentAttacksNoFaster(string chassis, int firepower, int interval)
    {
        var match = Field(firepower);
        Assert.NotNull(match.Blueprint(0, chassis).Perks.RangeBonus);
        Assert.Null(match.Blueprint(0, chassis).Perks.AttackRateBonus);
        var profile = match.Combat.ProfileOf(0, Chassis(chassis));
        Assert.False(profile.Melee);
        Assert.Equal(Fraction.One, profile.Rate);
        Assert.Equal(interval, profile.IntervalTicks);

        var unit = match.Spawn(0, chassis, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "8.5", "5.5");
        match.SetHitPoints(worker, Plenty);
        match.OrderAttack(0, worker, unit);
        var fired = new List<int>();
        for (var tick = 0; tick < (interval * 5) + 1; tick++)
        {
            match.Run(1);
            if (match.EventsOf(SimEventKind.ShotFired).Length > 0)
            {
                fired.Add(tick);
            }
        }

        Assert.Equal([.. Enumerable.Range(0, 6).Select(k => k * interval)], fired);
    }

    /// <summary>
    /// Design 2.3: a unit that is ready with nothing to attack keeps no surplus. It does not save up
    /// waiting, and its first attack on what comes is followed by a whole interval.
    /// </summary>
    [Fact]
    public void AUnitThatStandsReadyWithNothingToAttackSavesNothingUp()
    {
        var match = Field(firepower: 2);
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        match.Run(100);
        Assert.Equal(Fraction.Zero, match.UnitOf(hoplite).Wait);

        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.SetHitPoints(worker, Plenty);
        match.OrderAttack(0, worker, hoplite);
        match.Run(1);

        Assert.Equal(Plenty - 10_000, match.HitPointsOf(worker));
        Assert.Equal(20, match.UnitOf(hoplite).Wait);
        match.Run(17);
        Assert.Equal(Plenty - 10_000, match.HitPointsOf(worker));
        match.Run(1);
        Assert.Equal(Plenty - 20_000, match.HitPointsOf(worker));
    }

    /// <summary>
    /// The surplus is kept only by an attack. A unit whose wait runs out while its target is out of
    /// reach has none when it gets there, however long it walked.
    /// </summary>
    [Fact]
    public void AUnitWhoseWaitRunsOutWhileItWalksToItsTargetKeepsNoSurplus()
    {
        var match = Field(firepower: 2);
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var near = match.SpawnWorker(1, "6.5", "5.5");
        var far = match.SpawnWorker(1, "5.5", "11.5");
        match.SetHitPoints(near, Plenty);
        match.SetHitPoints(far, Plenty);
        match.OrderAttack(0, near, hoplite);
        match.Run(1);
        Assert.Equal(20, match.UnitOf(hoplite).Wait);

        // Sent at the far worker, it walks nearly 5 tiles at 2.6 tiles per second: longer than its wait.
        match.OrderAttack(0, far, hoplite);
        var hit = match.RunTo(SimEventKind.MeleeHit);
        Assert.Equal(far, hit.Entity);
        Assert.True(match.World.Tick > 30);

        // With the waiting of the walk saved up it would have less than a whole interval ahead of it.
        Assert.Equal(20, match.UnitOf(hoplite).Wait);
    }

    /// <summary>The accent is read from the owner's blueprint each tick: research speeds up a unit in the middle of a wait.</summary>
    [Fact]
    public void AUnitCountsFasterFromTheTickItsOwnerHasTheTier()
    {
        var match = Field(firepower: 1);
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.SetHitPoints(worker, Plenty);
        match.OrderAttack(0, worker, hoplite);
        match.Run(11);
        Assert.Equal(10, match.UnitOf(hoplite).Wait);

        match.SetTiers(0, 2, 1, 1, 1, 1, 1);
        match.Run(1);

        Assert.Equal(new Fraction(177, 20), match.UnitOf(hoplite).Wait);
    }

    /// <summary>Design 8.4, row 6: nothing changes reach. The accent gives a melee unit attacks, not distance.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NoAccentChangesHowFarAMeleeUnitReaches(int firepower)
    {
        var match = Field(firepower);
        Assert.Equal(Fixed.Zero, match.Combat.ProfileOf(0, Chassis(Hoplite)).Range);
        Assert.Equal(Squared("1.25"), match.Combat.ReachSquared(Line("guard"), CombatRules.NoLine));
        var inReach = match.Spawn(0, Hoplite, "5.5", "3.5");
        var outOfReach = match.Spawn(0, Hoplite, "5.5", "8.5");
        var first = match.SpawnWorker(1, "6.75", "3.5");
        var second = match.SpawnWorker(1, "6.7501", "8.5");
        match.OrderAttack(0, first, inReach);
        match.OrderAttack(0, second, outOfReach);

        match.Run(1);

        // At a tile and a quarter it hits from where it stands. A hair further it has to walk first.
        var hit = Assert.Single(match.EventsOf(SimEventKind.MeleeHit));
        Assert.Equal((first, inReach), (hit.Entity, hit.Other));
        Assert.Equal(TestMatches.Point("5.5", "3.5"), match.PositionOf(inReach));
        match.RunTo(SimEventKind.MeleeHit, limit: 10);
        Assert.NotEqual(TestMatches.Point("5.5", "8.5"), match.PositionOf(outOfReach));
    }
}
