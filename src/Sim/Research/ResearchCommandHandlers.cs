using Sim.Core;

namespace Sim.Research;

/// <summary>
/// Executes <see cref="CommandType.StartResearch"/>: the player starts researching the next tier
/// of the track whose index is in <see cref="Command.ContentId"/>, in the lowest-numbered slot
/// they have free, and pays for it. The command is dropped unless
/// <see cref="MatchResearch.CheckStart"/> allows it. Nothing else in the command is read.
/// </summary>
public sealed class StartResearchHandler : ICommandHandler
{
    private readonly MatchResearch _research;

    public StartResearchHandler(MatchResearch research)
    {
        ArgumentNullException.ThrowIfNull(research);
        _research = research;
    }

    public bool Execute(World world, in Command command) => _research.Start(command.Player, command.ContentId);
}

/// <summary>
/// Executes <see cref="CommandType.CancelResearch"/>: the player cancels the research of the track
/// whose index is in <see cref="Command.ContentId"/> and gets back everything paid for it. No two
/// slots work on one track, so the track names the research. The command is dropped when the
/// player is not researching that track. Nothing else in the command is read.
/// </summary>
public sealed class CancelResearchHandler : ICommandHandler
{
    private readonly MatchResearch _research;

    public CancelResearchHandler(MatchResearch research)
    {
        ArgumentNullException.ThrowIfNull(research);
        _research = research;
    }

    public bool Execute(World world, in Command command) => _research.Cancel(command.Player, command.ContentId);
}
