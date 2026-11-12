namespace Game;

/// <summary>
/// The rule for structures in the selection: structures and units are never selected together.
/// A click on a structure selects it, and adding to a selection of structures adds, but as
/// soon as a selection holds a unit its structures leave it.
/// </summary>
/// <remarks>
/// Orders by a click are for units, and what a structure can be told comes with the build menu
/// and the production panel, which speak to structures alone. The rule is kept on the
/// selection itself, so it holds however the selection was made: a click, a box, a group.
/// </remarks>
public static class StructureSelection
{
    /// <summary>Keeps the rule for the selection of this view from now on.</summary>
    public static void KeepApart(MatchView view)
    {
        var selection = view.Selection;
        var match = view.Match;
        selection.Changed += () =>
        {
            var structures = 0;
            foreach (var entity in selection.Entities)
            {
                structures += match.Structures.Contains(entity) ? 1 : 0;
            }

            // Removing them raises the event again, and by then there is nothing left to do.
            if (structures > 0 && structures < selection.Count)
            {
                selection.RemoveWhere(match.Structures.Contains);
            }
        };
    }
}
