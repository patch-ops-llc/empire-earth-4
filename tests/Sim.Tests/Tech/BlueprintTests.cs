using Sim.Data;
using Sim.Numerics;
using Sim.Tech;
using Sim.Tests.Data;

namespace Sim.Tests.Tech;

/// <summary>
/// Chassis blueprints against the worked examples of the game design (2.3, 2.4 and 3.1): design
/// examples B, B at first-playable tiers, C and E. The expected values were computed from the
/// formulas with a separate implementation, and agree with the numbers the design prints.
/// </summary>
public class BlueprintTests
{
    private const int Firepower = 0;
    private const int Protection = 1;
    private const int Mobility = 2;
    private const int Manufacturing = 3;
    private const int Logistics = 4;

    /// <summary>
    /// The stats of every example unit: hit points and damage per second in thousandths, Materials,
    /// Energy and build ticks.
    /// </summary>
    [Theory]
    [InlineData("crossbowman", 2, 2, 3, 5, 5, 3, 75_000, 7_500, 32, 0, 154)] // B swarm: 75 / 7.5, 32 Materials, 7.68 s.
    [InlineData("stormtrooper", 5, 5, 3, 2, 2, 3, 317_383, 19_531, 114, 16, 563)] // B elite: 317 / 19.5, 114 + 16, 28.125 s.
    [InlineData("knight", 5, 5, 3, 2, 2, 3, 390_625, 24_414, 231, 77, 600)] // B elite's older line.
    [InlineData("auxiliary", 1, 1, 2, 4, 4, 2, 60_000, 6_000, 28, 0, 154)] // B first playable, swarm: 60 / 6, 28 Materials.
    [InlineData("grenadier", 4, 4, 2, 1, 1, 2, 253_906, 15_625, 99, 14, 563)] // B first playable, elite: 253.9 / 15.6, 99 + 14.
    [InlineData("onager", 4, 4, 2, 1, 1, 2, 136_719, 23_438, 201, 112, 750)] // B first playable, elite: 136.7 / 23.4, 201 + 112.
    [InlineData("strike_jet", 6, 3, 7, 4, 3, 5, 195_313, 42_725, 188, 150, 938)] // C: 195 / 43, 188 + 150.
    [InlineData("cuirassier", 6, 3, 7, 4, 3, 5, 250_000, 30_518, 208, 69, 480)] // C, the line left behind.
    [InlineData("combat_drone", 7, 6, 6, 9, 9, 8, 183_105, 22_888, 48, 0, 192)] // E swarm: 48 Materials.
    [InlineData("autonomous_tank", 9, 9, 8, 6, 6, 7, 953_674, 59_605, 391, 130, 750)] // E elite: 391 + 130.
    public void AnExampleUnitHasTheStatsAndPriceTheDesignStates(
        string chassis, int fp, int pr, int mo, int mf, int lg, int ex, long hitPoints, long damagePerSecond, int materials, int energy, int buildTicks)
    {
        var blueprint = Blueprint(chassis, fp, pr, mo, mf, lg, ex);

        Assert.True(blueprint.Unlocked);
        Assert.Equal(
            (hitPoints, damagePerSecond, materials, energy, buildTicks),
            (blueprint.HitPoints, blueprint.DamagePerSecond, blueprint.Materials, blueprint.Energy, blueprint.BuildTicks));
    }

    /// <summary>The offsets the design's tables print for examples B and B at first-playable tiers: Firepower, Protection, Mobility, Manufacturing.</summary>
    [Theory]
    [InlineData("crossbowman", 2, 2, 3, 5, 5, 3, -1, -1, 0, 2)]
    [InlineData("stormtrooper", 5, 5, 3, 2, 2, 3, 0, 0, -2, -2)]
    [InlineData("auxiliary", 1, 1, 2, 4, 4, 2, -1, -1, 0, 2)]
    [InlineData("grenadier", 4, 4, 2, 1, 1, 2, 0, 0, -2, -2)]
    public void AnExampleUnitHasTheOffsetsTheDesignStates(
        string chassis, int fp, int pr, int mo, int mf, int lg, int ex, int firepower, int protection, int mobility, int manufacturing)
    {
        var blueprint = Blueprint(chassis, fp, pr, mo, mf, lg, ex);

        Assert.Equal([firepower, protection, mobility, manufacturing], blueprint.Offsets.Take(4));
    }

