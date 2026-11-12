using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Walls;

namespace Sim.Combat;

/// <summary>
/// The wall rule of design 2.5 as the orders and the attacks of units use it: a unit ordered to
/// a place it cannot reach attacks the nearest enemy wall tile in its way, and goes on with its
/// order once that tile is destroyed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who.</b> Every unit of a chassis: each has an attack. A worker has none and goes to the
/// nearest tile it can reach (design 5.3).
/// </para>
/// <para>
/// <b>The place</b> of a move or an attack-move order is where the order would take the unit if
/// the walls of its enemies were out of the way (<see cref="Breaching.GoalFor"/>), so that an
/// order onto a building inside a walled camp, or one whose place in the group falls on a tree
/// there, still leads through the wall. The place of an attack order is where the unit would
/// stand to attack: the target's own tile for a unit, a tile beside the footprint for a
/// structure. A unit that shoots and can bring its target into range from the ground it can
/// reach has no wall in its way: it is not kept from its target (design 2.4).
/// </para>
/// <para>
/// <b>When the search runs.</b> A wall is in the way exactly when the place is cut off by enemy
/// walls (<see cref="Breaching.IsCutOff"/>), which is a lookup, so the search runs only when it
/// finds a wall. When an order is given, a unit takes the wall that an earlier unit of the same
/// order found if it can walk straight to where that unit stands, so that a group breaks in at
/// one tile; at most <see cref="MaxSearchesPerOrder"/> searches are run for one order. Any other
/// unit finds its wall in the attacks of a later tick, on its turn to look, within the budget of
/// <see cref="Breaching.ExpansionsPerTick"/>. That is also how a unit finds the next wall behind
/// the one it broke, and a wall that closed its way after the order was given.
/// </para>
/// </remarks>
public sealed class WallRule
{
    /// <summary>The most searches for a wall that one order runs when it is given.</summary>
    public const int MaxSearchesPerOrder = MoveCommandHandler.MaxSharedRoutes;

    private readonly MapGrid _grid;
    private readonly Breaching _breaching;
    private readonly Targeting _targeting;
    private readonly Approach _approach;

    // Scratch for one order, emptied by BeginOrder. Not state.
    private readonly List<(FixedVector2 Position, int Region, EntityId Wall)> _found = [];
    private int _searches;

