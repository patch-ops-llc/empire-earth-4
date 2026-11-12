using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;

namespace Tools.Benchmark;

/// <summary>
/// The scenarios of whole armies: units of a chassis, which look for targets, fight, see and are
/// seen, where the scenarios of <see cref="Scenarios"/> have units that only walk. These are
/// the worst cases the budget names: a large fight with the blast, a siege with Towers and walls
/// that fall, and one move order for everything a player has, from all over the map. The fog is
/// worked out for both players in each, as in every match.
/// </summary>
internal static class ArmyScenarios
{
    // The open field of ridge: the cliff's tiles 46 to 49 across, open in rows 50 to 77.
    private const int FieldTop = 51;
    private const int FieldRows = 25;

    /// <summary>What an army is made of, rank by rank from the front, in tenths: the four lines of the first playable.</summary>
    private static readonly (string Chassis, int Tenths)[] Mix =
    [
        ("man_at_arms", 3),
        ("cuirassier", 2),
        ("musketeer", 3),
        ("field_cannon", 2),
    ];

    public static IReadOnlyList<Scenario> All(bool quick, int units) =>
    [
        new("fight", data => new Fight(Scenarios.Ridge(data), units, quick ? 300 : 3000)),
        new("siege", data => new Siege(Scenarios.Ridge(data), units, quick ? 300 : 3000)),
        new("move-everything", data => new MoveEverything(Scenarios.Ridge(data), units, quick ? 300 : 3000)),
    ];

    /// <summary>
    /// Places an army in ranks that run north to south, its front rank at a column and the
    /// others behind it: the melee infantry in front, then the cavalry, the ranged infantry and
    /// the artillery, in the shares of <see cref="Mix"/>. A unit stands on every tile of a rank,
    /// and on every second one for a line whose spacing is wider than a tile, with the next rank
    /// two tiles behind. A tile no unit can stand on is left out.
    /// </summary>
    /// <param name="match">The match.</param>
    /// <param name="player">The army's owner.</param>
    /// <param name="count">The number of units.</param>
    /// <param name="front">The column of the front rank.</param>
    /// <param name="back">Which way the other ranks lie from the front: -1 for west, 1 for east.</param>
    /// <param name="top">The first row of a rank.</param>
    /// <param name="rows">The number of rows a rank is long.</param>
    private static EntityId[] PlaceArmy(Match match, int player, int count, int front, int back, int top, int rows)
    {
        var units = new List<EntityId>(count);
        var column = front;
        for (var part = 0; part < Mix.Length; part++)
        {
            var chassis = match.Data.Chassis.Single(candidate => candidate.Id == Mix[part].Chassis);
            var spacing = match.Research.Tech(player).Blueprints[chassis.Index].Line.Base.Spacing.ToFraction().ToFixed();
            var step = spacing > Fixed.One ? 2 : 1;

            // The last part takes what the rounding of the others left.
            var wanted = part == Mix.Length - 1 ? count - units.Count : count * Mix[part].Tenths / 10;
            for (var placed = 0; placed < wanted; column += back * step)
            {
                if (column < 0 || column >= match.Grid.Width)
                {
                    throw new InvalidOperationException($"The map has no room for an army of {count} units.");
                }

                for (var row = top; row < top + rows && placed < wanted; row += step)
                {
                    if (match.Grid.IsPassable(column, row))
                    {
                        units.Add(match.SpawnChassis(player, chassis.Index, MapGrid.TileCenter(column, row)));
                        placed++;
                    }
                }
            }
        }

        return [.. units];
    }

    private static int Living(Match match, EntityId[] units) => units.Count(match.World.Entities.IsAlive);

    /// <summary>What the events of a fight add up to. Counted after each tick, outside the clock.</summary>
    private sealed class Tally
    {
        public int Deaths { get; private set; }

        public int Shots { get; private set; }

        public int MeleeHits { get; private set; }

        public int Blasts { get; private set; }

        public int StructuresLost { get; private set; }

        public int MostShotsInFlight { get; private set; }

        public void AfterTick(Match match)
        {
            foreach (var simEvent in match.Simulation.Events)
            {
                switch (simEvent.Kind)
                {
                    case SimEventKind.Death:
                        Deaths++;
                        break;
                    case SimEventKind.ShotFired:
                        Shots++;
                        break;
                    case SimEventKind.MeleeHit:
                        MeleeHits++;
                        break;
                    case SimEventKind.Blast:
                        Blasts++;
                        break;
                    case SimEventKind.StructureRemoved:
                        StructuresLost++;
                        break;
                    default:
                        break;
                }
            }

            MostShotsInFlight = Math.Max(MostShotsInFlight, match.Shots.Count);
        }
    }

