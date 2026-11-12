using Sim.Core;
using Sim.Economy;
using Sim.Structures;

namespace Game;

/// <summary>
/// The smoke test's structures: six seconds of play in which the local player's units are sent
/// into the middle of the nearest structure. It checks what the view shows of structures
/// against the rules: every structure stands on tiles nothing else can have, the tiles of the
/// local player's own are inside that player's territory, and no unit ends up standing on a
/// structure.
/// </summary>
/// <remarks>
/// The view itself is tested by running: the structure layer counts what it draws against the
/// match after every tick, and an error from it fails the test.
/// </remarks>
public sealed class StructuresSmoke : SmokePart
{
    public override string Name => "Structures";

    public override int Ticks => 120;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (tick == 0)
        {
            Begin(orders);
        }
        else if (tick == Ticks - 1)
        {
            End(orders);
        }
    }

    private static void Begin(SmokeOrders orders)
    {
        var match = orders.Match;
        var structures = match.Structures;
        var own = 0;
        for (var i = 0; i < structures.Count; i++)
        {
            var entity = structures.EntityAt(i);
            var placed = structures.ValueAt(i);
            if (match.Data.StructureOf(placed.Type) is not { } structure || !match.Bodies.TryGet(entity, out var body))
            {
                orders.Fail($"structure {entity.Value} is not one of the roster, or stands nowhere.");
                return;
            }

            if (placed.Size != structure.Footprint || match.HasRoomFor(structure, placed.X, placed.Y))
            {
                orders.Fail($"the tiles of structure {entity.Value} are not blocked by it.");
                return;
            }

            if (body.Owner != orders.Player)
            {
                continue;
            }

            own++;
            if (!Territory.Holds(match.Territories, match.World.Entities, orders.Player, placed.X, placed.Y, placed.Size))
            {
                orders.Fail($"structure {entity.Value} of the player stands outside the player's territory.");
                return;
            }
        }

        if (own == 0)
        {
            orders.Fail("the player has no structure.");
            return;
        }

        var units = orders.OwnUnits();
        if (units.Length == 0)
        {
            orders.Fail("the player has no unit to send to a structure.");
            return;
        }

        // Nobody can stand there, so they go to the nearest tile they can reach, which is beside it.
        var target = match.Bodies.Get(Nearest(orders, units[0])).Position;
        orders.Submit(new Command(CommandType.Move, orders.Player, units, target.X, target.Y));
    }

    private static void End(SmokeOrders orders)
    {
        var match = orders.Match;
        var movers = match.Movers;
        var structures = match.Structures;
        for (var i = 0; i < movers.Count; i++)
        {
            var unit = movers.EntityAt(i);
            if (!match.Bodies.TryGet(unit, out var body))
            {
                continue;
            }

            var x = body.Position.X.FloorToInt();
            var y = body.Position.Y.FloorToInt();
            for (var j = 0; j < structures.Count; j++)
            {
                if (structures.ValueAt(j).Tiles.Covers(x, y))
                {
                    orders.Fail($"unit {unit.Value} stands on structure {structures.EntityAt(j).Value}, at tile ({x}, {y}).");
                    return;
                }
            }
        }
    }

    // The structure whose middle is nearest to a unit, of any player's. Whole numbers only: the
    // distances are compared as squares of the simulation's own units.
    private static EntityId Nearest(SmokeOrders orders, EntityId unit)
    {
        var match = orders.Match;
        var from = match.Bodies.Get(unit).Position;
        var structures = match.Structures;
        var nearest = EntityId.None;
        var nearestDistance = long.MaxValue;
        for (var i = 0; i < structures.Count; i++)
        {
            var entity = structures.EntityAt(i);
            var to = match.Bodies.Get(entity).Position;
            long dx = to.X.Raw - from.X.Raw;
            long dy = to.Y.Raw - from.Y.Raw;
            var distance = (dx * dx) + (dy * dy);
            if (distance < nearestDistance)
            {
                nearest = entity;
                nearestDistance = distance;
            }
        }

        return nearest;
    }
}
