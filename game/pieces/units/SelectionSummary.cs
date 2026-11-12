using System.Globalization;
using Godot;

namespace Game;

/// <summary>
/// The bottom panel's line about the selection: how many units are selected. Only what can walk
/// is counted, so a selected structure is left to the line of the piece that draws structures.
/// </summary>
public partial class SelectionSummary : InterfacePart
{
    private Label _text = null!;

    protected override void Attach()
    {
        _text = new Label { Name = "SelectionSummary", Visible = false };
        Frame.SelectionArea.AddChild(_text);
        View.Selection.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        var count = 0;
        foreach (var entity in View.Selection.Entities)
        {
            count += View.Match.Movers.Contains(entity) ? 1 : 0;
        }

        _text.Visible = count > 0;
        _text.Text = count == 1
            ? "1 unit selected"
            : string.Create(CultureInfo.InvariantCulture, $"{count} units selected");
    }
}
