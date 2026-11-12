using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Vision;

namespace Ai;

/// <summary>What a raid is doing.</summary>
internal enum RaidPhase
{
    /// <summary>No raid is out.</summary>
    None,

    /// <summary>The party is on its way to the passage it goes through.</summary>
    Advance,

    /// <summary>The party is after workers and Generators.</summary>
    Raid,

    /// <summary>The party is on its way home.</summary>
    Return,
}

/// <summary>
/// The raids (design 5.5, item 5): a party of the personality's raiding line, Cavalry or Armor,
/// goes through the passage its army does not use and attacks the enemy's workers and
/// Generators. A party sets out when as many raiders as the personality sends are at home, and
/// with fewer when its army sets out to attack. It walks on move orders, so it fights nothing
/// on the way. It goes home when it has lost the personality's share of its value, and when it
/// knows of nothing left to raid.
/// </summary>
internal sealed class AiRaid
{
    /// <summary>The party goes for an enemy worker this many tiles from its middle, or nearer.</summary>
    public const int Reach = 16;

    private readonly AiSight _sight;
    private readonly AiFightDef _fight;
    private readonly Dictionary<int, int> _party = [];
    private readonly List<(int Target, EntityId Unit)> _toWorkers = [];
    private readonly List<SeenUnit> _workers = [];
    private readonly List<EntityId> _group = [];
    private readonly bool[] _startSeen;
    private AiReaction _launchReaction;
    private AiReaction _retreatReaction;
    private long _whole;
    private int _deadline;
    private int _stagingX;
    private int _stagingY;

    public AiRaid(AiSight sight, AiFightDef fight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(fight);
        _sight = sight;
        _fight = fight;
        _startSeen = new bool[sight.OtherStarts.Count];
    }

    public RaidPhase Phase { get; private set; }

    /// <summary>The times it sent a party out.</summary>
    public int Raids { get; private set; }

    /// <summary>The units of the party that are alive. Whoever sorts the player's units each round fills it.</summary>
    public List<OwnFighter> Units { get; } = [];

    /// <summary>True for a unit that went out with the party.</summary>
    public bool Has(EntityId unit) => _party.ContainsKey(unit.Value);

    /// <param name="home">The units at home. Those it takes for a party are taken out of the list.</param>
    /// <param name="isAttacking">True in the round its army sets out: the raiders at home then go with it, however few they are.</param>
    /// <param name="isDefending">True while the units at home are needed there.</param>
    /// <param name="musterX">The tile its army gathers on.</param>
    /// <param name="musterY">The tile its army gathers on.</param>
    /// <param name="commands">The list its orders are added to.</param>
    public void Think(List<OwnFighter> home, bool isAttacking, bool isDefending, int musterX, int musterY, List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(commands);

        switch (Phase)
        {
            case RaidPhase.None:
                var raiders = CountRaiders(home);
                var whole = _launchReaction.Holds(!isDefending && raiders >= _fight.RaidParty, _sight.Tick, _fight.ReactionTicks);

                // With its army, a party of any size: but the army is never left without a unit.
                if (whole || (isAttacking && raiders > 0 && raiders < home.Count))
                {
                    Launch(home, musterX, musterY, commands);
                }

                break;

            case RaidPhase.Advance:
            case RaidPhase.Raid:
                if (Units.Count == 0)
                {
                    End();
                }
                else if (_retreatReaction.Holds(_fight.IsTooMuchLost(_whole - LivingValue(), _whole), _sight.Tick, _fight.ReactionTicks))
                {
                    GoHome(musterX, musterY, commands);
                }
                else if (Phase == RaidPhase.Raid
                    || 2 * AiOrders.CountNear(Units, _stagingX, _stagingY, AiFight.Near) >= Units.Count
                    || _sight.Tick >= _deadline)
                {
                    Phase = RaidPhase.Raid;
                    if (!GiveOrders(commands))
                    {
                        GoHome(musterX, musterY, commands);
                    }
                }

                break;

            case RaidPhase.Return:
                if (Units.Count == 0
                    || 2 * AiOrders.CountNear(Units, musterX, musterY, AiFight.Near) >= Units.Count
                    || _sight.Tick >= _deadline)
                {
                    End();
                }

                break;
        }
    }

    private int CountRaiders(List<OwnFighter> home)
    {
        var count = 0;
        foreach (var unit in home)
        {
            if (_sight.Data.Chassis[unit.Chassis].Line == _fight.RaidLine)
            {
                count++;
            }
        }

        return count;
    }

    private void Launch(List<OwnFighter> home, int musterX, int musterY, List<Command> commands)
    {
        _party.Clear();
        _whole = 0;
        Array.Clear(_startSeen);
        for (var i = 0; i < home.Count && Units.Count < _fight.RaidParty;)
        {
            var unit = home[i];
            if (_sight.Data.Chassis[unit.Chassis].Line != _fight.RaidLine)
            {
                i++;
                continue;
            }

            var value = _sight.ValueOf(unit.Chassis);
            _party[unit.Entity.Value] = value;
            _whole += value;
            Units.Add(unit);
            home.RemoveAt(i);
        }

        Raids++;
        _retreatReaction.Reset();
        Phase = RaidPhase.Raid;

        // Through the passage its army does not use, toward what it knows of to raid, or the nearest start of another seat.
        var other = _fight.Passage == MarkerKind.Chokepoint ? MarkerKind.OpenField : MarkerKind.Chokepoint;
        if (TryFindGoal(musterX, musterY, out var goalX, out var goalY)
            && AiOrders.TryFindPassage(_sight, other, musterX, musterY, goalX, goalY, out _stagingX, out _stagingY))
        {
            Phase = RaidPhase.Advance;
            _deadline = DeadlineTo(_stagingX, _stagingY);
            SendAll(_stagingX, _stagingY, commands);
        }
    }

