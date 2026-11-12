using System.Globalization;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;

namespace Tools.Benchmark;

/// <summary>One case of the benchmark: its name and how to set it up, the same way every time.</summary>
internal sealed record Scenario(string Name, Func<GameData, Run> Create);

/// <summary>One play of a scenario: the match, what is ordered when, and what is watched on the way.</summary>
internal abstract class Run
{
    protected Run(Match match, int mostTicks)
    {
        Match = match;
        MostTicks = mostTicks;
    }

    public Match Match { get; }

    /// <summary>The play stops after this many ticks at the latest.</summary>
    public int MostTicks { get; }

    /// <summary>Queues the orders of the tick about to run. <paramref name="tick"/> counts from 0.</summary>
    public virtual void BeforeTick(int tick)
    {
    }

    /// <summary>Looks at the match after a tick. True ends the play. None of this is timed.</summary>
    public virtual bool AfterTick(int ticksRun) => false;

    /// <summary>What the play showed, and what is wrong with it if anything is.</summary>
    public abstract (string Findings, string? Problem) Finish(int ticksRun);

    protected void OrderMove(int player, EntityId[] units, FixedVector2 target) =>
        Match.Simulation.Commands.Enqueue(Match.World.Tick, new Command(CommandType.Move, player, units, target.X, target.Y));

    protected FixedVector2 PositionOf(EntityId unit) => Match.Bodies.Get(unit).Position;

    protected bool AllIdle(EntityId[] units) => units.All(unit => Match.Movers.Get(unit).State == MoveState.Idle);

    /// <summary>The least distance between two of the units, in tiles.</summary>
    protected double ClosestPair(EntityId[] units)
    {
        var least = long.MaxValue;
        for (var i = 0; i < units.Length; i++)
        {
            for (var j = i + 1; j < units.Length; j++)
            {
                least = Math.Min(least, FixedVector2.DistanceSquaredRaw(PositionOf(units[i]), PositionOf(units[j])));
            }
        }

        return Tiles(least);
    }

    /// <summary>The least distance between a unit of one list and a unit of the other, in tiles.</summary>
    protected double ClosestPair(EntityId[] first, EntityId[] second)
    {
        var least = long.MaxValue;
        foreach (var one in first)
        {
            foreach (var other in second)
            {
                least = Math.Min(least, FixedVector2.DistanceSquaredRaw(PositionOf(one), PositionOf(other)));
            }
        }

        return Tiles(least);
    }

    protected static double Tiles(long squaredRaw) => Math.Sqrt(squaredRaw) / Fixed.RawOne;

    protected static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// Watches the hard half of the spacing rule from outside (design 2.4): after every tick, every
/// pair of enemy units is at least its spacing apart, or no closer than it was before the tick.
/// </summary>
internal sealed class EnemySpacingWatch
{
    private readonly Match _match;
    private readonly EntityId[] _first;
    private readonly EntityId[] _second;
    private readonly FixedVector2[] _firstBefore;
    private readonly FixedVector2[] _secondBefore;

    public EnemySpacingWatch(Match match, EntityId[] first, EntityId[] second)
    {
        _match = match;
        _first = first;
        _second = second;
        _firstBefore = new FixedVector2[first.Length];
        _secondBefore = new FixedVector2[second.Length];
        Remember();
    }

    /// <summary>The number of pairs, over all ticks, that a tick brought closer than their spacing.</summary>
    public long Broken { get; private set; }

    public void AfterTick()
    {
        for (var i = 0; i < _first.Length; i++)
        {
            var one = _match.Bodies.Get(_first[i]).Position;
            for (var j = 0; j < _second.Length; j++)
            {
                var other = _match.Bodies.Get(_second[j]).Position;
                var now = FixedVector2.DistanceSquaredRaw(one, other);
                if (now >= 4L * Fixed.RawOne * Fixed.RawOne)
                {
                    continue;
                }

                var spacing = SpacingRule.Between(_match.Movers.Get(_first[i]), _match.Movers.Get(_second[j]));
                if (now < FixedVector2.SquareRaw(spacing) && now < FixedVector2.DistanceSquaredRaw(_firstBefore[i], _secondBefore[j]))
                {
                    Broken++;
                }
            }
        }

        Remember();
    }

    private void Remember()
    {
        for (var i = 0; i < _first.Length; i++)
        {
            _firstBefore[i] = _match.Bodies.Get(_first[i]).Position;
        }

        for (var j = 0; j < _second.Length; j++)
        {
            _secondBefore[j] = _match.Bodies.Get(_second[j]).Position;
        }
    }
}

internal static class Scenarios
{
    /// <summary>The map the scenarios play on. Its north pass is a chokepoint 6 tiles wide.</summary>
    public const string MapId = "ridge";