    public WallRule(MapGrid grid, Breaching breaching, Targeting targeting, Approach approach)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(breaching);
        ArgumentNullException.ThrowIfNull(targeting);
        ArgumentNullException.ThrowIfNull(approach);
        _grid = grid;
        _breaching = breaching;
        _targeting = targeting;
        _approach = approach;
    }

    /// <summary>Call it before the units of one order are asked for their walls: they share what they find.</summary>
    public void BeginOrder()
    {
        _found.Clear();
        _searches = 0;
    }

    /// <summary>Where a move or attack-move order to a place takes a unit once no enemy wall is in its way.</summary>
    public FixedVector2 GoalFor(int player, FixedVector2 position, FixedVector2 place) =>
        _breaching.GoalFor(player, position, place, position);

    /// <summary>True when enemy walls, and nothing else, keep a unit from a place.</summary>
    public bool IsCutOff(int player, FixedVector2 position, FixedVector2 place) => _breaching.IsCutOff(player, position, place);

    /// <summary>
    /// The wall in the way of a unit of the order that is being given, or none: none is in its
    /// way, or the order has run its searches and the unit finds its wall later.
    /// </summary>
    public EntityId FindForOrder(int player, FixedVector2 position, FixedVector2 place)
    {
        if (!_breaching.IsCutOff(player, position, place))
        {
            return EntityId.None;
        }

        // One order is one player's, so the regions of its units are numbered on one grid.
        var grid = _grid.For(player);
        var region = grid.RegionOf(position);
        foreach (var found in _found)
        {
            if (found.Region == region && grid.IsCorridorClear(position, found.Position, Mover.Clearance))
            {
                return found.Wall;
            }
        }

        if (_searches >= MaxSearchesPerOrder)
        {
            return EntityId.None;
        }

        _searches++;
        var wall = _breaching.FindWall(player, position, place, out _);
        _found.Add((position, region, wall));
        return wall;
    }

    /// <summary>
    /// The wall in the way of a unit, found in the attacks of a tick, or none: none is in its
    /// way, or the budget of the tick is spent.
    /// </summary>
    /// <param name="player">The unit's owner.</param>
    /// <param name="position">Where the unit stands.</param>
    /// <param name="place">The place it is ordered to.</param>
    /// <param name="budget">The tiles the searches of this tick may still expand. A search takes what it expands from it.</param>
    public EntityId Find(int player, FixedVector2 position, FixedVector2 place, ref int budget)
    {
        if (budget <= 0)
        {
            return EntityId.None;
        }

        var wall = _breaching.FindWall(player, position, place, out var expansions);
        budget -= expansions;
        return wall;
    }

    /// <summary>
    /// The place of an attack order, when the unit cannot get to its target and might past
    /// enemy walls: where it would stand to attack. False when it can walk up to the target as
    /// things are, when it shoots and has the target in range from the nearest tile it can
    /// reach, and when no place to attack from lies on the ground past the walls.
    /// </summary>
    /// <param name="player">The unit's owner.</param>
    /// <param name="profile">The unit's attack.</param>
    /// <param name="position">Where the unit stands.</param>
    /// <param name="mark">The target.</param>
    /// <param name="stand">Where it would stand. <see cref="IsCutOff"/> says whether enemy walls keep it from there.</param>
    public bool TryFindStand(int player, AttackProfile profile, FixedVector2 position, in Mark mark, out FixedVector2 stand)
    {
        ArgumentNullException.ThrowIfNull(profile);
        stand = default;
        var grid = _grid.For(player);
        var region = grid.RegionOf(position);
        var past = _breaching.Walls.PastWalls(player);
        var own = region == 0 ? 0 : past[TileOf(position)];
        if (own == 0)
        {
            return false;
        }

        FixedVector2 toward;
        if (!mark.IsStructure)
        {
            if (grid.RegionOf(mark.Center) == region || past[TileOf(mark.Center)] != own)
            {
                return false;
            }

            stand = mark.Center;
            toward = mark.Center;
        }
        else
        {
            // The tile beside the footprint that is nearest to the unit, of those past the walls:
            // the lower y of two equally near, then the lower x.
            var tiles = mark.Tiles;
            var nearest = long.MaxValue;
            for (var y = tiles.Y - 1; y <= tiles.Y + tiles.Size; y++)
            {
                for (var x = tiles.X - 1; x <= tiles.X + tiles.Size; x++)
                {
                    if (tiles.Covers(x, y) || !_grid.Contains(x, y))
                    {
                        continue;
                    }

                    if (grid.RegionOf(x, y) == region)
                    {
                        return false;
                    }

                    var center = MapGrid.TileCenter(x, y);
                    var distance = FixedVector2.DistanceSquaredRaw(position, center);
                    if (past[(y * _grid.Width) + x] == own && distance < nearest)
                    {
                        nearest = distance;
                        stand = center;
                    }
                }
            }

            if (nearest == long.MaxValue)
            {
                return false;
            }

            toward = tiles.NearestPointTo(position);
        }

        if (!profile.Melee && grid.TryFindNearestTile(toward, region, position, out var tileX, out var tileY))
        {
            // A wall it can shoot over does not keep it from its target.
            return !_targeting.Reaches(profile, MapGrid.TileCenter(tileX, tileY), in mark);
        }

        return true;
    }

    /// <summary>
    /// Turns a unit to the wall in its way: it walks up to the tile as to any structure, or
    /// stands when the tile is in reach. Nothing happens when the tile is gone; the unit's next
    /// turn in the attacks sees that and goes on.
    /// </summary>
    public void WalkToWall(World world, ref Combatant unit, ref Mover mover, in Body body, AttackProfile profile)
    {
        if (!_targeting.IsEnemy(world, body.Owner, unit.WallInWay, out var wall))
        {
            return;
        }

        var mark = _targeting.MarkOf(unit.WallInWay, in wall);
        if (_targeting.Reaches(profile, body.Position, in mark))
        {
            mover.Stop();
        }
        else
        {
            _approach.WalkToStructure(body.Owner, ref mover, body.Position, mark.Tiles);
        }

        unit.Engaged = true;
        unit.AimedAt = wall.Position;
    }

    private int TileOf(FixedVector2 point) => (point.Y.FloorToInt() * _grid.Width) + point.X.FloorToInt();
}
