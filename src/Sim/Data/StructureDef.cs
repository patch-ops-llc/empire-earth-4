namespace Sim.Data;

/// <summary>
/// One structure (design 2.5), from <c>structures/&lt;id&gt;.jsonc</c>. <see cref="Index"/> and the
/// lines in <see cref="Trains"/> are indexes into the roster in <see cref="RuleSet"/>;
/// <see cref="Track"/> is an index into <see cref="GameData.Tracks"/>. Distances are in tiles.
/// </summary>
/// <param name="Id">The structure's ID, which is its file's name.</param>
/// <param name="Index">Its place in <see cref="RuleSet.Structures"/>: the number a command names it by.</param>
/// <param name="Track">The track whose tier the structure shows and unlocks from, or null for one that shows none.</param>
/// <param name="UnlockTier">The tier of <see cref="Track"/> the owner needs to place it. 1 for a structure that is there from the start.</param>
/// <param name="Footprint">The side of the square of tiles it stands on and blocks.</param>
/// <param name="HitPoints">Hit points at Protection 1. Each Protection tier of the owner multiplies them.</param>
/// <param name="Materials">The price in Materials. For a wall, of one tile.</param>
/// <param name="Energy">The price in Energy.</param>
/// <param name="BuildTicks">The time it builds itself in once its foundation is laid. No track changes it.</param>
/// <param name="Sight">Sight when finished, measured from the center. No track changes it.</param>
/// <param name="TerritoryRadius">The radius of the territory it gives its owner when finished, or null.</param>
/// <param name="ResearchSlots">Research slots it gives its owner when finished.</param>
/// <param name="MaxPerPlayer">The most a player may have, counting those under way, or null for no limit.</param>
/// <param name="TrainsWorkers">Whether it trains workers.</param>
/// <param name="Trains">The lines it trains, in the order of the file, which is the order they are offered in.</param>
/// <param name="IsEnergyWorkSite">Whether workers stand at it and earn Energy, as <see cref="EnergyRules"/> says.</param>
/// <param name="Attack">How it attacks when finished, or null for a structure that does not.</param>
/// <param name="Wall">Set for the structure walls are made of, tile by tile, and for the gate made from it.</param>
public sealed record StructureDef(
    string Id,
    int Index,
    int? Track,
    int UnlockTier,
    int Footprint,
    DataNumber HitPoints,
    int Materials,
    int Energy,
    int BuildTicks,
    int Sight,
    int? TerritoryRadius,
    int ResearchSlots,
    int? MaxPerPlayer,
    bool TrainsWorkers,
    IReadOnlyList<int> Trains,
    bool IsEnergyWorkSite,
    StructureAttackDef? Attack,
    WallDef? Wall)
{
    /// <summary>
    /// True for the gate (design 2.5): a wall tile that its owner's units walk through once it is
    /// finished. It has no file: <see cref="GameData.Gate"/> makes it from the wall's.
    /// </summary>
    public bool IsGate { get; init; }
}

/// <summary>
/// The attack of a structure (design 2.5, the Tower). It is always ranged.
/// </summary>
/// <param name="DamagePerSecond">Damage per second at Firepower 1. Each Firepower tier of the owner multiplies it.</param>
/// <param name="Attack">Interval, range and shot speed. Range is measured from the nearest edge of the footprint.</param>
/// <param name="CannotHit">Lines it cannot attack, as roster indexes in rising order.</param>
/// <param name="CanHitAllFromProtection">
/// The owner's Protection tier from which <see cref="CannotHit"/> no longer applies, or null when
/// it always does.
/// </param>
public sealed record StructureAttackDef(
    DataNumber DamagePerSecond,
    AttackDef Attack,
    IReadOnlyList<int> CannotHit,
    int? CanHitAllFromProtection);

/// <summary>What makes a structure the wall (design 2.5): it is built in runs of tiles, and a tile may be a gate.</summary>
public sealed record WallDef(GateDef Gate);

/// <summary>
/// A gate tile: a wall tile its owner's units can walk through. It has the wall's hit points,
/// footprint and sight, and a price and build time of its own.
/// </summary>
public sealed record GateDef(int Materials, int Energy, int BuildTicks);
