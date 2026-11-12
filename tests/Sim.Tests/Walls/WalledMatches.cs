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
/// <remarks>
/// The wall rule knows only the walls a unit's owner sees or remembers (design 5.3). The walls of
/// these tests are remembered by every player from the moment they stand, as if each had seen
/// them once, so that the tests ask what the rule does with a wall and not how a player came to
/// know of it. <see cref="UnseenWallTile"/> and <see cref="UnseenRing"/> place walls that nobody
/// has seen; <c>WallsInFogTests</c> is about those.
/// </remarks>
internal static class WalledMatches
{
    /// <summary>A finished wall tile of a player, which every other player remembers.</summary>
    public static EntityId WallTile(this Match match, int player, int x, int y)
    {
        var wall = match.UnseenWallTile(player, x, y);
        match.MakeKnown(wall);
        return wall;
    }

    /// <summary>A finished wall tile of a player that no other player has seen.</summary>
    public static EntityId UnseenWallTile(this Match match, int player, int x, int y) =>
        match.PlaceStructure(player, SiegeMatches.Wall, x, y);

    /// <summary>A finished gate of a player, which every other player remembers.</summary>
    public static EntityId GateTile(this Match match, int player, int x, int y)
    {
        var gate = match.PlaceStructure(player, "gate", x, y);
        match.MakeKnown(gate);
        return gate;
    }

    /// <summary>Lets every other player remember a structure as it is now, as if each had seen it.</summary>
    public static void MakeKnown(this Match match, EntityId structure)
    {
        Assert.True(match.Vision.TryDescribeStructure(structure, out var seen));
        for (var player = 0; player < match.Vision.Memory.PlayerCount; player++)
        {
            if (player != seen.Owner)
            {
                match.Vision.Memory.Set(player, in seen);
            }
        }
    }

    /// <summary><see cref="Ring"/> of wall tiles that no other player has seen.</summary>
    public static void UnseenRing(this Match match, int player, (int X, int Y) low, (int X, int Y) high)
    {
        for (var y = low.Y; y <= high.Y; y++)
        {
            for (var x = low.X; x <= high.X; x++)
            {
                if (x == low.X || x == high.X || y == low.Y || y == high.Y)
                {
                    match.UnseenWallTile(player, x, y);
                }
            }
        }
    }

    /// <summary>
    /// A worker of a player, which sees 6 tiles round it and does nothing else: it gives its
    /// player sight of a target that an attack order is to name.
    /// </summary>
    public static EntityId Watcher(this Match match, int player, string x, string y) => match.SpawnWorker(player, x, y);

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