    /// <summary>The accents the design names in its examples, strongest first.</summary>
    [Theory]
    [InlineData("crossbowman", 2, 2, 3, 5, 5, 3, "Standardized II, Supplied II")]
    [InlineData("stormtrooper", 5, 5, 3, 2, 2, 3, "")]
    [InlineData("knight", 5, 5, 3, 2, 2, 3, "Overgunned II, Hardened II")]
    [InlineData("auxiliary", 1, 1, 2, 4, 4, 2, "Standardized II, Supplied II")]
    [InlineData("grenadier", 4, 4, 2, 1, 1, 2, "")]
    [InlineData("onager", 4, 4, 2, 1, 1, 2, "Overgunned II, Hardened II")]
    [InlineData("strike_jet", 6, 3, 7, 4, 3, 5, "")]
    [InlineData("cuirassier", 6, 3, 7, 4, 3, 5, "Rapid II, Overgunned II")]
    [InlineData("combat_drone", 7, 6, 6, 9, 9, 8, "Standardized I, Supplied I")]
    [InlineData("autonomous_tank", 9, 9, 8, 6, 6, 7, "Overgunned I, Hardened I")]
    public void AnExampleUnitCarriesTheAccentsTheDesignStates(string chassis, int fp, int pr, int mo, int mf, int lg, int ex, string accents)
    {
        var blueprint = Blueprint(chassis, fp, pr, mo, mf, lg, ex);

        Assert.Equal(accents, Names(blueprint));
    }

    /// <summary>Design 3.1, example B: the swarm's unit costs 1 supply and comes in pairs; the elite's costs 2.</summary>
    [Fact]
    public void TheSwarmUnitOfExampleBIsBuiltInPairsAndCostsOneSupply()
    {
        var crossbowman = Blueprint("crossbowman", 2, 2, 3, 5, 5, 3);
        var stormtrooper = Blueprint("stormtrooper", 5, 5, 3, 2, 2, 3);

        Assert.Equal((1, 2, 230), (crossbowman.Supply, crossbowman.BatchSize, crossbowman.BatchBuildTicks));
        Assert.Equal((2, 1, 563), (stormtrooper.Supply, stormtrooper.BatchSize, stormtrooper.BatchBuildTicks));
        Assert.Equal(
            [PerkKind.Batch, PerkKind.DeathRefund, PerkKind.Regeneration, PerkKind.SupplyReduction],
            crossbowman.Perks.Kinds);
        Assert.Empty(stormtrooper.Perks.Kinds);
    }

    /// <summary>
    /// Design 3.1, example B at first-playable tiers: "one shell of 93.75 every 4 s, blast radius
    /// 2.25". The shell is the unrounded damage per second times the interval; the rounded 23.438
    /// would give 93.752.
    /// </summary>
    [Fact]
    public void TheOnagerOfTheFirstPlayableExampleFiresAShellOf9375WithABlastOf225()
    {
        var onager = Blueprint("onager", 4, 4, 2, 1, 1, 2);

        Assert.Equal(new Fraction(375, 4), onager.DamagePerHit);
        Assert.Equal(80, onager.Attack.IntervalTicks);
        Assert.Equal(Fixed.FromRaw(147_456), onager.BlastRadius);
        Assert.Equal("2.25", onager.BlastRadius.ToString());
        Assert.Equal(Fixed.FromInt(3), onager.MinRange);
        Assert.Equal(3, onager.Supply);
        Assert.Equal(
            [PerkKind.RangeBonus, PerkKind.BlastRadiusBonus, PerkKind.AreaDamageReduction, PerkKind.Plating],
            onager.Perks.Kinds);
    }

    /// <summary>Design 3.1, example C: the Air line reaches the Strike Jet and Cavalry/Armor is stuck at the Cuirassier.</summary>
    [Fact]
    public void ExampleCFliesStrikeJetsOverCuirassiers()
    {
        var tech = TechResolver.Resolve(DesignData.Data, DesignData.Mix(6, 3, 7, 4, 3, 5));

        Assert.Equal("strike_jet", Newest(tech, "air"));
        Assert.Equal("cuirassier", Newest(tech, "cavalry_armor"));
        Assert.Equal("musketeer", Newest(tech, "line_infantry"));
    }

