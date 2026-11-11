using Sim.Core;
using Sim.Numerics;
using Sim.Research;
using Sim.Setup;

namespace Tools.Replay;

/// <summary>
/// Gives the orders of a match that nobody plays, for <c>replay record</c>: every few ticks one of
/// the players sends some of its units somewhere. The orders come from a random generator of their
/// own, so the same seed gives the same orders for the same match.
/// </summary>
/// <remarks>
/// The mix is chosen to reach the rules a recorded match should pin: groups that share routes,
/// many separate orders in one tick, which spend the route budget and make units wait, short hops
/// that need no route, targets on blocked ground and outside the map, and commands the simulation
/// must drop: for units of another player, for entities that never existed, from a player who is
/// not in the match and of a kind without a handler. A milestone that adds a kind of command adds
/// it to the mix, after the ones that are here, and records a new replay; replays recorded before
/// keep their commands.
/// <para>
/// Research (M14) is one order in three: mostly a research on some track, which the simulation
/// starts when the player has a free slot and the price and drops otherwise; sometimes a cancel,
/// usually of the research the player has under way; and research on tracks that do not exist
/// and from players who are not in the match. Research orders need no units.
/// </para>
/// </remarks>
internal sealed class ScriptedOrders
{
    private const int LongestPause = 120;

    /// <summary>The kinds of order from M2 to M5 are drawn below this number, and those of research from it.</summary>
    private const int FirstResearchKind = 100;
    private const int KindCount = 150;

    private readonly Match _match;
    private readonly DeterministicRandom _random;
    private readonly List<EntityId> _own = [];
    private readonly List<EntityId> _picked = [];
    private int _nextTick;

    public ScriptedOrders(Match match, ulong seed)
    {
        _match = match;
        _random = new DeterministicRandom(seed);
        _nextTick = 1 + _random.NextInt(10);
    }

    /// <summary>Adds the commands to schedule for a tick, which is the next to run, in the order they run.</summary>
    public void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick < _nextTick)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var player = _random.NextInt(_match.Players.Count);
        var kind = _random.NextInt(KindCount);
        if (kind >= FirstResearchKind)
        {
            commands.Add(ResearchOrder(player, kind - FirstResearchKind));
            return;
        }

        CollectUnitsOf(player);
        if (_own.Count == 0)
        {
            return;
        }

        if (kind < 45)
        {
            PickSome();
            commands.Add(Move(player, _picked, AnyPoint()));
        }
        else if (kind < 60)
        {
            commands.Add(Move(player, _own, AnyPoint()));
        }
        else if (kind < 78)
        {
            // One order each, and half the time from every player in the same tick: every unit
            // needs a route of its own, which is more than one tick's budget finds.
            var everyone = _random.NextInt(2) == 0;
            for (var other = 0; other < _match.Players.Count; other++)
            {
                if (other != player && !everyone)
                {
                    continue;
                }

                CollectUnitsOf(other);
                foreach (var unit in _own)
                {
                    commands.Add(new Command(CommandType.Move, other, [unit], Coordinate(_match.Grid.Width), Coordinate(_match.Grid.Height)));
                }
            }
        }
        else if (kind < 88)
        {
            PickSome();
            commands.Add(Move(player, _picked, PointNear(_picked[0])));
        }
        else if (kind < 93)
        {
            // Units of every player, one twice and one that never existed. Only the player's own move.
            _picked.Clear();
            for (var i = 0; i < _match.World.Entities.Count; i++)
            {
                _picked.Add(_match.World.Entities[i]);
            }

            _picked.Add(_own[0]);
            _picked.Add(new EntityId(1_000_000 + _random.NextInt(1000)));
            commands.Add(Move(player, _picked, AnyPoint()));
        }
        else if (kind < 96)
        {
            // Off the map: a little way, or as far as a number goes.
            var outside = _random.NextInt(2) == 0
                ? new FixedVector2(Fixed.FromInt(_match.Grid.Width + 1 + _random.NextInt(50)), Fixed.FromInt(-1 - _random.NextInt(50)))
                : new FixedVector2(Fixed.MaxValue, Fixed.MinValue);
            commands.Add(Move(player, _own, outside));
        }
        else if (kind < 98)
        {
            commands.Add(Move(_match.Players.Count + _random.NextInt(5), _own, AnyPoint()));
        }
        else
        {
            commands.Add(new Command((CommandType)(60_000 + _random.NextInt(1000)), player, [.. _own], Coordinate(_match.Grid.Width), Coordinate(_match.Grid.Height), _own[0], _random.NextInt(100)));
        }
    }

    private Command ResearchOrder(int player, int kind)
    {
        var tracks = _match.Data.Tracks.Count;
        if (kind < 30)
        {
            return new Command(CommandType.StartResearch, player, contentId: _random.NextInt(tracks));
        }

        if (kind < 42)
        {
            // The research under way three times in four, so that cancels are not nearly all dropped.
            var track = _random.NextInt(tracks);
            var slot = _match.Research.State.Slot(player, ResearchState.PlayerSlot);
            return new Command(CommandType.CancelResearch, player, contentId: slot.IsBusy && _random.NextInt(4) != 0 ? slot.Track : track);
        }

        if (kind < 47)
        {
            var noTrack = _random.NextInt(2) == 0 ? tracks + _random.NextInt(1000) : -1 - _random.NextInt(1000);
            return new Command(_random.NextInt(2) == 0 ? CommandType.StartResearch : CommandType.CancelResearch, player, contentId: noTrack);
        }

        // From nobody in the match, with fields that research does not read.
        var nobody = _match.Players.Count + _random.NextInt(5);
        var point = AnyPoint();
        return new Command(CommandType.StartResearch, nobody, [new EntityId(1)], point.X, point.Y, new EntityId(2), _random.NextInt(tracks));
    }

    private static Command Move(int player, List<EntityId> units, FixedVector2 target) =>
        new(CommandType.Move, player, [.. units], target.X, target.Y);

    private void CollectUnitsOf(int player)
    {
        _own.Clear();
        var bodies = _match.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var entity = bodies.EntityAt(i);
            if (bodies.ValueAt(i).Owner == player && _match.World.Entities.IsAlive(entity) && _match.Movers.Contains(entity))
            {
                _own.Add(entity);
            }
        }
    }

    /// <summary>Each of the player's units with an even chance, and at least one.</summary>
    private void PickSome()
    {
        _picked.Clear();
        foreach (var unit in _own)
        {
            if (_random.NextInt(2) == 0)
            {
                _picked.Add(unit);
            }
        }

        if (_picked.Count == 0)
        {
            _picked.Add(_own[_random.NextInt(_own.Count)]);
        }
    }

    /// <summary>A point anywhere on the map, on open or on blocked ground alike.</summary>
    private FixedVector2 AnyPoint() => new(Coordinate(_match.Grid.Width), Coordinate(_match.Grid.Height));

    /// <summary>A point within three tiles of a unit, which may lie off the map.</summary>
    private FixedVector2 PointNear(EntityId unit)
    {
        var position = _match.Bodies.Get(unit).Position;
        var reach = 3 * Fixed.RawOne;
        return new FixedVector2(
            Fixed.FromRaw(position.X.Raw + _random.NextInt(-reach, reach + 1)),
            Fixed.FromRaw(position.Y.Raw + _random.NextInt(-reach, reach + 1)));
    }

    private Fixed Coordinate(int tiles) => Fixed.FromRaw(_random.NextInt(tiles * Fixed.RawOne));
}
