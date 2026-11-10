using Sim.Data;
using Sim.Tech;
using Sim.Tests.Data;

namespace Sim.Tests.Tech;

/// <summary>
/// Data whose numbers cannot be computed exactly is rejected when it is loaded, so that no tier mix
/// can overflow in the middle of a match.
/// </summary>
public class FormulaCheckTests
{
    [Fact]
    public void AMultiplierWithTooManyDecimalPlacesForItsLargestExponentIsRejected()
    {
        var files = DesignData.Files().Edit(DesignData.RulesPath, "\"hp\": \"1.25\"", "\"hp\": \"1.123456789\"");

        var result = DataLoader.Load(files);

        // 1.123456789 to the fourth power no longer fits, and every chassis from epoch 3 up can reach it.
        Assert.False(result.IsValid);
        Assert.Equal(33, result.Errors.Count);
        Assert.Equal(
            "lines/air.jsonc: $.chassis[0]: a stat or price of chassis 'biplane' is too large to compute exactly at some tier mix; "
            + "lower the values involved or write the multipliers in rules.jsonc with fewer decimal places",
            result.Errors[0].ToString());
    }

    [Fact]
    public void TheSameMultiplierIsAcceptedWhenNoChassisCanReachTheExponent()
    {
        // The sample data has four tiers, so the multiplier is raised to the third power at most.
        var files = SampleData.With(SampleData.RulesPath, "\"hp\": \"1.25\"", "\"hp\": \"1.123456789\"");

        var data = DataAssert.Valid(files);

        Assert.Equal("grenadier", data.Chassis[0].Id);
        Assert.Equal(184_337, TechResolver.Resolve(data, [1, 4, 1, 1, 1, 1]).Blueprints[0].HitPoints);
    }

    [Fact]
    public void ASpeedTooLargeForTheDistanceUnitIsRejectedOnTheChassisThatReachIt()
    {
        var files = DesignData.Files().Edit(DesignData.RulesPath, "\"speed\": \"1.08\"", "\"speed\": \"5\"");

        var result = DataLoader.Load(files);

        // Speed x 5^8 at Mobility 9 is more tiles per tick than the distance type holds. Only a
        // chassis of epoch 7 or later can use Mobility 9: three in each of the five lines.
        Assert.False(result.IsValid);
        Assert.Equal(15, result.Errors.Count);
        Assert.Contains(result.Errors, error => error.ToString().StartsWith("lines/air.jsonc: $.chassis[2]: a stat or price of chassis 'strike_jet'", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error => error.File == "lines/air.jsonc" && error.Location == "$.chassis[1]");
    }

    [Fact]
    public void APriceTooLargeForAWholeNumberIsRejected()
    {
        var files = DesignData.Files().Edit(DesignData.RulesPath, "\"quality_cost\": \"1.15\"", "\"quality_cost\": \"9\"");

        var result = DataLoader.Load(files);

        // 70 Materials x 9^8 is more than a price holds. The Man-at-Arms, epoch 3, is the first
        // Guard chassis that can have three tracks two ahead of it and so reach that exponent.
        Assert.False(result.IsValid);
        Assert.All(result.Errors, error => Assert.Contains("is too large to compute exactly", error.Message, StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.File == DesignData.GuardPath && error.Location == "$.chassis[2]");
        Assert.DoesNotContain(result.Errors, error => error.File == DesignData.GuardPath && error.Location == "$.chassis[1]");
    }

    [Fact]
    public void AnIncomeMultiplierTooFineForTheLastTierIsRejected()
    {
        var files = DesignData.Files().Edit(DesignData.RulesPath, "\"income\": \"1.12\"", "\"income\": \"1.123456789\"");

        DataAssert.RejectedOnce(
            files,
            DesignData.RulesPath,
            "$.per_tier.income",
            "the income multiplier is too large to compute exactly by tier 9");
    }

    /// <summary>Whatever passes the check resolves: every mix of the design's data, at the extremes of every track.</summary>
    [Fact]
    public void EveryCornerMixOfTheDesignDataResolves()
    {
        var data = DesignData.Data;
        int[] corners = [1, 5, 9];
        var tiers = new int[6];
        var resolved = 0;

        void Visit(int track)
        {
            if (track == tiers.Length)
            {
                var tech = TechResolver.Resolve(data, tiers);
                Assert.All(tech.Blueprints, blueprint =>
                {
                    Assert.True(blueprint.HitPoints > 0 && blueprint.DamagePerSecond > 0 && blueprint.SpeedPerTick.Raw > 0);
                    Assert.True(blueprint.Materials > 0 && blueprint.Energy >= 0 && blueprint.BuildTicks > 0);
                    Assert.True(blueprint.Accents.Count <= data.Rules.AccentSlots);
                });
                resolved++;
                return;
            }

            foreach (var tier in corners)
            {
                tiers[track] = tier;
                Visit(track + 1);
            }
        }

        Visit(0);

        Assert.Equal(729, resolved);
    }
}
