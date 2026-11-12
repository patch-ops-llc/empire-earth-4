using Godot;
using Sim.Core;
using Sim.Numerics;

namespace Game;

/// <summary>
/// The smoke test's order from the minimap: ten seconds of play in which the local player's
/// units are sent to the point of the map that a point of the minimap stands for.
/// </summary>
/// <remarks>
/// Nothing presses a button in the smoke test. The part turns the point of the minimap into a
/// point of the map with <see cref="MinimapGeometry"/>, as a press on the minimap does, and gives
/// the move order a press of the order button there gives. So every exported build must turn the
/// point into the same target, or its state hash differs from the others'.
/// </remarks>
public sealed class MinimapSmoke : SmokePart
{
    // The point of the minimap, as shares of its width and its height: a quarter of the way
    // across and halfway down.
    private static readonly Vector2 Share = new(0.25f, 0.5f);

    private EntityId[] _units = [];
    private FixedVector2 _target;
    private long _nearestAtStart;

    public override string Name => "Minimap order";

    public override int Ticks => 200;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (tick == 0)
        {
            var map = orders.Match.Map;
            var geometry = MinimapGeometry.Fit(map.Width, map.Height, MinimapPart.LongestSide);
            _target = geometry.ToMapPoint(geometry.Size * Share);
            _units = orders.OwnUnits();
            if (_units.Length == 0)
            {
                orders.Fail("the player has no unit to send to the point of the minimap.");
                return;
            }

            _nearestAtStart = NearestSquared(orders);
            orders.Submit(new Command(CommandType.Move, orders.Player, _units, _target.X, _target.Y));
        }
        else if (tick == Ticks - 1 && _nearestAtStart > 0 && NearestSquared(orders) >= _nearestAtStart)
        {
            orders.Fail("no unit came nearer to the point of the map that the minimap's point stands for.");
        }
    }

    // The square of the distance from the target to the nearest of the units, in distance units.
    private long NearestSquared(SmokeOrders orders)
    {
        var nearest = long.MaxValue;
        foreach (var unit in _units)
        {
            if (orders.Match.Bodies.TryGet(unit, out var body))
            {
                var x = (long)body.Position.X.Raw - _target.X.Raw;
                var y = (long)body.Position.Y.Raw - _target.Y.Raw;
                nearest = Math.Min(nearest, (x * x) + (y * y));
            }
        }

        return nearest;
    }
}
