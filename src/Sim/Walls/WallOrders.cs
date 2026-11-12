using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;

namespace Sim.Walls;

/// <summary>
/// The wall orders of a match (design 2.5) and what their command and their system share. A wall
/// order is a run of tiles that one worker lays one after another; its state is the store of
/// <see cref="WallPlan"/>, one value for each tile that is paid for and not yet laid.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is paid.</b> The run is paid in full when the order is given: the price of one tile
/// for every tile of the run that can be laid at that moment as far as the player may know. A
/// tile that cannot be laid then, because it is off the map, not open ground, under a deposit or
/// a structure, or outside the player's territory, is skipped at once and never paid for, so
/// that a run drawn from an existing wall or across a wood costs what it adds.
/// <see cref="TilesToLay"/> gives those tiles, for the order and for the interface, which shows
/// the run and its price before the order is given.
/// </para>
/// <para>
/// <b>What the player may know</b> of a tile is what it may know of the site of any build order
/// (design 5.4, <see cref="ConstructionRules.CheckSiteAsKnown"/>): the ground it has explored,
/// its own structures, and the structures of others that it sees or remembers. A tile on ground
/// the player has not explored is therefore paid for, and skipped with its price back when the
/// worker comes to it and finds a tree there. The price of a run would otherwise count the
/// trees under the fog.
/// </para>
/// <para>
/// <b>What is given back.</b> A tile that cannot be laid when the worker comes to it is skipped
/// and its price given back, reported as a <see cref="SimEventKind.WallTileSkipped"/>. An order
/// that ends early, because it was cancelled, the worker was given another order or the worker
/// died, gives back every tile not yet laid, reported as one
/// <see cref="SimEventKind.BuildOrderEnded"/>. A tile that was laid is a structure of its own
/// from then on, and cancelling it is cancelling any structure that builds itself.
/// </para>
/// </remarks>
public sealed class WallOrders : IWorkerOrders
{
    private readonly ConstructionRules _rules;

    public WallOrders(GameData data, ConstructionRules rules, ComponentStore<WallPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(plans);
        _rules = rules;
        Plans = plans;
        foreach (var structure in data.Structures)
        {
            if (structure.Wall is not null)
            {
                Wall = structure;
                break;
            }
        }
    }

    /// <summary>The structure walls are made of, or null when the data has none.</summary>
    public StructureDef? Wall { get; }

    /// <summary>The tiles that are paid for and not yet laid, each an entity of its own.</summary>
    public ComponentStore<WallPlan> Plans { get; }

    /// <summary>
    /// The tiles of the run from one tile to another that a player could lay now as far as it
    /// may know, in the order of the run, added to <paramref name="tiles"/>. The price of the
    /// order is the price of <see cref="Wall"/> times their number.
    /// </summary>
    /// <returns>
    /// <see cref="BuildProblem.None"/> when at least one tile can be laid. Otherwise why the
    /// order would be refused: <see cref="BuildProblem.NoStructure"/>,
    /// <see cref="BuildProblem.Locked"/>, or what is wrong with the first tile of the run,
    /// <see cref="BuildProblem.SiteNotFree"/> or <see cref="BuildProblem.OutsideTerritory"/>.
    /// </returns>
    public BuildProblem TilesToLay(int player, int firstX, int firstY, int lastX, int lastY, List<(int X, int Y)> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (!_rules.HasPlayer(player) || Wall is not { } wall)
        {
            return BuildProblem.NoStructure;
        }

        if (!_rules.IsUnlocked(player, wall))
        {
            return BuildProblem.Locked;
        }

        if (!_rules.Grid.Contains(firstX, firstY) || !_rules.Grid.Contains(lastX, lastY))
        {
            return BuildProblem.SiteNotFree;
        }

        var found = false;
        var first = BuildProblem.None;
        var length = WallRun.LengthOf(firstX, firstY, lastX, lastY);
        for (var step = 0; step < length; step++)
        {
            var (x, y) = WallRun.TileAt(firstX, firstY, lastX, lastY, step);
            var problem = _rules.CheckSiteAsKnown(player, wall, x, y);
            if (problem == BuildProblem.None)
            {
                tiles.Add((x, y));
                found = true;
            }
            else if (step == 0)
            {
                first = problem;
            }
        }

        return found ? BuildProblem.None : first;
    }

