namespace Sim.Data;

/// <summary>What the economy runs on (design 4.1), from <c>resources.jsonc</c>.</summary>
public sealed record ResourceRules(MaterialsRules Materials, EnergyRules Energy, WorkerProfile Worker);

/// <summary>
/// Materials, earned by workers at deposits. A deposit pays the whole rate until it has delivered
/// <see cref="FullRateAmount"/> Materials and <see cref="DepletedRate"/> of the rate after that.
/// </summary>
public sealed record MaterialsRules(DataNumber PerWorkerPerMinute, int WorkersPerDeposit, int FullRateAmount, DataNumber DepletedRate);

/// <summary>Energy, earned by workers at Generators.</summary>
public sealed record EnergyRules(DataNumber PerWorkerPerMinute, int WorkersPerGenerator);

/// <summary>
/// The one worker type. It is not a chassis: only its hit points change with a tier, by its
/// owner's Protection. Distances are in tiles and speed in tiles per second.
/// </summary>
public sealed record WorkerProfile(
    DataNumber HitPoints,
    DataNumber Speed,
    int Sight,
    int Supply,
    DataNumber Spacing,
    int Materials,
    int Energy,
    int BuildTicks);
