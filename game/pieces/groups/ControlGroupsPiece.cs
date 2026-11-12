namespace Game;

/// <summary>
/// Selecting without the pointer: the numbered groups, the next idle worker, a look at the
/// selection, and the second click that selects every unit of one kind on screen.
/// </summary>
public sealed class ControlGroupsPiece : ViewPiece
{
    public override string Name => "Control groups";

    public override void AddTo(MatchView view) => view.Interface.AddPart(new ControlGroupsPart());
}
