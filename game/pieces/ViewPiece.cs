namespace Game;

/// <summary>
/// One piece of what the player sees and does in a match, as the plan cuts the work: the units
/// and their move orders, the research grid, the resources, the fighting orders and health bars.
/// A piece keeps its files in a folder of its own under <c>game/pieces/</c> and adds everything it
/// has in <see cref="AddTo"/>, so that two pieces built at the same time share no file but the
/// lists that name them.
/// </summary>
/// <remarks>
/// What a piece can add, each as a class in its own folder:
/// <list type="bullet">
/// <item>A <see cref="WorldLayer"/>, for a kind of thing drawn in the world: <c>view.World.AddLayer</c>.</item>
/// <item>
/// An <see cref="InterfacePart"/>, for controls in the top bar or the bottom panel:
/// <c>view.Interface.AddPart</c>. A full-screen panel is added with <c>view.Interface.AddPanel</c>,
/// a command for the selection with <c>view.Interface.AddCommand</c>, and a sentence for the
/// player is shown with <c>view.Interface.Say</c>.
/// </item>
/// <item>A <see cref="PointerPicker"/>, for a kind of thing the pointer can be on: <c>view.UnderPointer.AddPicker</c>.</item>
/// <item>An <see cref="OrderRow"/>, for an order given by a click: <c>view.Orders.Add</c>.</item>
/// </list>
/// A piece holds nothing of a match itself. The list of pieces is made once, and
/// <see cref="AddTo"/> is called for every match the game shows, with a new view each time.
/// A piece shares three files under <c>game/</c> with the others, and adds to each directly
/// under the comment line that names it: <c>ViewPieces.cs</c>, the list in <c>SmokeTest.cs</c>
/// for the orders it adds to the smoke test, and the input map in <c>project.godot</c> for every
/// key or button it reads. It also says what a tester now sees in <c>docs/testers.md</c>.
/// </remarks>
public abstract class ViewPiece
{
    /// <summary>The piece in a word or two, for the log.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Adds what this piece draws and does to the view of a match. Called once for every match,
    /// before its first tick, after the pieces listed before this one.
    /// </summary>
    public abstract void AddTo(MatchView view);
}
