namespace Sim.Data;

/// <summary>
/// The constants of the rule set, from <c>rules.jsonc</c>. <see cref="Lines"/> and
/// <see cref="Structures"/> are the roster's IDs sorted by ordinal comparison; an index into them is
/// the dense ID of a line or structure, whether or not its own data file exists yet.
/// </summary>
public sealed record RuleSet(
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Structures,
    EpochRules Epoch,
    ResearchPriceRules ResearchPrice,
    int EffectiveTierWindow,
    PerTierRules PerTier,
    PerStepRules PerStep,
    int AccentSlots,
    SupplyCapRules SupplyCap,
    int WallCrossingCost,
    MatchRules Match,
    StartRules Start,
    MapRules Map,
    CombatConstants Combat,
    DebugRules Debug,
    VisionRules Vision);

/// <summary>Design 1.4: the epoch is the tier that <see cref="TracksNeeded"/> tracks have reached.</summary>
public sealed record EpochRules(int TracksNeeded, int Reach);

/// <summary>Design 1.3: price multipliers by how far a tier is from the player's epoch.</summary>
public sealed record ResearchPriceRules(PriceMultiplier CatchUp, PriceMultiplier Frontier, PriceMultiplier Ahead);

public sealed record PriceMultiplier(DataNumber Cost, DataNumber Time);

/// <summary>Design 2.3 and 4.2: what one tier of a track multiplies.</summary>
public sealed record PerTierRules(DataNumber Damage, DataNumber HitPoints, DataNumber Speed, DataNumber Income);

/// <summary>Design 2.3: what one step of offset multiplies.</summary>
public sealed record PerStepRules(DataNumber QualityCost, DataNumber ManufacturingCost, DataNumber ManufacturingTime);

/// <summary>Design 4.1: supply cap is <see cref="Base"/> plus <see cref="PerLogisticsTier"/> per tier.</summary>
public sealed record SupplyCapRules(int Base, int PerLogisticsTier);

/// <summary>Design 5.2: the match settings of the first playable.</summary>
public sealed record MatchRules(int StartEpoch, int EpochCap)
{
    /// <summary>
    /// The lines a match is played with (design 6), as roster indexes in rising order, or null
    /// when <c>rules.jsonc</c> does not list them. The loader requires a data file for each, with
    /// a chassis for every epoch up to <see cref="EpochCap"/> and a structure that trains it.
    /// </summary>
    public IReadOnlyList<int>? Lines { get; init; }

    /// <summary>
    /// The structures a match is played with (design 6), as roster indexes in rising order, or
    /// null when <c>rules.jsonc</c> does not list them. The loader requires a data file for each,
    /// which unlocks no later than <see cref="EpochCap"/>.
    /// </summary>
    public IReadOnlyList<int>? Structures { get; init; }
}

/// <summary>Design 5.2: what every player starts with, besides a Capitol.</summary>
public sealed record StartRules(int Workers, int Materials, int Energy);

/// <summary>
/// Design 2.4 and 2.5: the numbers of combat that belong to no line and no structure. Distances
/// are in tiles.
/// </summary>
/// <param name="LookIntervalTicks">A unit or a Tower without a target looks for one every this many ticks.</param>
/// <param name="UnitReachMargin">Melee reach against a unit is the spacing between the two units plus this.</param>
/// <param name="StructureReach">Melee reach against a structure, from the attacker's center to the nearest edge of the footprint.</param>
/// <param name="Fortification">
/// The fortification rule: what multiplies the damage a structure takes from an attacker whose
/// effective Firepower is below the Protection tier of the structure's owner.
/// </param>
public sealed record CombatConstants(
    int LookIntervalTicks,
    DataNumber UnitReachMargin,
    DataNumber StructureReach,
    DataNumber Fortification);

/// <summary>
/// Switches for development, which the design does not have. Both players of a match have the
/// same data, so they have the same switches, and a switch is part of the content hash.
/// </summary>
/// <param name="Spawn">
/// Whether a match carries out <see cref="Core.CommandType.DebugSpawn"/>, which makes units from
/// nothing, and <see cref="Core.CommandType.DebugPlace"/>, which lays a finished structure the same
/// way. With it off each is dropped like any command without a handler.
/// </param>
public sealed record DebugRules(bool Spawn);

/// <summary>Design 5.3: the fog of war.</summary>
/// <param name="RefreshTicks">
/// What each player sees is worked out again in every tick whose number is a multiple of this, so
/// it may be up to one tick less than this old.
/// </param>
public sealed record VisionRules(int RefreshTicks);

/// <summary>
/// Design 5.3 and 5.4: what every map must satisfy. The footprint and territory radius of a start
/// are not here: they are those of the starting Capitol, in its own file.
/// </summary>
public sealed record MapRules(
    int DepositSize,
    int MainDepositsPerPlayer,
    int NaturalDepositsPerPlayer,
    int ContestedDeposits,
    int ChokepointMinWidth,
    int ChokepointMaxWidth,
    int OpenFieldMinWidth);
