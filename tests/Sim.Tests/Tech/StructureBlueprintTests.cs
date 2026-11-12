using Sim.Data;
using Sim.Numerics;
using Sim.Research;
using Sim.Tech;
using Sim.Tests.Data;

namespace Sim.Tests.Tech;

/// <summary>
/// Structure blueprints (design 2.5): hit points under the owner's Protection tier, a Tower's
/// damage under the owner's Firepower tier, and what a tier unlocks. The sample data has four tiers.
/// </summary>
public class StructureBlueprintTests
{
    private static readonly Lazy<GameData> Loaded = new(() => DataAssert.Valid(SampleStructures.Files()));

    private static GameData Data => Loaded.Value;

    [Fact]
    public void APlayerHasOneBlueprintForEveryStructureWithAFile()
    {
        var tech = TechResolver.Resolve(Data, [1, 1, 1, 1, 1, 1]);

        Assert.Equal(Data.Structures, tech.Structures.Select(blueprint => blueprint.Structure));
        foreach (var structure in Data.Structures)
        {
            Assert.Same(structure, tech.StructureOf(structure.Index)!.Structure);
        }

        Assert.All(tech.Structures, blueprint => Assert.True(blueprint.Unlocked));
    }

    [Fact]
    public void AStructureWithoutAFileHasNoBlueprint()
    {
        var files = SampleStructures.Files();
        files.Remove(SampleStructures.TowerPath);
        var data = DataAssert.Valid(files);

        var tech = TechResolver.Resolve(data, [1, 1, 1, 1, 1, 1]);

        Assert.Equal(3, tech.Structures.Count);
        Assert.Null(tech.StructureOf(data.Rules.Structures.ToList().IndexOf("tower")));
        Assert.Equal(
            ["capitol"],
            TechResolver.Resolve(DataAssert.Valid(SampleData.Files()), [1, 1, 1, 1, 1, 1]).Structures.Select(blueprint => blueprint.Structure.Id));
    }

    /// <summary>"All building hit points scale by 1.25 per Protection tier", in thousandths.</summary>
    [Theory]
    [InlineData("capitol", 1, 2_000_000)]
    [InlineData("capitol", 2, 2_500_000)]
    [InlineData("capitol", 3, 3_125_000)]
    [InlineData("capitol", 4, 3_906_250)]
    [InlineData("barracks", 2, 1_250_000)]
    [InlineData("tower", 1, 600_000)]
    [InlineData("tower", 3, 937_500)]
    [InlineData("tower", 4, 1_171_875)]
    [InlineData("wall", 1, 400_000)]
    [InlineData("wall", 4, 781_250)]
    public void HitPointsFollowTheOwnersProtectionTier(string structure, int protection, long hitPoints)
    {
        var blueprint = Blueprint(structure, firepower: 1, protection);

        Assert.Equal(protection, blueprint.ProtectionTier);
        Assert.Equal(hitPoints, blueprint.HitPoints);
    }

    [Fact]
    public void NoOtherTrackChangesAStructuresHitPoints()
    {
        var plain = TechResolver.Resolve(Data, [1, 2, 1, 1, 1, 1]);
        var advanced = TechResolver.Resolve(Data, [4, 2, 4, 4, 4, 4]);

        Assert.Equal(
            plain.Structures.Select(blueprint => blueprint.HitPoints),
            advanced.Structures.Select(blueprint => blueprint.HitPoints));
    }

    /// <summary>
    /// The research code raises a structure's hit points by the same rule. Research may not depend
    /// on the blueprints' code and the other way round, so the two are held together here.
    /// </summary>
    [Fact]
    public void TheBlueprintAgreesWithTheHitPointRuleOfResearchAtEveryTier()
    {
        for (var protection = 1; protection <= Data.TierCount; protection++)
        {
            foreach (var blueprint in TechResolver.Resolve(Data, [1, protection, 1, 1, 1, 1]).Structures)
            {
                Assert.Equal(
                    HitPointRules.AtProtectionTier(Data, blueprint.Structure.HitPoints.ToFraction(), protection),
                    blueprint.HitPoints);
            }
        }
    }

    /// <summary>
    /// "10 damage per second at tier 1 with one attack per second, scaled by the owner's Firepower
    /// tier": damage per second in thousandths, and the exact damage of one hit.
    /// </summary>
    [Theory]
    [InlineData(1, 10_000, 10, 1)]
    [InlineData(2, 12_500, 25, 2)]
    [InlineData(3, 15_625, 125, 8)]
    [InlineData(4, 19_531, 625, 32)]
    public void ATowersDamageFollowsTheOwnersFirepowerTier(int firepower, long damagePerSecond, int hitNumerator, int hitDenominator)
    {
        var attack = Blueprint("tower", firepower, protection: 1).Attack!;

        Assert.Equal(firepower, attack.EffectiveFirepower);
        Assert.Equal(damagePerSecond, attack.DamagePerSecond);
        Assert.Equal(new Fraction(hitNumerator, hitDenominator), attack.DamagePerHit);
    }

