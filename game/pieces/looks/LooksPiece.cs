namespace Game;

/// <summary>
/// What each chassis looks like on the map (design 3.2, "which chassis you field"): a shape for
/// every chassis the presentation data names, by which a player reads its line and its epoch
/// without clicking it.
/// </summary>
public sealed class LooksPiece : ViewPiece
{
    public override string Name => "Chassis looks";

    public override void AddTo(MatchView view) => view.World.AddLayer(new ChassisLooks());
}
