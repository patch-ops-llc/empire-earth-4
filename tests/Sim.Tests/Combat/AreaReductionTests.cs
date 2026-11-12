using Sim.Core;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Map;
using static Sim.Tests.Combat.SiegeMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// Hardened I (design 2.3): a unit with it takes 25% less area damage, which is the blast of an
/// Artillery shell and the splash of Overgunned II. A hit that lands on the unit itself is not
/// area damage, whoever makes it.
/// </summary>
/// <remarks>
/// A chassis of epoch 1 has Hardened I when its owner's Protection is 2. A Ballista fires one
/// shell of 48 with a blast radius of 1.5 tiles. In the tests with shells a wall stands between
/// the Ballista and the enemies, so that those who would fight back stay where the test put
/// them, as in <see cref="BlastTests"/>.
/// </remarks>
public class AreaReductionTests
{
    /// <summary>24 by 12 with a wall from top to bottom in column 5, where player 1 has Protection 2.</summary>
    private static Match Hardened(int protection = 2, int players = 2)
    {
        var rows = Enumerable.Repeat(".....#..................", 12).ToArray();
        var match = Create(TestMaps.FromRows(rows), players: players);
        match.SetTiers(1, 1, protection, 1, 1, 1, 1);
        return match;
    }

    private static SimEvent[] RunToBlast(Match match)
    {
        match.RunTo(SimEventKind.Blast);
        return match.EventsOf(SimEventKind.ShotHit);
    }

    [Fact]
    public void TheProfileOfAChassisWithHardenedOneHasTheShareOfTheAccentsFile()
    {
        var match = Hardened();

        foreach (var chassis in new[] { Hoplite, Peltast, CombatMatches.Hippeus, Ballista })
        {
            Assert.Equal(new Fraction(3, 4), match.Combat.ArmorOf(1, CombatMatches.Chassis(chassis)).AreaDamage);
            Assert.Equal(Fraction.One, match.Combat.ArmorOf(0, CombatMatches.Chassis(chassis)).AreaDamage);
        }

        // A chassis of epoch 2 is level with Protection 2 and has no accent from it.
        Assert.Equal(Fraction.One, match.Combat.ArmorOf(1, CombatMatches.Chassis("legionary")).AreaDamage);
    }

    [Theory]
    [InlineData(Hoplite, 36_000)]
    [InlineData(Peltast, 36_000)]
    [InlineData(Ballista, 36_000)]

    // Artillery does half to Cavalry/Armor: 48 x 0.5 x 0.75.
    [InlineData(CombatMatches.Hippeus, 18_000)]

    // Not ahead of its epoch, so not Hardened: the whole shell.
    [InlineData("legionary", 48_000)]
    public void AShellDoesAQuarterLessToAHardenedUnit(string chassis, long damage)
    {
        var match = Hardened();
        var ballista = match.Unit(0, Ballista, "2.5", "5.5");
        var target = match.Unit(1, chassis, "9.5", "5.5");
        var full = match.HitPointsOf(target);
        match.OrderAttack(0, target, ballista);

        var hit = Assert.Single(RunToBlast(match));

        Assert.Equal((target, damage), (hit.Entity, hit.Amount));
        Assert.Equal(full - damage, match.HitPointsOf(target));
    }

    [Fact]
    public void EveryoneInTheBlastAppliesItsOwnReductionTheTargetOfTheShellIncluded()
    {
        var match = Hardened(players: 3);
        var ballista = match.Unit(0, Ballista, "2.5", "5.5");
        var target = match.Unit(1, Hoplite, "9.5", "5.5");
        var beside = match.Unit(1, Peltast, "10.5", "5.5");
        var worker = match.SpawnWorker(1, "9.5", "6.5");
        var notHardened = match.Unit(2, Hoplite, "9.5", "4.5");
        match.OrderAttack(0, target, ballista);

        var hits = RunToBlast(match).ToDictionary(hit => hit.Entity, hit => hit.Amount);

        Assert.Equal(4, hits.Count);
        Assert.Equal(36_000, hits[target]);
        Assert.Equal(36_000, hits[beside]);

        // A worker carries no accent, and the third player has researched nothing.
        Assert.Equal(48_000, hits[worker]);
        Assert.Equal(48_000, hits[notHardened]);
    }

