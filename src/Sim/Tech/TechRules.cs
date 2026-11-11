using Sim.Data;
using Sim.Numerics;

namespace Sim.Tech;

/// <summary>
/// The rules that turn a player's track tiers into their epoch, research prices, supply cap and
/// income (design 1.3, 1.4 and section 4). Every method is a pure function of the data and the
/// tiers, which are given in the order of <see cref="GameData.Tracks"/>.
/// </summary>
public static class TechRules
{
    /// <summary>
    /// Design 1.4: the highest tier that at least <see cref="EpochRules.TracksNeeded"/> tracks have reached.
    /// </summary>
    public static int Epoch(GameData data, IReadOnlyList<int> tiers)
    {
        CheckTiers(data, tiers);

        var sorted = tiers.ToArray();
        Array.Sort(sorted);
        return sorted[^data.Rules.Epoch.TracksNeeded];
    }

    /// <summary>Design 4.1: the base plus a fixed amount per Logistics tier.</summary>
    public static int SupplyCap(GameData data, IReadOnlyList<int> tiers)
    {
        CheckTiers(data, tiers);

        var rules = data.Rules.SupplyCap;
        return rules.Base + (rules.PerLogisticsTier * tiers[data.TrackRoles.Logistics]);
    }

    /// <summary>
    /// Design 4.2: what worker income is multiplied by, exactly. Each tier of Extraction above the
    /// first multiplies it once more. The economy rounds once, after it has multiplied in the rate.
    /// </summary>
    public static Fraction IncomeMultiplier(GameData data, IReadOnlyList<int> tiers)
    {
        CheckTiers(data, tiers);

        return data.Rules.PerTier.Income.ToFraction().Pow(tiers[data.TrackRoles.Extraction] - 1);
    }

    /// <summary>
    /// Design 1.3: whether the next tier of <paramref name="track"/> may be researched and what it
    /// costs. No track may pass <paramref name="epochCap"/>, the match's epoch cap.
    /// </summary>
    public static ResearchOffer Research(GameData data, IReadOnlyList<int> tiers, int track, int epochCap)
    {
        CheckTiers(data, tiers);
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(track, data.Tracks.Count);
        CheckEpochCap(data, epochCap);

        var tier = tiers[track] + 1;
        if (tier > data.TierCount)
        {
            return new ResearchOffer(track, tier, ResearchStatus.AtTopTier, null);
        }

        if (tier > epochCap)
        {
            return new ResearchOffer(track, tier, ResearchStatus.AboveEpochCap, null);
        }

        var epoch = Epoch(data, tiers);
        if (tier > epoch + data.Rules.Epoch.Reach)
        {
            return new ResearchOffer(track, tier, ResearchStatus.BeyondReach, null);
        }

        var band = tier <= epoch ? ResearchBand.CatchUp : tier == epoch + 1 ? ResearchBand.Frontier : ResearchBand.Ahead;
        var multiplier = band switch
        {
            ResearchBand.CatchUp => data.Rules.ResearchPrice.CatchUp,
            ResearchBand.Frontier => data.Rules.ResearchPrice.Frontier,
            _ => data.Rules.ResearchPrice.Ahead,
        };

        var table = data.ResearchOf(tier);
        var cost = multiplier.Cost.ToFraction();
        var price = new ResearchPrice(
            band,
            checked((int)(cost * table.Materials).RoundHalfUp()),
            checked((int)(cost * table.Energy).RoundHalfUp()),
            AtLeastOneTick((multiplier.Time.ToFraction() * table.Ticks).RoundHalfUp()));
        return new ResearchOffer(track, tier, ResearchStatus.Available, price);
    }

    /// <summary>A time that rounds to nothing still takes one tick, so that nothing finishes in the tick it starts.</summary>
    internal static int AtLeastOneTick(long ticks) => checked((int)Math.Max(1, ticks));

    internal static void CheckTiers(GameData data, IReadOnlyList<int> tiers)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(tiers);
        if (tiers.Count != data.Tracks.Count)
        {
            throw new ArgumentException($"Expected one tier for each of the {data.Tracks.Count} tracks, found {tiers.Count}.", nameof(tiers));
        }

        for (var track = 0; track < tiers.Count; track++)
        {
            if (tiers[track] < 1 || tiers[track] > data.TierCount)
            {
                throw new ArgumentException(
                    $"The tier of {data.Tracks[track].Id} must be between 1 and {data.TierCount}, found {tiers[track]}.",
                    nameof(tiers));
            }
        }
    }

    internal static void CheckEpochCap(GameData data, int epochCap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(epochCap, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(epochCap, data.TierCount);
    }
}
