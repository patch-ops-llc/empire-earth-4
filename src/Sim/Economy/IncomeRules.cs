using Sim.Data;
using Sim.Numerics;

namespace Sim.Economy;

/// <summary>What one payment is (design 4.1 and 4.2).</summary>
public static class IncomeRules
{
    private const int SecondsPerMinute = 60;

    /// <summary>
    /// What a worker is paid for one second, in thousandths of the resource: the rate per minute
    /// over 60, times the owner's Extraction multiplier, times the depleted rate for a deposit
    /// that has delivered its full-rate amount. The product is exact and rounded once, half up.
    /// </summary>
    public static long Payment(ResourceRules rules, Resource resource, Fraction incomeMultiplier, bool depleted)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var perMinute = resource == Resource.Materials ? rules.Materials.PerWorkerPerMinute : rules.Energy.PerWorkerPerMinute;
        var payment = perMinute.ToFraction() / SecondsPerMinute * incomeMultiplier;
        if (depleted)
        {
            payment *= rules.Materials.DepletedRate.ToFraction();
        }

        return payment.RoundHalfUp(Stockpiles.PerResource);
    }

    /// <summary>
    /// Works out every payment the data can lead to, so that a rate too finely written for exact
    /// arithmetic, or one so low that a payment rounds to nothing, is an error when the data is
    /// loaded and never a surprise in the middle of a match.
    /// </summary>
    internal static void Check(GameData data, ErrorList errors)
    {
        var perTier = data.Rules.PerTier.Income.ToFraction();
        foreach (var (resource, location) in new[] { (Resource.Materials, "$.materials"), (Resource.Energy, "$.energy") })
        {
            try
            {
                for (var tier = 1; tier <= data.TierCount; tier++)
                {
                    var multiplier = perTier.Pow(tier - 1);
                    var lowest = Payment(data.Resources, resource, multiplier, depleted: resource == Resource.Materials);
                    _ = Payment(data.Resources, resource, multiplier, depleted: false);
                    if (lowest <= 0)
                    {
                        errors.Add(
                            DataRules.ResourcesFile,
                            location,
                            $"a worker's payment for one second rounds to nothing at Extraction tier {tier}; raise \"per_worker_per_minute\"");
                        break;
                    }
                }
            }
            catch (OverflowException)
            {
                errors.Add(
                    DataRules.ResourcesFile,
                    location,
                    "a worker's payment is too large to compute exactly at some Extraction tier; write the rates and multipliers with fewer decimal places");
            }
        }
    }
}