    /// <summary>Design 3.1, example B: "Manufacturing 5 would allow the Rifleman, but Firepower 2 stops the line at epoch 3".</summary>
    [Fact]
    public void ExampleBStopsAtTheCrossbowmanAndReachesTheStormtrooper()
    {
        var swarm = TechResolver.Resolve(DesignData.Data, DesignData.Mix(2, 2, 3, 5, 5, 3));
        var elite = TechResolver.Resolve(DesignData.Data, DesignData.Mix(5, 5, 3, 2, 2, 3));

        Assert.Equal("crossbowman", Newest(swarm, "line_infantry"));
        Assert.Equal("stormtrooper", Newest(elite, "guard"));
    }

    /// <summary>Design 2.2: primary tier T and secondary tier T - 1, and older chassis stay available.</summary>
    [Theory]
    [InlineData(1, 1, "hoplite")]
    [InlineData(1, 9, "hoplite")]
    [InlineData(2, 1, "hoplite, legionary")]
    [InlineData(3, 1, "hoplite, legionary")]
    [InlineData(3, 2, "hoplite, legionary, man_at_arms")]
    [InlineData(4, 3, "hoplite, legionary, man_at_arms, grenadier")]
    [InlineData(9, 3, "hoplite, legionary, man_at_arms, grenadier")]
    [InlineData(9, 8, "hoplite, legionary, man_at_arms, grenadier, stormtrooper, anti_tank_trooper, armored_infantry, exosuit_trooper, void_marine")]
    public void AChassisUnlocksAtPrimaryTierTAndSecondaryTierTMinusOne(int protection, int firepower, string unlocked)
    {
        var tech = TechResolver.Resolve(DesignData.Data, DesignData.Mix(firepower, protection, 1, 1, 1, 1));
        var guard = DesignData.Data.Lines.Single(line => line.Id == "guard");

        Assert.Equal(unlocked, string.Join(", ", guard.Chassis.Where(chassis => tech.Blueprints[chassis.Index].Unlocked).Select(chassis => chassis.Id)));
    }

    [Fact]
    public void ThereIsOneBlueprintForEveryChassisAtItsIndexUnlockedOrNot()
    {
        var data = DesignData.Data;

        var tech = TechResolver.Resolve(data, DesignData.Mix(1, 1, 1, 1, 1, 1));

        Assert.Equal(41, tech.Blueprints.Count);
        Assert.Equal(data.Chassis, tech.Blueprints.Select(blueprint => blueprint.Chassis));
        Assert.All(tech.Blueprints, blueprint => Assert.Same(data.LineOf(blueprint.Chassis.Line), blueprint.Line));
        Assert.Equal(
            ["ballista", "hippeus", "hoplite", "peltast"],
            tech.Blueprints.Where(blueprint => blueprint.Unlocked).Select(blueprint => blueprint.Chassis.Id));

        // A locked chassis still has its numbers, for the build menu to show what a research brings.
        var legionary = tech.Blueprints[DesignData.Chassis("legionary").Index];
        Assert.False(legionary.Unlocked);
        Assert.Equal((130_000, 8_000, 59, 8), (legionary.HitPoints, legionary.DamagePerSecond, legionary.Materials, legionary.Energy));
    }

    /// <summary>Design 2.3, rule 1: a chassis uses technology within two tiers of its own epoch.</summary>
    [Theory]
    [InlineData("hoplite", 1, 1, 0)]
    [InlineData("hoplite", 3, 3, 2)]
    [InlineData("hoplite", 4, 3, 2)]
    [InlineData("hoplite", 9, 3, 2)]
    [InlineData("stormtrooper", 1, 3, -2)]
    [InlineData("stormtrooper", 3, 3, -2)]
    [InlineData("stormtrooper", 4, 4, -1)]
    [InlineData("stormtrooper", 7, 7, 2)]
    [InlineData("stormtrooper", 8, 7, 2)]
    [InlineData("void_marine", 1, 7, -2)]
    [InlineData("void_marine", 9, 9, 0)]
    public void TheEffectiveTierIsThePlayersTierLimitedToTwoAroundTheEpoch(string chassis, int tier, int effective, int offset)
    {
        var blueprint = Blueprint(chassis, tier, tier, tier, tier, tier, tier);

        Assert.All(blueprint.EffectiveTiers, value => Assert.Equal(effective, value));
        Assert.All(blueprint.Offsets, value => Assert.Equal(offset, value));
    }

