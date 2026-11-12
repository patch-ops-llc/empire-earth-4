using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Construction;

/// <summary>
/// Executes <see cref="CommandType.Build"/>: one worker is sent to lay the foundation of a
/// structure, and the price is paid at once (design 5.4).
/// </summary>
/// <remarks>
/// <para>
/// The structure is the command's <see cref="Command.ContentId"/>, its index in the roster. The
/// tile that holds the command's target point is the lowest tile of the footprint: the one with
/// the lowest x and y. The command lists workers, and one of them takes the order: the nearest
/// to the site of those that have no build order and no wall order yet, or, when all have one,
/// the nearest of them, whose order this one replaces. Distance is measured in a straight line to the nearest
/// edge of the footprint, and of two equally near the one listed first goes.
/// </para>
/// <para>
/// The order is given when the structure can be ordered (it has a file, is no wall, and its
/// tier is reached), the site is, as far as the player may know, open ground free of structures
/// and deposits and, for all but a Capitol or an Outpost, inside the player's territory, or, for
/// those two, ground the player has explored, a listed entity is a living worker of the player,
/// the player is not at the limit of the structure, and the stockpile covers the price. What
/// the player may know is in <see cref="ConstructionRules.CheckSiteAsKnown"/>. The limit does not count an order that this one replaces, and the price counts what
/// that order gives back. Units on the site and enemy territory nearby are judged when the
/// worker arrives. A site the worker cannot reach is not refused here either: the worker walks
/// as near as it can and the order ends there.
/// </para>
/// <para>
/// The worker keeps its work site and returns to it when the foundation is laid (design 4.1):
/// it gives up its place and steps away. It walks to the tile of the footprint nearest to it.
/// </para>
/// <para>
/// A command that is not carried out changes nothing and is reported to its player as a
/// <see cref="SimEventKind.BuildRefused"/> that says why.
/// </para>
/// </remarks>
public sealed class BuildCommandHandler : ICommandHandler
{
    private readonly ConstructionRules _rules;

    public BuildCommandHandler(ConstructionRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
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
        var x = command.TargetX.FloorToInt();
        var y = command.TargetY.FloorToInt();
        site = new FixedVector2(command.TargetX, command.TargetY);
        if (_rules.Buildable(command.ContentId) is not { } structure)
        {
            return BuildProblem.NoStructure;
        }

        if (!_rules.IsUnlocked(player, structure))
        {
            return BuildProblem.Locked;
        }

        var ground = _rules.CheckSiteAsKnown(player, structure, x, y);
        if (_rules.Grid.Contains(x, y))
        {
            site = new TileSquare(x, y, structure.Footprint).Center;
        }

        if (ground != BuildProblem.None)
        {
            return ground;
        }

        var square = new TileSquare(x, y, structure.Footprint);
        var worker = PickWorker(world, in command, square);
        if (worker == EntityId.None)
        {
            return BuildProblem.NoWorker;
        }

        if (_rules.IsAtLimit(player, structure, worker))
        {
            return BuildProblem.LimitReached;
        }

        // An order the worker already has ends when it takes this one, and gives back what it holds.
        var held = _rules.HeldBy(worker);
        var stockpiles = _rules.Stockpiles;
        if (stockpiles.Materials(player) + (held.Materials * Stockpiles.PerResource) < structure.Materials * Stockpiles.PerResource
            || stockpiles.Energy(player) + (held.Energy * Stockpiles.PerResource) < structure.Energy * Stockpiles.PerResource)
        {
            return BuildProblem.CannotAfford;
        }

        _rules.EndAnyOrder(worker, BuildProblem.OtherOrder);

        if (!stockpiles.TryPay(player, structure.Materials, structure.Energy))
        {
            throw new InvalidOperationException($"Player {player} could not pay for '{structure.Id}' after the check that the stockpile covers it.");
        }

        _rules.Orders.Add(
            worker,
            new BuildOrder { Type = structure.Index, X = x, Y = y, Size = structure.Footprint, Materials = structure.Materials, Energy = structure.Energy });
        _rules.Work.StepAway(worker);
        SendToSite(world, worker, square);
        return BuildProblem.None;
    }

    private EntityId PickWorker(World world, in Command command, TileSquare site)
    {
        var picked = EntityId.None;
        var pickedIsFree = false;
        long nearest = 0;
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
            var distance = site.DistanceSquaredRawTo(body.Position);
            if (picked == EntityId.None || (isFree && !pickedIsFree) || (isFree == pickedIsFree && distance < nearest))
            {
                (picked, pickedIsFree, nearest) = (entity, isFree, distance);
            }
        }

        return picked;
    }

    /// <summary>
    /// Sends the worker to the tile of the footprint that is nearest to it, of those in the
    /// ground it stands in, ties to the lower y and then the lower x. When the footprint has no
    /// such tile, the move order takes the worker to the nearest tile it can reach.
    /// </summary>
    private void SendToSite(World world, EntityId worker, TileSquare site)
    {
        var body = _rules.Bodies.Get(worker);
        ref var mover = ref _rules.Movers.Get(worker);
        if (site.DistanceSquaredRawTo(body.Position) <= FixedVector2.SquareRaw(ConstructionRules.ArrivalReach))
        {
            // It is there already and lays the foundation in this tick.
            mover.Stop();
            return;
        }

        var region = _rules.Grid.RegionOf(body.Position);
        var found = false;
        var foundInRegion = false;
        long nearest = 0;
        var target = default(FixedVector2);
        for (var y = site.Y; y < site.Y + site.Size; y++)
        {
            for (var x = site.X; x < site.X + site.Size; x++)
            {
                var inRegion = region != 0 && _rules.Grid.RegionOf(x, y) == region;
                var center = MapGrid.TileCenter(x, y);
                var distance = FixedVector2.DistanceSquaredRaw(center, body.Position);
                if (!found || (inRegion && !foundInRegion) || (inRegion == foundInRegion && distance < nearest))
                {
                    (found, foundInRegion, nearest, target) = (true, inRegion, distance, center);
                }
            }
        }

        ReadOnlySpan<EntityId> one = [worker];
        if (!_rules.Walk.Execute(world, new Command(CommandType.Move, body.Owner, one, target.X, target.Y)))
        {
            mover.Stop();
        }
    }
}
