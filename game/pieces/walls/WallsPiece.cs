namespace Game;

/// <summary>
/// The view piece for walls (18v): the wall in the build menu, laid by pressing at its first
/// tile and dragging to its last, with the tiles that would be laid and their price shown on
/// the way. The tiles of an order that are still to be laid, drawn as a plan. What a selected
/// tile of wall is, and what became of an order that did not go as it was drawn.
/// </summary>
/// <remarks>
/// A tile of wall is a structure, and the piece for structures draws it: joined to the tiles
/// beside it, lower for the damage it has taken (<see cref="WallShapes"/>), unfinished while it
/// builds itself, and under the fog of war as every structure is. So a wall is selected,
/// cancelled, remembered and shown on the minimap by the parts that do so for structures.
/// </remarks>
public sealed class WallsPiece : ViewPiece
{
    public override string Name => "Walls";

    public override void AddTo(MatchView view)
    {
        var structures = view.World.GetChildren().OfType<StructureLayer>().FirstOrDefault()
            ?? throw new InvalidOperationException("Walls are added after the piece that draws structures.");
        var menu = view.Interface.Parts.OfType<BuildMenu>().FirstOrDefault()
            ?? throw new InvalidOperationException("Walls are added after the build menu.");

        view.World.AddLayer(new WallPlanLayer(structures));
        var preview = new WallRunPreview();
        view.World.AddLayer(preview);
        var marks = new WallSkipMarks();
        view.World.AddLayer(marks);
        var inspected = new InspectedWallMark();
        view.World.AddLayer(inspected);

        var tag = new WallPriceTag();
        view.Interface.AddPart(tag);
        var aid = new WallDevelopmentAid();
        view.Interface.AddPart(aid);
        var given = new GivenWallOrders();
        menu.AddTool(new WallTool(preview, tag, given, aid));

        var messages = new WallMessages(given, marks);
        view.Interface.AddPart(messages);
        foreach (var build in view.Interface.Parts.OfType<BuildMessages>())
        {
            build.LeaveTo(messages.IsOfWallOrder);
        }

        view.Interface.AddPart(new WallInspection(structures, inspected));
    }
}
