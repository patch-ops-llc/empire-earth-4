using Sim.Data;
using Sim.Numerics;

namespace Sim.Tech;

/// <summary>
/// What one structure is for one player (design 2.5): its hit points under that player's
/// Protection tier and, for one that attacks, its damage under the player's Firepower tier. A
/// structure has no epoch, so it follows the player's actual tiers with no window. Price,
/// footprint, build time, sight and territory radius do not change with a tier and are read from
/// <see cref="Structure"/>.
/// </summary>
public sealed class StructureBlueprint
{
    internal StructureBlueprint()
    {
    }

    public required StructureDef Structure { get; init; }

    /// <summary>
    /// Whether the player may place the structure: it has no unlock tier, or the player's tier in
    /// its track has reached it. The other values are set either way.
    /// </summary>
    public required bool Unlocked { get; init; }

    /// <summary>The owner's Protection tier, which is what an attacker's Firepower is measured against.</summary>
    public required int ProtectionTier { get; init; }

    /// <summary>Maximum hit points in thousandths. A foundation has them from the moment it is laid.</summary>
    public required long HitPoints { get; init; }

    /// <summary>How the structure attacks when finished, or null for one that does not.</summary>
    public required StructureAttackBlueprint? Attack { get; init; }
}

/// <summary>The attack of a structure for one player (design 2.5, the Tower).</summary>
public sealed class StructureAttackBlueprint
{
    internal StructureAttackBlueprint()
    {
    }

    /// <summary>
    /// The owner's Firepower tier. It is the attack's effective Firepower wherever a rule compares
    /// one with a target's Protection.
    /// </summary>
    public required int EffectiveFirepower { get; init; }

    /// <summary>Damage per second in thousandths, for display and comparison.</summary>
    public required long DamagePerSecond { get; init; }

    /// <summary>
    /// The damage of one hit before anything the target applies, exactly: the unrounded damage per
    /// second times the attack interval, as for a unit.
    /// </summary>
    public required Fraction DamagePerHit { get; init; }

    /// <summary>Interval, range and shot speed, as written in the data.</summary>
    public required AttackDef Attack { get; init; }

    /// <summary>Range in tiles, measured from the nearest edge of the footprint.</summary>
    public required Fixed Range { get; init; }

    /// <summary>
    /// The lines the attack cannot hit under the owner's Protection tier, as roster indexes in
    /// rising order. Empty once that tier has reached
    /// <see cref="StructureAttackDef.CanHitAllFromProtection"/>.
    /// </summary>
    public required IReadOnlyList<int> CannotHit { get; init; }
}
