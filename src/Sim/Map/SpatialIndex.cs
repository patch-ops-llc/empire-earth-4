using Sim.Core;
using Sim.Numerics;

namespace Sim.Map;

/// <summary>
/// Answers "which entities are near here" without walking all of them: every body is filed under
/// the square of the map its center is in, and a query reads only the squares it touches.
/// </summary>
/// <remarks>
/// <para>
/// The index is a copy of the positions in the <see cref="Body"/> store as they were when
/// <see cref="Rebuild"/> last ran. <see cref="SpatialIndexSystem"/> runs it once a tick, right
/// after movement. A body added or moved after that is seen from the next rebuild; an entity
/// destroyed after it is left out of every answer at once.
/// </para>
/// <para>
/// It is not part of the state and is not hashed: rebuilding it from the same bodies gives the same
/// index, and a world restored from a snapshot rebuilds it. Every answer lists entities in ascending
/// ID order, the order every system must use, but for <see cref="QueryCircleUnsorted"/>, which is
/// for searches whose outcome does not depend on the order.
/// </para>
/// </remarks>
public sealed class SpatialIndex
{
    /// <summary>The side of a square, in tiles.</summary>
    public const int CellSize = 4;

    private readonly EntityRegistry _entities;
    private readonly ComponentStore<Body> _bodies;
    private readonly int _columns;
    private readonly int _rows;

    // The entries of square c are those from _firstOfCell[c] up to _firstOfCell[c + 1], in ID order.
    private readonly int[] _firstOfCell;
    private Entry[] _entries = new Entry[64];

