namespace Game;

/// <summary>
/// Construction in the view, the second part of 15v: the build menu and the placing of a
/// structure, the sites the player's workers are walking to, structures that are building
/// themselves with how far they are, the command that cancels either (design 2.5, 5.4), and
/// the order that sends workers to a finished Generator.
/// </summary>
/// <remarks>
/// It comes after <see cref="StructuresPiece"/>, whose layer draws every structure and gives an
/// unfinished one the look of <see cref="FoundationLook"/>. The production panel is a piece of
/// its own, with production (15d).
/// </remarks>
public sealed class BuildPiece : ViewPiece
{
    public override string Name => "Build menu";

    public override void AddTo(MatchView view)
    {
        var structures = view.World.GetChildren().OfType<StructureLayer>().FirstOrDefault()
            ?? throw new InvalidOperationException("The build menu is added after the piece that draws structures.");
        view.World.AddLayer(new BuildSiteLayer());
        view.World.AddLayer(new FoundationBars(structures));
        var preview = new PlacementPreview();
        view.World.AddLayer(preview);
        view.Interface.AddPart(new BuildMenu(preview));
        view.Interface.AddCommand(CancelBuild.Command());
        view.Interface.AddPart(new FoundationReadout());
        view.Interface.AddPart(new BuildMessages());
        view.Orders.Add(GeneratorWork.Row());
    }
}
