namespace Game;

/// <summary>
/// The minimap: the whole map in a corner of the window, with the terrain, the deposits, every
/// unit and the outline of the view, and a way to move the view and to give an order on it.
/// </summary>
public sealed class MinimapPiece : ViewPiece
{
    public override string Name => "Minimap";

    public override void AddTo(MatchView view) => view.Interface.AddPart(new MinimapPart());
}
