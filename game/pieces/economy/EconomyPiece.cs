namespace Game;

/// <summary>
/// The economy in the view (design 4.1): what the player has in the top bar, the deposits where
/// the map has them, and the order that sends workers to work at one.
/// </summary>
/// <remarks>
/// A Generator is a structure, and the piece that draws structures draws it and adds the row
/// that sends workers to it.
/// </remarks>
public sealed class EconomyPiece : ViewPiece
{
    public override string Name => "Economy";

    public override void AddTo(MatchView view)
    {
        var deposits = new DepositLayer();
        view.World.AddLayer(deposits);
        view.UnderPointer.AddPicker(new DepositPicker());
        view.Interface.AddPart(new ResourceReadout());
        view.Orders.Add(new GatherOrder(deposits).Row());
    }
}
