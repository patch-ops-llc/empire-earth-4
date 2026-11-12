using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Game;

/// <summary>
/// The smoke test's use of the control groups: the local player's units are put into two groups,
/// the first is sent somewhere as a group and the second is left alone. Six seconds of play.
/// </summary>
/// <remarks>
/// The keys themselves are not pressed: the smoke test takes no input. What runs here is what
/// stands behind them, the groups and the search for idle workers, against a match that is
/// played. Every number is whole, so that the part takes the same course on every machine.
/// </remarks>
public sealed class ControlGroupsSmoke : SmokePart
{
    // The first group's order goes at least this many tiles from each of its units, so that none
    // of them has arrived when the part looks again.
    private const int FarTiles = 8;

    // The tiles tried as the order's target lie this far apart.
    private const int TargetStep = 4;

    // The tick at which the order must have begun, a second after it was given.
    private const int LookAgainTick = 20;

    private readonly ControlGroups _groups = new();
    private readonly List<EntityId> _idle = [];
    private readonly List<EntityId> _leftIdle = [];
    private FixedVector2 _target;
    private long _nearestAtStart;

    public override string Name => "Control groups";

    public override int Ticks => 120;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (tick == 0)
        {
            Begin(orders);
        }
        else if (tick == LookAgainTick)
        {
            LookAgain(orders);
        }
        else if (tick == Ticks - 1)
        {
            End(orders);
        }
    }

    private void Begin(SmokeOrders orders)
    {
        var match = orders.Match;
        var units = orders.OwnUnits();
        if (units.Length == 0)
        {
            orders.Fail("the player has no unit to put into a group.");
            return;
        }

        var first = (units.Length + 1) / 2;
        _groups.Set(0, units.Take(first));
        _groups.Set(1, units.Skip(first));
        if (_groups.Of(0).Count + _groups.Of(1).Count != units.Length || !_groups.Holds(0, [.. units.Take(first)]))
        {
            orders.Fail("the two groups do not hold the player's units between them.");
            return;
        }

        // The second group's idle workers are given no order, and must still be idle at the end.
        _leftIdle.Clear();
        foreach (var unit in _groups.Of(1))
        {
            if (IdleWorkers.IsIdle(match, unit))
            {
                _leftIdle.Add(unit);
            }
        }

        if (!TryFindTarget(orders, out _target))
        {
            orders.Fail($"no tile of the map is {FarTiles} tiles from every unit of the first group.");
            return;
        }

        _nearestAtStart = Nearest(orders);
        orders.Submit(new Command(CommandType.Move, orders.Player, [.. _groups.Of(0)], _target.X, _target.Y));
    }

    private void LookAgain(SmokeOrders orders)
    {
        var match = orders.Match;
        foreach (var unit in _groups.Of(0))
        {
            if (IdleWorkers.IsIdle(match, unit))
            {
                orders.Fail($"{unit} of the first group counts as idle a second after its order.");
                return;
            }
        }

        // Asking for the next idle worker again and again goes through each of them once, in ID
        // order, and then begins again.
        IdleWorkers.Find(match, orders.Player, _idle);
        var shown = EntityId.None;
        foreach (var expected in _idle)
        {
            shown = IdleWorkers.Next(_idle, shown);
            if (shown != expected || !IdleWorkers.IsIdle(match, shown) || _groups.Of(0).Contains(shown))
            {
                orders.Fail($"the next idle worker was {shown} where {expected} was due.");
                return;
            }
        }

        var again = IdleWorkers.Next(_idle, shown);
        if (again != (_idle.Count > 0 ? _idle[0] : EntityId.None))
        {
            orders.Fail($"after the last idle worker came {again} and not the first.");
        }
    }

    private void End(SmokeOrders orders)
    {
        var match = orders.Match;
        var entities = match.World.Entities;
        _groups.RemoveWhere(entity => !entities.IsAlive(entity));

        if (_groups.Of(0).Count > 0 && Nearest(orders) >= _nearestAtStart)
        {
            orders.Fail("no unit of the first group came nearer to where the group was sent.");
            return;
        }

        foreach (var unit in _leftIdle)
        {
            if (entities.IsAlive(unit) && !IdleWorkers.IsIdle(match, unit))
            {
                orders.Fail($"{unit} of the second group was idle, was given no order, and is not idle now.");
                return;
            }
        }
    }

    // The tile nearest the group's first unit among those that are far from every unit of it.
    private bool TryFindTarget(SmokeOrders orders, out FixedVector2 target)
    {
        var match = orders.Match;
        var grid = match.Grid;
        var found = false;
        var best = long.MaxValue;
        target = default;
        for (var y = TargetStep / 2; y < grid.Height; y += TargetStep)
        {
            for (var x = TargetStep / 2; x < grid.Width; x += TargetStep)
            {
                if (!grid.IsPassable(x, y))
                {
                    continue;
                }

                var center = MapGrid.TileCenter(x, y);
                var far = true;
                var toFirst = -1L;
                foreach (var unit in _groups.Of(0))
                {
                    var distance = DistanceSquared(match.Bodies.Get(unit).Position, center);
                    toFirst = toFirst < 0 ? distance : toFirst;
                    far = far && distance >= Square((long)FarTiles * Fixed.RawOne);
                }

                if (far && toFirst < best)
                {
                    found = true;
                    best = toFirst;
                    target = center;
                }
            }
        }

        return found;
    }

    // The squared distance to the target of the unit of the first group that is nearest it.
    private long Nearest(SmokeOrders orders)
    {
        var nearest = long.MaxValue;
        foreach (var unit in _groups.Of(0))
        {
            nearest = Math.Min(nearest, DistanceSquared(orders.Match.Bodies.Get(unit).Position, _target));
        }

        return nearest;
    }

    private static long DistanceSquared(FixedVector2 a, FixedVector2 b) =>
        Square((long)a.X.Raw - b.X.Raw) + Square((long)a.Y.Raw - b.Y.Raw);

    private static long Square(long value) => value * value;
}
