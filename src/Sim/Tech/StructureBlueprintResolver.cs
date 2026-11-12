using Sim.Data;
using Sim.Numerics;

namespace Sim.Tech;

/// <summary>Builds a <see cref="StructureBlueprint"/> from a player's tiers (design 2.5).</summary>
internal static class StructureBlueprintResolver
{
    private const string Advice = "lower the values involved or write the multipliers in rules.jsonc with fewer decimal places";

    public static StructureBlueprint Resolve(GameData data, IReadOnlyList<int> tiers, StructureDef structure)
    {
        var roles = data.TrackRoles;
        var protection = tiers[roles.Protection];
        var firepower = tiers[roles.Firepower];

        StructureAttackBlueprint? attack = null;
        if (structure.Attack is { } definition)
        {
            var damagePerSecond = DamagePerSecond(data, definition, firepower);
            attack = new StructureAttackBlueprint
            {
                EffectiveFirepower = firepower,
                DamagePerSecond = damagePerSecond.RoundHalfUp(UnitFormulas.Thousandths),
                DamagePerHit = UnitFormulas.DamagePerHit(damagePerSecond, definition.Attack),
                Attack = definition.Attack,
                Range = UnitFormulas.Distance(definition.Attack.Range!.Value),
                CannotHit = definition.CanHitAllFromProtection is { } tier && protection >= tier ? [] : definition.CannotHit,
            };
        }

        return new StructureBlueprint
        {
            Structure = structure,
            Unlocked = structure.Track is not { } track || tiers[track] >= structure.UnlockTier,
            ProtectionTier = protection,
            HitPoints = HitPoints(data, structure, protection).RoundHalfUp(UnitFormulas.Thousandths),
            Attack = attack,
        };
    }

    /// <summary>
    /// Runs the formulas of every structure at every tier, so that a value too large or too finely
    /// written for exact arithmetic is an error when the data is loaded, as for the chassis.
    /// </summary>
    public static void Check(GameData data, ErrorList errors)
    {
        foreach (var structure in data.Structures)
        {
            var file = $"{DataRules.StructuresFolder}{structure.Id}{DataRules.FileExtension}";
            try
            {
                for (var tier = 1; tier <= data.TierCount; tier++)
                {
                    _ = HitPoints(data, structure, tier).RoundHalfUp(UnitFormulas.Thousandths);
                }
            }
            catch (OverflowException)
            {
                errors.Add(file, "$.hp", $"the hit points are too large to compute exactly at some Protection tier; {Advice}");
            }

            if (structure.Attack is not { } attack)
            {
                continue;
            }

            try
            {
                _ = UnitFormulas.Distance(attack.Attack.Range!.Value);
                for (var tier = 1; tier <= data.TierCount; tier++)
                {
                    var damagePerSecond = DamagePerSecond(data, attack, tier);
                    _ = damagePerSecond.RoundHalfUp(UnitFormulas.Thousandths);
                    _ = UnitFormulas.DamagePerHit(damagePerSecond, attack.Attack).RoundHalfUp(UnitFormulas.Thousandths);
                }
            }
            catch (OverflowException)
            {
                errors.Add(file, "$.attack", $"the damage or the range is too large to compute exactly at some Firepower tier; {Advice}");
            }
        }
    }

    /// <summary>"All building hit points scale by 1.25 per Protection tier": the owner's tier, with no window.</summary>
    private static Fraction HitPoints(GameData data, StructureDef structure, int protectionTier) =>
        structure.HitPoints.ToFraction() * data.Rules.PerTier.HitPoints.ToFraction().Pow(protectionTier - 1);

    /// <summary>A structure's damage is scaled by the owner's Firepower tier, as a unit's is by its effective tier.</summary>
    private static Fraction DamagePerSecond(GameData data, StructureAttackDef attack, int firepowerTier) =>
        attack.DamagePerSecond.ToFraction() * data.Rules.PerTier.Damage.ToFraction().Pow(firepowerTier - 1);
}
