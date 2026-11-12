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
public sealed record MatchRules(int StartEpoch, int EpochCap);

/// <summary>Design 5.2: what every player starts with, besides a Capitol.</summary>
public sealed record StartRules(int Workers, int Materials, int Energy);

/// <summary>
/// Switches for development, which the design does not have. Both players of a match have the
/// same data, so they have the same switches, and a switch is part of the content hash.
/// </summary>
/// <param name="Spawn">
/// Whether a match carries out <see cref="Core.CommandType.DebugSpawn"/>, which makes units from
/// nothing. With it off the command is dropped like any command without a handler.
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
