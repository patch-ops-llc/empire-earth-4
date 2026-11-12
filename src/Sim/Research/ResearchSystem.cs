using Sim.Core;

namespace Sim.Research;

/// <summary>
/// Moves every research one tick on, finishes those that are due and closes the slots whose
/// holders have died. It runs after everything that can destroy a holder, so that a research
/// cannot finish in the tick in which its building falls.
/// </summary>
public sealed class ResearchSystem : ISystem
{
    private readonly MatchResearch _research;

    public ResearchSystem(MatchResearch research)
    {
        ArgumentNullException.ThrowIfNull(research);
        _research = research;
    }

    public void Run(World world) => _research.Advance();
}
