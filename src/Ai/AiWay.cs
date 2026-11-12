using Sim.Combat;
using Sim.Core;
using Sim.Numerics;

namespace Ai;

/// <summary>
/// What it does about a unit of its own that an enemy stands in the way of. A blocked unit keeps
/// its order and waits, and only on an attack-move does it fight what is in its way (design
/// 2.4). So a unit that has stood blocked for <see cref="BlockedTicks"/> on a move order, or on
/// an order to attack something further off, is given an attack-move to where it was going. It
/// is left on that until its way is open again, and for no longer than <see cref="LongestTicks"/>:
/// the round's other orders for it are taken out. After that the parts that lead it give it its
/// order again.
/// </summary>
/// <remarks>
/// Without this, two computers stop each other for good: the raiders of one stand at the other's
/// Barracks on orders to attack workers behind it, and the units that come out of it stand
/// before them on move orders to where the army gathers. The scout is not its concern: it is
/// sent to look, and turns for home by itself when its walk has taken too long.
/// </remarks>
internal sealed class AiWay
{
    /// <summary>The ticks a unit stands blocked before it is told to fight what is in its way.</summary>
    public const int BlockedTicks = 2 * Sim.SimInfo.TicksPerSecond;

    /// <summary>The longest a unit is left on the attack-move, in ticks, while it stands blocked and fights nothing.</summary>
    public const int LongestTicks = 20 * Sim.SimInfo.TicksPerSecond;

    private readonly AiSight _sight;
    private readonly Dictionary<int, int> _blockedSince = [];
    private readonly Dictionary<int, int> _clearingSince = [];
    private readonly HashSet<int> _seen = [];
    private readonly List<int> _gone = [];
    private readonly List<(int X, int Y, EntityId Unit)> _toClear = [];
    private readonly List<EntityId> _group = [];

    public AiWay(AiSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        _sight = sight;
    }

    /// <summary>The times it told a unit to fight what was in its way.</summary>
    public int Cleared { get; private set; }

    /// <summary>True for a unit that is on an attack-move of this part's, which the other parts leave it on.</summary>
    public bool IsClearing(EntityId unit) => _clearingSince.ContainsKey(unit.Value);

    /// <summary>
    /// Call it after the parts that lead units have given the round's orders.
    /// </summary>
    /// <param name="orderTicks">The ticks an order of its may take to run: for so long a unit it has ordered is not judged by the order it is on.</param>
    /// <param name="commands">The list of the round's orders.</param>
    /// <param name="first">The position in the list of the first order of this round.</param>
    public void Think(int orderTicks, List<Command> commands, int first)
    {
        ArgumentNullException.ThrowIfNull(commands);

        _seen.Clear();
        _toClear.Clear();
        foreach (var unit in _sight.Fighters)
        {
            var id = unit.Entity.Value;
            _seen.Add(id);
            if (unit.Entity == _sight.Scout)
            {
                _blockedSince.Remove(id);
                _clearingSince.Remove(id);
                continue;
            }

            if (_clearingSince.TryGetValue(id, out var since))
            {
                var waited = _sight.Tick - since;
                var keeps = waited <= orderTicks
                    || (unit.Order == UnitOrder.AttackMove && (unit.Target != EntityId.None || (unit.IsBlocked && waited < LongestTicks)));
                if (keeps)
                {
                    continue;
                }

                _clearingSince.Remove(id);
                _blockedSince.Remove(id);
            }

            if (!unit.IsBlocked || unit.Order is not (UnitOrder.Move or UnitOrder.Attack))
            {
                _blockedSince.Remove(id);
                continue;
            }

            if (!_blockedSince.TryGetValue(id, out var blocked))
            {
                _blockedSince[id] = _sight.Tick;
            }
            else if (_sight.Tick - blocked >= BlockedTicks)
            {
                _blockedSince.Remove(id);
                _clearingSince[id] = _sight.Tick;
                _toClear.Add((unit.Destination.X.FloorToInt(), unit.Destination.Y.FloorToInt(), unit.Entity));
            }
        }

        Forget(_blockedSince);
        Forget(_clearingSince);
        if (_clearingSince.Count == 0)
        {
            return;
        }

        LeaveAlone(commands, first);

        _toClear.Sort(static (left, right) =>
            left.X != right.X ? left.X.CompareTo(right.X)
            : left.Y != right.Y ? left.Y.CompareTo(right.Y)
            : left.Unit.CompareTo(right.Unit));
        for (var i = 0; i < _toClear.Count;)
        {
            var (x, y, _) = _toClear[i];
            _group.Clear();
            for (; i < _toClear.Count && _toClear[i].X == x && _toClear[i].Y == y; i++)
            {
                _group.Add(_toClear[i].Unit);
            }

            Cleared += _group.Count;
            AiOrders.Send(CommandType.AttackMove, _sight.Player, _group, x, y, commands);
        }
    }

    /// <summary>Forgets the units that are gone.</summary>
    private void Forget(Dictionary<int, int> units)
    {
        _gone.Clear();
        foreach (var id in units.Keys)
        {
            if (!_seen.Contains(id))
            {
                _gone.Add(id);
            }
        }

        foreach (var id in _gone)
        {
            units.Remove(id);
        }
    }

    /// <summary>Takes the units it has on an attack-move out of the round's orders to units, and drops an order that is left with none.</summary>
    private void LeaveAlone(List<Command> commands, int first)
    {
        for (var i = commands.Count - 1; i >= first; i--)
        {
            var command = commands[i];
            if (command.Type is not (CommandType.Move or CommandType.AttackMove or CommandType.Attack or CommandType.Stop))
            {
                continue;
            }

            var kept = 0;
            foreach (var entity in command.Entities)
            {
                kept += _clearingSince.ContainsKey(entity.Value) ? 0 : 1;
            }

            if (kept == command.Entities.Length)
            {
                continue;
            }

            if (kept == 0)
            {
                commands.RemoveAt(i);
                continue;
            }

            _group.Clear();
            foreach (var entity in command.Entities)
            {
                if (!_clearingSince.ContainsKey(entity.Value))
                {
                    _group.Add(entity);
                }
            }

            commands[i] = new Command(
                command.Type,
                command.Player,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_group),
                command.TargetX,
                command.TargetY,
                command.TargetEntity,
                command.ContentId);
        }
    }
}
