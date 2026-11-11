using Sim.Data;
using Sim.Numerics;

namespace Sim.Tech;

/// <summary>An accent a unit carries: the track that gives it and its level, 1 or 2.</summary>
public readonly record struct Accent(int Track, int Level);

/// <summary>
/// What one chassis is for one player: its stats, price and accents under that player's track tiers
/// (design 2.2 and 2.3). Every value is computed exactly and rounded once. Lists that go by track
/// are in the order of <see cref="GameData.Tracks"/>.
/// </summary>
public sealed class ChassisBlueprint
{
    internal ChassisBlueprint()
    {
    }

    public required ChassisDef Chassis { get; init; }

    public required LineDef Line { get; init; }

    /// <summary>
    /// Whether the player may build this chassis: the line's primary track has reached the chassis's
    /// epoch and its secondary track the epoch before. The other values are set either way.
    /// </summary>
    public required bool Unlocked { get; init; }

    /// <summary>The player's tier in each track, limited to the window around the chassis's epoch.</summary>
    public required IReadOnlyList<int> EffectiveTiers { get; init; }

    /// <summary>The effective tier minus the chassis's epoch, for each track.</summary>
    public required IReadOnlyList<int> Offsets { get; init; }

    /// <summary>The accents the unit carries, strongest first. At most <see cref="RuleSet.AccentSlots"/>.</summary>
    public required IReadOnlyList<Accent> Accents { get; init; }

    /// <summary>
    /// The perks of <see cref="Accents"/> that apply to this chassis. A perk for ranged attacks is not
    /// here on a melee chassis, even when the unit carries the accent that gives it.
    /// </summary>
    public required PerkSet Perks { get; init; }

    /// <summary>Maximum hit points in thousandths.</summary>
    public required long HitPoints { get; init; }

    /// <summary>Damage per second in thousandths, for display and comparison.</summary>
    public required long DamagePerSecond { get; init; }

    /// <summary>
    /// The damage of one hit before anything the target applies, exactly: the unrounded damage per
    /// second times the attack interval. Combat multiplies this with its other factors and rounds once.
    /// </summary>
    public required Fraction DamagePerHit { get; init; }

    /// <summary>How the chassis attacks, as written in the data and before perks.</summary>
    public required AttackDef Attack { get; init; }

    /// <summary>Tiles per tick.</summary>
    public required Fixed SpeedPerTick { get; init; }

    /// <summary>Attack range in tiles with the range bonus applied, or null for a melee attack.</summary>
    public required Fixed? Range { get; init; }

    /// <summary>The distance inside which the chassis cannot fire, or null when it has none.</summary>
    public required Fixed? MinRange { get; init; }

    /// <summary>Blast radius in tiles with the blast radius bonus applied, or null when hits are not area hits.</summary>
    public required Fixed? BlastRadius { get; init; }

    /// <summary>Sight in tiles, with the sight bonus applied.</summary>
    public required int Sight { get; init; }

    /// <summary>Supply per unit, with the supply reduction applied.</summary>
    public required int Supply { get; init; }

    /// <summary>The price of one unit.</summary>
    public required int Materials { get; init; }

    public required int Energy { get; init; }

    /// <summary>The build time of an order for one unit.</summary>
    public required int BuildTicks { get; init; }

    /// <summary>Units one order produces when the stockpile and free supply cover them: 2 with the batch perk, otherwise 1.</summary>
    public required int BatchSize { get; init; }

    /// <summary>
    /// The build time of an order for <see cref="BatchSize"/> units. Each unit of the order still
    /// costs the full price and supply.
    /// </summary>
    public required int BatchBuildTicks { get; init; }

    /// <summary>The level of the accent of <paramref name="track"/> on this unit: 0 when it does not carry it.</summary>
    public int AccentLevel(int track)
    {
        foreach (var accent in Accents)
        {
            if (accent.Track == track)
            {
                return accent.Level;
            }
        }

        return 0;
    }
}
