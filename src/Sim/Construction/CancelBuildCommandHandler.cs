using Sim.Combat;
using Sim.Core;

namespace Sim.Construction;

/// <summary>
/// Executes <see cref="CommandType.CancelBuild"/>: cancels the build order of each listed worker
/// and removes each listed structure that is still building itself (design 2.5).
/// </summary>
/// <remarks>
/// <para>
/// The command lists what to cancel. A worker of the player that is carrying out a build order
/// loses it, and the whole price is given back; the worker stops and goes back to the work site
/// it was assigned to, if it had one. A structure of the player that is not finished is removed,
/// and 75% of the price paid is given back, each resource rounded half up; its tiles are free
/// again from this tick. Everything else in the list is skipped: a finished structure cannot be
/// removed by its owner (design 2.5, no demolition).
/// </para>
/// <para>
/// A command that cancels nothing changes nothing and is reported as a
/// <see cref="SimEventKind.OrderRefused"/>.
/// </para>
/// </remarks>
public sealed class CancelBuildCommandHandler : ICommandHandler
{
    private readonly ConstructionRules _rules;

    public CancelBuildCommandHandler(ConstructionRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    public bool Execute(World world, in Command command)
    {
        ArgumentNullException.ThrowIfNull(world);

        var cancelled = false;
        if (_rules.HasPlayer(command.Player))
        {
            foreach (var entity in command.Entities)
            {
                if (!world.Entities.IsAlive(entity) || !_rules.Bodies.TryGet(entity, out var body) || body.Owner != command.Player)
                {
                    continue;
                }

                if (_rules.Orders.Contains(entity))
                {
                    _rules.EndOrder(entity, BuildProblem.Cancelled);
                    _rules.Movers.Get(entity).Stop();
                    _rules.Work.Return(world, entity);
                    cancelled = true;
                }
                else if (_rules.Foundations.TryGet(entity, out var foundation))
                {
                    // The structure system takes it off the map later in this tick, and reports it.
                    _rules.Stockpiles.Add(
                        body.Owner,
                        ConstructionRules.RefundOf(foundation.Materials),
                        ConstructionRules.RefundOf(foundation.Energy));
                    world.Entities.Destroy(entity);
                    cancelled = true;
                }
            }
        }

        if (!cancelled)
        {
            OrderCommandHandler.Refuse(_rules.Events, in command);
        }

        return cancelled;
    }
}
