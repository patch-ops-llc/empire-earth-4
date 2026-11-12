using Sim.Core;
using Sim.Map;
using Sim.Numerics;
using Sim.Production;

namespace Game;

/// <summary>
/// The smoke test's production: 17 seconds of play in which the local player selects the
/// Capitol, fills its queue with workers, sets its rally point, cancels every order but the
/// first and waits for that worker. It checks what the panel and the rally point layer show
/// against the match on the way, and at the end that one worker more stands at the rally point.
/// </summary>
/// <remarks>
/// It orders as many workers as the stockpile, the supply and the queue allow, five at most,
/// so that it holds whatever the parts before it spent. Everything is counted in whole numbers.
/// </remarks>
public sealed class ProductionSmoke : SmokePart
{
    // How far from the Capitol's middle the rally point is put, in tiles, and how near to it the
    // new worker must stand at the end.
    private const int RallyDistance = 7;
    private const int ArriveWithin = 2;

    private const int CheckQueueAt = 20;
    private const int CheckCancelAt = 40;

    private readonly HashSet<EntityId> _unitsBefore = [];
    private EntityId _structure;
    private FixedVector2 _rally;
    private int _ordered;

    public override string Name => "Production";

    public override int Ticks => 340;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        switch (tick)
        {
            case 0:
                Begin(orders);
                break;
            case CheckQueueAt:
                CheckQueue(orders);
                break;
            case CheckCancelAt:
                CheckCancel(orders);
                break;
            default:
                if (tick == Ticks - 1)
                {
                    End(orders);
                }

                break;
        }
    }

    private void Begin(SmokeOrders orders)
    {
        var match = orders.Match;
        var player = orders.Player;
        _structure = OwnProducer(orders);
        if (_structure == EntityId.None)
        {
            orders.Fail("the player has no structure that trains.");
            return;
        }

        if (match.Production.Producers.Get(_structure).Count != 0)
        {
            orders.Fail($"the queue of structure {_structure.Value} is not empty when the part begins.");
            return;
        }

        if (Part(orders) is not { } part)
        {
            orders.Fail("the view has no production panel.");
            return;
        }

        // Selecting it is all the panel needs. It gives no order.
        orders.View.Selection.Set([_structure]);
        if (part.Shown != _structure || part.Offers.Count == 0 || part.Offers[0].Unit != TrainOrder.Worker)
        {
            orders.Fail($"the panel does not offer a worker at structure {_structure.Value}.");
            return;
        }

        var offer = part.Offers[0];
        if (offer.Check != match.Production.CheckTrain(player, _structure, TrainOrder.Worker))
        {
            orders.Fail("the panel and the simulation disagree about whether a worker can be ordered.");
            return;
        }

        _unitsBefore.UnionWith(orders.OwnUnits());
        _ordered = Affordable(orders, offer);
        EntityId[] structure = [_structure];
        for (var i = 0; i < _ordered; i++)
        {
            orders.Submit(new Command(CommandType.Train, player, structure, contentId: TrainOrder.Worker));
        }

        if (!TryFindRallyPoint(orders, out _rally))
        {
            orders.Fail($"no place for a rally point {RallyDistance} tiles from structure {_structure.Value}.");
            return;
        }

        orders.Submit(new Command(CommandType.SetRallyPoint, player, structure, _rally.X, _rally.Y));
    }

    private void CheckQueue(SmokeOrders orders)
    {
        var match = orders.Match;
        var producer = match.Production.Producers.Get(_structure);
        if (producer.Count != _ordered || !producer.RallySet || producer.RallyPoint != _rally)
        {
            orders.Fail($"structure {_structure.Value} has {producer.Count} orders of {_ordered} given, or not the rally point it was given.");
            return;
        }

        if (Part(orders) is not { } part || part.QueueShown != _ordered)
        {
            orders.Fail($"the panel does not show the {_ordered} orders of structure {_structure.Value}.");
            return;
        }

        if (_ordered == Producer.MaxOrders && part.Offers[0].Check != TrainCheck.QueueFull)
        {
            orders.Fail("the panel offers a worker while the queue is full.");
            return;
        }

        // The time a button names is the time the simulation gives the order in production.
        if (_ordered > 0 && producer.BuildTicks > 0 && part.Offers[0].BuildTicks != producer.BuildTicks)
        {
            orders.Fail($"the panel says a worker takes {part.Offers[0].BuildTicks} ticks, and the one in production takes {producer.BuildTicks}.");
            return;
        }

        if (RallyLayer(orders) is not { FlagCount: 1 })
        {
            orders.Fail("the rally point of the selected structure is not drawn.");
            return;
        }

        // The last order first, so that each position is still there when its turn comes.
        EntityId[] structure = [_structure];
        for (var position = _ordered - 1; position >= 1; position--)
        {
            orders.Submit(new Command(CommandType.CancelTrain, orders.Player, structure, contentId: position));
        }
    }

    private void CheckCancel(SmokeOrders orders)
    {
        var left = Math.Min(_ordered, 1);
        var count = orders.Match.Production.Producers.Get(_structure).Count;
        if (count != left || Part(orders) is not { } part || part.QueueShown != left)
        {
            orders.Fail($"structure {_structure.Value} has {count} orders after the cancelling, where {left} should be left, or the panel shows another number.");
        }
    }

    private void End(SmokeOrders orders)
    {
        var match = orders.Match;
        var count = match.Production.Producers.Get(_structure).Count;
        if (count != 0 || Part(orders) is not { QueueShown: 0 })
        {
            orders.Fail($"structure {_structure.Value} still has {count} orders, or the panel shows some.");
            return;
        }

        var trained = Array.FindAll(orders.OwnUnits(), unit => !_unitsBefore.Contains(unit));
        var expected = Math.Min(_ordered, 1);
        if (trained.Length != expected)
        {
            orders.Fail($"{trained.Length} units were trained where {expected} was ordered and kept.");
            return;
        }

        foreach (var unit in trained)
        {
            // In the simulation's own units, as squares, so that no fraction is rounded.
            var at = match.Bodies.Get(unit).Position;
            long dx = at.X.Raw - _rally.X.Raw;
            long dy = at.Y.Raw - _rally.Y.Raw;
            long reach = Fixed.FromInt(ArriveWithin).Raw;
            if ((dx * dx) + (dy * dy) > reach * reach)
            {
                orders.Fail($"the trained unit {unit.Value} does not stand within {ArriveWithin} tiles of the rally point.");
                return;
            }
        }

        orders.View.Selection.Clear();
        if (Part(orders) is not { } part || part.Shown != EntityId.None || RallyLayer(orders) is not { FlagCount: 0 })
        {
            orders.Fail("the panel or the rally point is still shown with nothing selected.");
        }
    }

    // The orders for a worker that can be placed now, one after another: what the queue has room
    // for, the stockpile covers and the supply holds.
    private static int Affordable(SmokeOrders orders, ProductionOffer offer)
    {
        var match = orders.Match;
        var player = orders.Player;
        var count = Producer.MaxOrders;
        while (count > 0
            && (!match.Stockpiles.Covers(player, count * offer.Materials, count * offer.Energy)
                || !match.Supply.HasRoomFor(player, count * offer.Supply)))
        {
            count--;
        }

        return count;
    }

    // The player's structure that trains with the lowest ID: the Capitol, in a match as it starts.
    private static EntityId OwnProducer(SmokeOrders orders)
    {
        var match = orders.Match;
        var producers = match.Production.Producers;
        var found = EntityId.None;
        for (var i = 0; i < producers.Count; i++)
        {
            var entity = producers.EntityAt(i);
            if (ProductionOffer.IsProducerOf(match, orders.Player, entity) && (found == EntityId.None || entity.CompareTo(found) < 0))
            {
                found = entity;
            }
        }

        return found;
    }

    // A tile a unit can stand on and walk to, a fixed way from the structure's middle: the first
    // of eight directions that has one.
    private bool TryFindRallyPoint(SmokeOrders orders, out FixedVector2 point)
    {
        var match = orders.Match;
        var placed = match.Structures.Get(_structure);
        var middleX = placed.X + (placed.Size / 2);
        var middleY = placed.Y + (placed.Size / 2);
        var units = orders.OwnUnits();
        var region = units.Length > 0 ? match.Grid.RegionOf(match.Bodies.Get(units[0]).Position) : 0;

        ReadOnlySpan<(int X, int Y)> directions = [(1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (-1, -1), (1, -1)];
        foreach (var (x, y) in directions)
        {
            var tileX = middleX + (x * RallyDistance);
            var tileY = middleY + (y * RallyDistance);
            if (match.Grid.IsPassable(tileX, tileY) && (region == 0 || match.Grid.RegionOf(tileX, tileY) == region))
            {
                point = MapGrid.TileCenter(tileX, tileY);
                return true;
            }
        }

        point = default;
        return false;
    }

    private static ProductionPart? Part(SmokeOrders orders) =>
        orders.View.Interface.Parts.OfType<ProductionPart>().FirstOrDefault();

    private static RallyPointLayer? RallyLayer(SmokeOrders orders) =>
        orders.View.World.GetChildren().OfType<RallyPointLayer>().FirstOrDefault();
}