    // The north pass of ridge: the cliff's tiles 46 to 49 across, open in rows 20 to 25.
    private const int PassWest = 46;
    private const int PassEast = 50;
    private const int PassMiddleY = 23;

    /// <summary>Line infantry: 3 tiles per second.</summary>
    private static Fixed Speed => Mover.SpeedPerTick(3);

    public static IReadOnlyList<Scenario> All(bool quick) =>
    [
        new("chokepoint", data => new Chokepoint(Ridge(data), quick ? 48 : 200, held: false, quick ? 1500 : 4000)),
        new("chokepoint-held", data => new Chokepoint(Ridge(data), quick ? 48 : 200, held: true, quick ? 150 : 1200)),
        new("march", data => new March(Ridge(data), quick ? 60 : 500, quick ? 1500 : 4000)),
        new("crossing", data => new Crossing(Ridge(data), quick ? 60 : 500, quick ? 150 : 1200)),
        new("separate-orders", data => new SeparateOrders(Ridge(data), quick ? 60 : 500, quick ? 60 : 400)),
        new("search-ridge", data => new WholeMapSearch(Ridge(data), 3)),
        new("search-256", data => new WholeMapSearch(Serpentine(data, quick ? 64 : 256), 3)),
        .. WallScenarios.All(quick),
    ];

    private static Match Ridge(GameData data) =>
        MatchSetup.Create(data, data.Maps.Single(map => map.Id == MapId), seed: 1, [PlayerSetup.WithoutStart, PlayerSetup.WithoutStart]);

    /// <summary>
    /// A square map crossed by walls with a gap at alternating ends, so that a search from one
    /// corner to the opposite one has to look at nearly every tile.
    /// </summary>
    private static Match Serpentine(GameData data, int size)
    {
        var tiles = new TerrainKind[size * size];
        for (var y = 2; y < size; y += 4)
        {
            var gapAtEnd = y % 8 == 2;
            for (var x = 0; x < size; x++)
            {
                var gap = gapAtEnd ? x >= size - 2 : x < 2;
                tiles[(y * size) + x] = gap ? TerrainKind.Open : TerrainKind.Cliff;
            }
        }

        var map = new MapDef("serpentine", size, size, MapSymmetry.MirrorX, tiles, [], [], []);
        return MatchSetup.Create(data, map, seed: 1, [PlayerSetup.WithoutStart, PlayerSetup.WithoutStart]);
    }

    /// <summary>
    /// Places units in a square grid at one tile, row by row from a first tile. A tile no unit can
    /// stand on, such as a deposit's, is left out, and the block is that much longer.
    /// </summary>
    private static EntityId[] Block(Match match, int player, int firstX, int firstY, int columns, int count)
    {
        var units = new EntityId[count];
        var placed = 0;
        for (var tile = 0; placed < count; tile++)
        {
            var center = MapGrid.TileCenter(firstX + (tile % columns), firstY + (tile / columns));
            if (match.Grid.IsPassable(center))
            {
                units[placed++] = match.SpawnUnit(player, center, Speed);
            }
        }

        return units;
    }

    /// <summary>
    /// The plan's chokepoint case: a block of units in a square grid at its spacing, 16 wide, is
    /// ordered through the north pass. With <c>held</c>, three enemy units stand in the pass less
    /// than twice the spacing apart, which closes it, and nobody may get through.
    /// </summary>
    private sealed class Chokepoint : Run
    {
        private const int Columns = 16;

        private readonly EntityId[] _units;
        private readonly EntityId[] _guards = [];
        private readonly EnemySpacingWatch? _watch;
        private readonly FixedVector2[] _before;
        private readonly int[] _onTile;
        private int _firstThrough = -1;
        private int _lastThrough = -1;
        private int _atRest = -1;
        private int _mostInPass;
        private int _mostOnOneTile;

        public Chokepoint(Match match, int count, bool held, int mostTicks)
            : base(match, mostTicks)
        {
            var rows = (count + Columns - 1) / Columns;
            _units = Block(match, 0, 26, PassMiddleY - (rows / 2), Columns, count);
            _before = new FixedVector2[count];
            _onTile = new int[match.Grid.Width * match.Grid.Height];
            if (held)
            {
                // The pass is 6 tiles wide and units keep 3/8 of a tile from its sides.
                _guards =
                [
                    match.SpawnUnit(1, new FixedVector2(Fixed.FromInt(48), Fixed.FromRaw((21 * Fixed.RawOne) + (Fixed.RawOne * 3 / 10))), Speed),
                    match.SpawnUnit(1, new FixedVector2(Fixed.FromInt(48), Fixed.FromInt(PassMiddleY)), Speed),
                    match.SpawnUnit(1, new FixedVector2(Fixed.FromInt(48), Fixed.FromRaw((24 * Fixed.RawOne) + (Fixed.RawOne * 7 / 10))), Speed),
                ];
                _watch = new EnemySpacingWatch(match, _units, _guards);
            }

            match.RefreshDerivedState();
        }

