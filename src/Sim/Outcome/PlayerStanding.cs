namespace Sim.Outcome;

/// <summary>
/// Where a player stands in the match (design 5.1). The numbers are part of the state, and so of
/// snapshots: never change or reuse one.
/// </summary>
public enum PlayerStanding
{
    /// <summary>In the match.</summary>
    Playing = 0,

    /// <summary>Out by conquest: the player had no Capitol and no Outpost left.</summary>
    Eliminated = 1,

    /// <summary>Out by the player's own order to concede.</summary>
    Conceded = 2,
}
