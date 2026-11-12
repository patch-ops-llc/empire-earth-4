using Sim.Core;
using Sim.Map;

namespace Sim.Economy;

/// <summary>
/// Executes <see cref="CommandType.Gather"/>: assigns the listed workers to the work site in
/// <see cref="Command.TargetEntity"/>, a deposit or a Generator, in place of whatever each was
/// doing (design 4.1).
/// </summary>
/// <remarks>
/// <para>
/// The command is valid when its player is in the match, its target is a living work site, a
/// Generator among them only when it is the player's own, and at least one listed entity can
/// take the order: alive, owned by the player, a worker, and able to walk to one of the site's
/// places. Entities that cannot take it are skipped. A command that assigns nobody is dropped.
/// </para>
/// <para>
/// Any deposit may be the target, also one outside the player's territory: the workers stand at
/// their places and earn nothing until it is inside. The workers are assigned in the order the
/// command lists them, and each takes its place as <see cref="WorkAssignments"/> says. A worker
/// already at the site gives up its place and its count of ticks like any other and takes a place
/// anew.
/// </para>
/// </remarks>
public sealed class GatherCommandHandler : ICommandHandler
{
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<WorkSite> _sites;
    private readonly WorkAssignments _work;
    private readonly int _playerCount;

    public GatherCommandHandler(ComponentStore<Body> bodies, ComponentStore<WorkSite> sites, WorkAssignments work, int playerCount)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(work);
        _bodies = bodies;
        _sites = sites;
        _work = work;
        _playerCount = playerCount;
    }

    public bool Execute(World world, in Command command)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (command.Player < 0
            || command.Player >= _playerCount
            || !world.Entities.IsAlive(command.TargetEntity)
            || !_sites.TryGet(command.TargetEntity, out var site)
            || (site.Kind == WorkSiteKind.Generator && site.Owner != command.Player))
        {
            return false;
        }

        var assigned = false;
        foreach (var entity in command.Entities)
        {
            if (world.Entities.IsAlive(entity) && _bodies.TryGet(entity, out var body) && body.Owner == command.Player)
            {
                assigned |= _work.Assign(world, entity, command.TargetEntity);
            }
        }

        return assigned;
    }
}