    [Fact]
    public void TheWindowIsData()
    {
        var data = DataAssert.Valid(DesignData.Files().Edit(DesignData.RulesPath, "\"effective_tier_window\": 2", "\"effective_tier_window\": 1"));

        var hoplite = TechResolver.Resolve(data, DesignData.Mix(9, 9, 1, 1, 1, 1)).Blueprints[DesignData.Chassis("hoplite").Index];

        Assert.Equal([2, 2, 1, 1, 1, 1], hoplite.EffectiveTiers);
        Assert.Equal((162_500, 10_000), (hoplite.HitPoints, hoplite.DamagePerSecond));
    }

    /// <summary>Each stat follows one track only (design 2.3, rule 2).</summary>
    [Fact]
    public void EachStatFollowsItsOwnTrack()
    {
        var plain = Blueprint("hoplite", 1, 1, 1, 1, 1, 1);
        var firepower = Blueprint("hoplite", 2, 1, 1, 1, 1, 1);
        var protection = Blueprint("hoplite", 1, 2, 1, 1, 1, 1);
        var mobility = Blueprint("hoplite", 1, 1, 2, 1, 1, 1);
        var manufacturing = Blueprint("hoplite", 1, 1, 1, 2, 1, 1);
        var others = Blueprint("hoplite", 1, 1, 1, 1, 3, 3);

        Assert.Equal((130_000, 8_000, 8_520, 70, 10, 360), Numbers(plain));
        Assert.Equal((130_000, 10_000, 8_520, 81, 12, 360), Numbers(firepower));
        Assert.Equal((162_500, 8_000, 8_520, 81, 12, 360), Numbers(protection));
        Assert.Equal((130_000, 8_000, 9_201, 81, 12, 360), Numbers(mobility));
        Assert.Equal((130_000, 8_000, 8_520, 63, 9, 288), Numbers(manufacturing));
        Assert.Equal(Numbers(plain), Numbers(others));
    }

    /// <summary>
    /// The largest exponents the formulas can meet with nine tiers: a chassis of epoch 7 with
    /// Firepower, Protection and Mobility all two ahead pays 1.15 to the power of 12.
    /// </summary>
    [Theory]
    [InlineData(5, 594, 198, 750)]
    [InlineData(9, 390, 130, 307)]
    public void ThePriceAtTheLargestExponentIsExact(int manufacturing, int materials, int energy, int buildTicks)
    {
        var tank = Blueprint("main_battle_tank", 9, 9, 9, manufacturing, 5, 5);

        Assert.Equal([2, 2, 2], tank.Offsets.Take(3));
        Assert.Equal((953_674, 59_605, 27_293, materials, energy, buildTicks), Numbers(tank));
    }

    /// <summary>A chassis behind in every track is cheaper than its line's tier-1 price, because the exponent is negative.</summary>
    [Theory]
    [InlineData(1, 87, 29, 600)]
    [InlineData(4, 63, 21, 307)]
    public void AChassisWithLaggingTracksIsWeakerAndCheaper(int manufacturing, int materials, int energy, int buildTicks)
    {
        var eques = Blueprint("eques", 1, 1, 2, manufacturing, 1, 1);

        Assert.Equal((160_000, 10_000, 15_925, materials, energy, buildTicks), Numbers(eques));
    }

    [Fact]
    public void TheFormulaNumbersAreData()
    {
        var files = DesignData.Files();
        files.Edit(DesignData.RulesPath, "\"damage\": \"1.25\", \"hp\": \"1.25\", \"speed\": \"1.08\"", "\"damage\": \"1.5\", \"hp\": \"2\", \"speed\": \"1.1\"");
        files.Edit(DesignData.RulesPath, "\"quality_cost\": \"1.15\", \"manufacturing_cost\": \"0.90\", \"manufacturing_time\": \"0.80\"", "\"quality_cost\": \"1.2\", \"manufacturing_cost\": \"0.5\", \"manufacturing_time\": \"0.25\"");
        var data = DataAssert.Valid(files);

        var hoplite = TechResolver.Resolve(data, DesignData.Mix(2, 2, 2, 2, 1, 1)).Blueprints[DesignData.Chassis("hoplite").Index];

        // 130 x 2, 8 x 1.5, 2.6 x 1.1 / 20 tiles per tick, 70 x 1.2^3 x 0.5 = 60.48, 10 x 0.864 = 8.64, 18 s x 0.25.
        Assert.Equal((260_000, 12_000, 9_372, 60, 9, 90), Numbers(hoplite));
    }

