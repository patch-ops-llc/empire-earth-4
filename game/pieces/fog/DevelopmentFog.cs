using Godot;

namespace Game;

/// <summary>
/// A development aid, not part of the game: a switch that lifts the fog of war from the screen,
/// by a key and by a button in a panel marked as an aid. A tester who plays alone makes units
/// for the other seat and has to see them, and with the fog they are out of sight.
/// </summary>
/// <remarks>
/// <para>
/// The switch is <see cref="PlayerSight.FogOff"/>, and it changes the screen of this machine and
/// nothing else. The simulation has no such switch: with the fog off the units still pick as
/// targets only what their player sees, and an attack order on a unit the player does not see
/// is not given. The panel says so.
/// </para>
/// <para>
/// It works in a match played alone. In a shared match it is not offered, so that each player
/// there is shown their own picture and no more, which is what the fog is for.
/// </para>
/// <para>
/// Like the aid that makes units, it is there only while the rules have <c>debug.spawn</c> on,
/// the one switch for development in <c>game/data/rules.jsonc</c>. With the switch off this
/// part shows nothing and reads no key, so the aid leaves the game when the other does.
/// </para>
/// </remarks>
public partial class DevelopmentFog : InterfacePart
{
    /// <summary>Turns the fog off and on again, alone. Named in the input map of project.godot.</summary>
    public static readonly StringName ToggleAction = "fog_dev_toggle";

    private Button? _button;
    private Label? _state;

    /// <summary>True when the rules allow development aids in this match.</summary>
    public bool IsAllowed => View.Match.Data.Rules.Debug.Spawn;

    /// <summary>True when the switch is offered in this match: an allowed one that is played alone.</summary>
    public bool IsOffered => IsAllowed && View.Run is LocalRun;

    protected override void Attach()
    {
        if (IsAllowed)
        {
            Build();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsAllowed || !@event.IsActionPressed(ToggleAction))
        {
            return;
        }

        if (IsOffered)
        {
            Set(!View.Sight.FogOff);
        }
        else
        {
            Frame.Say("Development fog switch: only in a match played alone. In a shared match each player sees their own picture.");
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>Lifts the fog from the screen, or lays it back. Nothing happens in a match where the switch is not offered.</summary>
    public void Set(bool fogOff)
    {
        if (!IsOffered || View.Sight.FogOff == fogOff)
        {
            return;
        }

        View.Sight.FogOff = fogOff;
        ShowState();
        Frame.Say(fogOff
            ? "Development: the fog is off on this screen. The units still see only what they see."
            : "Development: the fog is on again.");
    }

    private void Build()
    {
        if (!IsOffered)
        {
            var shared = Frame.AddDevelopmentAid(
                "Development aid: fog of war",
                "Not part of the game. In a shared match the fog stays on: each player sees their own picture.");
            shared.AddChild(Small("In a shared match the fog stays on."));
            return;
        }

        var column = Frame.AddDevelopmentAid(
            "Development aid: fog of war",
            "Not part of the game. Shows the whole map and every unit on this screen. The units still see only what they see, and attack only that.");
        _state = Small(string.Empty);
        column.AddChild(_state);
        _button = UiStyle.FrameButton(string.Empty);
        _button.AddThemeFontSizeOverride("font_size", 13);
        _button.Pressed += () => Set(!View.Sight.FogOff);
        column.AddChild(_button);
        ShowState();
    }

    private void ShowState()
    {
        if (_button is null || _state is null)
        {
            return;
        }

        var off = View.Sight.FogOff;
        var key = KeyOf(ToggleAction);
        _state.Text = off ? "The fog is OFF on this screen." : "The fog is on, as in the game.";
        _state.AddThemeColorOverride("font_color", off ? UiStyle.Warning : Colors.White);
        _button.Text = off ? $"Lay the fog back ({key})" : $"Lift the fog ({key})";
    }

    // A line of small print, which wraps at the width of the aid.
    private static Label Small(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }

    // The key an action is given with, as the player knows it.
    private static string KeyOf(StringName action)
    {
        foreach (var input in InputMap.ActionGetEvents(action))
        {
            if (input is InputEventKey key)
            {
                return OS.GetKeycodeString(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode);
            }
        }

        return action.ToString();
    }
}