    /// <summary>
    /// The large fight with the blast: two armies of the four lines face each other across the
    /// open field and are sent at each other by attack-move, in the same tick. A fifth of each is
    /// artillery, whose shells damage everything within their radius.
    /// </summary>
    private sealed class Fight : Run
    {
        private readonly EntityId[] _west;
        private readonly EntityId[] _east;
        private readonly Tally _tally = new();
        private int _over = -1;

        public Fight(Match match, int count, int mostTicks)
            : base(match, mostTicks)
        {
            _west = PlaceArmy(match, 0, count / 2, 40, -1, FieldTop, FieldRows);
            _east = PlaceArmy(match, 1, count - (count / 2), 55, 1, FieldTop, FieldRows);
            match.RefreshDerivedState();
            match.Vision.Refresh();
        }

        public override void BeforeTick(int tick)
        {
            if (tick == 0)
            {
                var middle = Fixed.FromInt(FieldTop + (FieldRows / 2));
                OrderAttackMove(0, _west, new FixedVector2(Fixed.FromInt(62), middle));
                OrderAttackMove(1, _east, new FixedVector2(Fixed.FromInt(33), middle));
            }
        }

        public override bool AfterTick(int ticksRun)
        {
            _tally.AfterTick(Match);
            if (Living(Match, _west) == 0 || Living(Match, _east) == 0)
            {
                _over = ticksRun;
                return true;
            }

            return false;
        }

        public override (string Findings, string? Problem) Finish(int ticksRun)
        {
            var findings = $"{_west.Length} against {_east.Length} of four lines on the open field: {_tally.Deaths} died, " +
                $"{_tally.MeleeHits} melee hits, {_tally.Shots} shots with at most {_tally.MostShotsInFlight} in flight, {_tally.Blasts} blasts; " +
                (_over < 0 ? $"not over after {ticksRun} ticks" : $"over at tick {_over}") +
                $" with {Living(Match, _west)} and {Living(Match, _east)} left";
            return (findings,
                _tally.Deaths == 0 ? "nobody died in the fight."
                : _tally.Blasts == 0 ? "no shell landed in the fight."
                : _tally.MeleeHits == 0 ? "no melee unit reached an enemy."
                : null);
        }
    }

    /// <summary>
    /// An army is sent by attack-move at a base behind a wall with one gate, held by Towers and
    /// by an army two thirds its size: structures as targets, Towers that pick targets, and
    /// walls that fall, each of which changes the ground routes are found on.
    /// </summary>
    private sealed class Siege : Run
    {
        private const int WallColumn = 68;
        private const int WallTop = 24;
        private const int WallBottom = 71;
        private const int ArmyTop = 26;
        private const int ArmyRows = 44;

        private readonly EntityId[] _attackers;
        private readonly EntityId[] _defenders;
        private readonly Tally _tally = new();
        private readonly int _structures;

        public Siege(Match match, int count, int mostTicks)
            : base(match, mostTicks)
        {
            // The base of player 1, east of the cliff: the Capitol, what a base has round it, a
            // wall from north to south with a gate five tiles wide, and Towers behind the wall.
            match.PlaceStructure(1, "capitol", 86, 44);
            (string Id, int X, int Y)[] buildings =
            [
                ("barracks", 80, 34), ("barracks", 80, 56), ("works", 91, 42), ("works", 91, 50),
                ("academy", 93, 45), ("academy", 93, 47), ("generator", 84, 30), ("generator", 84, 62),
            ];
            foreach (var (id, x, y) in buildings)
            {
                match.PlaceStructure(1, id, x, y);
            }

            for (var y = WallTop; y <= WallBottom; y++)
            {
                if (y is < 45 or > 49)
                {
                    match.PlaceStructure(1, "wall", WallColumn, y);
                }
            }

            foreach (var y in new[] { 27, 33, 39, 43, 50, 55, 61, 67 })
            {
                match.PlaceStructure(1, "tower", WallColumn + 2, y);
            }

            _structures = match.Structures.Count;
            // Two in five hold the base, and three in five come at it.
            _defenders = PlaceArmy(match, 1, count * 2 / 5, WallColumn + 5, 1, ArmyTop, ArmyRows);
            _attackers = PlaceArmy(match, 0, count - (count * 2 / 5), 61, -1, ArmyTop, ArmyRows);
            match.RefreshDerivedState();
            match.Vision.Refresh();
        }

