using Sim.Core;
using Sim.Tech;

namespace Sim.Research;

/// <summary>
/// A part of the simulation that keeps values worked out from a player's blueprints in its own
/// state, and has to bring them up to date when a research finishes (design 2.3: "when a research
/// finishes, every existing unit updates"). Hit points are such a value: a unit keeps the same
/// fraction of its maximum, which <see cref="HitPointRules.KeepFraction"/> computes.
/// </summary>
/// <remarks>
/// Handlers are part of the rules of the game. They are added before the first tick with
/// <see cref="MatchResearch.AddTechChangeHandler"/> and run in the order they were added, inside
/// the tick in which the research finishes, so every machine runs them alike.
/// </remarks>
public interface ITechChangeHandler
{
    /// <summary>
    /// Called once for each research that finishes, after the player's table has been replaced.
    /// </summary>
    /// <param name="world">The world, in the middle of a tick.</param>
    /// <param name="player">The player whose research finished.</param>
    /// <param name="before">What followed from the player's tiers until now.</param>
    /// <param name="after">What follows from them now: one track is a tier higher.</param>
    void TechChanged(World world, int player, PlayerTech before, PlayerTech after);
}
