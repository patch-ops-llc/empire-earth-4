using Sim.Core;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Map;
using static Sim.Tests.Combat.SiegeMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// Hardened II, plating (design 2.3; design 8.4, row 42): a unit with it takes 30% less damage
/// from an attacker whose effective Firepower is below the unit's effective Protection. The
/// attacker's Firepower is fixed when it attacks, the target's Protection is read when the hit
/// lands, and a Tower's Firepower is its owner's tier.
/// </summary>
/// <remarks>
/// A chassis of epoch 1 has Hardened II when its owner's Protection is 3, and a chassis of epoch
/// 3 when it is 5. A Hoplite hits for 8 at Firepower 1, 10 at 2 and 12.5 at 3; a Peltast for 6,
/// 7.5 and 9.375, times 1.25 against a Guard; a Tower for 10, times 1.25 for each tier of its
/// owner's Firepower above 1. The plated unit fights back, so these tests read what a hit did
/// from its event.
/// </remarks>
public class PlatingTests
{
    private static Match Field(int firepower, int protection, int players = 2)
    {
        var match = Create(TestMaps.Open(32, 24), players: players);
        match.SetTiers(0, firepower, 1, 1, 1, 1, 1);
        match.SetTiers(1, 1, protection, 1, 1, 1, 1);
        return match;
    }

    /// <summary>The damage of the first melee hit that a unit of player 0 lands on a unit of player 1, next to each other.</summary>
    private static long MeleeHit(Match match, string attacker, string target)
    {
        var unit = match.Unit(0, attacker, "5.5", "5.5");
        var enemy = match.Unit(1, target, "6.5", "5.5");
        match.OrderAttack(0, enemy, unit);
        match.Run(1);
        var hit = Assert.Single(match.EventsOf(SimEventKind.MeleeHit), landed => landed.Other == unit);
        Assert.Equal(enemy, hit.Entity);
        return hit.Amount;
    }

    [Fact]
    public void TheProfileOfAChassisWithHardenedTwoHasTheShareOfTheAccentsFileAndItsEffectiveProtection()
    {
        var match = Field(firepower: 1, protection: 3);

        var plated = match.Combat.ArmorOf(1, CombatMatches.Chassis(Hoplite));
        Assert.Equal((3, new Fraction(7, 10), new Fraction(3, 4)), (plated.Protection, plated.Plating, plated.AreaDamage));

        // One tier ahead is Hardened I, without plating; level with its epoch, neither.
        var legionary = match.Combat.ArmorOf(1, CombatMatches.Chassis("legionary"));
        Assert.Equal((3, Fraction.One, new Fraction(3, 4)), (legionary.Protection, legionary.Plating, legionary.AreaDamage));
        var manAtArms = match.Combat.ArmorOf(1, CombatMatches.Chassis("man_at_arms"));
        Assert.Equal((3, Fraction.One, Fraction.One), (manAtArms.Protection, manAtArms.Plating, manAtArms.AreaDamage));
        var plain = match.Combat.ArmorOf(0, CombatMatches.Chassis(Hoplite));
        Assert.Equal((1, Fraction.One, Fraction.One), (plain.Protection, plain.Plating, plain.AreaDamage));
    }

    [Theory]

    // Below Protection 3: 8 x 0.7 and 10 x 0.7.
    [InlineData(1, 5_600)]
    [InlineData(2, 7_000)]

    // Level with it: the whole 12.5.
    [InlineData(3, 12_500)]
    public void AHitFromBelowTheTargetsProtectionLosesThirtyPercent(int firepower, long damage)
    {
        var match = Field(firepower, protection: 3);

        Assert.Equal(damage, MeleeHit(match, Hoplite, Hoplite));
    }

    [Fact]
    public void AUnitThatIsOnlyOneTierAheadIsNotPlated()
    {
        var match = Field(firepower: 1, protection: 2);

        Assert.Equal(8_000, MeleeHit(match, Hoplite, Hoplite));
    }

    /// <summary>Design 2.3, rule 1: a chassis uses its owner's tier only within two tiers of its own epoch.</summary>
    [Fact]
    public void BothSidesAreTheEffectiveTiersOfTheChassisNotTheTiersOfThePlayers()
    {
        // A Hoplite of a player with Firepower 5 fights with Firepower 3. A Man-at-arms of epoch 3
        // under Protection 5 has Protection 5 and is plated: 12.5 x 0.7.
        Assert.Equal(8_750, MeleeHit(Field(firepower: 5, protection: 5), Hoplite, "man_at_arms"));

        // The attacker's Man-at-arms uses all of Firepower 5: 8 x 1.25^4 = 19.53125, whole.
        Assert.Equal(19_531, MeleeHit(Field(firepower: 5, protection: 5), "man_at_arms", "man_at_arms"));

        // A Hoplite under Protection 5 has Protection 3 and is plated. A Legionary with Firepower 4
        // is above that, though below its owner's 5: 8 x 1.25^3, whole.
        Assert.Equal(15_625, MeleeHit(Field(firepower: 4, protection: 5), "legionary", Hoplite));
    }

    [Fact]
    public void AWorkerHasNoPlating()
    {
        var match = Field(firepower: 1, protection: 3);
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.OrderAttack(0, worker, hoplite);

        match.Run(1);

        Assert.Equal(8_000, Assert.Single(match.EventsOf(SimEventKind.MeleeHit)).Amount);
    }

