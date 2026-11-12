using Sim.Core;

namespace Sim.Outcome;

/// <summary>
/// Executes <see cref="CommandType.Concede"/>: the player who gives it is out of the match
/// (design 5.1). Nothing else in the command is read. It is dropped when the match is already
/// decided, so the winner cannot concede a match it has won; one from a player who is already
/// out never gets here (<see cref="Standings.Admits"/>).
/// </summary>
/// <remarks>
/// Two orders to concede in one tick run in the order they were queued, like any commands: in a
/// match of two the first decides it, and the second is dropped.
/// </remarks>
public sealed class ConcedeCommandHandler : ICommandHandler
{
    private readonly Standings _standings;

    public ConcedeCommandHandler(Standings standings)
    {
        ArgumentNullException.ThrowIfNull(standings);
        _standings = standings;
    }

    public bool Execute(World world, in Command command)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!_standings.IsPlaying(command.Player) || _standings.IsDecided)
        {
            return false;
        }

        _standings.TakeOut(command.Player, PlayerStanding.Conceded, world.Tick);
        return true;
    }
}
