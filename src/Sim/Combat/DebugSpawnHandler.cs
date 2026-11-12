using Sim.Core;
using Sim.Numerics;
using Sim.Setup;

namespace Sim.Combat;

/// <summary>
/// Executes <see cref="CommandType.DebugSpawn"/>, the development spawn: it makes one unit of a
/// chassis from nothing for the player who sends the command, at the command's target point. The
/// design has no such order. It is there so that a fight can be seen, tested and recorded before
/// units can be trained in a match.
/// </summary>
/// <remarks>
/// <para>
/// The match setup gives the command this handler only while the rules say so
/// (<see cref="Data.DebugRules.Spawn"/>). Otherwise the command has no handler and is dropped. The
/// switch is data, so both players of a match have the same one.
/// </para>
/// <para>
/// The chassis is the command's <see cref="Command.ContentId"/>, its dense index in the data. The
/// command is valid when its player is in the match, the chassis is one of the data's, and a unit
/// can stand on the tile of the point. The chassis need not be unlocked, the unit costs nothing,
/// and it is made even when its player has no free supply: supply only holds back orders that are
/// paid for. The unit gets the hit points and speed of its owner's blueprint as it is in this
/// tick, and is idle at its post, exactly as <see cref="CombatSpawn.SpawnChassis"/> places it.
/// </para>
/// <para>
/// One command makes one unit. A force is as many commands as it has units.
/// </para>
/// <para>
/// A command that is not valid changes nothing and is reported as a
/// <see cref="SimEventKind.OrderRefused"/>.
/// </para>
/// </remarks>
public sealed class DebugSpawnHandler : ICommandHandler
{
    private readonly Match _match;
    private readonly SimEventList _events;

    public DebugSpawnHandler(Match match, SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(events);
        _match = match;
        _events = events;
    }

    public bool Execute(World world, in Command command)
    {
        ArgumentNullException.ThrowIfNull(world);

        var position = new FixedVector2(command.TargetX, command.TargetY);
        if (command.Player < 0
            || command.Player >= _match.Players.Count
            || command.ContentId < 0
            || command.ContentId >= _match.Data.Chassis.Count
            || !_match.Grid.IsPassable(position))
        {
            OrderCommandHandler.Refuse(_events, in command);
            return false;
        }

        _match.SpawnChassis(command.Player, command.ContentId, position);
        return true;
    }
}
