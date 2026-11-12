using Godot;
using Sim.Construction;
using Sim.Core;

namespace Game;

/// <summary>
/// An entry of the build menu for something that is not placed with one click, as a structure
/// is: a run of wall tiles is drawn from a first tile to a last. The menu lists the entry,
/// gives it its key and its place in the panel, and hands it the pointer while it is chosen.
/// </summary>
/// <remarks>
/// <para>
/// The menu keeps the buttons the player already has in hand: the select button is pressed and
/// let go to place, the key that adds to a selection places more than one, and the order button
/// and the key that closes a panel go back. A tool says what each of them does while it is
/// chosen, and what the menu says about it.
/// </para>
/// <para>
/// A tool is added with <see cref="BuildMenu.AddTool"/> by the piece it belongs to, after the
/// menu. Its entry comes after the structures, in the order the tools were added.
/// </para>
/// </remarks>
public abstract class BuildTool
{
    /// <summary>
    /// What the tool is in one word of small letters, as the ID of a structure's file is. Its
    /// action in the input map is "build_" and this.
    /// </summary>
    public abstract string Id { get; }

    /// <summary>Its name on the entry and in a sentence.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Whether the player may use the tool at all now: locked, beyond what the player has, or
    /// <see cref="BuildProblem.None"/>. The entry is dark for anything else.
    /// </summary>
    public abstract BuildProblem Problem(MatchView view);

    /// <summary>What the second line of the entry says: the price, or what is in the way.</summary>
    public abstract string EntryLine(MatchView view, BuildProblem problem);

    /// <summary>Why the menu does not offer the tool now, as a sentence, or null when it does.</summary>
    public abstract string? WhyNot(MatchView view, BuildProblem problem);

    /// <summary>What the tool places, in a sentence for the line under the entries.</summary>
    public abstract string Detail(MatchView view);

    /// <summary>What the menu's line of advice says now, and whether it is a warning.</summary>
    public abstract (string Text, bool Warn) Hint(MatchView view);

    /// <summary>
    /// Called in every frame while the tool is chosen, with the point of the ground under the
    /// pointer as map x and y in tiles, or null while the pointer is over the interface or off
    /// the ground, and the selected workers of the player, in ID order.
    /// </summary>
    public abstract void Point(MatchView view, Vector2? ground, IReadOnlyList<EntityId> workers);

    /// <summary>The select button went down over the ground.</summary>
    public abstract void Press(MatchView view);

    /// <summary>
    /// The select button was let go. Returns true when an order was sent, after which the menu
    /// closes unless the player is placing more.
    /// </summary>
    public abstract bool Release(MatchView view, IReadOnlyList<EntityId> workers);

    /// <summary>
    /// The order button or the key that closes a panel. Returns true when the tool took a step
    /// back by itself, as from a run that was begun to none, and false when there was none to
    /// take, and the menu goes back to its entries.
    /// </summary>
    public abstract bool Back(MatchView view);

    /// <summary>True while the select button is held for the tool, between a press it took and its release.</summary>
    public abstract bool IsHeld { get; }

    /// <summary>The menu has left the tool: it shows nothing more.</summary>
    public abstract void Leave(MatchView view);
}
