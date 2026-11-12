using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Structures;

namespace Sim.Walls;

/// <summary>
/// Where the wall tiles stand and whose they are, and for each player the ground its units could
/// walk if the walls of its enemies were out of the way (design 2.5). The wall rule asks it
/// whether a wall is what keeps a unit from a place, without a search.
/// </summary>
/// <remarks>
/// <para>
/// Worked out from the structures that stand, like the tiles of the <see cref="MapGrid"/>, and
/// not state: <see cref="StructureTiles"/> has it rebuilt whenever it rebuilds the tiles, so the
/// two always agree. A wall tile counts from the moment it is laid, while it still builds
/// itself.
/// </para>
/// <para>
/// <b>The ground past the walls.</b> For a player, every tile a unit can stand on has a number,
/// and two tiles share it exactly when a walk leads from one to the other for a walker that may
/// also cross the wall tiles of other players. A walk goes as any walk does: a diagonal step
/// needs both tiles beside it passable, so two wall tiles that touch at a corner are crossed
/// through one of them. A tile nobody can stand on has the number zero, a wall tile too. The
/// numbers of a player are worked out when first asked for after a rebuild.
/// </para>
/// </remarks>
public sealed class WallMap : ITileFollower
{
    private readonly MapGrid _grid;
    private readonly EntityRegistry _entities;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<PlacedStructure> _structures;
    private readonly bool[] _isWall;
    private readonly EntityId[] _wallAt;
    private readonly int[] _ownerAt;
    private readonly int[]?[] _past;
    private readonly bool[]?[] _enemyWalls;
    private readonly bool[] _fresh;
    private readonly int[] _pending;

    public WallMap(
        GameData data,
        MapGrid grid,
        EntityRegistry entities,
        ComponentStore<Body> bodies,
        ComponentStore<PlacedStructure> structures,
        int playerCount)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentOutOfRangeException.ThrowIfNegative(playerCount);
        _grid = grid;
        _entities = entities;
        _bodies = bodies;
        _structures = structures;
        _isWall = new bool[data.Rules.Structures.Count];
        foreach (var structure in data.Structures)
        {
            _isWall[structure.Index] = structure.Wall is not null;
        }

        var tiles = grid.Width * grid.Height;
        _wallAt = new EntityId[tiles];
        _ownerAt = new int[tiles];
        _pending = new int[tiles];
        _past = new int[]?[playerCount];
        _enemyWalls = new bool[]?[playerCount];
        _fresh = new bool[playerCount];
    }

    /// <summary>True for a structure of the roster that is a wall tile to everyone.</summary>
    public bool IsWall(int structure) => structure >= 0 && structure < _isWall.Length && _isWall[structure];

    /// <summary>The wall tile that stands on a tile, or none.</summary>
    public EntityId WallAt(int x, int y) => _grid.Contains(x, y) ? _wallAt[(y * _grid.Width) + x] : EntityId.None;

    /// <summary>
    /// The number of the ground a tile belongs to for a player's units once the walls of the
    /// player's enemies are out of the way. Zero for a tile nobody can stand on now.
    /// </summary>
    public int PastWallsOf(int player, int x, int y) => _grid.Contains(x, y) ? PastWalls(player)[(y * _grid.Width) + x] : 0;

    /// <summary><see cref="PastWallsOf"/> for every tile, as <c>y * width + x</c>.</summary>
    public ReadOnlySpan<int> PastWalls(int player)
    {
        Refresh(player);
        return _past[player];
    }

    /// <summary>For every tile, as <c>y * width + x</c>, whether a wall tile of an enemy of the player stands on it.</summary>
    internal bool[] EnemyWalls(int player)
    {
        Refresh(player);
        return _enemyWalls[player]!;
    }

    public void TilesRebuilt()
    {
        Array.Clear(_wallAt);
        Array.Clear(_ownerAt);
        for (var i = 0; i < _structures.Count; i++)
        {
            var structure = _structures.ValueAt(i);
            var entity = _structures.EntityAt(i);
            if (!IsWall(structure.Type) || !_entities.IsAlive(entity) || !_bodies.TryGet(entity, out var body))
            {
                continue;
            }

            for (var y = structure.Y; y < structure.Y + structure.Size; y++)
            {
                for (var x = structure.X; x < structure.X + structure.Size; x++)
                {
                    if (_grid.Contains(x, y))
                    {
                        _wallAt[(y * _grid.Width) + x] = entity;
                        _ownerAt[(y * _grid.Width) + x] = body.Owner + 1;
                    }
                }
            }
        }

        Array.Clear(_fresh);
    }

    private void Refresh(int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, _fresh.Length);
        if (_fresh[player])
        {
            return;
        }

        var width = _grid.Width;
        var enemyWalls = _enemyWalls[player] ??= new bool[_ownerAt.Length];
        var past = _past[player] ??= new int[_ownerAt.Length];
        for (var tile = 0; tile < enemyWalls.Length; tile++)
        {
            enemyWalls[tile] = _ownerAt[tile] != 0 && _ownerAt[tile] != player + 1;
        }

        // Flooded along the four sides, as the regions of the grid are: a diagonal step needs both
        // tiles beside it, so whatever it joins is joined by two straight steps already.
        Array.Clear(past);
        var count = 0;
        for (var first = 0; first < past.Length; first++)
        {
            if (past[first] != 0 || !CanCross(first))
            {
                continue;
            }

            count++;
            past[first] = count;
            _pending[0] = first;
            var pendingCount = 1;
            while (pendingCount > 0)
            {
                var tile = _pending[--pendingCount];
                var x = tile % width;
                if (x > 0)
                {
                    Visit(tile - 1);
                }

                if (x < width - 1)
                {
                    Visit(tile + 1);
                }

                if (tile >= width)
                {
                    Visit(tile - width);
                }

                if (tile < past.Length - width)
                {
                    Visit(tile + width);
                }
            }

            void Visit(int tile)
            {
                if (past[tile] == 0 && CanCross(tile))
                {
                    past[tile] = count;
                    _pending[pendingCount++] = tile;
                }
            }
        }

        // A wall tile joins the ground on its two sides and is no ground itself.
        for (var tile = 0; tile < past.Length; tile++)
        {
            if (enemyWalls[tile])
            {
                past[tile] = 0;
            }
        }

        _fresh[player] = true;

        bool CanCross(int tile) => enemyWalls[tile] || _grid.IsPassable(tile % width, tile / width);
    }
}
