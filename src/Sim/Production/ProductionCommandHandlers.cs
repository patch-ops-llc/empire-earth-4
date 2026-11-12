using Sim.Combat;
using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Production;

/// <summary>
/// Executes <see cref="CommandType.Train"/>: the player places one order at the end of the queue
/// of the structure the command lists, for the chassis whose index is in
/// <see cref="Command.ContentId"/>, or for a worker when that is <see cref="TrainOrder.Worker"/>.
/// </summary>
/// <remarks>
/// The command lists exactly one entity, the structure: one command is one order at one
/// structure, so that it is one answer to whether the stockpile and the supply cover it. It is
/// carried out when <see cref="MatchProduction.CheckTrain"/> allows it. Otherwise it changes
/// nothing and is reported as a <see cref="SimEventKind.TrainRefused"/> with the reason.
/// </remarks>
public sealed class TrainHandler : ICommandHandler
{
    private readonly MatchProduction _production;

    public TrainHandler(MatchProduction production)
    {
        ArgumentNullException.ThrowIfNull(production);
        _production = production;
    }

    public bool Execute(World world, in Command command)
    {
        var check = command.Entities.Length == 1
            ? _production.Train(command.Player, command.Entities[0], command.ContentId)
            : TrainCheck.NoProducer;
        if (check != TrainCheck.Allowed)
        {
            _production.ReportRefused(in command, check);
        }

        return check == TrainCheck.Allowed;
    }
}

/// <summary>
/// Executes <see cref="CommandType.CancelTrain"/>: the player takes one order out of the queue of
/// the structure the command lists and gets back what was paid for it. The order is named by its
/// position in the queue, in <see cref="Command.ContentId"/>: 0 is the order in production.
/// </summary>
/// <remarks>
/// The command lists exactly one entity. It is dropped, and reported as a
/// <see cref="SimEventKind.OrderRefused"/>, when that is no production structure of the
/// player's or has no order at that position.
/// </remarks>
public sealed class CancelTrainHandler : ICommandHandler
{
    private readonly MatchProduction _production;
    private readonly SimEventList _events;

    public CancelTrainHandler(MatchProduction production, SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(production);
        ArgumentNullException.ThrowIfNull(events);
        _production = production;
        _events = events;
    }

    public bool Execute(World world, in Command command)
    {
        var cancelled = command.Entities.Length == 1 && _production.Cancel(command.Player, command.Entities[0], command.ContentId);
        if (!cancelled)
        {
            OrderCommandHandler.Refuse(_events, in command);
        }

        return cancelled;
    }
}

/// <summary>
/// Executes <see cref="CommandType.SetRallyPoint"/>: every production structure of the player's
/// that the command lists gets the command's target point as its rally point (design 5.4).
/// </summary>
/// <remarks>
/// The command is valid when its point is on the map and at least one listed entity is a living,
/// finished production structure of the player's. The others are skipped. A command that is not
/// valid changes nothing and is reported as a <see cref="SimEventKind.OrderRefused"/>. Nothing
/// but the point is kept: what the point lies on is looked at when a unit appears.
/// </remarks>
public sealed class SetRallyPointHandler : ICommandHandler
{
    private readonly MatchProduction _production;
    private readonly MapGrid _grid;
    private readonly SimEventList _events;

    public SetRallyPointHandler(MatchProduction production, MapGrid grid, SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(production);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(events);
        _production = production;
        _grid = grid;
        _events = events;
    }

    public bool Execute(World world, in Command command)
    {
        var point = new FixedVector2(command.TargetX, command.TargetY);
        var set = false;
        if (_grid.Contains(point))
        {
            foreach (var structure in command.Entities)
            {
                set |= _production.SetRallyPoint(command.Player, structure, point);
            }
        }

        if (!set)
        {
            OrderCommandHandler.Refuse(_events, in command);
        }

        return set;
    }
}
