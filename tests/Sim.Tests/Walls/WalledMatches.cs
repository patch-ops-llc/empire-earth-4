using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Setup;

namespace Sim.Tests.Walls;

/// <summary>
/// What the tests of the wall rule share: walls that stand finished before the first tick, on the
/// open field of <see cref="SiegeMatches"/>, where a wall tile has 400 hit points.
/// </summary>
internal static class WalledMatches
{
    /// <summary>A finished wall tile of a player.</summary>
    public static EntityId WallTile(this Match match, int player, int x, int y) =>
        match.PlaceStructure(player, SiegeMatches.Wall, x, y);

    /// <summary>
    /// A closed ring of wall: every tile on the edge of the box from one corner tile to the
    /// other, the four corner tiles included.
    /// </summary>
    public static void Ring(this Match match, int player, (int X, int Y) low, (int X, int Y) high)
    {
        for (var y = low.Y; y <= high.Y; y++)
        {
            for (var x = low.X; x <= high.X; x++)
            {
                if (x == low.X || x == high.X || y == low.Y || y == high.Y)
                {
                    match.WallTile(player, x, y);
                }
            }
        }
    }

    /// <summary>
    /// A closed ring whose tiles touch only at their corners: the tiles at a given distance from
    /// a center tile, counted in steps along the two axes.
    /// </summary>
    public static void Diamond(this Match match, int player, (int X, int Y) center, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            var dx = radius - Math.Abs(dy);
            match.WallTile(player, center.X + dx, center.Y + dy);
            if (dx != 0)
            {
                match.WallTile(player, center.X - dx, center.Y + dy);
            }
        }
    }

    /// <summary>The wall tile on a tile.</summary>
    public static EntityId WallOn(this Match match, int x, int y)
    {
        var wall = match.WallMap.WallAt(x, y);
        Assert.NotEqual(EntityId.None, wall);
        return wall;
    }

    /// <summary>Leaves every wall tile that stands with so many hit points, so that a test need not wait out 400.</summary>
    public static void WeakenWalls(this Match match, int hitPoints)
    {
        for (var i = 0; i < match.Structures.Count; i++)
        {
            if (match.WallMap.IsWall(match.Structures.ValueAt(i).Type))
            {
                match.SetHitPoints(match.Structures.EntityAt(i), hitPoints * 1000L);
            }
        }
    }

    /// <summary>The wall tiles that have lost hit points or are gone, of those that stood when the list was made.</summary>
    public static List<EntityId> Damaged(this Match match, IReadOnlyDictionary<EntityId, long> before) =>
        [.. before.Where(tile => !match.IsAlive(tile.Key) || match.HitPointsOf(tile.Key) < tile.Value).Select(tile => tile.Key).Order()];

    /// <summary>Every wall tile that stands, with its hit points.</summary>
    public static Dictionary<EntityId, long> WallHitPoints(this Match match)
    {
        var tiles = new Dictionary<EntityId, long>();
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var entity = match.Structures.EntityAt(i);
            if (match.WallMap.IsWall(match.Structures.ValueAt(i).Type) && match.IsAlive(entity))
            {
                tiles.Add(entity, match.HitPointsOf(entity));
            }
        }

        return tiles;
    }

    /// <summary>Queues a move order for the next tick to run.</summary>
    public static void OrderMove(this Match match, int player, FixedVector2 target, params EntityId[] entities) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, CombatMatches.MoveCommand(player, target, entities));

    /// <summary>True when a unit has no walk to finish.</summary>
    public static bool Stands(this Match match, EntityId unit) => match.Movers.Get(unit).State == MoveState.Idle;

    /// <summary>The tile an entity stands in.</summary>
    public static (int X, int Y) TileOf(this Match match, EntityId entity)
    {
        var position = match.PositionOf(entity);
        return (position.X.FloorToInt(), position.Y.FloorToInt());
    }

    /// <summary>True when an entity stands inside a box of tiles, its edge tiles included.</summary>
    public static bool IsInside(this Match match, EntityId entity, (int X, int Y) low, (int X, int Y) high)
    {
        var (x, y) = match.TileOf(entity);
        return x >= low.X && x <= high.X && y >= low.Y && y <= high.Y;
    }

    public static FixedVector2 Center(int x, int y) => MapGrid.TileCenter(x, y);
}
