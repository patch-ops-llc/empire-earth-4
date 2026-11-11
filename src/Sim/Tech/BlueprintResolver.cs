using Sim.Data;

namespace Sim.Tech;

/// <summary>Builds a <see cref="ChassisBlueprint"/> from a player's tiers (design 2.2 and 2.3).</summary>
internal static class BlueprintResolver
{
    /// <summary>The largest level an accent has, however far a track is ahead.</summary>
    private const int TopAccentLevel = 2;

    /// <summary>Units in one order with the batch perk. The design fixes it at a pair.</summary>
    private const int BatchPair = 2;

    public static ChassisBlueprint Resolve(GameData data, IReadOnlyList<int> tiers, ChassisDef chassis)
    {
        var line = data.LineOf(chassis.Line)
            ?? throw new ArgumentException($"Chassis '{chassis.Id}' is not a chassis of this data.", nameof(chassis));
        var roles = data.TrackRoles;
        var epoch = chassis.Epoch;

        var effective = new int[tiers.Count];
        var offsets = new int[tiers.Count];
        for (var track = 0; track < effective.Length; track++)
        {
            effective[track] = UnitFormulas.EffectiveTier(data, tiers[track], epoch);
            offsets[track] = effective[track] - epoch;
        }

        var accents = SelectAccents(data, tiers, offsets);
        var perks = ApplicablePerks(data, accents, chassis.Attack);

        var damagePerSecond = UnitFormulas.DamagePerSecond(data, line, effective[roles.Firepower]);
        var costMultiplier = UnitFormulas.CostMultiplier(
            data,
            epoch,
            offsets[roles.Firepower] + offsets[roles.Protection] + offsets[roles.Mobility],
            offsets[roles.Manufacturing]);
        var buildTicks = UnitFormulas.BuildTicks(data, line, offsets[roles.Manufacturing]);
        var attack = chassis.Attack;

        return new ChassisBlueprint
        {
            Chassis = chassis,
            Line = line,
            Unlocked = tiers[line.PrimaryTrack] >= epoch && tiers[line.SecondaryTrack] >= epoch - 1,
            EffectiveTiers = effective,
            Offsets = offsets,
            Accents = accents,
            Perks = perks,
            HitPoints = UnitFormulas.HitPoints(data, line, effective[roles.Protection]).RoundHalfUp(UnitFormulas.Thousandths),
            DamagePerSecond = damagePerSecond.RoundHalfUp(UnitFormulas.Thousandths),
            DamagePerHit = UnitFormulas.DamagePerHit(damagePerSecond, attack),
            Attack = attack,
            SpeedPerTick = UnitFormulas.SpeedPerTick(data, line, effective[roles.Mobility]),
            Range = attack.Range is { } range ? UnitFormulas.Distance(range, perks.RangeBonus?.Multiplier) : null,
            MinRange = attack.MinRange is { } minRange ? UnitFormulas.Distance(minRange) : null,
            BlastRadius = attack.BlastRadius is { } blast ? UnitFormulas.Distance(blast, perks.BlastRadiusBonus?.Multiplier) : null,
            Sight = checked(line.Base.Sight + (perks.SightBonus?.Tiles ?? 0)),
            Supply = Supply(line.Base.Supply, perks.SupplyReduction),
            Materials = UnitFormulas.Price(line.Base.Materials, costMultiplier),
            Energy = UnitFormulas.Price(line.Base.Energy, costMultiplier),
            BuildTicks = TechRules.AtLeastOneTick(buildTicks.RoundHalfUp()),
            BatchSize = perks.Batch is null ? 1 : BatchPair,
            BatchBuildTicks = TechRules.AtLeastOneTick(
                (perks.Batch is { } batch ? buildTicks * batch.TimeMultiplier.ToFraction() : buildTicks).RoundHalfUp()),
        };
    }

    /// <summary>
    /// Rule 3: a track ahead of the chassis gives its accent, and the unit carries the accents of the
    /// largest offsets. Ties go to the track where the player's actual tier is higher, then to the
    /// track that comes first in <c>tracks.jsonc</c>.
    /// </summary>
    private static Accent[] SelectAccents(GameData data, IReadOnlyList<int> tiers, int[] offsets)
    {
        var candidates = new List<int>();
        foreach (var accent in data.Accents)
        {
            if (offsets[accent.Track] > 0)
            {
                candidates.Add(accent.Track);
            }
        }

        candidates.Sort((left, right) =>
        {
            var byOffset = offsets[right].CompareTo(offsets[left]);
            if (byOffset != 0)
            {
                return byOffset;
            }

            var byTier = tiers[right].CompareTo(tiers[left]);
            return byTier != 0 ? byTier : left.CompareTo(right);
        });

        var accents = new Accent[Math.Min(candidates.Count, data.Rules.AccentSlots)];
        for (var slot = 0; slot < accents.Length; slot++)
        {
            accents[slot] = new Accent(candidates[slot], Math.Min(offsets[candidates[slot]], TopAccentLevel));
        }

        return accents;
    }

    /// <summary>The perks of the accents, without those that do not apply to a chassis that attacks this way.</summary>
    private static PerkSet ApplicablePerks(GameData data, Accent[] accents, AttackDef attack)
    {
        var perks = PerkSet.None;
        foreach (var accent in accents)
        {
            perks = perks.With(data.AccentOf(accent.Track)!.PerksAt(accent.Level));
        }

        var ranged = attack.Kind == AttackKind.Ranged;
        var blast = attack.BlastRadius is not null;
        return perks with
        {
            RangeBonus = ranged ? perks.RangeBonus : null,
            AttackRateBonus = ranged ? null : perks.AttackRateBonus,
            Splash = blast ? null : perks.Splash,
            BlastRadiusBonus = blast ? perks.BlastRadiusBonus : null,
        };
    }

    /// <summary>
    /// "Costs 1 less supply, minimum 1": the reduction never takes supply below its minimum, and
    /// never raises the supply of a unit that is already below it.
    /// </summary>
    private static int Supply(int supply, SupplyReductionPerk? reduction) =>
        reduction is null || supply <= reduction.Minimum ? supply : Math.Max(reduction.Minimum, supply - reduction.Amount);
}