    /// <summary>Design 2.3: speed, range and blast radius are rounded once to 1/65,536 of a tile.</summary>
    [Fact]
    public void DistancesAreRoundedOnceToTheDistanceUnit()
    {
        var crossbowman = Blueprint("crossbowman", 2, 2, 3, 5, 5, 3);
        var onager = Blueprint("onager", 4, 4, 2, 1, 1, 2);
        var tank = Blueprint("autonomous_tank", 9, 9, 8, 6, 6, 7);
        var musketeer = Blueprint("musketeer", 5, 1, 1, 4, 1, 1);

        // 3 x 1.08^2 / 20 = 0.17496 tiles per tick, which is 11,466.18 units.
        Assert.Equal(Fixed.FromRaw(11_466), crossbowman.SpeedPerTick);
        Assert.Equal(Fixed.FromInt(5), crossbowman.Range);
        Assert.Null(crossbowman.MinRange);
        Assert.Null(crossbowman.BlastRadius);

        // Overgunned II includes level I: 9 x 1.15 = 10.35 tiles, which is 678,297.6 units.
        Assert.Equal(Fixed.FromRaw(678_298), onager.Range);
        Assert.Equal(Fixed.FromRaw(6_370), onager.SpeedPerTick);

        // 5 x 1.15 = 5.75 and 6 x 1.15 = 6.9 tiles.
        Assert.Equal(Fixed.FromRaw(376_832), tank.Range);
        Assert.Equal(Fixed.FromRaw(452_198), musketeer.Range);
    }

    /// <summary>A perk for one kind of attack is not on a chassis that attacks another way (design 2.3).</summary>
    [Fact]
    public void OvergunnedGivesEachChassisThePerksThatFitItsAttack()
    {
        var melee = Blueprint("cuirassier", 6, 3, 7, 4, 3, 5);
        var ranged = Blueprint("musketeer", 6, 1, 1, 4, 1, 1);
        var blast = Blueprint("field_cannon", 6, 1, 1, 1, 3, 1);

        Assert.Equal([new Accent(Firepower, 2)], ranged.Accents);
        Assert.Equal([new Accent(Firepower, 2)], blast.Accents);

        Assert.Equal([PerkKind.AttackRateBonus, PerkKind.Splash, PerkKind.SightBonus, PerkKind.Charge], melee.Perks.Kinds);
        Assert.Null(melee.Range);
        Assert.Equal(12, melee.Sight);

        Assert.Equal([PerkKind.RangeBonus, PerkKind.Splash], ranged.Perks.Kinds);
        Assert.Equal(Fixed.FromRaw(452_198), ranged.Range);
        Assert.Null(ranged.BlastRadius);

        Assert.Equal([PerkKind.RangeBonus, PerkKind.BlastRadiusBonus], blast.Perks.Kinds);
        Assert.Equal("2.25", blast.BlastRadius.ToString());
    }