    public bool Has(EntityId worker) => NextOf(worker) != EntityId.None;

    public (int Materials, int Energy) HeldBy(EntityId worker)
    {
        var materials = 0;
        var energy = 0;
        for (var i = 0; i < Plans.Count; i++)
        {
            var plan = Plans.ValueAt(i);
            if (plan.Worker == worker)
            {
                materials = checked(materials + plan.Materials);
                energy = checked(energy + plan.Energy);
            }
        }

        return (materials, energy);
    }

    /// <summary>
    /// Ends the wall order of a worker: every tile not yet laid is given back to the player who
    /// paid for it, who is told why with one <see cref="SimEventKind.BuildOrderEnded"/> at the
    /// tile that was next. Nothing happens for a worker without a wall order.
    /// </summary>
    public void EndOrder(EntityId worker, BuildProblem why)
    {
        var next = NextOf(worker);
        if (next == EntityId.None)
        {
            return;
        }

        var first = Plans.Get(next);
        var held = HeldBy(worker);
        for (var i = Plans.Count - 1; i >= 0; i--)
        {
            if (Plans.ValueAt(i).Worker == worker)
            {
                Drop(Plans.EntityAt(i));
            }
        }

        _rules.Stockpiles.Add(first.Owner, held.Materials, held.Energy);
        _rules.Events.Add(
            SimEventKind.BuildOrderEnded, first.Owner, worker, position: MapGrid.TileCenter(first.X, first.Y), amount: (long)why);
    }

    /// <summary>The tile the worker lays next: the oldest of its order. None for a worker without a wall order.</summary>
    internal EntityId NextOf(EntityId worker)
    {
        for (var i = 0; i < Plans.Count; i++)
        {
            if (Plans.ValueAt(i).Worker == worker)
            {
                return Plans.EntityAt(i);
            }
        }

        return EntityId.None;
    }

    /// <summary>Takes a tile out of its order, with nothing given back: it was laid, or what it held has been counted.</summary>
    internal void Drop(EntityId plan)
    {
        Plans.Remove(plan);
        _rules.World.Entities.Destroy(plan);
    }

    /// <summary>Skips a tile that cannot be laid: its price goes back to the player, who is told why.</summary>
    internal void Skip(EntityId plan, BuildProblem why)
    {
        var tile = Plans.Get(plan);
        Drop(plan);
        _rules.Stockpiles.Add(tile.Owner, tile.Materials, tile.Energy);
        _rules.Events.Add(
            SimEventKind.WallTileSkipped, tile.Owner, tile.Worker, position: MapGrid.TileCenter(tile.X, tile.Y), amount: (long)why);
    }

    /// <summary>
    /// Sends a worker to a tile of its order: to the center of the tile, by the move order, which
    /// takes it to the nearest tile it can reach when it cannot reach that one. A worker that is
    /// within reach of the tile already stands.
    /// </summary>
    internal void SendTo(World world, EntityId worker, int x, int y)
    {
        var body = _rules.Bodies.Get(worker);
        ref var mover = ref _rules.Movers.Get(worker);
        if (IsWithinReach(body.Position, x, y))
        {
            mover.Stop();
            return;
        }

        var target = MapGrid.TileCenter(x, y);
        ReadOnlySpan<EntityId> one = [worker];
        if (!_rules.Walk.Execute(world, new Command(CommandType.Move, body.Owner, one, target.X, target.Y)))
        {
            mover.Stop();
        }
    }

    /// <summary>True when a worker standing at a point has arrived at a tile, as at any site (design 5.4).</summary>
    internal static bool IsWithinReach(FixedVector2 position, int x, int y) =>
        new TileSquare(x, y, 1).DistanceSquaredRawTo(position) <= FixedVector2.SquareRaw(ConstructionRules.ArrivalReach);
}
