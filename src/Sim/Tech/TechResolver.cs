using Sim.Data;

namespace Sim.Tech;

/// <summary>
/// The resolver: one pure function from a player's track tiers and the data to everything derived
/// from them. The simulation calls it at match start and whenever a research finishes. It reads no
/// world and keeps nothing, so the research grid can call it again with one tier raised and compare.
/// </summary>
public static class TechResolver
{
    /// <summary>Resolves <paramref name="tiers"/>, one per track, against the epoch cap in the data.</summary>
    public static PlayerTech Resolve(GameData data, IReadOnlyList<int> tiers)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Resolve(data, tiers, data.Rules.Match.EpochCap);
    }

    /// <summary>Resolves <paramref name="tiers"/>, one per track, for a match with this epoch cap.</summary>
    public static PlayerTech Resolve(GameData data, IReadOnlyList<int> tiers, int epochCap)
    {
        TechRules.CheckTiers(data, tiers);
        TechRules.CheckEpochCap(data, epochCap);

        int[] ownTiers = [.. tiers];
        var research = new ResearchOffer[ownTiers.Length];
        for (var track = 0; track < research.Length; track++)
        {
            research[track] = TechRules.Research(data, ownTiers, track, epochCap);
        }

        return new PlayerTech(
            ownTiers,
            TechRules.Epoch(data, ownTiers),
            epochCap,
            TechRules.SupplyCap(data, ownTiers),
            TechRules.IncomeMultiplier(data, ownTiers),
            research);
    }
}
