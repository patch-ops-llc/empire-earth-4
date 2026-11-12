using Sim.Core;
using Sim.Map;

namespace Sim.Economy;

/// <summary>
/// Wraps the handler of an order that takes a worker away from its work, such as the move order:
/// a worker that is given the order gives up its place and its count of ticks (design 4.1).
/// </summary>
/// <remarks>
/// A worker is given the order when the handler carries the command out and the command lists the
/// worker, alive and owned by the command's player. A command the handler drops changes nothing.
/// An order that keeps the assignment, as the build order of a later milestone does, is not
/// wrapped.
/// </remarks>
public sealed class LeaveWorkHandler : ICommandHandler
{
    private readonly ICommandHandler _order;
    private readonly ComponentStore<Body> _bodies;
    private readonly WorkAssignments _work;

    public LeaveWorkHandler(ICommandHandler order, ComponentStore<Body> bodies, WorkAssignments work)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(work);
        _order = order;
        _bodies = bodies;
        _work = work;
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
            if (world.Entities.IsAlive(entity) && _bodies.TryGet(entity, out var body) && body.Owner == command.Player)
            {
                _work.Release(entity);
            }
        }

        return true;
    }
}
