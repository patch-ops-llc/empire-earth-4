using Sim.Core;
using Sim.Map;
using Sim.Movement;

namespace Sim.Structures;

/// <summary>
/// Keeps the tiles of the <see cref="MapGrid"/> in line with the structures that stand: the tiles
/// under a living structure are blocked, and no others besides the ground. The grid's tiles and
/// regions are worked out from the state and are not state themselves, so this is what
/// <see cref="Setup.Match.RefreshDerivedState"/> runs first.
/// </summary>
/// <remarks>
/// Whatever places or removes a structure inside a tick calls <see cref="Changed"/> before anything
/// else looks at the grid, so the grid matches the store of structures whenever a system or a
/// command handler reads it. A structure that dies is noticed by <see cref="StructureSystem"/>.
/// </remarks>
public sealed class StructureTiles
{
    private readonly MapGrid _grid;
    private readonly EntityRegistry _entities;
    private readonly ComponentStore<PlacedStructure> _structures;
    private readonly RouteCache _routes;
    private readonly Rerouting _rerouting;

    // Scratch for one rebuild. Not state.
    private TileSquare[] _squares = [];

    public StructureTiles(MapGrid grid, EntityRegistry entities, ComponentStore<PlacedStructure> structures, RouteCache routes, Rerouting rerouting)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(rerouting);
        _grid = grid;
        _entities = entities;
        _structures = structures;
        _routes = routes;
        _rerouting = rerouting;
    }

    /// <summary>
    /// Blocks exactly the tiles of the living structures, numbers the regions again and empties
    /// the route cache. It changes nothing in the state: movers keep the routes their state names,
    /// which the cache finds again on the same tiles.
    /// </summary>
    /// <returns>True when a tile changed.</returns>
    public bool Rebuild()
    {
        if (_squares.Length < _structures.Count)
        {
            _squares = new TileSquare[Math.Max(_structures.Count, 2 * _squares.Length)];
        }

        var count = 0;
        for (var i = 0; i < _structures.Count; i++)
        {
            if (_entities.IsAlive(_structures.EntityAt(i)))
            {
                _squares[count++] = _structures.ValueAt(i).Tiles;
            }
        }

        var changed = _grid.BlockOnly(_squares.AsSpan(0, count));
        _routes.Clear();
        return changed;
    }

    /// <summary>
    /// Call it inside a tick, or before the first, right after a structure was placed or removed:
    /// the tiles and regions follow, the routes found on the old tiles are dropped, and every unit
    /// on its way gets a way that fits the new tiles.
    /// </summary>
    public void Changed(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (Rebuild())
        {
            _rerouting.Reroute(world);
        }
    }

    /// <summary>
    /// True when a structure could be laid on the square: every tile is on the map, open ground,
    /// and under no other structure.
    /// </summary>
    public bool IsFree(TileSquare square)
    {
        for (var y = square.Y; y < square.Y + square.Size; y++)
        {
            for (var x = square.X; x < square.X + square.Size; x++)
            {
                if (!_grid.IsPassable(x, y))
                {
                    return false;
                }
            }
        }

        return square.Size > 0;
    }
}
