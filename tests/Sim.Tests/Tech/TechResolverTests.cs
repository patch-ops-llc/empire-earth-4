using Sim.Numerics;
using Sim.Tech;
using Sim.Tests.Data;

namespace Sim.Tests.Tech;

/// <summary>The resolver puts everything the tiers decide for one player into one value.</summary>
public class TechResolverTests
{
    [Fact]
    public void ResolvesEpochCapsIncomeAndOneResearchOfferPerTrack()
    {
        var data = DesignData.Data;
        var tiers = DesignData.Mix(7, 4, 6, 6, 4, 5);

        var tech = TechResolver.Resolve(data, tiers);

        Assert.Equal(tiers, tech.Tiers);
        Assert.Equal(6, tech.Epoch);
        Assert.Equal(9, tech.EpochCap);
        Assert.Equal(100, tech.SupplyCap);
        Assert.Equal(new Fraction(28, 25).Pow(4), tech.IncomeMultiplier);
        Assert.Equal(
            [
                new ResearchOffer(0, 8, ResearchStatus.Available, new ResearchPrice(ResearchBand.Ahead, 1650, 2850, 2700)),
                new ResearchOffer(1, 5, ResearchStatus.Available, new ResearchPrice(ResearchBand.CatchUp, 175, 275, 900)),
                new ResearchOffer(2, 7, ResearchStatus.Available, new ResearchPrice(ResearchBand.Frontier, 750, 1300, 2400)),
                new ResearchOffer(3, 7, ResearchStatus.Available, new ResearchPrice(ResearchBand.Frontier, 750, 1300, 2400)),
                new ResearchOffer(4, 5, ResearchStatus.Available, new ResearchPrice(ResearchBand.CatchUp, 175, 275, 900)),
                new ResearchOffer(5, 6, ResearchStatus.Available, new ResearchPrice(ResearchBand.CatchUp, 250, 425, 1050)),
            ],
            tech.Research);
    }

    [Fact]
    public void TheEpochCapComesFromTheRulesUnlessTheMatchGivesOne()
    {
        var data = DataAssert.Valid(DesignData.Files().Edit(DesignData.RulesPath, "\"epoch_cap\": 9", "\"epoch_cap\": 4"));
        var tiers = DesignData.Mix(4, 4, 4, 1, 1, 1);

        var byRules = TechResolver.Resolve(data, tiers);
        var byMatch = TechResolver.Resolve(data, tiers, epochCap: 6);

        Assert.Equal(4, byRules.EpochCap);
        Assert.Equal(ResearchStatus.AboveEpochCap, byRules.Research[0].Status);
        Assert.Equal(6, byMatch.EpochCap);
        Assert.Equal(ResearchStatus.Available, byMatch.Research[0].Status);
    }

    [Fact]
    public void TheResultDoesNotChangeWhenTheCallerChangesItsTiersAfterwards()
    {
        var tiers = DesignData.Mix(2, 2, 2, 1, 1, 1);

        var tech = TechResolver.Resolve(DesignData.Data, tiers);
        tiers[0] = 9;

        Assert.Equal([2, 2, 2, 1, 1, 1], tech.Tiers);
    }

    [Fact]
    public void TheSameInputsGiveTheSameResult()
    {
        var first = TechResolver.Resolve(DesignData.Data, DesignData.Mix(6, 3, 7, 4, 3, 5));
        var second = TechResolver.Resolve(DataAssert.Valid(DesignData.Files()), DesignData.Mix(6, 3, 7, 4, 3, 5));

        Assert.Equal(first.Epoch, second.Epoch);
        Assert.Equal(first.SupplyCap, second.SupplyCap);
        Assert.Equal(first.IncomeMultiplier, second.IncomeMultiplier);
        Assert.Equal(first.Research, second.Research);
        Assert.Equal(first.Blueprints.Count, second.Blueprints.Count);
        for (var chassis = 0; chassis < first.Blueprints.Count; chassis++)
        {
            var (one, other) = (first.Blueprints[chassis], second.Blueprints[chassis]);
            Assert.Equal(one.Chassis, other.Chassis);
            Assert.Equal(one.Unlocked, other.Unlocked);
            Assert.Equal(one.EffectiveTiers, other.EffectiveTiers);
            Assert.Equal(one.Accents, other.Accents);
            Assert.Equal(one.Perks, other.Perks);
            Assert.Equal(
                (one.HitPoints, one.DamagePerSecond, one.DamagePerHit, one.SpeedPerTick, one.Range, one.BlastRadius),
                (other.HitPoints, other.DamagePerSecond, other.DamagePerHit, other.SpeedPerTick, other.Range, other.BlastRadius));
            Assert.Equal(
                (one.Sight, one.Supply, one.Materials, one.Energy, one.BuildTicks, one.BatchSize, one.BatchBuildTicks),
                (other.Sight, other.Supply, other.Materials, other.Energy, other.BuildTicks, other.BatchSize, other.BatchBuildTicks));
        }
    }
}
