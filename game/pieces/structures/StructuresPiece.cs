namespace Game;

/// <summary>
/// Structures and territory in the view, the first part of 15v: every structure is drawn on the
/// tiles it blocks, the ground each player holds is shown tile by tile, and a structure of the
/// player's own can be selected, which names it and its hit points in the bottom panel.
/// </summary>
public sealed class StructuresPiece : ViewPiece
{
    public override string Name => "Structures";

    public override void AddTo(MatchView view)
    {
        // The territory first, so that it lies under everything drawn after it.
        view.World.AddLayer(new TerritoryLayer());
        view.World.AddLayer(new StructureLayer());

        view.UnderPointer.AddPicker(new StructurePicker());
        view.Interface.AddPart(new StructureReadout());
        StructureSelection.KeepApart(view);

        // The build menu and its placement preview come here with construction (15c), and the
        // production panel with production (15d).
    }
}
