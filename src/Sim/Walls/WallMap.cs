using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Structures;
using Sim.Vision;

namespace Sim.Walls;

/// <summary>
/// Where the wall tiles stand and whose they are, and for each player the ground its units could
/// walk if the walls of its enemies that the player knows of were out of the way (design 2.5,
/// 5.3). The wall rule asks it whether a wall is what keeps a unit from a place, without a
/// search.
/// </summary>
/// <remarks>
/// <para>
/// Worked out from the structures that stand and from what each player sees and remembers, like
/// the tiles of the <see cref="MapGrid"/>, and not state: <see cref="StructureTiles"/> has it
/// rebuilt whenever it rebuilds the tiles, so the two always agree, and what a player knows is
/// looked at again whenever the fog of war has been written. A wall tile counts from the moment
/// it is laid, while it still builds itself.
/// </para>
/// <para>
/// <b>The walls a player knows.</b> A wall tile of another player is a wall to a player only when
/// the player sees it or remembers it (<see cref="MatchVision.KnowsStructure"/>): then its units
/// may be sent to break it. A wall tile that stands and that the player has never seen is ground
/// nobody can walk, as any structure the player has never seen is to the routes: it blocks, and
/// no unit of the player goes for it. A wall tile that stands stays known once it is: a memory
/// ends only when the structure is seen to be gone.
/// </para>
/// <para>
/// <b>The ground past the walls.</b> For a player, every tile a unit can stand on has a number,
/// and two tiles share it exactly when a walk leads from one to the other for a walker that may
/// also cross the wall tiles of other players that the player knows. A walk goes as any walk
/// does: a diagonal step needs both tiles beside it passable, so two wall tiles that touch at a
/// corner are crossed through one of them. A tile nobody can stand on has the number zero, a
/// wall tile too. The numbers of a player are worked out when first asked for after a rebuild,
/// and again when the player has come to know a wall since.
/// </para>
/// </remarks>
public sealed class WallMap : ITileFollower
{
    private readonly MapGrid _grid;
    private readonly EntityRegistry _entities;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<PlacedStructure> _structures;
    private readonly MatchVision _vision;
    private readonly bool[] _isWall;
    private readonly EntityId[] _wallAt;
    private readonly int[] _ownerAt;
    private readonly int[]?[] _past;
    private readonly bool[]?[] _enemyWalls;
    private readonly bool[] _fresh;
    private readonly int[] _pending;

    // The tiles that have a wall, in the order of the structures. Made with the tiles.
    private readonly List<int> _wallTiles = [];

    // For each player, the writes of the fog of war and of the memories when its walls were last looked at.
    private readonly int[] _lookedAt;

    public WallMap(
        GameData data,
        MapGrid grid,
        EntityRegistry entities,
        ComponentStore<Body> bodies,
        ComponentStore<PlacedStructure> structures,
        MatchVision vision,
        int playerCount)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentOutOfRangeException.ThrowIfNegative(playerCount);
        _grid = grid;
        _entities = entities;
        _bodies = bodies;
        _structures = structures;
        _vision = vision;
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
        _lookedAt = new int[playerCount];
    }

    /// <summary>True for a structure of the roster that is a wall tile to everyone.</summary>
    public bool IsWall(int structure) => structure >= 0 && structure < _isWall.Length && _isWall[structure];

    /// <summary>
    /// The wall tile that stands on a tile, or none, seen or not. What a player knows of a tile
    /// is <see cref="KnownWallAt"/>.
    /// </summary>
    public EntityId WallAt(int x, int y) => _grid.Contains(x, y) ? _wallAt[(y * _grid.Width) + x] : EntityId.None;

    /// <summary>
    /// The wall tile of another player that stands on a tile and that the player knows of, or
    /// none: the walls the wall rule may send the player's units to break.
    /// </summary>
    public EntityId KnownWallAt(int player, int x, int y) =>
        _grid.Contains(x, y) && EnemyWalls(player)[(y * _grid.Width) + x] ? _wallAt[(y * _grid.Width) + x] : EntityId.None;

    /// <summary>
    /// The number of the ground a tile belongs to for a player's units once the walls of the
    /// player's enemies that it knows of are out of the way. Zero for a tile nobody can stand
    /// on now.
    /// </summary>
    public int PastWallsOf(int player, int x, int y) => _grid.Contains(x, y) ? PastWalls(player)[(y * _grid.Width) + x] : 0;

    /// <summary><see cref="PastWallsOf"/> for every tile, as <c>y * width + x</c>.</summary>
    public ReadOnlySpan<int> PastWalls(int player)
    {
        Refresh(player);
        return _past[player];
    }

    /// <summary>
    /// For every tile, as <c>y * width + x</c>, whether a wall tile of an enemy of the player
    /// stands on it and the player knows of it.
    /// </summary>
    internal bool[] EnemyWalls(int player)
    {
        Refresh(player);
        return _enemyWalls[player]!;
    }

    public void TilesRebuilt()
    {
        Array.Clear(_wallAt);
        Array.Clear(_ownerAt);
        _wallTiles.Clear();
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
                        _wallTiles.Add((y * _grid.Width) + x);
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
        // What the player knows changes only when the fog of war or a memory is written, so
        // between two writes the answer stands. Neither count ever goes down, so their sum is
        // another number after any write. The answer depends on the state alone, never on when
        // it was asked.
        var writes = unchecked(_vision.Grid.Writes + _vision.Memory.Writes);
        if (_fresh[player] && _lookedAt[player] == writes)
        {
            return;
        }

        var width = _grid.Width;
        var enemyWalls = _enemyWalls[player] ??= new bool[_ownerAt.Length];
        var past = _past[player] ??= new int[_ownerAt.Length];
        var changed = !_fresh[player];
        if (changed)
        {
            Array.Clear(enemyWalls);
        }

        foreach (var tile in _wallTiles)
        {
            var known = _ownerAt[tile] != player + 1 && _vision.KnowsStructure(player, _wallAt[tile]);
            changed |= enemyWalls[tile] != known;
            enemyWalls[tile] = known;
        }

        _lookedAt[player] = writes;
        if (!changed)
        {
            return;
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