    /// <summary>Design 2.3: the attacker's effective Firepower is fixed when the shot is fired.</summary>
    [Fact]
    public void AShotKeepsTheFirepowerItWasFiredWith()
    {
        var match = Field(firepower: 2, protection: 3);
        var peltast = match.Unit(0, Peltast, "5.5", "5.5");
        var hoplite = match.Unit(1, Hoplite, "10.5", "5.5");
        match.OrderAttack(0, hoplite, peltast);
        match.Run(1);
        Assert.Equal(2, match.Shots.ValueAt(0).Firepower);

        // The attacker's owner reaches Firepower 3 while the shot is in the air. The shot is still
        // one of Firepower 2, with its damage and below the target's Protection: 7.5 x 1.25 x 0.7 = 6.5625.
        match.SetTiers(0, 3, 1, 1, 1, 1, 1);
        Assert.Equal(6_563, match.RunTo(SimEventKind.ShotHit).Amount);

        // The next shot is fired with Firepower 3: 9.375 x 1.25 = 11.71875, whole.
        match.Run(1);
        Assert.Equal(11_719, match.RunTo(SimEventKind.ShotHit).Amount);
    }

    /// <summary>Design 2.3: the target's effective Protection is read on impact.</summary>
    [Fact]
    public void TheTargetsProtectionIsReadWhenTheShotLands()
    {
        var match = Field(firepower: 1, protection: 1);
        var peltast = match.Unit(0, Peltast, "5.5", "5.5");
        var hoplite = match.Unit(1, Hoplite, "10.5", "5.5");
        match.OrderAttack(0, hoplite, peltast);
        match.Run(1);
        Assert.Equal(1, match.Shots.Count);

        // The target's owner reaches Protection 3 while the shot is in the air: 6 x 1.25 x 0.7.
        match.SetTiers(1, 1, 3, 1, 1, 1, 1);

        Assert.Equal(5_250, match.RunTo(SimEventKind.ShotHit).Amount);
    }

    /// <summary>Design 2.3: the reduction multiplies with Hardened I.</summary>
    [Fact]
    public void PlatingMultipliesWithTheAreaReduction()
    {
        var rows = Enumerable.Repeat(".....#..................", 12).ToArray();
        var match = Create(TestMaps.FromRows(rows));
        match.SetTiers(1, 1, 3, 1, 1, 1, 1);
        var ballista = match.Unit(0, Ballista, "2.5", "5.5");
        var hoplite = match.Unit(1, Hoplite, "9.5", "5.5");
        match.OrderAttack(0, hoplite, ballista);

        match.RunTo(SimEventKind.Blast);

        // A shell of 48 from Firepower 1: 48 x 0.75 x 0.7.
        Assert.Equal(25_200, Assert.Single(match.EventsOf(SimEventKind.ShotHit)).Amount);
    }

    [Fact]
    public void EveryFactorIsMultipliedInBeforeTheOneRounding()
    {
        var match = Create(TestMaps.Open(32, 24), players: 3);
        match.SetTiers(0, 4, 1, 1, 1, 1, 1);
        match.SetTiers(2, 1, 5, 1, 1, 1, 1);
        var legionary = match.Unit(0, "legionary", "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var knight = match.Unit(2, Knight, "7.5", "5.5");
        match.OrderAttack(0, target, legionary);

        match.Run(1);

        // The splash of 4.6875 on a Knight of Protection 5: x 1.5 x 0.75 x 0.7 = 3.69140625. Rounding at
        // each step would give 4.688, 7.032, 5.274, 3.692.
        var splashed = Assert.Single(match.EventsOf(SimEventKind.SplashHit));
        Assert.Equal((knight, 3_691L), (splashed.Entity, splashed.Amount));
    }

    /// <summary>Design 8.4, row 42: plating against a Tower uses its owner's Firepower tier.</summary>
    [Theory]

    // Below Protection 3: 10 x 0.7 and 12.5 x 0.7.
    [InlineData(1, 7_000)]
    [InlineData(2, 8_750)]

    // Level with it: 10 x 1.25^2, whole.
    [InlineData(3, 15_625)]
    public void AgainstATowerPlatingComparesWithTheFirepowerTierOfItsOwner(int firepower, long damage)
    {
        var match = Field(firepower, protection: 3);
        var tower = match.PlaceStructure(0, Tower, 10, 10);
        var hoplite = match.Unit(1, Hoplite, "16.5", "11");
        Assert.Equal(firepower, match.Combat.TurretOf(0, StructureIndex(Tower))!.Firepower);

        var hit = match.RunTo(SimEventKind.ShotHit);

        Assert.Equal((hoplite, damage), (hit.Entity, hit.Amount));
        Assert.True(match.IsAlive(tower));
    }

    /// <summary>A Tower has no epoch: its Firepower is its owner's tier itself, with no window round it.</summary>
    [Theory]

    // 10 x 1.25^3 x 0.7 = 13.671875, below the Protection 5 of a Man-at-arms.
    [InlineData(4, 13_672)]

    // 10 x 1.25^5 = 30.517578125, above it.
    [InlineData(6, 30_518)]
    public void ATowersFirepowerIsNotLimitedToAWindow(int firepower, long damage)
    {
        var match = Field(firepower, protection: 5);
        match.PlaceStructure(0, Tower, 10, 10);
        var manAtArms = match.Unit(1, "man_at_arms", "16.5", "11");

        var hit = match.RunTo(SimEventKind.ShotHit);

        Assert.Equal((manAtArms, damage), (hit.Entity, hit.Amount));
    }

    /// <summary>The Tower's side is fixed when it fires, like a unit's.</summary>
    [Fact]
    public void ATowersShotKeepsTheFirepowerItWasFiredWith()
    {
        var match = Field(firepower: 2, protection: 3);
        match.PlaceStructure(0, Tower, 10, 10);
        match.Unit(1, Hoplite, "17.5", "11");
        match.RunTo(SimEventKind.ShotFired);

        match.SetTiers(0, 3, 1, 1, 1, 1, 1);

        Assert.Equal(8_750, match.RunTo(SimEventKind.ShotHit).Amount);
    }
}
