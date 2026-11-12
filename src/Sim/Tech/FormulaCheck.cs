using Sim.Data;
using Sim.Numerics;

namespace Sim.Tech;

/// <summary>
/// Runs every formula over every value it can meet, so that numbers too large or too finely
/// written for exact arithmetic are an error when the data is loaded and never an overflow in the
/// middle of a match.
/// </summary>
internal static class FormulaCheck
{
    private const string Advice = "lower the values involved or write the multipliers in rules.jsonc with fewer decimal places";

    public static void Check(GameData data, ErrorList errors)
    {
        try
        {
            var income = data.Rules.PerTier.Income.ToFraction();
            for (var tier = 1; tier <= data.TierCount; tier++)
            {
                _ = income.Pow(tier - 1);
            }
        }
        catch (OverflowException)
        {
            errors.Add(DataRules.RulesFile, "$.per_tier.income", $"the income multiplier is too large to compute exactly by tier {data.TierCount}; {Advice}");
        }

        foreach (var line in data.Lines)
        {
            for (var i = 0; i < line.Chassis.Count; i++)
            {
                try
                {
                    Compute(data, line, line.Chassis[i]);
                }
                catch (OverflowException)
                {
                    errors.Add(
                        $"{DataRules.LinesFolder}{line.Id}{DataRules.FileExtension}",
                        $"$.chassis[{i}]",
                        $"a stat or price of chassis '{line.Chassis[i].Id}' is too large to compute exactly at some tier mix; {Advice}");
                }
            }
        }

        StructureBlueprintResolver.Check(data, errors);
    }

    /// <summary>
    /// Each quantity depends on few offsets, so every case is covered without trying every mix: hit
    /// points, damage, speed and time on one track each, and the price on the sum of three offsets
    /// and the Manufacturing offset.
    /// </summary>
    private static void Compute(GameData data, LineDef line, ChassisDef chassis)
    {
        var epoch = chassis.Epoch;
        var lowest = UnitFormulas.EffectiveTier(data, 1, epoch);
        var highest = UnitFormulas.EffectiveTier(data, data.TierCount, epoch);

        for (var tier = lowest; tier <= highest; tier++)
        {
            _ = UnitFormulas.HitPoints(data, line, tier).RoundHalfUp(UnitFormulas.Thousandths);
            var damagePerSecond = UnitFormulas.DamagePerSecond(data, line, tier);
            _ = damagePerSecond.RoundHalfUp(UnitFormulas.Thousandths);
            _ = UnitFormulas.DamagePerHit(damagePerSecond, chassis.Attack).RoundHalfUp(UnitFormulas.Thousandths);
            _ = UnitFormulas.SpeedPerTick(data, line, tier);

            var manufacturingOffset = tier - epoch;
            var buildTicks = UnitFormulas.BuildTicks(data, line, manufacturingOffset);
            _ = TechRules.AtLeastOneTick(buildTicks.RoundHalfUp());
            foreach (var accent in data.Accents)
            {
                if (accent.PerksAt(int.MaxValue).Batch is { } batch)
                {
                    _ = TechRules.AtLeastOneTick((buildTicks * batch.TimeMultiplier.ToFraction()).RoundHalfUp());
                }
            }

            const int QualityTracks = 3;
            for (var qualityOffsets = QualityTracks * (lowest - epoch); qualityOffsets <= QualityTracks * (highest - epoch); qualityOffsets++)
            {
                var multiplier = UnitFormulas.CostMultiplier(data, epoch, qualityOffsets, manufacturingOffset);
                _ = UnitFormulas.Price(line.Base.Materials, multiplier);
                _ = UnitFormulas.Price(line.Base.Energy, multiplier);
            }
        }

        var attack = chassis.Attack;
        DataNumber? rangeBonus = null;
        DataNumber? blastBonus = null;
        foreach (var accent in data.Accents)
        {
            var perks = accent.PerksAt(int.MaxValue);
            rangeBonus ??= perks.RangeBonus?.Multiplier;
            blastBonus ??= perks.BlastRadiusBonus?.Multiplier;
        }

        if (attack.Range is { } range)
        {
            _ = UnitFormulas.Distance(range, rangeBonus);
        }

        if (attack.BlastRadius is { } blast)
        {
            _ = UnitFormulas.Distance(blast, blastBonus);
        }
    }
}
