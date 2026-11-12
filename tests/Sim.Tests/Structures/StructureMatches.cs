using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Economy;
using Sim.Tests.Setup;

namespace Sim.Tests.Structures;

/// <summary>
/// Matches for the scenario tests of structures: the sample data with a Generator, which is the
/// 2 by 2 structure these tests place, on maps made in code.
/// </summary>
internal static class StructureMatches
{
    public static readonly Fixed QuarterTile = Fixed.One / 4;

    public static GameData GeneratorData => EconomyMatches.Data;

    public static Match Create(MapDef map, int players = 2) => EconomyMatches.Create(map, GeneratorData, players);

    /// <summary>A match on the sample map in which both players start as design 5.2 says.</summary>
    public static Match WithStarts(GameData? data = null) =>
        MatchSetup.Create(data ?? GeneratorData, (data ?? GeneratorData).Maps[0], 1, [new(0), new(1)]);

    /// <summary>The structures of a player, in the order of their IDs.</summary>
    public static EntityId[] StructuresOf(this Match match, int player) =>
    [
        .. Enumerable.Range(0, match.Structures.Count)
            .Select(match.Structures.EntityAt)
            .Where(entity => match.World.Entities.IsAlive(entity) && match.Bodies.Get(entity).Owner == player),
    ];

    /// <summary>Brings something's hit points to zero, so that the deaths of the next tick take it.</summary>
    public static void Raze(this Match match, EntityId entity) => match.Health.Get(entity).HitPoints = 0;

    public static SimEvent[] EventsOf(this Match match, SimEventKind kind) =>
        [.. match.Simulation.Events.Where(simEvent => simEvent.Kind == kind)];

    public static MoveState MoveStateOf(this Match match, EntityId unit) => match.Movers.Get(unit).State;

    public static (int X, int Y) TileOf(this Match match, EntityId unit)
    {
        var position = match.PositionOf(unit);
        return (position.X.FloorToInt(), position.Y.FloorToInt());
    }

    /// <summary>
    /// Runs ticks until the unit stands still, and checks in every tick that it stands on a tile
    /// units can stand on. Returns the number of ticks run.
    /// </summary>
    public static int RunUntilIdle(this Match match, EntityId unit, int limit = 600)
    {
        for (var ticks = 1; ticks <= limit; ticks++)
        {
            match.Simulation.Tick();
            Assert.True(match.Grid.IsPassable(match.PositionOf(unit)), $"The unit stands on blocked ground at {match.PositionOf(unit)} in tick {match.World.Tick - 1}.");
            if (match.MoveStateOf(unit) == MoveState.Idle)
            {
                return ticks;
            }
        }

        throw new InvalidOperationException($"The unit was still on its way after {limit} ticks.");
    }

    /// <summary>The number of tiles the grid lets units stand on.</summary>
    public static int PassableTiles(this MapGrid grid)
    {
        var count = 0;
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                count += grid.IsPassable(x, y) ? 1 : 0;
            }
        }

        return count;
    }
}
