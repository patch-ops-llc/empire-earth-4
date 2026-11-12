using Sim.Numerics;
using Sim.Research;
using Sim.Tests.Tech;

namespace Sim.Tests.Research;

public class HitPointRulesTests
{
    /// <summary>Design 2.3, "Hit points on update": the same fraction of the new maximum, rounded once, half up.</summary>
    [Theory]
    [InlineData(130_000, 130_000, 162_500, 162_500)]
    [InlineData(52_000, 130_000, 162_500, 65_000)]
    [InlineData(65_000, 130_000, 162_500, 81_250)]
    [InlineData(0, 130_000, 162_500, 0)]
    [InlineData(1, 130_000, 162_500, 1)]
    [InlineData(2, 130_000, 162_500, 3)]
    [InlineData(3, 130_000, 162_500, 4)]
    [InlineData(100_001, 130_000, 162_500, 125_001)]
    [InlineData(77_777, 130_000, 130_000, 77_777)]
    [InlineData(1, 3, 2, 1)]
    [InlineData(1, 1_000_000, 1, 1)]
    [InlineData(9_000_000_000_000_000, 9_000_000_000_000_000, 9_000_000_000_000_001, 9_000_000_000_000_001)]
    [InlineData(4_500_000_000_000_000, 9_000_000_000_000_000, 9_000_000_000_000_000, 4_500_000_000_000_000)]
    public void AUnitKeepsItsFractionOfHitPoints(long hitPoints, long oldMaximum, long newMaximum, long expected) =>
        Assert.Equal(expected, HitPointRules.KeepFraction(hitPoints, oldMaximum, newMaximum));

    [Theory]
    [InlineData(-1, 10, 10)]
    [InlineData(11, 10, 20)]
    [InlineData(5, 0, 10)]
    [InlineData(5, 10, 0)]
    [InlineData(5, -10, 10)]
    public void HitPointsOutsideTheirMaximumAreRefused(long hitPoints, long oldMaximum, long newMaximum) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HitPointRules.KeepFraction(hitPoints, oldMaximum, newMaximum));

    /// <summary>Design 4.1: a worker has 50 hit points at Protection 1, x1.25 for each tier of its owner.</summary>
    [Theory]
    [InlineData(1, 50_000)]
    [InlineData(2, 62_500)]
    [InlineData(3, 78_125)]
    [InlineData(4, 97_656)]
    [InlineData(9, 298_023)]
    public void AWorkersHitPointsFollowItsOwnersProtectionTier(int tier, long expected) =>
        Assert.Equal(expected, HitPointRules.AtProtectionTier(DesignData.Data, 50, tier));

    /// <summary>Design 2.5: a Capitol has 2,000 hit points, and a structure's follow Protection the same way.</summary>
    [Fact]
    public void AStructuresHitPointsFollowItsOwnersProtectionTierTheSameWay()
    {
        Assert.Equal(2_000_000, HitPointRules.AtProtectionTier(DesignData.Data, 2000, 1));
        Assert.Equal(4_882_813, HitPointRules.AtProtectionTier(DesignData.Data, 2000, 5));
        Assert.Equal(1_563, HitPointRules.AtProtectionTier(DesignData.Data, Fraction.Parse("1.25"), 2));
    }

    [Fact]
    public void ATierOrABaseThatCannotBeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HitPointRules.AtProtectionTier(DesignData.Data, 50, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => HitPointRules.AtProtectionTier(DesignData.Data, 50, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => HitPointRules.AtProtectionTier(DesignData.Data, 0, 1));
        Assert.Throws<ArgumentNullException>(() => HitPointRules.AtProtectionTier(null!, 50, 1));
    }

    /// <summary>A worker whose owner researches Protection keeps its fraction, as a unit does.</summary>
    [Fact]
    public void AWorkerAtTwoFifthsStaysAtTwoFifths()
    {
        var before = HitPointRules.AtProtectionTier(DesignData.Data, 50, 1);
        var after = HitPointRules.AtProtectionTier(DesignData.Data, 50, 2);

        Assert.Equal(25_000, HitPointRules.KeepFraction(20_000, before, after));
    }
}
