using Sim.Core;
using Sim.Numerics;

namespace Game;

/// <summary>
/// The smoke test's move orders: 30 seconds of play in which the local player's units are sent
/// across the map, onto a cliff, beside a forest and to the map's last point.
/// </summary>
public sealed class MoveOrdersSmoke : SmokePart
{
    // The orders, each given once the part has run the number of ticks named. Units are the local
    // player's, counted from 0 in ID order. Targets are in sixteenths of a tile, so that they are
    // whole numbers here. On ridge the local player starts on the left, and the cliff runs down
    // the middle of the map with gaps in it.
    private static readonly Order[] Orders =
    [
        // Everyone to the far side of the cliff, which takes a route through a gap.
        new(AtTick: 0, FirstUnit: 0, UnitCount: 6, X16: 936, Y16: 484),

        // Three of them onto the cliff itself, where nobody can stand: they go to the nearest tile they can reach.
        new(AtTick: 100, FirstUnit: 0, UnitCount: 3, X16: 760, Y16: 8),

        // The others to a point right beside a forest, which is moved off the trees.
        new(AtTick: 220, FirstUnit: 3, UnitCount: 3, X16: 543, Y16: 72),

        // One to the last point of the map, in its far corner.
        new(AtTick: 400, FirstUnit: 0, UnitCount: 1, X16: 1536, Y16: 1536),
    ];

    private EntityId[] _units = [];

    public override string Name => "Move orders";

    public override int Ticks => 600;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (tick == 0)
        {
            _units = orders.OwnUnits();
        }

        foreach (var order in Orders)
        {
            if (order.AtTick == tick)
            {
                Give(order, orders);
            }
        }
    }

    private void Give(Order order, SmokeOrders orders)
    {
        if (order.FirstUnit + order.UnitCount > _units.Length)
        {
            orders.Fail($"the script orders unit {order.FirstUnit + order.UnitCount - 1} and the player has {_units.Length}.");
            return;
        }

        var map = orders.Match.Map;
        var units = _units.AsSpan(order.FirstUnit, order.UnitCount);
        var x = Fixed.FromRaw(order.X16 * (Fixed.RawOne / 16));
        var y = Fixed.FromRaw(order.Y16 * (Fixed.RawOne / 16));

        // The far edge of the map is not on it. Its last point is one distance unit inside.
        x = Fixed.FromRaw(Math.Min(x.Raw, (map.Width * Fixed.RawOne) - 1));
        y = Fixed.FromRaw(Math.Min(y.Raw, (map.Height * Fixed.RawOne) - 1));

        orders.Submit(new Command(CommandType.Move, orders.Player, units, x, y));
    }

    private readonly record struct Order(int AtTick, int FirstUnit, int UnitCount, int X16, int Y16);
}