        public override void BeforeTick(int tick)
        {
            if (tick == 0)
            {
                OrderMove(0, _units, new FixedVector2(Fixed.FromInt(66), Fixed.FromInt(PassMiddleY)));
            }

            for (var i = 0; i < _units.Length; i++)
            {
                _before[i] = PositionOf(_units[i]);
            }
        }

        public override bool AfterTick(int ticksRun)
        {
            _watch?.AfterTick();
            Array.Clear(_onTile);
            var through = 0;
            var inPass = 0;
            var moved = false;
            for (var i = 0; i < _units.Length; i++)
            {
                var position = PositionOf(_units[i]);
                moved |= position != _before[i];
                var x = position.X.FloorToInt();
                through += x >= PassEast ? 1 : 0;
                inPass += x is >= PassWest and < PassEast ? 1 : 0;
                _mostOnOneTile = Math.Max(_mostOnOneTile, ++_onTile[(position.Y.FloorToInt() * Match.Grid.Width) + x]);
            }

            _mostInPass = Math.Max(_mostInPass, inPass);
            if (through > 0 && _firstThrough < 0)
            {
                _firstThrough = ticksRun;
            }

            if (through == _units.Length && _lastThrough < 0)
            {
                _lastThrough = ticksRun;
            }

            if (_guards.Length == 0 && !moved && AllIdle(_units))
            {
                _atRest = ticksRun;
                return true;
            }

            return false;
        }

        public override (string Findings, string? Problem) Finish(int ticksRun)
        {
            if (_guards.Length > 0)
            {
                var passed = _units.Count(unit => PositionOf(unit).X.FloorToInt() >= PassEast);
                var findings = $"pass held by {_guards.Length} enemy units: {passed} of {_units.Length} got through; " +
                    $"closest enemy pair {Number(ClosestPair(_units, _guards))} tiles; pairs brought closer than their spacing: {_watch!.Broken}";
                return (findings, passed > 0 ? "units walked through a closed enemy line."
                    : _watch.Broken > 0 ? "enemy units came closer than their spacing."
                    : null);
            }

            var closest = ClosestPair(_units);
            var shown = $"first unit through at tick {_firstThrough}, last at {_lastThrough}, at rest at {_atRest}; " +
                $"at most {_mostInPass} units in the pass and {_mostOnOneTile} on one tile; closest pair at rest {Number(closest)} tiles";
            return (shown, _atRest < 0 ? $"the group was not through the pass and at rest after {ticksRun} ticks."
                : closest < 1 ? "units at rest stand closer than their spacing."
                : null);
        }
    }

    /// <summary>
    /// Two blocks of one player cross the map, one through the north pass and one through the
    /// open field: the everyday cost of many units on the move, with no enemy near.
    /// </summary>
    private sealed class March : Run
    {
        private readonly EntityId[] _north;
        private readonly EntityId[] _south;
        private readonly EntityId[] _all;
        private readonly FixedVector2[] _before;

        public March(Match match, int count, int mostTicks)
            : base(match, mostTicks)
        {
            _north = Block(match, 0, 26, 15, 16, count / 2);
            _south = Block(match, 0, 26, 56, 16, count - (count / 2));
            _all = [.. _north, .. _south];
            _before = new FixedVector2[_all.Length];
            match.RefreshDerivedState();
        }

        public override void BeforeTick(int tick)
        {
            if (tick == 0)
            {
                OrderMove(0, _north, new FixedVector2(Fixed.FromInt(66), Fixed.FromInt(PassMiddleY)));
                OrderMove(0, _south, new FixedVector2(Fixed.FromInt(66), Fixed.FromInt(64)));
            }

            for (var i = 0; i < _all.Length; i++)
            {
                _before[i] = PositionOf(_all[i]);
            }
        }

        public override bool AfterTick(int ticksRun)
        {
            for (var i = 0; i < _all.Length; i++)
            {
                if (PositionOf(_all[i]) != _before[i])
                {
                    return false;
                }
            }

            return AllIdle(_all);
        }

        public override (string Findings, string? Problem) Finish(int ticksRun)
        {
            var closest = ClosestPair(_all);
            var arrived = AllIdle(_all);
            return ($"two blocks across the map, at rest at tick {ticksRun}; closest pair at rest {Number(closest)} tiles",
                !arrived ? $"the blocks were not at rest after {ticksRun} ticks."
                : closest < 1 ? "units at rest stand closer than their spacing."
                : null);
        }
    }

