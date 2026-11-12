using Sim.Construction;
using Sim.Core;
using Sim.Map;
using Sim.Setup;
using Sim.Walls;

namespace Sim.Tests.Walls;

/// <summary>What the tests of wall orders share: the order itself and what a match holds of one.</summary>
internal static class WallMatches
{
    /// <summary>A wall order for the run from one tile to another. The point is the middle of the first tile.</summary>
    public static Command BuildWall(this Match match, int player, (int X, int Y) first, (int X, int Y) last, params EntityId[] workers)
    {
        var point = MapGrid.TileCenter(first.X, first.Y);
        return new Command(CommandType.BuildWall, player, workers, point.X, point.Y, contentId: (last.Y * match.Grid.Width) + last.X);
    }

    /// <summary>Queues a wall order for the next tick to run.</summary>
    public static void OrderWall(this Match match, int player, (int X, int Y) first, (int X, int Y) last, params EntityId[] workers) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, match.BuildWall(player, first, last, workers));

    /// <summary>The tiles a worker has yet to lay, in the order it lays them.</summary>
    public static (int X, int Y)[] PlansOf(this Match match, EntityId worker)
    {
        var tiles = new List<(int X, int Y)>();
        for (var i = 0; i < match.WallPlans.Count; i++)
        {
            var plan = match.WallPlans.ValueAt(i);
            if (plan.Worker == worker)
            {
                tiles.Add((plan.X, plan.Y));
            }
        }

        return [.. tiles];
    }

    /// <summary>The tiles under wall, laid or finished, of a player, in the order they were laid.</summary>
    public static (int X, int Y)[] WallOf(this Match match, int player)
    {
        var wall = match.Walls.Wall!.Index;
        var tiles = new List<(int X, int Y)>();
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var structure = match.Structures.ValueAt(i);
            var entity = match.Structures.EntityAt(i);
            if (structure.Type == wall && match.World.Entities.IsAlive(entity) && match.Bodies.Get(entity).Owner == player)
            {
                tiles.Add((structure.X, structure.Y));
            }
        }

        return [.. tiles];
    }

    /// <summary>Runs ticks until the worker has no wall order, and returns the number of the last tick run.</summary>
    public static int RunUntilWallOrderEnds(this Match match, EntityId worker, int limit = 4000)
    {
        for (var ticks = 0; ticks < limit; ticks++)
        {
            match.Simulation.Tick();
            if (!match.Walls.Has(worker))
            {
                return match.World.Tick - 1;
            }
        }

        throw new InvalidOperationException($"The worker still had its wall order after {limit} ticks.");
    }

    /// <summary>The events of the last tick of one kind, each as its entity and the reason in its amount.</summary>
    public static (EntityId Entity, BuildProblem Why)[] Reasons(this Match match, SimEventKind kind) =>
        [.. match.Simulation.Events.Where(simEvent => simEvent.Kind == kind).Select(simEvent => (simEvent.Entity, (BuildProblem)simEvent.Amount))];
}
