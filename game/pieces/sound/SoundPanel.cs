using System.Globalization;
using Godot;

namespace Game;

/// <summary>
/// What the Sound panel of the top bar shows: the volume, the button that turns all sound off,
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
        column.AddChild(MuteButton());
        column.AddChild(UiStyle.Text("Both are kept for the next match and the next time the game starts."));
        column.AddChild(new HSeparator());
        column.AddChild(UiStyle.Text("Every sound of a match, to hear by itself. The sound when:"));

        // Three across, so that the page fits a small window with all nine on it.
        var list = new GridContainer { Name = "Sounds", Columns = 3 };
        list.AddThemeConstantOverride("h_separation", 10);
        list.AddThemeConstantOverride("v_separation", 8);
        var waiting = false;
        foreach (var cue in SoundCues.All)
        {
            var waits = sound.Kinds.IsWaiting(cue);
            waiting |= waits;
            var button = UiStyle.FrameButton(waits ? SoundCues.Name(cue) + " *" : SoundCues.Name(cue));
            button.Name = $"Hear{cue}";
            button.Alignment = HorizontalAlignment.Left;
            button.Pressed += () => sound.Audition(cue);
            list.AddChild(button);
        }

        column.AddChild(list);
        if (waiting)
        {
            column.AddChild(UiStyle.Text("* No match of this build can make it yet: the part of the game it answers is not built in."));
        }

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

    // A button that says what it does, and not a box to tick: the box of the engine's own look
    // is dark on this dark page and hard to find.
    private static Button MuteButton()
    {
        var button = UiStyle.FrameButton(MuteWords(SoundSettings.Muted));
        button.Name = "Mute";
        button.ToggleMode = true;
        button.ButtonPressed = SoundSettings.Muted;
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        button.Toggled += muted =>
        {
            SoundSettings.SetMuted(muted);
            button.Text = MuteWords(muted);
        };
        return button;
    }

    private static string MuteWords(bool muted) =>
        muted ? "The sound is off. Click to turn it on" : "The sound is on. Click to turn it off";

    private static string Percent(float volume) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(volume * 100.0)}%");
}