    /// <summary>
    /// Two players' blocks are ordered onto each other's ground through the open field. They
    /// meet, and neither may walk through the other: the costliest case for the spacing rule
    /// between enemies, with every unit blocked or steering.
    /// </summary>
    private sealed class Crossing : Run
    {
        private readonly EntityId[] _west;
        private readonly EntityId[] _east;
        private readonly EnemySpacingWatch _watch;

        public Crossing(Match match, int count, int mostTicks)
            : base(match, mostTicks)
        {
            _west = Block(match, 0, 26, 56, 16, count / 2);
            _east = Block(match, 1, 54, 56, 16, count - (count / 2));
            _watch = new EnemySpacingWatch(match, _west, _east);
            match.RefreshDerivedState();
        }

        public override void BeforeTick(int tick)
        {
            if (tick == 0)
            {
                OrderMove(0, _west, new FixedVector2(Fixed.FromInt(62), Fixed.FromInt(64)));
                OrderMove(1, _east, new FixedVector2(Fixed.FromInt(34), Fixed.FromInt(64)));
            }
        }

        public override bool AfterTick(int ticksRun)
        {
            _watch.AfterTick();
            return false;
        }

        public override (string Findings, string? Problem) Finish(int ticksRun)
        {
            var waiting = _west.Concat(_east).Count(unit => Match.Movers.Get(unit).State != MoveState.Idle);
            return ($"two enemy blocks ordered through each other: {waiting} units still on their order; " +
                $"closest enemy pair {Number(ClosestPair(_west, _east))} tiles; pairs brought closer than their spacing: {_watch.Broken}",
                _watch.Broken > 0 ? "enemy units came closer than their spacing." : null);
        }
    }

    /// <summary>
    /// Risk R19, first case: every unit gets an order of its own in one tick, each to another
    /// place, so each needs its own route.
    /// </summary>
    private sealed class SeparateOrders : Run
    {
        private readonly EntityId[] _units;
        private readonly FixedVector2[] _targets;
        private int _longestWait;

        public SeparateOrders(Match match, int count, int mostTicks)
            : base(match, mostTicks)
        {
            var random = new DeterministicRandom(12);
            _units = new EntityId[count];
            _targets = new FixedVector2[count];
            for (var i = 0; i < count; i++)
            {
                _units[i] = match.SpawnUnit(i % 2, RandomTile(match, random), Speed);
                _targets[i] = RandomTile(match, random);
            }

            match.RefreshDerivedState();
        }

        public override void BeforeTick(int tick)
        {
            if (tick != 1)
            {
                return;
            }

            for (var i = 0; i < _units.Length; i++)
            {
                OrderMove(i % 2, [_units[i]], _targets[i]);
            }
        }

        public override bool AfterTick(int ticksRun)
        {
            if (_units.Any(unit => Match.Movers.Get(unit).State == MoveState.Planning))
            {
                _longestWait = ticksRun - 1;
            }

            return false;
        }

        public override (string Findings, string? Problem) Finish(int ticksRun) =>
            ($"{_units.Length} separate orders in one tick: {Match.Routes.SearchCount} route searches, the last unit waited {_longestWait} ticks for its route", null);

        private static FixedVector2 RandomTile(Match match, DeterministicRandom random)
        {
            while (true)
            {
                var (x, y) = (random.NextInt(match.Grid.Width), random.NextInt(match.Grid.Height));
                if (match.Grid.IsPassable(x, y))
                {
                    return MapGrid.TileCenter(x, y);
                }
            }
        }
    }

    /// <summary>
    /// Risk R19, second case: one route search from one corner of the map to the opposite one. A
    /// search always runs to its end, so its whole cost falls on one tick, the worst of the run.
    /// </summary>
    private sealed class WholeMapSearch : Run
    {
        private readonly EntityId _unit;
        private readonly int _goal;

        public WholeMapSearch(Match match, int mostTicks)
            : base(match, mostTicks)
        {
            _unit = match.SpawnUnit(0, MapGrid.TileCenter(0, 0), Speed);
            _goal = (match.Grid.Width * match.Grid.Height) - 1;
            match.RefreshDerivedState();
        }

        public override void BeforeTick(int tick)
        {
            if (tick == 0)
            {
                OrderMove(0, [_unit], MapGrid.TileCenter(Match.Grid.Width - 1, Match.Grid.Height - 1));
            }
        }

        public override (string Findings, string? Problem) Finish(int ticksRun)
        {
            var route = Match.Routes.Get(0, _goal);
            var following = Match.Movers.Get(_unit).State == MoveState.Following;
            return ($"one search across a map of {Match.Grid.Width} by {Match.Grid.Height}: {route.Expansions} tiles expanded, a route of {route.Tiles.Count} tiles; the worst tick is the search",
                following ? null : "the unit did not get its route.");
        }
    }
}