        public override void BeforeTick(int tick)
        {
            if (tick == 0)
            {
                OrderAttackMove(0, _attackers, new FixedVector2(Fixed.FromInt(88), Fixed.FromInt(46)));
            }
        }

        public override bool AfterTick(int ticksRun)
        {
            _tally.AfterTick(Match);
            return Living(Match, _attackers) == 0 || (Living(Match, _defenders) == 0 && Match.Structures.Count == 0);
        }

        public override (string Findings, string? Problem) Finish(int ticksRun)
        {
            var findings = $"{_attackers.Length} sent at a base of {_structures} structures held by {_defenders.Length}: {_tally.Deaths - _tally.StructuresLost} units died, " +
                $"{_tally.StructuresLost} structures fell, {_tally.Shots} shots with at most {_tally.MostShotsInFlight} in flight, {_tally.Blasts} blasts, " +
                $"{Match.Routes.SearchCount} route searches; after {ticksRun} ticks {Living(Match, _attackers)} attackers and {Living(Match, _defenders)} defenders are left";
            return (findings,
                _tally.Deaths == 0 ? "nobody died in the siege."
                : _tally.StructuresLost == 0 ? "no structure fell in the siege."
                : null);
        }
    }

    /// <summary>
    /// The full-map move order: the units of both players stand all over the map, and each
    /// player sends everything it has to one corner with one order, in the same tick. Every
    /// unit needs a way across the map at once, the two players' units cross on the way, and
    /// each player's units come to rest round one point.
    /// </summary>
    private sealed class MoveEverything : Run
    {
        private readonly EntityId[][] _units;
        private readonly FixedVector2[] _before;
        private readonly EntityId[] _all;
        private int _atRest = -1;
        private int _mostWalking;

        public MoveEverything(Match match, int count, int mostTicks)
            : base(match, mostTicks)
        {
            var random = new DeterministicRandom(25);
            var taken = new HashSet<(int, int)>();
            var units = new[] { new List<EntityId>(), new List<EntityId>() };
            for (var i = 0; i < count; i++)
            {
                var chassis = match.Data.Chassis.Single(candidate => candidate.Id == Mix[i % Mix.Length].Chassis);
                while (true)
                {
                    var tile = (random.NextInt(match.Grid.Width), random.NextInt(match.Grid.Height));
                    if (match.Grid.IsPassable(tile.Item1, tile.Item2) && taken.Add(tile))
                    {
                        units[i % 2].Add(match.SpawnChassis(i % 2, chassis.Index, MapGrid.TileCenter(tile.Item1, tile.Item2)));
                        break;
                    }
                }
            }

            _units = [[.. units[0]], [.. units[1]]];
            _all = [.. _units[0], .. _units[1]];
            _before = new FixedVector2[_all.Length];
            match.RefreshDerivedState();
            match.Vision.Refresh();
        }

        public override void BeforeTick(int tick)
        {
            if (tick == 1)
            {
                OrderMove(0, _units[0], MapGrid.TileCenter(12, 12));
                OrderMove(1, _units[1], MapGrid.TileCenter(Match.Grid.Width - 13, Match.Grid.Height - 13));
            }

            for (var i = 0; i < _all.Length; i++)
            {
                _before[i] = Match.World.Entities.IsAlive(_all[i]) ? PositionOf(_all[i]) : default;
            }
        }

        public override bool AfterTick(int ticksRun)
        {
            var walking = 0;
            var moved = false;
            for (var i = 0; i < _all.Length; i++)
            {
                if (!Match.World.Entities.IsAlive(_all[i]))
                {
                    continue;
                }

                walking += Match.Movers.Get(_all[i]).State != MoveState.Idle ? 1 : 0;
                moved |= PositionOf(_all[i]) != _before[i];
            }

            _mostWalking = Math.Max(_mostWalking, walking);
            if (ticksRun > 2 && walking == 0 && !moved)
            {
                _atRest = ticksRun;
                return true;
            }

            return false;
        }

        public override (string Findings, string? Problem) Finish(int ticksRun)
        {
            var findings = $"{_units[0].Length} and {_units[1].Length} units from all over the map, each player's sent to a corner by one order: " +
                $"{Match.Routes.SearchCount} route searches, at most {_mostWalking} walking at once, " +
                (_atRest < 0 ? $"not at rest after {ticksRun} ticks" : $"at rest at tick {_atRest}") +
                $"; {Living(Match, _all)} alive; the players see {Match.Vision.Grid.CountVisible(0)} and {Match.Vision.Grid.CountVisible(1)} tiles";
            return (findings, _mostWalking < _all.Length / 2 ? "most units did not take the order." : null);
        }
    }
}
