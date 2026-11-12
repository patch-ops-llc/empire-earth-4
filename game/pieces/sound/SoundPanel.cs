using System.Globalization;
using Godot;

namespace Game;

/// <summary>
/// What the Sound panel of the top bar shows: the volume, the switch that turns all sound off,
/// and a button for each sound of a match, to hear it by itself.
/// </summary>
/// <remarks>
/// The menu the game opens on has no page for settings, so the two settings are here, in a
/// panel of the frame round a match. They are the game's all the same
/// (<see cref="SoundSettings"/>): a later match and a later start of the game find them as they
/// were left.
/// </remarks>
public static class SoundPanel
{
    /// <summary>Builds the panel's content for a match.</summary>
    /// <param name="sound">The sound part of the match, which plays what a button asks for.</param>
    public static Control Build(SoundPart sound)
    {
        var column = UiStyle.Column(12);
        column.Name = "SoundPanel";
        column.AddChild(VolumeRow(sound));
        column.AddChild(MuteBox());
        column.AddChild(UiStyle.Text("Both are kept for the next match and the next time the game starts."));
        column.AddChild(new HSeparator());
        column.AddChild(UiStyle.Text("Every sound of a match, to hear by itself. The sound when:"));

        var list = new GridContainer { Name = "Sounds", Columns = 2 };
        list.AddThemeConstantOverride("h_separation", 16);
        list.AddThemeConstantOverride("v_separation", 6);
        foreach (var cue in SoundCues.All)
        {
            var button = UiStyle.FrameButton(SoundCues.Name(cue));
            button.Name = $"Hear{cue}";
            button.Alignment = HorizontalAlignment.Left;
            button.Pressed += () => sound.Audition(cue);
            list.AddChild(button);
            list.AddChild(new Label
            {
                Text = sound.Kinds.IsWaiting(cue) ? "No match of this build can make it yet: what it answers is not built in." : string.Empty,
            });
        }

        column.AddChild(list);
        return column;
    }

    private static HBoxContainer VolumeRow(SoundPart sound)
    {
        var row = UiStyle.Row();
        row.AddChild(new Label { Text = "Volume" });

        var percent = new Label { Name = "VolumePercent", Text = Percent(SoundSettings.Volume), CustomMinimumSize = new Vector2(52f, 0f) };
        var slider = new HSlider
        {
            Name = "Volume",
            MinValue = 0.0,
            MaxValue = 100.0,
            Step = 5.0,
            Value = Math.Round(SoundSettings.Volume * 100.0),
            CustomMinimumSize = new Vector2(280f, 0f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.None,
        };
        slider.ValueChanged += value =>
        {
            SoundSettings.SetVolume((float)(value / 100.0));
            percent.Text = Percent(SoundSettings.Volume);
        };

        // The player hears what was chosen when the slider is let go.
        slider.DragEnded += _ => sound.Audition(SoundCue.OrderGiven);
        row.AddChild(slider);
        row.AddChild(percent);
        return row;
    }

    private static CheckBox MuteBox()
    {
        var box = new CheckBox
        {
            Name = "Mute",
            Text = "Mute: no sound at all",
            ButtonPressed = SoundSettings.Muted,
            FocusMode = Control.FocusModeEnum.None,
        };
        box.Toggled += SoundSettings.SetMuted;
        return box;
    }

    private static string Percent(float volume) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(volume * 100.0)}%");
}
