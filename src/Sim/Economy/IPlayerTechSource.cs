using Sim.Tech;

namespace Sim.Economy;

/// <summary>
/// Where the economy reads what follows from a player's track tiers: the Extraction multiplier
/// on income and the supply cap. In a match the source is its research.
/// </summary>
public interface IPlayerTechSource
{
    /// <summary>What follows from the player's tiers as they are now.</summary>
    PlayerTech Tech(int player);
}
