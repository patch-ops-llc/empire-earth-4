namespace Sim.Data;

/// <summary>
/// The accent one track gives a unit (design 2.3), from <c>accents.jsonc</c>. <see cref="Track"/> is
/// an index into <see cref="GameData.Tracks"/>. A unit at level II has the perks of both levels.
/// </summary>
public sealed record AccentDef(int Track, string Name, PerkSet LevelOne, PerkSet LevelTwo)
{
    /// <summary>The perks of an accent at <paramref name="level"/>: level I alone, or both levels from level II up.</summary>
    public PerkSet PerksAt(int level) => level switch
    {
        <= 0 => PerkSet.None,
        1 => LevelOne,
        _ => LevelOne.With(LevelTwo),
    };
}

/// <summary>
/// The kinds of perk the code knows. Each is one rule shape with its numbers in data; a perk that
/// needs a new shape needs a new kind.
/// </summary>
public enum PerkKind
{
    RangeBonus,
    AttackRateBonus,
    Splash,
    BlastRadiusBonus,
    AreaDamageReduction,
    Plating,
    SightBonus,
    Charge,
    Batch,
    DeathRefund,
    Regeneration,
    SupplyReduction,
}

/// <summary>Range times <see cref="Multiplier"/>. Applies to a chassis with a ranged attack.</summary>
public sealed record RangeBonusPerk(DataNumber Multiplier);

/// <summary>
/// <see cref="Multiplier"/> times as many attacks in the same time: 1.15 is 23 attacks in the time of
/// 20. Applies to a chassis with a melee attack.
/// </summary>
public sealed record AttackRateBonusPerk(DataNumber Multiplier);

/// <summary>
/// Every hit also deals <see cref="Share"/> of its damage to each other enemy within
/// <see cref="Radius"/> tiles of the target. Applies to a chassis whose attack has no blast.
/// </summary>
public sealed record SplashPerk(DataNumber Share, DataNumber Radius);

/// <summary>Blast radius times <see cref="Multiplier"/>. Applies to a chassis whose attack has a blast.</summary>
public sealed record BlastRadiusBonusPerk(DataNumber Multiplier);

/// <summary>Area damage taken is multiplied by <see cref="Multiplier"/>.</summary>
public sealed record AreaDamageReductionPerk(DataNumber Multiplier);

/// <summary>
/// Damage taken is multiplied by <see cref="Multiplier"/> when the attacker's effective Firepower is
/// below this unit's effective Protection.
/// </summary>
public sealed record PlatingPerk(DataNumber Multiplier);

/// <summary>Sight range plus <see cref="Tiles"/>.</summary>
public sealed record SightBonusPerk(int Tiles);

/// <summary>
/// The first attack after moving <see cref="Distance"/> tiles or more deals <see cref="Multiplier"/>
/// times its damage. Applies to a chassis with an attack.
/// </summary>
public sealed record ChargePerk(DataNumber Distance, DataNumber Multiplier);

/// <summary>Each queue slot produces two units in <see cref="TimeMultiplier"/> times the build time of one.</summary>
public sealed record BatchPerk(DataNumber TimeMultiplier);

/// <summary><see cref="Share"/> of the Materials paid for the unit is refunded when it dies.</summary>
public sealed record DeathRefundPerk(DataNumber Share);

/// <summary>
/// <see cref="SharePerSecond"/> of maximum hit points is added once per second, after
/// <see cref="DelayTicks"/> ticks without damage.
/// </summary>
public sealed record RegenerationPerk(DataNumber SharePerSecond, int DelayTicks);

/// <summary>The unit costs <see cref="Amount"/> less supply, but not less than <see cref="Minimum"/>.</summary>
public sealed record SupplyReductionPerk(int Amount, int Minimum);

/// <summary>
/// Perks by kind. A kind appears at most once in <c>accents.jsonc</c>, so a set holds at most one perk
/// of each kind and a missing one is null.
/// </summary>
public sealed record PerkSet
{
    public static PerkSet None { get; } = new();

    public RangeBonusPerk? RangeBonus { get; init; }

    public AttackRateBonusPerk? AttackRateBonus { get; init; }

    public SplashPerk? Splash { get; init; }

    public BlastRadiusBonusPerk? BlastRadiusBonus { get; init; }

    public AreaDamageReductionPerk? AreaDamageReduction { get; init; }

    public PlatingPerk? Plating { get; init; }

    public SightBonusPerk? SightBonus { get; init; }

    public ChargePerk? Charge { get; init; }

    public BatchPerk? Batch { get; init; }

    public DeathRefundPerk? DeathRefund { get; init; }

    public RegenerationPerk? Regeneration { get; init; }

    public SupplyReductionPerk? SupplyReduction { get; init; }

    /// <summary>The kinds this set holds, in the order of <see cref="PerkKind"/>.</summary>
    public IReadOnlyList<PerkKind> Kinds
    {
        get
        {
            var kinds = new List<PerkKind>();
            foreach (var kind in Enum.GetValues<PerkKind>())
            {
                if (Has(kind))
                {
                    kinds.Add(kind);
                }
            }

            return kinds;
        }
    }

    public bool Has(PerkKind kind) => kind switch
    {
        PerkKind.RangeBonus => RangeBonus is not null,
        PerkKind.AttackRateBonus => AttackRateBonus is not null,
        PerkKind.Splash => Splash is not null,
        PerkKind.BlastRadiusBonus => BlastRadiusBonus is not null,
        PerkKind.AreaDamageReduction => AreaDamageReduction is not null,
        PerkKind.Plating => Plating is not null,
        PerkKind.SightBonus => SightBonus is not null,
        PerkKind.Charge => Charge is not null,
        PerkKind.Batch => Batch is not null,
        PerkKind.DeathRefund => DeathRefund is not null,
        PerkKind.Regeneration => Regeneration is not null,
        PerkKind.SupplyReduction => SupplyReduction is not null,
        _ => false,
    };

    /// <summary>This set and <paramref name="other"/> together. Where both hold a kind, this set's perk stays.</summary>
    public PerkSet With(PerkSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new PerkSet
        {
            RangeBonus = RangeBonus ?? other.RangeBonus,
            AttackRateBonus = AttackRateBonus ?? other.AttackRateBonus,
            Splash = Splash ?? other.Splash,
            BlastRadiusBonus = BlastRadiusBonus ?? other.BlastRadiusBonus,
            AreaDamageReduction = AreaDamageReduction ?? other.AreaDamageReduction,
            Plating = Plating ?? other.Plating,
            SightBonus = SightBonus ?? other.SightBonus,
            Charge = Charge ?? other.Charge,
            Batch = Batch ?? other.Batch,
            DeathRefund = DeathRefund ?? other.DeathRefund,
            Regeneration = Regeneration ?? other.Regeneration,
            SupplyReduction = SupplyReduction ?? other.SupplyReduction,
        };
    }
}
