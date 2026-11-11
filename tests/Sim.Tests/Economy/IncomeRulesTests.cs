using Sim.Data;
using Sim.Economy;
using Sim.Numerics;
using Sim.Tests.Data;
using Sim.Tests.Setup;

namespace Sim.Tests.Economy;

/// <summary>Design 4.1 and 4.2: one payment is the rate for a second times the Extraction multiplier, exact, rounded once to thousandths.</summary>
public class IncomeRulesTests
{
    private static readonly ResourceRules Rules = TestMatches.Data.Resources;

    [Theory]
    [InlineData(1, 750, 375, 500)]
    [InlineData(2, 840, 420, 560)]
    [InlineData(3, 941, 470, 627)] // 0.9408, 0.4704 and 0.6272
    [InlineData(5, 1180, 590, 787)] // 1.18013952, 0.59006976 and 0.78675968
    [InlineData(9, 1857, 928, 1238)] // 1.8569724, 0.9284862 and 1.2379816
    public void APaymentIsTheRateForASecondTimesTheExtractionMultiplierRoundedOnce(int tier, long materials, long depleted, long energy)
    {
        var multiplier = Fraction.Parse("1.12").Pow(tier - 1);

        Assert.Equal(materials, IncomeRules.Payment(Rules, Resource.Materials, multiplier, depleted: false));
        Assert.Equal(depleted, IncomeRules.Payment(Rules, Resource.Materials, multiplier, depleted: true));
        Assert.Equal(energy, IncomeRules.Payment(Rules, Resource.Energy, multiplier, depleted: false));
    }

    /// <summary>The half that decides the rounding is exact: 0.03 a minute is 0.0005 a second, which rounds up.</summary>
    [Theory]
    [InlineData("0.03", 1)]
    [InlineData("0.0299", 0)]
    [InlineData("0.09", 2)]
    [InlineData("0.0899", 1)]
    public void APaymentIsRoundedHalfUp(string perMinute, long payment)
    {
        Assert.True(DataNumber.TryParse(perMinute, out var rate));
        var rules = Rules with { Energy = Rules.Energy with { PerWorkerPerMinute = rate } };

        Assert.Equal(payment, IncomeRules.Payment(rules, Resource.Energy, Fraction.One, depleted: false));
    }

    [Fact]
    public void ARateSoLowThatAPaymentRoundsToNothingIsRejected()
    {
        var energy = SampleData.With(SampleData.ResourcesPath, "\"per_worker_per_minute\": 30", "\"per_worker_per_minute\": \"0.02\"");
        DataAssert.RejectedOnce(energy, SampleData.ResourcesPath, "$.energy", "a worker's payment for one second rounds to nothing at Extraction tier 1");

        // 0.05 a minute pays 0.001 a second at the whole rate and nothing at half of it.
        var materials = SampleData.With(SampleData.ResourcesPath, "\"per_worker_per_minute\": 45", "\"per_worker_per_minute\": \"0.05\"");
        DataAssert.RejectedOnce(materials, SampleData.ResourcesPath, "$.materials", "a worker's payment for one second rounds to nothing at Extraction tier 1");

        DataAssert.Valid(SampleData.With(SampleData.ResourcesPath, "\"per_worker_per_minute\": 30", "\"per_worker_per_minute\": \"0.03\""));
    }
}
