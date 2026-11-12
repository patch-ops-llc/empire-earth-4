using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;

namespace Tools.Benchmark;

/// <summary>
/// The cases with walls and gates (design 2.5). Fighting units are ordered into a camp that a
/// closed ring of enemy wall cuts off, so every one of them is under the wall rule: the second
/// search for the wall in the way, the places by that wall, and, when a tile falls, the tiles
/// and regions worked out again and a new route for everyone on the way. With gates, each
/// player has a finished gate, so each has passable tiles and regions of its own, and the
/// camp's workers walk in through its gate while the enemy breaks in.
/// </summary>
internal static class WallScenarios
{
    // The camp of player 1, in the open east of the cliff of ridge: its ring stands on the edge
    // of this box of tiles, 96 tiles of wall round 23 by 23 tiles of ground.
    private const int West = 62;
    private const int North = 44;
    private const int East = 86;
    private const int South = 68;

    // The gate of the camp, in its north side, away from where the enemy comes from.
    private const int GateX = 74;

    // The small camp of player 0, which is there for its gate alone: with it, player 0 has
    // passable tiles and regions of its own as well.
    private const int OwnWest = 20;
    private const int OwnNorth = 54;
    private const int OwnSide = 5;

    public static IReadOnlyList<Scenario> All(bool quick) =>
    [
        new("wall-breach", data => new Camp(Ridge(data), quick ? 40 : 200, apart: false, gates: false, quick ? 1500 : 4000)),
        new("wall-breach-apart", data => new Camp(Ridge(data), quick ? 40 : 200, apart: true, gates: false, quick ? 1500 : 4000)),
        new("gates", data => new Camp(Ridge(data), quick ? 40 : 200, apart: false, gates: true, quick ? 1500 : 4000)),
    ];

    private static Match Ridge(GameData data) =>
        MatchSetup.Create(data, data.Maps.Single(map => map.Id == Scenarios.MapId), seed: 1, [PlayerSetup.WithoutStart, PlayerSetup.WithoutStart]);

    /// <summary>
    /// A block of Hoplites of player 0, ten wide, west of the camp of player 1, is ordered to
    /// the west half of the camp: with one order for all, or with an order for each unit in the
    /// same tick. With gates, a quarter as many workers of player 1 stand north of the camp and
    /// are ordered to its east half in the same tick, which takes them through their gate.
    /// </summary>
    private sealed class Camp : Run
    {
        private const int Columns = 10;

        private readonly EntityId[] _attackers;
        private readonly EntityId[] _workers;
        private readonly bool _apart;
        private readonly int _tilesOfWall;
        private long _searchesWhenOrdered;
        private int _allHaveAWall = -1;
        private int _firstHit = -1;
        private int _firstFallen = -1;
        private int _firstInside = -1;
        private int _workersInside = -1;
        private int _mostAtTheWall;
        private int _atRest = -1;

        public Camp(Match match, int count, bool apart, bool gates, int mostTicks)
            : base(match, mostTicks)
        {
            _apart = apart;
            Ring(match, 1, West, North, East, South, gates ? (GateX, North) : null);
            if (gates)
            {
                Ring(match, 0, OwnWest, OwnNorth, OwnWest + OwnSide, OwnNorth + OwnSide, (OwnWest + OwnSide, OwnNorth + 2));
            }

            _tilesOfWall = match.Structures.Count;
            var hoplite = match.Data.Chassis.Single(chassis => chassis.Id == "hoplite").Index;
            _attackers = new EntityId[count];
            for (var i = 0; i < count; i++)
            {
                _attackers[i] = match.SpawnChassis(0, hoplite, MapGrid.TileCenter(West - 22 + (i % Columns), North + 6 + (i / Columns)));
            }

            _workers = new EntityId[gates ? count / 4 : 0];
            for (var i = 0; i < _workers.Length; i++)
            {
                _workers[i] = match.SpawnWorker(1, MapGrid.TileCenter(West + 6 + (i % Columns), North - 12 + (i / Columns)));
            }

            match.RefreshDerivedState();
        }

        private static FixedVector2 WestHalf => MapGrid.TileCenter(West + 7, (North + South) / 2);

        private static FixedVector2 EastHalf => MapGrid.TileCenter(East - 5, (North + South) / 2);

