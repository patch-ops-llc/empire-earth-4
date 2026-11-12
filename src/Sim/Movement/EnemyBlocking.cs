using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Movement;

/// <summary>
/// Answers, for a unit about to move, whether the enemy units near it allow the step: the hard
/// half of the spacing rule (design 2.4), as <see cref="SpacingRule.AllowsStep"/> states it for
/// one enemy. The systems that move units ask it before every change of a position. It also
/// tells them which units are near a point, so that neither walks all of them.
/// </summary>
/// <remarks>
/// <para>
/// It tests against where each enemy stands at the moment of asking, as the design rules, and
/// units move in the order of their IDs. To find the units near a point it files every unit under
/// the square of the map it stands in when <see cref="Begin"/> is called, and a question reaches
/// far enough to find every unit that has moved since. Units are known here by their place in the
/// store of movers, which is the order of their IDs.
/// </para>
/// <para>
/// Nothing here is state: all of it is made again from the stores by every <see cref="Begin"/>.
/// It is separate from <see cref="SpatialIndex"/>, which answers for all bodies in ID order and
/// is brought up to date once a tick, after the units have moved.
/// </para>
/// </remarks>
internal sealed class EnemyBlocking
{
    /// <summary>The side of a square, in tiles: about the reach of one question.</summary>
    public const int CellSize = 2;

    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly int _columns;
    private readonly int _rows;

    // The units of square c are _filed[_firstOfCell[c]] up to _filed[_firstOfCell[c + 1]].
    private readonly int[] _firstOfCell;
    private readonly List<int> _near = [];
    private int[] _filed = new int[64];

    // By a unit's place in the store of movers, as they were at Begin.
    private int[] _bodyIndex = new int[64];
    private int[] _owner = new int[64];
    private FixedVector2[] _stood = new FixedVector2[64];
    private Fixed _moved;

    public EnemyBlocking(MapGrid grid, ComponentStore<Body> bodies, ComponentStore<Mover> movers)
    {
        _bodies = bodies;
        _movers = movers;
        _columns = ((grid.Width - 1) / CellSize) + 1;
        _rows = ((grid.Height - 1) / CellSize) + 1;
        _firstOfCell = new int[(_columns * _rows) + 1];
    }

    /// <summary>The largest spacing of any unit: how far away a neighbor can matter.</summary>
    public Fixed WidestSpacing { get; private set; }

    /// <summary>
    /// Call once before a system moves anything: files every living unit under where it stands now.
    /// </summary>
    /// <param name="world">The world, for which units are alive.</param>
    /// <param name="beyondSpeed">How much further than its speed the system may move a unit before it has finished.</param>
    public void Begin(World world, Fixed beyondSpeed)
    {
        var count = _movers.Count;
        if (_filed.Length < count)
        {
            var size = Math.Max(count, _filed.Length * 2);
            _filed = new int[size];
            _bodyIndex = new int[size];
            _owner = new int[size];
            _stood = new FixedVector2[size];
        }

        // Count the units of each square, turn the counts into where each square begins, then
        // file the units.
        Array.Clear(_firstOfCell);
        WidestSpacing = Fixed.Zero;
        var fastest = Fixed.Zero;
        for (var i = 0; i < count; i++)
        {
            var entity = _movers.EntityAt(i);
            var bodyIndex = world.Entities.IsAlive(entity) ? _bodies.IndexOf(entity) : -1;
            _bodyIndex[i] = bodyIndex;
            if (bodyIndex < 0)
            {
                continue;
            }

            ref var mover = ref _movers.ValueAt(i);
            ref var body = ref _bodies.ValueAt(bodyIndex);
            WidestSpacing = Fixed.Max(WidestSpacing, mover.Spacing);
            fastest = Fixed.Max(fastest, mover.Speed);
            _owner[i] = body.Owner;
            _stood[i] = body.Position;
            _firstOfCell[CellOf(body.Position) + 1]++;
        }

        for (var cell = 1; cell < _firstOfCell.Length; cell++)
        {
            _firstOfCell[cell] += _firstOfCell[cell - 1];
        }

        for (var i = 0; i < count; i++)
        {
            if (_bodyIndex[i] >= 0)
            {
                _filed[_firstOfCell[CellOf(_stood[i])]++] = i;
            }
        }

        // Filing moved each square's beginning to its end, which is where the next one begins.
        for (var cell = _firstOfCell.Length - 1; cell > 0; cell--)
        {
            _firstOfCell[cell] = _firstOfCell[cell - 1];
        }

        _firstOfCell[0] = 0;
        _moved = fastest + beyondSpeed;
    }

