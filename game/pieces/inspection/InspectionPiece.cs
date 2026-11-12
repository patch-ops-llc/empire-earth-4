namespace Game;

/// <summary>
/// Readability (design 3.2, pulled forward from the plan's M20): the inspection panel that says
/// what the selected units are, the accent badges over units in the world, and the announcement
/// of a new epoch. It gives no order.
/// </summary>
public sealed class InspectionPiece : ViewPiece
{
    public override string Name => "Inspection";

    public override void AddTo(MatchView view)
    {
        view.World.AddLayer(new AccentBadges());
        var mark = new InspectedMark();
        view.World.AddLayer(mark);
        view.Interface.AddPart(new InspectionPart(mark));
        view.Interface.AddPart(new EpochAnnouncement());
    }
}
