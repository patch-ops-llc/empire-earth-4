using Godot;
using Sim.Research;

namespace Game;

/// <summary>
/// The research slots the player has (design 1.3), side by side: for each, the research in it with
/// how far it is and a button that cancels it, or that it is free.
/// </summary>
public partial class ResearchSlotsStrip : HBoxContainer
{
    private readonly ResearchSlotBox[] _boxes = new ResearchSlotBox[ResearchState.SlotsPerPlayer];
    private Label _none = null!;

    /// <summary>Raised with a track when the player clicks the button that cancels its research.</summary>
    public event Action<int>? CancelChosen;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);

        _none = new Label { Text = "You have no research slot.", Visible = false, VerticalAlignment = VerticalAlignment.Center };
        _none.AddThemeColorOverride("font_color", UiStyle.Warning);
        AddChild(_none);

        for (var number = 0; number < _boxes.Length; number++)
        {
            var box = new ResearchSlotBox { SizeFlagsHorizontal = SizeFlags.ExpandFill, Visible = false };
            box.CancelChosen += track => CancelChosen?.Invoke(track);
            AddChild(box);
            _boxes[number] = box;
        }
    }

    /// <summary>Shows the slots as the match stands.</summary>
    /// <param name="reading">The player's research.</param>
    /// <param name="cancelling">The track whose research the player has just cancelled, or -1.</param>
    public void Present(ResearchReading reading, int cancelling)
    {
        if (!IsNodeReady())
        {
            return;
        }

        var open = reading.OpenSlots();
        _none.Visible = open == 0;
        for (var number = 0; number < _boxes.Length; number++)
        {
            _boxes[number].Present(reading, reading.Slot(number), alone: open == 1, cancelling);
        }
    }
}