    /// <summary>Whether the unit was alive and on the map at <see cref="Begin"/>.</summary>
    public bool IsFiled(int unit) => _bodyIndex[unit] >= 0;

    /// <summary>Where the unit stood at <see cref="Begin"/>.</summary>
    public FixedVector2 StoodAt(int unit) => _stood[unit];

    /// <summary>The player the unit belongs to.</summary>
    public int OwnerOf(int unit) => _owner[unit];

    /// <summary>The unit's place in the store of bodies.</summary>
    public int BodyIndexOf(int unit) => _bodyIndex[unit];

    /// <summary>
    /// Lists every unit that stood within a distance of a point at <see cref="Begin"/>, and some
    /// that stood further away, in no order that means anything. The list is emptied first.
    /// </summary>
    public void Near(FixedVector2 center, Fixed reach, List<int> results)
    {
        results.Clear();
        var firstColumn = ColumnOf(center.X - reach);
        var lastColumn = ColumnOf(center.X + reach);
        var lastRow = RowOf(center.Y + reach);
        for (var row = RowOf(center.Y - reach); row <= lastRow; row++)
        {
            var first = _firstOfCell[(row * _columns) + firstColumn];
            var end = _firstOfCell[(row * _columns) + lastColumn + 1];
            for (var i = first; i < end; i++)
            {
                results.Add(_filed[i]);
            }
        }
    }

    /// <summary>
    /// Whether every enemy near a unit allows it to go straight from one point to another. When
    /// one does not, <paramref name="blocker"/> is the nearest of those in the way, the oldest
    /// among equally near ones.
    /// </summary>
    /// <param name="unit">The unit's place in the store of movers.</param>
    /// <param name="spacing">The unit's own spacing.</param>
    /// <param name="from">Where the unit stands.</param>
    /// <param name="to">Where it would go.</param>
    /// <param name="blocker">The enemy in the way, when there is one.</param>
    public bool Allows(int unit, Fixed spacing, FixedVector2 from, FixedVector2 to, out Blocker blocker)
    {
        blocker = default;
        if (from == to || WidestSpacing == Fixed.Zero)
        {
            return true;
        }

        // Far enough to find every unit that can be within the widest spacing of the way, also
        // one that has moved since the units were filed.
        var reach = WidestSpacing + Fixed.Abs(to.X - from.X) + Fixed.Abs(to.Y - from.Y) + _moved;
        Near(from, reach, _near);
        var owner = _owner[unit];
        var nearest = long.MaxValue;
        var nearestUnit = -1;
        foreach (var other in _near)
        {
            if (other == unit || !SpacingRule.AreEnemyGroundUnits(owner, _owner[other]))
            {
                continue;
            }

            var position = _bodies.ValueAt(_bodyIndex[other]).Position;
            var between = SpacingRule.Between(spacing, _movers.ValueAt(other).Spacing);
            if (SpacingRule.AllowsStep(from, to, position, between))
            {
                continue;
            }

            var distance = FixedVector2.DistanceSquaredRaw(from, position);
            if (distance < nearest || (distance == nearest && other < nearestUnit))
            {
                nearest = distance;
                nearestUnit = other;
                blocker = new Blocker(position, between);
            }
        }

        return nearestUnit < 0;
    }

    // A unit off the map, which nothing should produce, is filed under the nearest square.
    private int ColumnOf(Fixed x) => Math.Clamp(x.FloorToInt() / CellSize, 0, _columns - 1);

    private int RowOf(Fixed y) => Math.Clamp(y.FloorToInt() / CellSize, 0, _rows - 1);

    private int CellOf(FixedVector2 position) => (RowOf(position.Y) * _columns) + ColumnOf(position.X);

    /// <summary>An enemy unit in the way: where it stands and the spacing to keep from it.</summary>
    public readonly record struct Blocker(FixedVector2 Position, Fixed Spacing);
}