        public override void BeforeTick(int tick)
        {
            if (tick != 0)
            {
                return;
            }

            if (_apart)
            {
                foreach (var unit in _attackers)
                {
                    OrderMove(0, [unit], WestHalf);
                }
            }
            else
            {
                OrderMove(0, _attackers, WestHalf);
            }

            if (_workers.Length > 0)
            {
                OrderMove(1, _workers, EastHalf);
            }
        }

        public override bool AfterTick(int ticksRun)
        {
            if (ticksRun == 1)
            {
                _searchesWhenOrdered = Match.Breaching.SearchCount;
            }

            foreach (var happened in Match.Simulation.Events)
            {
                if (happened.Kind == SimEventKind.MeleeHit && _firstHit < 0 && Match.Structures.Contains(happened.Entity))
                {
                    _firstHit = ticksRun;
                }
                else if (happened.Kind == SimEventKind.StructureRemoved && _firstFallen < 0)
                {
                    _firstFallen = ticksRun;
                }
            }

            var atTheWall = 0;
            var inside = 0;
            var living = 0;
            var walking = false;
            foreach (var unit in _attackers)
            {
                if (!Match.World.Entities.IsAlive(unit))
                {
                    continue;
                }

                living++;
                atTheWall += Match.Combatants.Get(unit).WallInWay != EntityId.None ? 1 : 0;
                inside += IsInside(unit) ? 1 : 0;
                walking |= Match.Movers.Get(unit).State != MoveState.Idle;
            }

            _mostAtTheWall = Math.Max(_mostAtTheWall, atTheWall);
            if (_allHaveAWall < 0 && atTheWall == _attackers.Length)
            {
                _allHaveAWall = ticksRun;
            }

            if (inside > 0 && _firstInside < 0)
            {
                _firstInside = ticksRun;
            }

            if (_workersInside < 0 && _workers.Length > 0 && _workers.All(IsInside))
            {
                _workersInside = ticksRun;
            }

            // Over when the attackers are in and stand still, and the workers, if any, are in or dead.
            if (inside == living && !walking && atTheWall == 0
                && _workers.All(worker => !Match.World.Entities.IsAlive(worker) || Match.Movers.Get(worker).State == MoveState.Idle))
            {
                _atRest = ticksRun;
                return true;
            }

            return false;
        }

        public override (string Findings, string? Problem) Finish(int ticksRun)
        {
            var inside = _attackers.Count(unit => Match.World.Entities.IsAlive(unit) && IsInside(unit));
            var fallen = _tilesOfWall - Match.Structures.Count;
            var orders = _apart ? $"{_attackers.Length} orders in one tick" : "one order";
            var shown = $"{orders} into a closed camp: {_searchesWhenOrdered} searches for a wall in that tick, every unit had its wall at tick {_allHaveAWall}; " +
                $"first hit on it at tick {_firstHit}, first tile down at {_firstFallen}, {fallen} down in all; " +
                $"first unit inside at {_firstInside}, {inside} of {_attackers.Length} inside and at rest at {_atRest}";
            if (_workers.Length > 0)
            {
                shown += $"; each player has a gate, and the camp's {_workers.Length} workers were in through theirs at tick {_workersInside}";
            }

            return (shown,
                _mostAtTheWall == 0 ? "no unit had a wall in its way."
                : _allHaveAWall < 0 ? "not every unit found the wall in its way."
                : _firstFallen < 0 ? "no tile of the wall fell."
                : _atRest < 0 ? $"the units were not inside the camp and at rest after {ticksRun} ticks."
                : _workers.Length > 0 && _workersInside < 0 ? "the camp's workers did not get in through their gate."
                : null);
        }

        /// <summary>Inside the camp, or on a tile of its ring: in the gate, or where a tile of wall stood.</summary>
        private bool IsInside(EntityId unit)
        {
            var position = PositionOf(unit);
            var x = position.X.FloorToInt();
            var y = position.Y.FloorToInt();
            return x >= West && x <= East && y >= North && y <= South;
        }

        /// <summary>Finished wall of a player on the edge of a box, with a finished gate in place of one tile if one is named.</summary>
        private static void Ring(Match match, int player, int west, int north, int east, int south, (int X, int Y)? gate)
        {
            for (var y = north; y <= south; y++)
            {
                for (var x = west; x <= east; x++)
                {
                    if (x == west || x == east || y == north || y == south)
                    {
                        match.PlaceStructure(player, (x, y) == gate ? "gate" : "wall", x, y);
                    }
                }
            }
        }
    }
}
