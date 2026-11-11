using Sim.Data;
using Sim.Tech;

namespace Sim.Economy;

/// <summary>
/// Where the economy reads what follows from a player's track tiers: the Extraction multiplier
/// on income and the supply cap. Research in a match is the source once it is there.
/// </summary>
public interface IPlayerTechSource
{
    /// <summary>What follows from the player's tiers as they are now.</summary>
    PlayerTech Tech(int player);
}

/// <summary>
/// Every track of every player at the match's starting epoch (design 5.2), for the whole match.
/// It stands in until research in a match can raise a tier.
/// </summary>
public sealed class StartingTech : IPlayerTechSource
{
    private readonly PlayerTech _tech;

    public StartingTech(GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        int[] tiers = [.. Enumerable.Repeat(data.Rules.Match.StartEpoch, data.Tracks.Count)];
        _tech = TechResolver.Resolve(data, tiers);
    }

    public PlayerTech Tech(int player) => _tech;
}
