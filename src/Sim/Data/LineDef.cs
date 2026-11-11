namespace Sim.Data;

/// <summary>
/// One line of units (design 2.2 and 2.4). <see cref="Index"/>, <see cref="BuiltAt"/> and the lines in
/// <see cref="Counters"/> and <see cref="CannotHit"/> are indexes into the roster in
/// <see cref="RuleSet"/>; the two tracks are indexes into <see cref="GameData.Tracks"/>.
/// </summary>
public sealed record LineDef(
    string Id,
    int Index,
    int PrimaryTrack,
    int SecondaryTrack,
    int BuiltAt,
    LineProfile Base,
    AttackDef Attack,
    IReadOnlyList<LineCounter> Counters,
    DataNumber? StructureCounter,
    IReadOnlyList<int> CannotHit,
    IReadOnlyList<ChassisDef> Chassis);

/// <summary>A line's values at tier 1. Distances are in tiles and speed in tiles per second.</summary>
public sealed record LineProfile(
    DataNumber HitPoints,
    DataNumber DamagePerSecond,
    DataNumber Speed,
    int Sight,
    int Supply,
    DataNumber Spacing,
    int Materials,
    int Energy,
    int BuildTicks);

public enum AttackKind
{
    Melee,
    Ranged,
}

/// <summary>
/// How a line or chassis attacks. A melee attack has only a kind and an interval. A ranged attack
/// has a range and a projectile speed, and may have a minimum range, inside which it cannot fire, and
/// a blast radius, which makes every hit an area hit centered on the target.
/// </summary>
public sealed record AttackDef(
    AttackKind Kind,
    int IntervalTicks,
    DataNumber? Range = null,
    DataNumber? ProjectileSpeed = null,
    DataNumber? MinRange = null,
    DataNumber? BlastRadius = null);

/// <summary>A damage multiplier against one line.</summary>
public sealed record LineCounter(int Line, DataNumber Multiplier);

/// <summary>
/// One line's model for one epoch. <see cref="Index"/> is its place in <see cref="GameData.Chassis"/>,
/// which is sorted by ID over all lines. <see cref="Line"/> is the roster index of its line, as in
/// <see cref="LineDef.Index"/>. <see cref="Attack"/> already has the chassis's overrides applied.
/// </summary>
public sealed record ChassisDef(string Id, int Index, int Line, int Epoch, AttackDef Attack);
