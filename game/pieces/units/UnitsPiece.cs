using Sim.Core;

namespace Game;

/// <summary>
/// Units in the view, as M9 built them: a unit can be pointed at, the selection is counted in the
/// bottom panel, and a click with the order button sends the selected units to the point clicked.
/// </summary>
public sealed class UnitsPiece : ViewPiece
{
    public override string Name => "Units";

    public override void AddTo(MatchView view)
    {
        view.UnderPointer.AddPicker(new UnitPicker());
        view.Interface.AddPart(new SelectionSummary());

        // The move order is the row for any target, so every row that names a kind of target is
        // asked before it. A move order never fights and never works: it only walks (design 2.4).
        view.Orders.Add(new OrderRow
        {
            Name = "Move",
            Targets = PointerTargets.Any,
            Give = Move,
        });
    }

    private static bool Move(OrderRequest request)
    {
        var client = request.View.Client;
        var target = request.Target.Point;
        return client.Submit(new Command(CommandType.Move, client.Player, request.Selected, target.X, target.Y));
    }
}
