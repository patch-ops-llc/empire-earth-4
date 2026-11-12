using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The bottom panel's line about one selected unit: what it is and its hit points. With several
/// units selected the bars over them say it, and the line is not shown. A unit is what can walk:
/// a selected structure is left to the line of the piece that draws structures.
/// </summary>
public partial class SelectedUnitReadout : InterfacePart
{
    private Label _text = null!;

    protected override void Attach()
    {
        _text = new Label { Name = "SelectedUnit", Visible = false };
        Frame.SelectionArea.AddChild(_text);
        View.Selection.Changed += Refresh;
    }

    public override void AfterTick() => Refresh();

    private void Refresh()
    {
        var selection = View.Selection;
        var entity = selection.Count == 1 ? selection.Entities.First() : EntityId.None;
        if (entity == EntityId.None || !View.Match.Movers.Contains(entity) || !View.Match.Health.TryGet(entity, out var health))
        {
            _text.Visible = false;
            return;
        }

        _text.Text = $"{CombatWords.NameOf(View, entity)}: {CombatWords.HitPoints(health.HitPoints)} of {CombatWords.HitPoints(health.Maximum)} hit points";
        _text.Visible = true;
    }
}
