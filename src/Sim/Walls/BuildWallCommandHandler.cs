using Sim.Construction;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Walls;

/// <summary>
/// Executes <see cref="CommandType.BuildWall"/>: one worker is sent to lay a straight run of wall
/// tiles one after another, and the run is paid at once (design 2.5).
/// </summary>
/// <remarks>
/// <para>
/// The first tile of the run is the tile that holds the command's target point. The last tile
/// is the command's <see cref="Command.ContentId"/>, as <c>y * width + x</c>. The tiles between
/// are those of <see cref="WallRun"/>. The command lists workers, and one of them takes the
/// order, picked as for a build order: the nearest to an end of the run of those that have no
/// build order and no wall order yet, or, when all have one, the nearest of them, whose order
/// this one replaces. The ends are the first and the last tile that can be laid.
/// </para>
/// <para>
/// The order is given when the data has a wall and the player may build it, both tiles are on
/// the map, at least one tile of the run can be laid, a listed entity is a living worker of the
/// player, and the stockpile covers the price of every tile that can be laid, counting what the
/// worker's old order gives back. See <see cref="WallOrders"/> for what is paid and what is given
/// back.
/// </para>
/// <para>
/// The worker lays the tiles from the end of the run that is nearer to it, the first tile when
/// both are equally near. It keeps its work site and returns to it when the run is done, as with
/// a build order.
/// </para>
/// <para>
/// A command that is not carried out changes nothing and is reported to its player as a
/// <see cref="SimEventKind.BuildRefused"/> that says why, at the center of the first tile.
/// </para>
/// </remarks>
public sealed class BuildWallCommandHandler : ICommandHandler
{
    private readonly WallOrders _walls;
    private readonly ConstructionRules _rules;

    // Scratch for one command, emptied at its start. Not state.
    private readonly List<(int X, int Y)> _tiles = [];

    public BuildWallCommandHandler(WallOrders walls, ConstructionRules rules)
    {
        ArgumentNullException.ThrowIfNull(walls);
        ArgumentNullException.ThrowIfNull(rules);
        _walls = walls;
        _rules = rules;
    }

    public bool Execute(World world, in Command command)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!_rules.HasPlayer(command.Player))
        {
            return false;
        }

        var problem = TryOrder(world, in command, out var site);
        if (problem == BuildProblem.None)
        {
            return true;
        }

        _rules.Events.Add(
            SimEventKind.BuildRefused,
            command.Player,
            command.Entities.IsEmpty ? EntityId.None : command.Entities[0],
            position: site,
            amount: (long)problem);
        return false;
    }

    private BuildProblem TryOrder(World world, in Command command, out FixedVector2 site)
    {
        var player = command.Player;
        var grid = _rules.Grid;
        var firstX = command.TargetX.FloorToInt();
        var firstY = command.TargetY.FloorToInt();
        site = grid.Contains(firstX, firstY) ? MapGrid.TileCenter(firstX, firstY) : new FixedVector2(command.TargetX, command.TargetY);

        // A last tile that is no tile of the map is refused with the tiles off the map.
        var onMap = command.ContentId >= 0 && command.ContentId < grid.Width * grid.Height;
        var lastX = onMap ? command.ContentId % grid.Width : -1;
        var lastY = onMap ? command.ContentId / grid.Width : -1;

        _tiles.Clear();
        var problem = _walls.TilesToLay(player, firstX, firstY, lastX, lastY, _tiles);
        if (problem != BuildProblem.None)
        {
            return problem;
        }

        var wall = _walls.Wall!;
        var worker = PickWorker(world, in command, out var fromLast);
        if (worker == EntityId.None)
        {
            return BuildProblem.NoWorker;
        }

        // An order the worker already has ends when it takes this one, and gives back what it holds.
        var held = _rules.HeldBy(worker);
        var stockpiles = _rules.Stockpiles;
        var materials = checked(wall.Materials * _tiles.Count);
        var energy = checked(wall.Energy * _tiles.Count);
        if (stockpiles.Materials(player) + (held.Materials * Stockpiles.PerResource) < materials * Stockpiles.PerResource
            || stockpiles.Energy(player) + (held.Energy * Stockpiles.PerResource) < energy * Stockpiles.PerResource)
        {
            return BuildProblem.CannotAfford;
        }

        _rules.EndAnyOrder(worker, BuildProblem.OtherOrder);
        if (!stockpiles.TryPay(player, materials, energy))
        {
            throw new InvalidOperationException($"Player {player} could not pay for {_tiles.Count} wall tiles after the check that the stockpile covers them.");
        }

        if (fromLast)
        {
            _tiles.Reverse();
        }

        foreach (var (x, y) in _tiles)
        {
            _walls.Plans.Add(
                world.Entities.Create(),
                new WallPlan { Worker = worker, Owner = player, X = x, Y = y, Materials = wall.Materials, Energy = wall.Energy });
        }

        _rules.Work.StepAway(worker);
        _walls.SendTo(world, worker, _tiles[0].X, _tiles[0].Y);
        return BuildProblem.None;
    }

    /// <summary>
    /// The worker that takes the order, and the end of the run it starts from: the nearest to
    /// either end, in a straight line to the nearest edge of the tile; of two equally near, the
    /// one listed first.
    /// </summary>
    private EntityId PickWorker(World world, in Command command, out bool fromLast)
    {
        var first = new TileSquare(_tiles[0].X, _tiles[0].Y, 1);
        var last = new TileSquare(_tiles[^1].X, _tiles[^1].Y, 1);
        var picked = EntityId.None;
        var pickedIsFree = false;
        long nearest = 0;
        fromLast = false;
        foreach (var entity in command.Entities)
        {
            if (!world.Entities.IsAlive(entity)
                || !_rules.Workers.Contains(entity)
                || !_rules.Bodies.TryGet(entity, out var body)
                || body.Owner != command.Player)
            {
                continue;
            }

            var isFree = !_rules.HasOrder(entity);
            var toFirst = first.DistanceSquaredRawTo(body.Position);
            var toLast = last.DistanceSquaredRawTo(body.Position);
            var distance = Math.Min(toFirst, toLast);
            if (picked == EntityId.None || (isFree && !pickedIsFree) || (isFree == pickedIsFree && distance < nearest))
            {
                (picked, pickedIsFree, nearest, fromLast) = (entity, isFree, distance, toLast < toFirst);
            }
        }

        return picked;
    }
}
