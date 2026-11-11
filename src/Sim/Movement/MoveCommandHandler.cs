using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Movement;

/// <summary>
/// Executes <see cref="CommandType.Move"/>: sends the listed entities to the target position,
/// replacing whatever move order each had.
/// </summary>
/// <remarks>
/// The command is valid when its player is in the match, its target is a point on the map, and at
/// least one listed entity can take the order: alive, owned by the player, and a mover. Entities
/// that cannot take it are skipped, because a unit may die between the moment a player gives an
/// order and the tick it runs, and the rest of the group should still go. A command that moves
/// nobody is dropped.
/// </remarks>
public sealed class MoveCommandHandler : ICommandHandler
{
    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly int _playerCount;

    public MoveCommandHandler(MapGrid grid, ComponentStore<Body> bodies, ComponentStore<Mover> movers, int playerCount)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        _grid = grid;
        _bodies = bodies;
        _movers = movers;
        _playerCount = playerCount;
    }

    public bool Execute(World world, in Command command)
    {
        ArgumentNullException.ThrowIfNull(world);

        var target = new FixedVector2(command.TargetX, command.TargetY);
        if (command.Player < 0 || command.Player >= _playerCount || !_grid.Contains(target))
        {
            return false;
        }

        var moved = false;
        foreach (var entity in command.Entities)
        {
            if (!world.Entities.IsAlive(entity)
                || !_bodies.TryGet(entity, out var body)
                || body.Owner != command.Player)
            {
                continue;
            }

            var index = _movers.IndexOf(entity);
            if (index < 0)
            {
                continue;
            }

            ref var mover = ref _movers.ValueAt(index);
            mover.State = MoveState.Moving;
            mover.Destination = target;
            moved = true;
        }

        return moved;
    }
}
