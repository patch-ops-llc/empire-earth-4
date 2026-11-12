using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The order a click with the order button gives to selected structures that train: the place
/// clicked becomes their rally point (design 5.4). From then on a unit that appears walks
/// there, and a worker goes to work when the point lies on a deposit or on a Generator of the
/// player's own.
/// </summary>
public static class RallyOrder
{
    /// <summary>The color of the mark the order leaves, and of the flag that stands at a rally point.</summary>
    public static readonly Color Mark = new(0.62f, 0.86f, 1f);

    /// <summary>
    /// The row for any target. It has an order only for a selection of structures that train,
    /// so the rows for units are asked before it and it is asked when they have none.
    /// </summary>
    public static OrderRow Row() => new()
    {
        Name = "Rally point",
        Targets = PointerTargets.Any,
        Mark = Mark,
        Give = Give,
    };

    private static bool Give(OrderRequest request)
    {
        var view = request.View;
        var producers = Array.FindAll(request.Selected, entity => ProductionOffer.IsProducerOf(view.Match, view.Player, entity));
        if (producers.Length == 0)
        {
            return false;
        }

        var point = request.Target.Point;
        return view.Client.Submit(new Command(CommandType.SetRallyPoint, view.Player, producers, point.X, point.Y));
    }
}
