using System.Globalization;
using Godot;
using Sim.Research;

namespace Game;

/// <summary>
/// One research slot in the research panel: a line of text and, while a research is in the slot,
/// a bar, the time left and the button that cancels it.
/// </summary>
public partial class ResearchSlotBox : PanelContainer
{
    private Label _text = null!;
    private ProgressBar _bar = null!;
    private Label _left = null!;
    private Button _cancel = null!;
    private int _track = -1;

    /// <summary>Raised with a track when the player clicks the button that cancels its research.</summary>
    public event Action<int>? CancelChosen;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", ResearchColors.Boxed(ResearchColors.Box, 10f, 6f, 3f));

        var row = UiStyle.Row(10);
        AddChild(row);

        _text = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _text.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(_text);

        _bar = new ProgressBar
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            Step = 0.0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(150f, 12f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        _bar.AddThemeStyleboxOverride("background", ResearchColors.Bar(ResearchColors.Trough));
        row.AddChild(_bar);

        _left = new Label { CustomMinimumSize = new Vector2(92f, 0f) };
        _left.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(_left);

        _cancel = UiStyle.FrameButton("Cancel, full refund");
        _cancel.AddThemeFontSizeOverride("font_size", 13);
        _cancel.Pressed += () =>
        {
            if (_track >= 0)
            {
                CancelChosen?.Invoke(_track);
            }
        };
        row.AddChild(_cancel);
    }

    /// <summary>Shows the slot as the match has it.</summary>
    /// <param name="reading">The player's research.</param>
    /// <param name="slot">The slot.</param>
    /// <param name="alone">Whether it is the only slot the player has, which leaves room to say more.</param>
    /// <param name="cancelling">The track whose research the player has just cancelled, or -1.</param>
    public void Present(ResearchReading reading, ResearchSlot slot, bool alone, int cancelling)
    {
        Visible = slot.IsOpen;
        _bar.Visible = slot.IsBusy;
        _left.Visible = slot.IsBusy;
        _cancel.Visible = slot.IsBusy;
        if (!slot.IsBusy)
        {
            _track = -1;
            _text.Text = alone ? "Your research slot is free. Click a bright cell to start a research." : "Slot free";
            _text.AddThemeColorOverride("font_color", ResearchColors.Faint);
            return;
        }

        var data = reading.Data;
        var track = data.Tracks[slot.Track];
        if (_track != slot.Track)
        {
            _track = slot.Track;
            var color = TrackColors.Of(data, slot.Track);
            _text.AddThemeColorOverride("font_color", color);
            _bar.AddThemeStyleboxOverride("fill", ResearchColors.Bar(color));
        }

        _text.Text = string.Create(CultureInfo.InvariantCulture, $"Researching {track.Name} {slot.Tier}: {track.TierNames[slot.Tier - 1]}");
        _bar.Value = slot.Ticks > 0 ? 1.0 - ((double)slot.TicksLeft / slot.Ticks) : 0.0;
        _left.Text = $"{ResearchWords.Duration(slot.TicksLeft)} left";
        _cancel.Disabled = cancelling == slot.Track;
    }
}
