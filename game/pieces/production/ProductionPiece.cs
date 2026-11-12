namespace Game;

/// <summary>
/// Production in the view, the second half of 15v: with a structure that trains selected, the
/// bottom panel offers its units and shows its queue, a click with the order button sets its
/// rally point, and the rally point is drawn.
/// </summary>
public sealed class ProductionPiece : ViewPiece
{
    public override string Name => "Production";

    public override void AddTo(MatchView view)
    {
        view.World.AddLayer(new RallyPointLayer());
        view.Interface.AddPart(new ProductionPart());
        view.Orders.Add(RallyOrder.Row());
    }
}
