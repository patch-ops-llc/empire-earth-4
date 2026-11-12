using System.Globalization;
using System.Text;
using Sim;
using Sim.Data;
using Sim.Numerics;
using Sim.Tech;

namespace Game;

/// <summary>How the research grid says things: times, prices, names and what a perk does.</summary>
public static class ResearchWords
{
    /// <summary>A number of ticks as the player reads it, rounded up to a second: "45 s", "1 min 15 s".</summary>
    public static string Duration(int ticks)
    {
        var seconds = (Math.Max(ticks, 0) + SimInfo.TicksPerSecond - 1) / SimInfo.TicksPerSecond;
        if (seconds < 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{seconds} s");
        }

        return seconds % 60 == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 60} min")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60} min {seconds % 60} s");
    }

    /// <summary>A price in full: "100 Materials and 100 Energy".</summary>
    public static string Price(int materials, int energy)
    {
        if (energy == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{materials} Materials");
        }

        return materials == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{energy} Energy")
            : string.Create(CultureInfo.InvariantCulture, $"{materials} Materials and {energy} Energy");
    }

    /// <summary>A whole number as text.</summary>
    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The level of an accent as it is written on a badge.</summary>
    public static string Level(int level) => level >= 2 ? "II" : "I";

    /// <summary>An ID of the data as a name: "field_cannon" is "Field cannon".</summary>
    public static string NameOf(string id)
    {
        if (id.Length == 0)
        {
            return id;
        }

        var text = id.Replace('_', ' ');
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>The name of an epoch, counted from 1.</summary>
    public static string Epoch(GameData data, int epoch) =>
        epoch >= 1 && epoch <= data.EpochNames.Count ? data.EpochNames[epoch - 1] : Number(epoch);

    /// <summary>What a track's tiers raise, in a word or two (design 1.1).</summary>
    public static string Raises(GameData data, int track)
    {
        var roles = data.TrackRoles;
        if (track == roles.Firepower)
        {
            return "Damage";
        }

        if (track == roles.Protection)
        {
            return "Hit points";
        }

        if (track == roles.Mobility)
        {
            return "Speed";
        }

        if (track == roles.Manufacturing)
        {
            return "Unit cost";
        }

        if (track == roles.Logistics)
        {
            return "Supply cap";
        }

        return track == roles.Extraction ? "Income" : string.Empty;
    }

    /// <summary>What one more tier of a track does, as a sentence, with the numbers of the rules and of the preview.</summary>
    public static string Effect(GameData data, int track, ResearchPreview preview)
    {
        var roles = data.TrackRoles;
        var rules = data.Rules;

        // A unit uses a track's tier only inside a window round its own epoch (design 2.3), so one
        // more tier changes the units of these epochs and no others.
        var tier = preview.Offer.Tier;
        var first = Math.Max(1, tier - rules.EffectiveTierWindow);
        var last = Math.Min(data.TierCount, tier + rules.EffectiveTierWindow - 1);
        var units = first == last
            ? string.Create(CultureInfo.InvariantCulture, $"units of epoch {first}")
            : string.Create(CultureInfo.InvariantCulture, $"units of epochs {first} to {last}");

        if (track == roles.Firepower)
        {
            return $"Damage x{rules.PerTier.Damage} and price x{rules.PerStep.QualityCost} for {units}.";
        }

        if (track == roles.Protection)
        {
            return $"Hit points x{rules.PerTier.HitPoints} and price x{rules.PerStep.QualityCost} for {units}.";
        }

        if (track == roles.Mobility)
        {
            return $"Speed x{rules.PerTier.Speed} and price x{rules.PerStep.QualityCost} for {units}.";
        }

        if (track == roles.Manufacturing)
        {
            return $"Price x{rules.PerStep.ManufacturingCost} and build time x{rules.PerStep.ManufacturingTime} for {units}.";
        }

        if (track == roles.Logistics)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Supply cap {preview.Before.SupplyCap} to {preview.After.SupplyCap}.");
        }

        if (track == roles.Extraction)
        {
            return $"Worker income x{rules.PerTier.Income}, for both resources.";
        }

        return string.Empty;
    }

    /// <summary>What the perks of one level of an accent do, as the rest of a sentence.</summary>
    public static string Perks(PerkSet perks)
    {
        var parts = new List<string>();
        if (perks.RangeBonus is { } range)
        {
            parts.Add($"{More(range.Multiplier)} range for units that shoot");
        }

        if (perks.AttackRateBonus is { } rate)
        {
            parts.Add($"{More(rate.Multiplier)} attack rate in melee");
        }

        if (perks.Splash is { } splash)
        {
            parts.Add($"every hit also deals {Percent(splash.Share)} of its damage to enemies within {splash.Radius} tile of the target");
        }

        if (perks.BlastRadiusBonus is { } blast)
        {
            parts.Add($"{More(blast.Multiplier)} blast radius for weapons that have one");
        }

        if (perks.AreaDamageReduction is { } area)
        {
            parts.Add($"{Less(area.Multiplier)} area damage taken");
        }

        if (perks.Plating is { } plating)
        {
            parts.Add($"{Less(plating.Multiplier)} damage from attackers whose Firepower is below the unit's Protection");
        }

        if (perks.SightBonus is { } sight)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"+{sight.Tiles} sight"));
        }

        if (perks.Charge is { } charge)
        {
            parts.Add($"{More(charge.Multiplier)} damage on the first attack after moving {charge.Distance} tiles");
        }

        if (perks.Batch is { } batch)
        {
            parts.Add($"an order makes two units in {batch.TimeMultiplier} times the build time of one");
        }

        if (perks.DeathRefund is { } refund)
        {
            parts.Add($"{Percent(refund.Share)} of the unit's Materials come back when it dies");
        }

        if (perks.Regeneration is { } regeneration)
        {
            parts.Add($"regains {Percent(regeneration.SharePerSecond)} of its hit points a second after {Duration(regeneration.DelayTicks)} without damage");
        }

        if (perks.SupplyReduction is { } supply)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"costs {supply.Amount} less supply"));
        }

        return string.Join("; ", parts);
    }

    /// <summary>A list as a sentence has it: "A", "A and B", "A, B and C".</summary>
    public static string List(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        if (items.Count == 1)
        {
            return items[0];
        }

        var text = new StringBuilder();
        for (var i = 0; i < items.Count - 1; i++)
        {
            text.Append(i == 0 ? string.Empty : ", ").Append(items[i]);
        }

        return text.Append(" and ").Append(items[^1]).ToString();
    }

    // A share as a percentage: 0.3 is "30%".
    private static string Percent(DataNumber share) => Whole(share.ToFraction() * 100) + "%";

    // What a multiplier above 1 adds: 1.15 is "+15%".
    private static string More(DataNumber multiplier) => "+" + Whole((multiplier.ToFraction() - 1) * 100) + "%";

    // What a multiplier below 1 takes away: 0.75 is "25% less".
    private static string Less(DataNumber multiplier) => Whole((Fraction.One - multiplier.ToFraction()) * 100) + "% less";

    private static string Whole(Fraction value) => Number(value.RoundHalfUp());
}
