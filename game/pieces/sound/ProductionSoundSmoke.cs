using Sim.Core;

namespace Game;

/// <summary>
/// The smoke test's check that the production panel's orders are answered with a sound: two
/// seconds of play in which the local player selects a structure that trains, orders what its
/// first usable button offers, and cancels that order in the queue, which gives back what was
/// paid. Each of the two is an order the panel sends, and each is answered.
/// </summary>
public sealed class ProductionSoundSmoke : PanelSoundSmoke
{
    private EntityId _structure;
    private int _queueBefore;
    private bool _ordered;

    public override string Name => "Sound of the production panel";

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        switch (tick)
        {
            case 0:
                Train(orders);
                break;

            case TakeBackAt when _ordered:
                Cancel(orders);
                break;

            case GoneAt when _ordered:
                var left = orders.Match.Production.Producers.Get(_structure).Count;
                if (left != _queueBefore)
                {
                    orders.Fail($"the queue of structure {_structure.Value} holds {left} orders and held {_queueBefore} before one was placed and cancelled.");
                }

                orders.View.Selection.Clear();
                break;
        }
    }

    private void Train(SmokeOrders orders)
    {
        var match = orders.Match;
        var view = orders.View;
        if (view.Interface.Parts.OfType<ProductionPart>().FirstOrDefault() is not { } panel)
        {
            orders.Fail("the view has no production panel.");
            return;
        }

        _structure = OwnProducer(orders);
        if (_structure == EntityId.None)
        {
            orders.Fail("the local player has no structure that trains.");
            return;
        }

        _queueBefore = match.Production.Producers.Get(_structure).Count;
        view.Selection.Set([_structure]);
        if (panel.Shown != _structure)
        {
            orders.Fail($"the production panel does not show structure {_structure.Value} when it is selected.");
            return;
        }

        var place = -1;
        for (var i = 0; i < panel.Offers.Count && place < 0; i++)
        {
            place = panel.Offers[i].Allowed ? i : -1;
        }

        if (place < 0)
        {
            var why = panel.Offers.Count == 0
                ? "It offers nothing."
                : ProductionWords.ReasonSentence(match, orders.Player, panel.Offers[0].Unit, panel.Offers[0].Check);
            orders.Fail($"structure {_structure.Value} can train nothing now. {why}");
            return;
        }

        var name = ProductionWords.NameOf(match, panel.Offers[place].Unit);
        _ordered = Answered(orders, $"ordered a {name} in the production panel", () => panel.Train(place));
    }

    private void Cancel(SmokeOrders orders)
    {
        var view = orders.View;
        if (view.Interface.Parts.OfType<ProductionPart>().FirstOrDefault() is not { } panel)
        {
            return;
        }

        var queued = orders.Match.Production.Producers.Get(_structure).Count;
        if (queued != _queueBefore + 1 || panel.Shown != _structure || panel.QueueShown != queued)
        {
            orders.Fail(
                $"the queue of structure {_structure.Value} holds {queued} orders and the panel shows {panel.QueueShown}, " +
                $"{TakeBackAt} ticks after the panel sent one to a queue of {_queueBefore}.");
            _ordered = false;
            return;
        }

        _ordered = Answered(orders, "cancelled an order in the production panel", () => panel.Cancel(queued - 1));
    }

    // The local player's structure that trains with the lowest number.
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
}