    /// <summary>A hit on the unit itself is not area damage: a melee hit, a shot, and the shot of a Tower.</summary>
    [Fact]
    public void AHitOnTheUnitItselfIsNotReduced()
    {
        var match = Create(TestMaps.Open(32, 24));
        match.SetTiers(1, 1, 2, 1, 1, 1, 1);
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var first = match.Unit(1, Peltast, "6.5", "5.5");
        match.OrderAttack(0, first, hoplite);
        match.Run(1);
        var melee = Assert.Single(match.EventsOf(SimEventKind.MeleeHit), hit => hit.Other == hoplite);
        Assert.Equal(8_000, melee.Amount);

        match = Create(TestMaps.Open(32, 24));
        match.SetTiers(1, 1, 2, 1, 1, 1, 1);
        var peltast = match.Unit(0, Peltast, "5.5", "5.5");
        var second = match.Unit(1, Ballista, "10.5", "5.5");
        match.OrderAttack(0, second, peltast);
        Assert.Equal(6_000, match.RunTo(SimEventKind.ShotHit).Amount);

        match = Create(TestMaps.Open(32, 24));
        match.SetTiers(1, 1, 2, 1, 1, 1, 1);
        match.PlaceStructure(0, Tower, 10, 10);
        match.Unit(1, Ballista, "16.5", "11");
        Assert.Equal(10_000, match.RunTo(SimEventKind.ShotHit).Amount);
    }

    [Fact]
    public void ASplashDoesAQuarterLessToAHardenedUnitAndTheHitItselfIsWhole()
    {
        var match = Create(TestMaps.Open(32, 24));
        match.SetTiers(0, 3, 1, 1, 1, 1, 1);
        match.SetTiers(1, 1, 2, 1, 1, 1, 1);
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.Unit(1, Hoplite, "6.5", "5.5");
        var beside = match.Unit(1, Peltast, "6.5", "4.5");
        var worker = match.SpawnWorker(1, "6.5", "6.5");
        match.OrderAttack(0, target, hoplite);

        match.Run(1);

        // The hit of 12.5 lands whole on its Hardened target.
        var hit = Assert.Single(match.EventsOf(SimEventKind.MeleeHit), landed => landed.Other == hoplite);
        Assert.Equal((target, 12_500L), (hit.Entity, hit.Amount));

        // Its splash of 3.75 is area damage: 2.8125 on the Hardened Peltast, 2.813 half up, and whole on the worker.
        var splashed = match.EventsOf(SimEventKind.SplashHit).ToDictionary(landed => landed.Entity, landed => landed.Amount);
        Assert.Equal(2_813, splashed[beside]);
        Assert.Equal(3_750, splashed[worker]);
    }

    [Fact]
    public void TheReductionIsMultipliedInBeforeTheOneRounding()
    {
        var match = Create(TestMaps.Open(32, 24), players: 3);
        match.SetTiers(0, 4, 1, 1, 1, 1, 1);
        match.SetTiers(2, 1, 2, 1, 1, 1, 1);
        var legionary = match.Unit(0, "legionary", "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var horse = match.Unit(2, CombatMatches.Hippeus, "7.5", "5.5");
        match.OrderAttack(0, target, legionary);

        match.Run(1);

        // 15.625 x 0.3 x 1.5 x 0.75 = 5.2734375. Rounding the splash first would give 4.688 x 1.5 x 0.75 = 5.274.
        var splashed = Assert.Single(match.EventsOf(SimEventKind.SplashHit));
        Assert.Equal((horse, 5_273L), (splashed.Entity, splashed.Amount));
    }

    /// <summary>Design 2.4: the target's side is applied on impact.</summary>
    [Fact]
    public void TheTargetsAccentIsReadWhenTheShellLands()
    {
        var match = Hardened(protection: 1);
        var ballista = match.Unit(0, Ballista, "2.5", "5.5");
        var target = match.Unit(1, Hoplite, "9.5", "5.5");
        match.OrderAttack(0, target, ballista);
        match.Run(1);
        Assert.Equal(1, match.Shots.Count);
        Assert.Equal(Fraction.One, match.Combat.ArmorOf(1, CombatMatches.Chassis(Hoplite)).AreaDamage);

        // The target's owner reaches Protection 2 while the shell is in the air.
        match.SetTiers(1, 1, 2, 1, 1, 1, 1);

        Assert.Equal(36_000, Assert.Single(RunToBlast(match)).Amount);
    }

    /// <summary>Level II includes level I: a plated unit is Hardened too.</summary>
    [Fact]
    public void AUnitWithHardenedTwoStillTakesAQuarterLessAreaDamage()
    {
        var match = Hardened(protection: 3);
        match.SetTiers(0, 3, 1, 1, 1, 1, 1);
        var ballista = match.Unit(0, Ballista, "2.5", "5.5");
        var target = match.Unit(1, Hoplite, "9.5", "5.5");
        match.OrderAttack(0, target, ballista);

        // Firepower 3 is not below Protection 3, so only Hardened I applies: 48 x 1.25^2 x 0.75.
        Assert.Equal(56_250, Assert.Single(RunToBlast(match)).Amount);
    }
}