    /// <summary>Design 2.3, rule 3: offset +1 gives level I, +2 gives level II, and a track at or behind the chassis gives nothing.</summary>
    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "Hardened I")]
    [InlineData(3, "Hardened II")]
    [InlineData(9, "Hardened II")]
    public void TheLevelOfAnAccentFollowsTheOffset(int protection, string accents)
    {
        Assert.Equal(accents, Names(Blueprint("hoplite", 1, protection, 1, 1, 1, 1)));
    }

    /// <summary>"A unit carries at most two accents: the two largest offsets."</summary>
    [Theory]
    [InlineData(2, 2, 3, 3, 1, "Rapid II, Standardized II")]
    [InlineData(3, 2, 2, 2, 3, "Overgunned II, Supplied II")]
    [InlineData(2, 3, 2, 2, 2, "Hardened II, Overgunned I")]
    [InlineData(1, 2, 1, 3, 2, "Standardized II, Hardened I")]
    public void AUnitCarriesTheAccentsOfItsTwoLargestOffsets(int fp, int pr, int mo, int mf, int lg, string accents)
    {
        Assert.Equal(accents, Names(Blueprint("hoplite", fp, pr, mo, mf, lg, 1)));
    }

    /// <summary>"Ties go to the track where the player's actual tier is higher, then to the fixed order."</summary>
    [Theory]
    [InlineData(3, 4, 5, 6, 7, "Supplied II, Standardized II")] // All five at +2: the two highest actual tiers.
    [InlineData(7, 6, 5, 4, 3, "Overgunned II, Hardened II")]
    [InlineData(3, 3, 5, 3, 5, "Rapid II, Supplied II")] // Equal offsets and equal tiers: Mobility comes before Logistics.
    [InlineData(3, 3, 3, 3, 3, "Overgunned II, Hardened II")] // Everything equal: the fixed order.
    [InlineData(2, 2, 2, 2, 2, "Overgunned I, Hardened I")]
    [InlineData(2, 9, 3, 2, 2, "Hardened II, Rapid II")] // The window hides how far ahead a track is; the actual tier breaks the tie.
    [InlineData(2, 3, 9, 2, 2, "Rapid II, Hardened II")]
    public void TiesGoToTheHigherActualTierAndThenToTheTrackOrder(int fp, int pr, int mo, int mf, int lg, string accents)
    {
        Assert.Equal(accents, Names(Blueprint("hoplite", fp, pr, mo, mf, lg, 1)));
    }

    [Fact]
    public void ExtractionGivesNoAccent()
    {
        var hoplite = Blueprint("hoplite", 1, 1, 1, 1, 1, 9);

        Assert.Equal(2, hoplite.Offsets[5]);
        Assert.Empty(hoplite.Accents);
        Assert.Equal(PerkSet.None, hoplite.Perks);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "Overgunned II")]
    [InlineData(3, "Overgunned II, Hardened II, Rapid II")]
    [InlineData(5, "Overgunned II, Hardened II, Rapid II, Standardized II, Supplied II")]
    public void TheNumberOfAccentSlotsIsData(int slots, string accents)
    {
        var data = DataAssert.Valid(DesignData.Files().Edit(DesignData.RulesPath, "\"accent_slots\": 2", $"\"accent_slots\": {slots}"));

        var hoplite = TechResolver.Resolve(data, DesignData.Mix(3, 3, 3, 3, 3, 1)).Blueprints[DesignData.Chassis("hoplite").Index];

        Assert.Equal(accents, Names(hoplite));
    }

    /// <summary>With a wider window a track can be three ahead. It outranks a track two ahead, and its accent is still level II.</summary>
    [Fact]
    public void AnAccentHasNoLevelAboveTwo()
    {
        var data = DataAssert.Valid(DesignData.Files().Edit(DesignData.RulesPath, "\"effective_tier_window\": 2", "\"effective_tier_window\": 3"));

        var hoplite = TechResolver.Resolve(data, DesignData.Mix(3, 3, 4, 1, 1, 1)).Blueprints[DesignData.Chassis("hoplite").Index];

        Assert.Equal([new Accent(Mobility, 2), new Accent(Firepower, 2)], hoplite.Accents);
        Assert.Equal(3, hoplite.Offsets[Mobility]);
    }

    [Fact]
    public void WhichAccentATrackGivesAndHowStrongItsPerksAreIsData()
    {
        var files = DesignData.Files();
        files.Edit(DesignData.AccentsPath, "\"sight_bonus\": { \"tiles\": 2 }", "\"sight_bonus\": { \"tiles\": 3 }");
        files.Edit(DesignData.AccentsPath, "\"batch\": { \"time_multiplier\": \"1.5\" }", "\"batch\": { \"time_multiplier\": \"1.25\" }");
        files.Edit(DesignData.AccentsPath, "\"supply_reduction\": { \"amount\": 1, \"minimum\": 1 }", "\"supply_reduction\": { \"amount\": 2, \"minimum\": 2 }");
        files.Edit(DesignData.AccentsPath, "\"range_bonus\": { \"multiplier\": \"1.15\" }", "\"range_bonus\": { \"multiplier\": \"1.5\" }");
        var data = DataAssert.Valid(files);

        PlayerTech Resolve(params int[] tiers) => TechResolver.Resolve(data, tiers);
        var knight = Resolve(1, 2, 5, 1, 5, 1).Blueprints[DesignData.Chassis("knight").Index];
        var peltast = Resolve(2, 1, 1, 2, 3, 1).Blueprints[DesignData.Chassis("peltast").Index];

        // Rapid II and Supplied II on a Knight: sight 10 + 3, and supply 3 - 2 would be 1 but stops at 2.
        Assert.Equal((13, 2), (knight.Sight, knight.Supply));

        // Supplied II and Overgunned I on a Peltast (supply 2, already at the minimum): range 5 x 1.5.
        Assert.Equal((2, "7.5"), (peltast.Supply, peltast.Range.ToString()));

        // Standardized I on a Peltast: 12 s x 0.8 = 192 ticks, and a pair in 1.25 times that.
        var paired = Resolve(1, 1, 1, 2, 1, 1).Blueprints[DesignData.Chassis("peltast").Index];
        Assert.Equal((192, 2, 240), (paired.BuildTicks, paired.BatchSize, paired.BatchBuildTicks));
    }

    /// <summary>"Light footprint: costs 1 less supply, minimum 1" for every line's supply.</summary>
    [Theory]
    [InlineData("peltast", 2, 1)]
    [InlineData("hippeus", 3, 2)]
    public void LightFootprintTakesOneSupplyOff(string chassis, int supply, int reduced)
    {
        Assert.Equal(supply, Blueprint(chassis, 1, 1, 1, 1, 2, 1).Supply);
        Assert.Equal(reduced, Blueprint(chassis, 1, 1, 1, 1, 3, 1).Supply);
    }

    [Fact]
    public void ABuildTimeNeverRoundsToNoTicks()
    {
        var files = DesignData.Files();
        files.Edit(DesignData.GuardPath, "\"build_seconds\": 18", "\"build_seconds\": \"0.05\"");
        files.Edit(DesignData.RulesPath, "\"manufacturing_time\": \"0.80\"", "\"manufacturing_time\": \"0.4\"");
        var data = DataAssert.Valid(files);

        var hoplite = TechResolver.Resolve(data, DesignData.Mix(1, 1, 1, 3, 1, 1)).Blueprints[DesignData.Chassis("hoplite").Index];

        // One tick x 0.4^2 is 0.16 of a tick, and a pair takes 0.24 of one.
        Assert.Equal((1, 1), (hoplite.BuildTicks, hoplite.BatchBuildTicks));
    }

    [Fact]
    public void AccentLevelIsZeroForAnAccentTheUnitDoesNotCarry()
    {
        var knight = Blueprint("knight", 5, 5, 3, 2, 2, 3);

        Assert.Equal(
            [2, 2, 0, 0, 0, 0],
            new[] { Firepower, Protection, Mobility, Manufacturing, Logistics, 5 }.Select(knight.AccentLevel));
    }

    private static ChassisBlueprint Blueprint(string chassis, int fp, int pr, int mo, int mf, int lg, int ex) =>
        TechResolver.Resolve(DesignData.Data, DesignData.Mix(fp, pr, mo, mf, lg, ex)).Blueprints[DesignData.Chassis(chassis).Index];

    private static (long HitPoints, long DamagePerSecond, int SpeedRaw, int Materials, int Energy, int BuildTicks) Numbers(ChassisBlueprint blueprint) =>
        (blueprint.HitPoints, blueprint.DamagePerSecond, blueprint.SpeedPerTick.Raw, blueprint.Materials, blueprint.Energy, blueprint.BuildTicks);

    private static string Names(ChassisBlueprint blueprint) => string.Join(
        ", ",
        blueprint.Accents.Select(accent => $"{DesignData.Data.AccentOf(accent.Track)!.Name} {(accent.Level == 1 ? "I" : "II")}"));

    /// <summary>The unlocked chassis of the highest epoch in a line.</summary>
    private static string Newest(PlayerTech tech, string line) =>
        tech.Blueprints.Where(blueprint => blueprint.Unlocked && blueprint.Line.Id == line).MaxBy(blueprint => blueprint.Chassis.Epoch)!.Chassis.Id;
}