    public SpatialIndex(MapGrid grid, EntityRegistry entities, ComponentStore<Body> bodies)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(bodies);
        _entities = entities;
        _bodies = bodies;
        _columns = ((grid.Width - 1) / CellSize) + 1;
        _rows = ((grid.Height - 1) / CellSize) + 1;
        _firstOfCell = new int[(_columns * _rows) + 1];
    }

    /// <summary>The number of entities in the index.</summary>
    public int Count { get; private set; }

    /// <summary>Files every living body again, from its position now.</summary>
    public void Rebuild()
    {
        Array.Clear(_firstOfCell);
        if (_entries.Length < _bodies.Count)
        {
            _entries = new Entry[Math.Max(_bodies.Count, _entries.Length * 2)];
        }

        // Count the bodies of each square, turn the counts into where each square begins, then
        // file the bodies in store order, which is ID order.
        Count = 0;
        for (var i = 0; i < _bodies.Count; i++)
        {
            if (_entities.IsAlive(_bodies.EntityAt(i)))
            {
                _firstOfCell[CellOf(_bodies.ValueAt(i).Position) + 1]++;
                Count++;
            }
        }

        for (var cell = 1; cell < _firstOfCell.Length; cell++)
        {
            _firstOfCell[cell] += _firstOfCell[cell - 1];
        }

        for (var i = 0; i < _bodies.Count; i++)
        {
            var entity = _bodies.EntityAt(i);
            var listed = _entities.PositionOf(entity);
            if (_entities.IsAlive(entity, listed))
            {
                var position = _bodies.ValueAt(i).Position;
                _entries[_firstOfCell[CellOf(position)]++] = new Entry(entity, position, i, listed);
            }
        }

        // Filing moved each square's beginning to its end, which is where the next one begins.
        for (var cell = _firstOfCell.Length - 1; cell > 0; cell--)
        {
            _firstOfCell[cell] = _firstOfCell[cell - 1];
        }

        _firstOfCell[0] = 0;
    }

    /// <summary>
    /// Lists the living entities whose center is within a distance of a point, the distance itself
    /// included, in ascending ID order. The list is emptied first.
    /// </summary>
    public void QueryCircle(FixedVector2 center, Fixed radius, List<EntityId> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentOutOfRangeException.ThrowIfNegative(radius.Raw, nameof(radius));

        results.Clear();
        var reach = new FixedVector2(radius, radius);
        var limit = FixedVector2.SquareRaw(radius);
        Collect(center - reach, center + reach, results, withinCircle: true, center, limit);
    }

    /// <summary>
    /// Lists what <see cref="QueryCircle"/> lists, each entity with where its body is in the
    /// store, and without sorting: in the order of the squares, and by ID within a square. The
    /// list is emptied first.
    /// </summary>
    /// <remarks>
    /// For a search that looks at every candidate anyway and asks most of them very little, like
    /// picking the nearest enemy among hundreds of units: the sort and a lookup for every
    /// candidate are most of what such a search costs. The order is the same on every machine,
    /// as it follows from the bodies alone, but it is not ID order. So use it only where the
    /// outcome does not depend on the order in which the candidates come: a caller that picks one
    /// of them must compare IDs itself where two are otherwise equal.
    /// </remarks>
    public void QueryCircleUnsorted(FixedVector2 center, Fixed radius, List<Filed> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentOutOfRangeException.ThrowIfNegative(radius.Raw, nameof(radius));

        results.Clear();
        var reach = new FixedVector2(radius, radius);
        var min = center - reach;
        var max = center + reach;
        var limit = FixedVector2.SquareRaw(radius);
        var firstColumn = ColumnOf(min.X);
        var lastColumn = ColumnOf(max.X);
        var firstRow = RowOf(min.Y);
        var lastRow = RowOf(max.Y);
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var cell = (row * _columns) + column;
                for (var i = _firstOfCell[cell]; i < _firstOfCell[cell + 1]; i++)
                {
                    ref readonly var entry = ref _entries[i];
                    var position = entry.Position;
                    if (position.X < min.X || position.X > max.X || position.Y < min.Y || position.Y > max.Y)
                    {
                        continue;
                    }

                    if (FixedVector2.DistanceSquaredRaw(position, center) <= limit && _entities.IsAlive(entry.Entity, entry.Listed))
                    {
                        results.Add(new Filed(entry.Entity, entry.Body));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Lists the living entities whose center is inside a rectangle, its edges included, in
    /// ascending ID order. The list is emptied first.
    /// </summary>
    /// <param name="min">The corner with the lowest x and y.</param>
    /// <param name="max">The corner with the highest x and y.</param>
    /// <param name="results">Receives the entities.</param>
    public void QueryBox(FixedVector2 min, FixedVector2 max, List<EntityId> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        results.Clear();
        Collect(min, max, results, withinCircle: false, default, 0);
    }

    private void Collect(FixedVector2 min, FixedVector2 max, List<EntityId> results, bool withinCircle, FixedVector2 center, long limit)
    {
        if (min.X > max.X || min.Y > max.Y)
        {
            return;
        }

        var firstColumn = ColumnOf(min.X);
        var lastColumn = ColumnOf(max.X);
        var firstRow = RowOf(min.Y);
        var lastRow = RowOf(max.Y);
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var cell = (row * _columns) + column;
                for (var i = _firstOfCell[cell]; i < _firstOfCell[cell + 1]; i++)
                {
                    var entry = _entries[i];
                    var position = entry.Position;
                    if (position.X < min.X || position.X > max.X || position.Y < min.Y || position.Y > max.Y)
                    {
                        continue;
                    }

                    if (withinCircle && FixedVector2.DistanceSquaredRaw(position, center) > limit)
                    {
                        continue;
                    }

                    if (_entities.IsAlive(entry.Entity, entry.Listed))
                    {
                        results.Add(entry.Entity);
                    }
                }
            }
        }

        // Each square lists its entities in ID order, and IDs are unique, so this order is total.
        results.Sort();
    }

    // A body off the map, which nothing should produce, is filed under the nearest square.
    private int ColumnOf(Fixed x) => Math.Clamp(x.FloorToInt() / CellSize, 0, _columns - 1);

    private int RowOf(Fixed y) => Math.Clamp(y.FloorToInt() / CellSize, 0, _rows - 1);

    private int CellOf(FixedVector2 position) => (RowOf(position.Y) * _columns) + ColumnOf(position.X);

    // Body and Listed are where the entity was in the body store and in the list of entities
    // when it was filed: hints that save a search while nothing has been added or removed since.
    private readonly record struct Entry(EntityId Entity, FixedVector2 Position, int Body, int Listed);
}

/// <summary>An entity that <see cref="SpatialIndex.QueryCircleUnsorted"/> found.</summary>
/// <param name="Entity">The entity.</param>
/// <param name="Body">
/// Where its body was in the store when the index was last rebuilt: a hint for
/// <see cref="ComponentStore{T}.IndexOf(EntityId, int)"/>, which finds the body without a search
/// while nothing has been added to the store or taken from it since.
/// </param>
public readonly record struct Filed(EntityId Entity, int Body);
