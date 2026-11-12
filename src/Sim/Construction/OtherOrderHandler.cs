using Sim.Core;
using Sim.Economy;

namespace Sim.Construction;

/// <summary>
/// Wraps the handler of an order that a worker can be given in place of a build order: a worker
/// that is given it loses its build order, and the price is given back in full (design 5.4). A
/// wall order ends the same way, and gives back every tile not yet laid (design 2.5).
/// </summary>
/// <remarks>
/// A worker is given the order when the handler carries the command out and the command lists
/// the worker, alive and owned by the command's player, as for
/// <see cref="LeaveWorkHandler"/>. A gather order is given only to the workers it assigns, so
/// there the worker must also be at work for the order's site afterwards. A worker that kept
/// its work site for the build order, and that the new order has not given other work, is left
/// without an assignment: it is no longer coming back.
/// </remarks>
public sealed class OtherOrderHandler : ICommandHandler
{
    private readonly ICommandHandler _order;
    private readonly ConstructionRules _rules;

    public OtherOrderHandler(ICommandHandler order, ConstructionRules rules)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(rules);
        _order = order;
        _rules = rules;
    }

    public bool Execute(World world, in Command command)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!_order.Execute(world, command))
        {
            return false;
        }

        foreach (var entity in command.Entities)
        {
            if (!world.Entities.IsAlive(entity)
                || !_rules.HasOrder(entity)
                || !_rules.Bodies.TryGet(entity, out var body)
                || body.Owner != command.Player)
            {
                continue;
            }

            var worker = _rules.Workers.Get(entity);
            if (command.Type == CommandType.Gather && (worker.State == WorkerState.Away || worker.Site != command.TargetEntity))
            {
                continue;
            }

            _rules.EndAnyOrder(entity, BuildProblem.OtherOrder);
            if (worker.State == WorkerState.Away)
            {
                _rules.Work.Release(entity);
            }
        }

        return true;
    }
}
