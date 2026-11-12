using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The bottom panel's line about a selected structure that is still building itself: how far it
/// is and how long it has left. It stands under the line that names the structure.
/// </summary>
public partial class FoundationReadout : InterfacePart
{
    private Label _text = null!;

    /// <summary>What the line says now, or null while it is not shown.</summary>
    public string? Shown => _text.Visible ? _text.Text : null;

    protected override void Attach()
    {
        _text = new Label { Name = "SelectedFoundation", Visible = false };
        _text.AddThemeColorOverride("font_color", ResearchColors.Faint);
        Frame.SelectionArea.AddChild(_text);
        View.Selection.Changed += Refresh;
    }

    public override void AfterTick() => Refresh();

    private void Refresh()
    {
        var match = View.Match;
        var selection = View.Selection;
        var found = EntityId.None;
        if (selection.Count == 1)
        {
            foreach (var entity in selection.Entities)
            {
                found = entity;
            }
        }

        if (match.World.Entities.IsAlive(found)
            && match.Foundations.TryGet(found, out var foundation)
            && StructureWords.DefOf(match, found) is { } structure)
        {
            _text.Text = BuildWords.Progress(structure, foundation.Built);
            _text.Visible = true;
        }
        else
        {
            _text.Visible = false;
        }
    }
}