    private void GoHome(int musterX, int musterY, List<Command> commands)
    {
        Phase = RaidPhase.Return;
        _deadline = DeadlineTo(musterX, musterY);
        SendAll(musterX, musterY, commands);
    }

    private void End()
    {
        _party.Clear();
        _whole = 0;
        Units.Clear();
        _launchReaction.Reset();
        Phase = RaidPhase.None;
    }

    /// <summary>
    /// Orders the party: each unit at the enemy worker nearest to it, of those within reach of
    /// the party; with no worker in reach, all at the nearest Generator it knows of; with none
    /// of those, to the nearest start of another seat it has not looked at.
    /// </summary>
    /// <returns>False when it knows of nothing to raid and nowhere to look.</returns>
    private bool GiveOrders(List<Command> commands)
    {
        var (middleX, middleY) = AiOrders.MiddleOf(Units);

        _workers.Clear();
        foreach (var unit in _sight.Enemies)
        {
            if (unit.IsWorker
                && AiSearch.DistanceSquared(unit.Position.X.FloorToInt(), unit.Position.Y.FloorToInt(), middleX, middleY) <= Reach * Reach)
            {
                _workers.Add(unit);
            }
        }

        if (_workers.Count > 0)
        {
            _toWorkers.Clear();
            foreach (var unit in Units)
            {
                var nearest = 0;
                var nearestDistance = long.MaxValue;
                for (var i = 0; i < _workers.Count; i++)
                {
                    var distance = AiSearch.DistanceSquared(
                        _workers[i].Position.X.FloorToInt(),
                        _workers[i].Position.Y.FloorToInt(),
                        AiOrders.TileX(unit),
                        AiOrders.TileY(unit));
                    if (distance < nearestDistance)
                    {
                        nearest = i;
                        nearestDistance = distance;
                    }
                }

                if (unit.Order != UnitOrder.Attack || unit.Target != _workers[nearest].Entity)
                {
                    _toWorkers.Add((nearest, unit.Entity));
                }
            }

            _toWorkers.Sort(static (left, right) =>
                left.Target != right.Target ? left.Target.CompareTo(right.Target) : left.Unit.CompareTo(right.Unit));
            for (var i = 0; i < _toWorkers.Count;)
            {
                var target = _toWorkers[i].Target;
                _group.Clear();
                for (; i < _toWorkers.Count && _toWorkers[i].Target == target; i++)
                {
                    _group.Add(_toWorkers[i].Unit);
                }

                AiOrders.Attack(_sight.Player, _group, _workers[target].Entity, commands);
            }

            return true;
        }

        if (TryFindGenerator(middleX, middleY, out var generator))
        {
            _group.Clear();
            foreach (var unit in Units)
            {
                if (unit.Order != UnitOrder.Attack || unit.Target != generator.Entity)
                {
                    _group.Add(unit.Entity);
                }
            }

            AiOrders.Attack(_sight.Player, _group, generator.Entity, commands);
            return true;
        }

        if (!TryFindStart(middleX, middleY, out var x, out var y))
        {
            return false;
        }

        _group.Clear();
        foreach (var unit in Units)
        {
            if (unit.Order != UnitOrder.Move)
            {
                _group.Add(unit.Entity);
            }
        }

        AiOrders.Send(CommandType.Move, _sight.Player, _group, x, y, commands);
        return true;
    }

    private bool TryFindGoal(int fromX, int fromY, out int x, out int y)
    {
        if (TryFindGenerator(fromX, fromY, out var generator))
        {
            x = generator.X + (generator.Size / 2);
            y = generator.Y + (generator.Size / 2);
            return true;
        }

        return TryFindStart(fromX, fromY, out x, out y);
    }

    private bool TryFindGenerator(int fromX, int fromY, out SeenStructure generator)
    {
        generator = default;
        var bestDistance = long.MaxValue;
        foreach (var structure in _sight.EnemyStructures)
        {
            var distance = AiSearch.DistanceToSquare(structure.X, structure.Y, structure.Size, fromX, fromY);
            if (_sight.Data.StructureOf(structure.Type) is { IsEnergyWorkSite: true } && distance < bestDistance)
            {
                generator = structure;
                bestDistance = distance;
            }
        }

        return bestDistance != long.MaxValue;
    }

    /// <summary>The nearest start of another seat that it has not seen since the party went out.</summary>
    private bool TryFindStart(int fromX, int fromY, out int x, out int y)
    {
        var best = -1;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < _startSeen.Length; i++)
        {
            var start = _sight.OtherStarts[i];
            _startSeen[i] |= _sight.IsVisible(start.X, start.Y);
            var distance = AiSearch.DistanceSquared(start.X, start.Y, fromX, fromY);
            if (!_startSeen[i] && distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        (x, y) = best < 0 ? (0, 0) : _sight.OtherStarts[best];
        return best >= 0;
    }

    private long LivingValue()
    {
        long value = 0;
        foreach (var unit in Units)
        {
            value += _party[unit.Entity.Value];
        }

        return value;
    }

    private int DeadlineTo(int x, int y)
    {
        long furthest = 0;
        foreach (var unit in Units)
        {
            furthest = Math.Max(furthest, AiSearch.DistanceSquared(AiOrders.TileX(unit), AiOrders.TileY(unit), x, y));
        }

        return AiSearch.DeadlineFor(_sight.Tick, furthest);
    }

    private void SendAll(int x, int y, List<Command> commands)
    {
        _group.Clear();
        foreach (var unit in Units)
        {
            _group.Add(unit.Entity);
        }

        AiOrders.Send(CommandType.Move, _sight.Player, _group, x, y, commands);
    }
}
