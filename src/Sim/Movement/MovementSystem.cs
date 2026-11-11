using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Movement;

/// <summary>
/// Moves every mover that has an order one step toward its destination, oldest entity first.
/// </summary>
/// <remarks>
/// A mover walks in a straight line at its speed and arrives exactly on its destination. It never
/// steps onto a tile it cannot stand on: when the next step would, it stops where it is and its
/// order ends. There is no route finding yet, so an obstacle in the way ends the order.
/// </remarks>
public sealed class MovementSystem : ISystem
{
    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;

    public MovementSystem(MapGrid grid, ComponentStore<Body> bodies, ComponentStore<Mover> movers)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        _grid = grid;
        _bodies = bodies;
        _movers = movers;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (var i = 0; i < _movers.Count; i++)
        {
            ref var mover = ref _movers.ValueAt(i);
            if (mover.State != MoveState.Moving)
            {
                continue;
            }

            var entity = _movers.EntityAt(i);
            if (!world.Entities.IsAlive(entity))
            {
                continue;
            }

            ref var body = ref _bodies.Get(entity);
            var position = body.Position;
            if (position == mover.Destination)
            {
                mover.State = MoveState.Idle;
                continue;
            }

            if (mover.Speed <= Fixed.Zero)
            {
                continue;
            }

            var next = FixedVector2.MoveTowards(position, mover.Destination, mover.Speed);
            if (!_grid.IsPassable(next))
            {
                mover.State = MoveState.Idle;
                continue;
            }

            // From the whole way left, not from the step, whose rounding would make the angle coarse.
            mover.Facing = (mover.Destination - position).ToAngle();
            body.Position = next;
            if (next == mover.Destination)
            {
                mover.State = MoveState.Idle;
            }
        }
    }
}
