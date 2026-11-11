namespace Sim.Data;

/// <summary>
/// What it costs to raise any track to <see cref="Tier"/> at frontier price (design 1.3), from
/// <c>research.jsonc</c>. The multipliers in <see cref="ResearchPriceRules"/> apply on top.
/// </summary>
public sealed record ResearchTier(int Tier, int Materials, int Energy, int Ticks);
