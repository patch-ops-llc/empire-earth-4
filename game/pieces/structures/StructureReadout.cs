using System.Globalization;
using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The bottom panel's line about a selected structure: its name and its hit points, or how many
/// structures are selected when there are several.
/// </summary>
/// <remarks>
/// It is one of the short lines of the frame's selection area, which give way to a part that
/// describes the selection in full. Such a part finds the same words in
/// <see cref="StructureWords"/>.
/// </remarks>
public partial class StructureReadout : InterfacePart
{
    private Label _text = null!;

    protected override void Attach()
    {
        _text = new Label { Name = "SelectedStructure", Visible = false };
        Frame.SelectionArea.AddChild(_text);
        View.Selection.Changed += Refresh;
    }

    // The hit points of a structure change while it is selected.
    public override void AfterTick() => Refresh();

    private void Refresh()
    {
        var match = View.Match;
        var count = 0;
        var first = EntityId.None;
        foreach (var entity in View.Selection.Entities)
        {
            if (StructureWords.IsStructure(match, entity))
            {
                first = count == 0 ? entity : first;
                count++;
            }
        }

        _text.Visible = count > 0;
        if (count == 1)
        {
            _text.Text = StructureWords.Describe(match, first);
        }
        else if (count > 1)
        {
            _text.Text = string.Create(CultureInfo.InvariantCulture, $"{count} structures selected");
        }
    }
}
