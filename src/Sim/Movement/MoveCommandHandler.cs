using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Movement;

/// <summary>
/// Executes <see cref="CommandType.Move"/>: sends the listed entities to the target position,
/// replacing whatever move order each had.
/// </summary>
/// <remarks>
/// <para>
/// The command is valid when its player is in the match, its target is a point on the map, and at
/// least one listed entity can take the order: alive, owned by the player, and a mover. Entities
/// that cannot take it are skipped, because a unit may die between the moment a player gives an
/// order and the tick it runs, and the rest of the group should still go. A command that moves
/// nobody is dropped.
/// </para>
/// <para>
/// A unit whose way to the target is a clear straight line walks it. Any other unit gets a route.
/// One order makes one route for the whole group where it can: a unit takes the route of an earlier
/// unit in the list when it can walk straight to that route's first tile, and starts a route of its
/// own from the tile it stands on when it cannot.
/// </para>
/// <para>
/// A target no unit can stand on, or one cut off from a unit, is not an error. The unit goes to
/// the center of the tile nearest the target among those it can reach. A target right beside
/// blocked ground is moved off it by <see cref="Mover.StandOff"/>, inside its tile.
/// </para>
/// </remarks>
public sealed class MoveCommandHandler : ICommandHandler
{
    /// <summary>The most routes one order makes for the others to share, in each region.</summary>
    public const int MaxSharedRoutes = 8;

    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly int _playerCount;

    // Scratch for one command, emptied at its start. Not state.
    private readonly List<Goal> _goals = [];
    private readonly List<SharedStart> _starts = [];

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

        _goals.Clear();
        _starts.Clear();
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

            var position = body.Position;
            var region = _grid.RegionOf(position);
            if (region == 0)
            {
                continue;
            }

            var goal = GoalFor(region, target, position);
            ref var mover = ref _movers.ValueAt(index);
            moved = true;

            var tile = (position.Y.FloorToInt() * _grid.Width) + position.X.FloorToInt();
            if (tile == goal.Tile || _grid.IsCorridorClear(position, goal.Destination, Mover.Clearance))
            {
                mover.WalkStraightTo(goal.Destination);
                continue;
            }

            mover.WalkRouteTo(goal.Destination, StartFor(region, tile, position), goal.Tile);
        }

        return moved;
    }

    /// <summary>Where the units of one region go: the same place for all of them.</summary>
    private Goal GoalFor(int region, FixedVector2 target, FixedVector2 firstPosition)
    {
        foreach (var known in _goals)
        {
            if (known.Region == region)
            {
                return known;
            }
        }

        var tileX = target.X.FloorToInt();
        var tileY = target.Y.FloorToInt();
        FixedVector2 destination;
        if (_grid.RegionOf(tileX, tileY) == region)
        {
            destination = _grid.KeepClear(target, Mover.StandOff);
        }
        else
        {
            // Every region has a tile, so this finds one.
            _grid.TryFindNearestTile(target, region, firstPosition, out tileX, out tileY);
            destination = MapGrid.TileCenter(tileX, tileY);
        }

        var goal = new Goal(region, (tileY * _grid.Width) + tileX, destination);
        _goals.Add(goal);
        return goal;
    }

    /// <summary>The first tile of the route a unit takes: that of an earlier unit of this order if it can walk straight there, else its own.</summary>
    private int StartFor(int region, int ownTile, FixedVector2 position)
    {
        var shared = 0;
        foreach (var start in _starts)
        {
            if (start.Region != region)
            {
                continue;
            }

            shared++;
            var center = MapGrid.TileCenter(start.Tile % _grid.Width, start.Tile / _grid.Width);
            if (start.Tile == ownTile || _grid.IsCorridorClear(position, center, Mover.Clearance))
            {
                return start.Tile;
            }
        }

        if (shared < MaxSharedRoutes)
        {
            _starts.Add(new SharedStart(region, ownTile));
        }

        return ownTile;
    }

    private readonly record struct Goal(int Region, int Tile, FixedVector2 Destination);

    private readonly record struct SharedStart(int Region, int Tile);
}
