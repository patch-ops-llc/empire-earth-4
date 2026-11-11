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

        var blueprints = new ChassisBlueprint[data.Chassis.Count];
        for (var chassis = 0; chassis < blueprints.Length; chassis++)
        {
            blueprints[chassis] = BlueprintResolver.Resolve(data, ownTiers, data.Chassis[chassis]);
        }

        return new PlayerTech(
            ownTiers,
            TechRules.Epoch(data, ownTiers),
            epochCap,
            TechRules.SupplyCap(data, ownTiers),
            TechRules.IncomeMultiplier(data, ownTiers),
            research,
            blueprints);
    }

    /// <summary>
    /// What researching the next tier of <paramref name="track"/> would change, against the epoch cap
    /// in the data. Null when the track is at its last tier.
    /// </summary>
    public static ResearchPreview? Preview(GameData data, IReadOnlyList<int> tiers, int track)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Preview(data, tiers, track, data.Rules.Match.EpochCap);
    }

    /// <summary>
    /// What researching the next tier of <paramref name="track"/> would change. Null when the track
    /// is at its last tier. The preview is also given for a tier that may not be researched yet,
    /// so the grid can show what lies behind the reach limit; <see cref="ResearchPreview.Offer"/>
    /// says whether it may start.
    /// </summary>
    public static ResearchPreview? Preview(GameData data, IReadOnlyList<int> tiers, int track, int epochCap)
    {
        var before = Resolve(data, tiers, epochCap);
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(track, data.Tracks.Count);
        if (before.Tiers[track] >= data.TierCount)
        {
            return null;
        }

        int[] raised = [.. before.Tiers];
        raised[track]++;
        var after = Resolve(data, raised, epochCap);

        var unlocked = new List<int>();
        var changes = new List<AccentChange>();
        for (var chassis = 0; chassis < before.Blueprints.Count; chassis++)
        {
            var old = before.Blueprints[chassis];
            var updated = after.Blueprints[chassis];
            if (!old.Unlocked)
            {
                if (updated.Unlocked)
                {
                    unlocked.Add(chassis);
                }

                continue;
            }

            for (var accentTrack = 0; accentTrack < data.Tracks.Count; accentTrack++)
            {
                var levelBefore = old.AccentLevel(accentTrack);
                var levelAfter = updated.AccentLevel(accentTrack);
                if (levelBefore != levelAfter)
                {
                    changes.Add(new AccentChange(chassis, accentTrack, levelBefore, levelAfter));
                }
            }
        }

        return new ResearchPreview(before.Research[track], before, after, unlocked, changes);
    }
}