    [Fact]
    public void ATowersDamagePerHitIsItsDamagePerSecondTimesItsInterval()
    {
        var files = SampleStructures.With(SampleStructures.TowerPath, "\"interval_seconds\": 1", "\"interval_seconds\": \"2.5\"");

        var attack = TechResolver.Resolve(DataAssert.Valid(files), [3, 1, 1, 1, 1, 1]).Structures.Single(blueprint => blueprint.Structure.Id == "tower").Attack!;

        // 15.625 a second, one shot every 2.5 seconds.
        Assert.Equal(50, attack.Attack.IntervalTicks);
        Assert.Equal(new Fraction(625, 16), attack.DamagePerHit);
        Assert.Equal(15_625, attack.DamagePerSecond);
    }

    [Fact]
    public void ATowersRangeAndShotSpeedDoNotChangeWithATier()
    {
        var early = Blueprint("tower", firepower: 1, protection: 1).Attack!;
        var late = TechResolver.Resolve(Data, [4, 4, 4, 4, 4, 4]).Structures.Single(blueprint => blueprint.Structure.Id == "tower").Attack!;

        Assert.Equal(Fixed.FromInt(7), early.Range);
        Assert.Equal(Fixed.FromInt(7), late.Range);
        Assert.Same(early.Attack, late.Attack);
        Assert.Equal(DataNumber.FromInteger(20), late.Attack.ProjectileSpeed);
    }

    /// <summary>"Hits Air from Protection 5", which is Protection 3 in the sample data.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void ATowerHitsEveryLineOnceItsOwnerHasTheProtectionTierItNames(int protection, bool hitsAir)
    {
        var attack = Blueprint("tower", firepower: 4, protection).Attack!;
        var air = Data.Rules.Lines.ToList().IndexOf("air");

        Assert.Equal(hitsAir ? [] : [air], attack.CannotHit);
    }

    [Fact]
    public void ATowerWithoutThatTierNeverHitsTheLinesItCannotHit()
    {
        var files = SampleStructures.With(SampleStructures.TowerPath, ", \"can_hit_all_from_protection\": 3", "");
        var data = DataAssert.Valid(files);

        var attack = TechResolver.Resolve(data, [4, 4, 4, 4, 4, 4]).Structures.Single(blueprint => blueprint.Structure.Id == "tower").Attack!;

        Assert.Single(attack.CannotHit);
    }

    [Fact]
    public void AStructureWithoutAnAttackHasNone()
    {
        Assert.Null(Blueprint("capitol", firepower: 4, protection: 4).Attack);
        Assert.Null(Blueprint("wall", firepower: 4, protection: 4).Attack);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void AStructureWithAnUnlockTierNeedsThatTierOfItsTrack(int manufacturing, bool unlocked)
    {
        var files = SampleStructures.With(SampleStructures.BarracksPath, "\"track\": \"manufacturing\",", "\"track\": \"manufacturing\", \"unlock_tier\": 3,");
        var data = DataAssert.Valid(files);

        // Every other track is at its top tier, so only the structure's own track counts.
        var barracks = TechResolver.Resolve(data, [4, 4, 4, manufacturing, 4, 4]).Structures.Single(blueprint => blueprint.Structure.Id == "barracks");

        Assert.Equal(unlocked, barracks.Unlocked);
        Assert.Equal(1_953_125, barracks.HitPoints);
    }

    [Fact]
    public void HitPointsTooLargeToComputeExactlyAreRejectedWhenTheDataIsLoaded()
    {
        // Four tiers raise the multiplier to the third power, which no longer fits with 1,000,000 hit points.
        var files = SampleStructures.With(SampleData.RulesPath, "\"hp\": \"1.25\"", "\"hp\": \"1.123456789\"");
        files.Edit(SampleStructures.WallPath, "\"hp\": 400", "\"hp\": \"999999.123456789\"");

        DataAssert.Rejected(files, SampleStructures.WallPath, "$.hp", "the hit points are too large to compute exactly at some Protection tier");
    }

    [Fact]
    public void ATowersDamageTooLargeToComputeExactlyIsRejectedWhenTheDataIsLoaded()
    {
        var files = SampleStructures.With(SampleData.RulesPath, "\"damage\": \"1.25\"", "\"damage\": \"1.123456789\"");
        files.Edit(SampleStructures.TowerPath, "\"dps\": 10", "\"dps\": \"9999.123456789\"");
        files.Edit(SampleStructures.TowerPath, "\"interval_seconds\": 1", "\"interval_seconds\": \"0.35\"");

        DataAssert.Rejected(files, SampleStructures.TowerPath, "$.attack", "the damage or the range is too large to compute exactly at some Firepower tier");
    }

    private static StructureBlueprint Blueprint(string structure, int firepower, int protection) =>
        TechResolver.Resolve(Data, [firepower, protection, 1, 1, 1, 1]).Structures.Single(blueprint => blueprint.Structure.Id == structure);
}
