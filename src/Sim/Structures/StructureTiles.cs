using Sim.Construction;
using Sim.Core;
using Sim.Data;
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
/// <para>
/// A finished gate is open to its owner (design 2.5): its tile is blocked for everyone else and
/// is ground on the grid of its owner, <see cref="MapGrid.For"/>. A gate is finished when it has
/// no <see cref="Foundation"/>; one that still builds itself blocks its tile for everyone, as
/// every foundation does. So which gates are open is read from the state too, and
/// <see cref="Changed"/> is also called when a gate is finished.
/// </para>
/// <para>
/// Whatever places or removes a structure inside a tick calls <see cref="Changed"/> before anything
/// else looks at the grid, so the grid matches the store of structures whenever a system or a
/// command handler reads it. A structure that dies is noticed by <see cref="StructureSystem"/>.
/// </para>
/// </remarks>
public sealed class StructureTiles
{
    private readonly MapGrid _grid;
    private readonly EntityRegistry _entities;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<PlacedStructure> _structures;
    private readonly ComponentStore<Foundation> _foundations;
    private readonly RouteCache _routes;
    private readonly Rerouting _rerouting;
    private readonly int _gate;

    private readonly List<ITileFollower> _followers = [];

    // Scratch for one rebuild. Not state.
    private TileSquare[] _squares = [];
    private int[] _openTo = [];

    public StructureTiles(
        GameData data,
        MapGrid grid,
        EntityRegistry entities,
        ComponentStore<Body> bodies,
        ComponentStore<PlacedStructure> structures,
        ComponentStore<Foundation> foundations,
        RouteCache routes,
        Rerouting rerouting)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(foundations);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(rerouting);
        _grid = grid;
        _entities = entities;
        _bodies = bodies;
        _structures = structures;
        _foundations = foundations;
        _routes = routes;
        _rerouting = rerouting;
        _gate = data.Gate?.Index ?? -1;
    }

    /// <summary>
    /// Blocks exactly the tiles of the living structures, opens each finished gate to its owner,
    /// numbers the regions again, empties the route cache and tells the followers. It changes
    /// nothing in the state: movers keep the routes their state names, which the cache finds
    /// again on the same tiles.
    /// </summary>
    /// <returns>True when a tile changed for anyone.</returns>
    public bool Rebuild()
    {
        if (_squares.Length < _structures.Count)
        {
            _squares = new TileSquare[Math.Max(_structures.Count, 2 * _squares.Length)];
            _openTo = new int[_squares.Length];
        }

        var count = 0;
        for (var i = 0; i < _structures.Count; i++)
        {
            var entity = _structures.EntityAt(i);
            if (!_entities.IsAlive(entity))
            {
                continue;
            }

            var structure = _structures.ValueAt(i);
            _squares[count] = structure.Tiles;
            _openTo[count] = structure.Type == _gate && !_foundations.Contains(entity) && _bodies.TryGet(entity, out var body)
                ? body.Owner
                : -1;
            count++;
        }

        var changed = _grid.BlockOnly(_squares.AsSpan(0, count), _openTo.AsSpan(0, count));
        _routes.Clear();
        foreach (var follower in _followers)
        {
            follower.TilesRebuilt();
        }

        return changed;
    }

    /// <summary>Has something else that is worked out from the structures rebuilt whenever the tiles are.</summary>
    public void Follow(ITileFollower follower)
    {
        ArgumentNullException.ThrowIfNull(follower);
        _followers.Add(follower);
    }

    /// <summary>
    /// Call it inside a tick, or before the first, right after a structure was placed or removed
    /// or a gate was finished: the tiles and regions follow, the routes found on the old tiles
    /// are dropped, and every unit on its way whose ground changed gets a way that fits the new
    /// tiles.
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
    /// and under no other structure, a gate of the player's own included.
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
