using System.Globalization;
using Godot;
using Sim.Core;
using Sim.Research;

namespace Game;

/// <summary>
/// A development aid, not part of the game: a small panel and two keys that raise one track of
/// the player's, or of the other seat's alone, by a tier at a press, at once and for nothing, so
/// that accents and their perks can be seen without minutes of research. It sends the
/// development tier (<see cref="CommandType.DebugSetTier"/>), which the simulation carries out
/// only while the rules have <c>debug.spawn</c> on. With the switch off this part shows nothing
/// and reads no key.
/// </summary>
/// <remarks>
/// <para>
/// It goes the way the aid that makes units goes (<see cref="DevelopmentSpawn"/>): the local
/// player's command through the player's own side of the session, and the other seat's through
/// the host of a match played alone. The simulation does what a finished research does, so the
/// units on the map get the accent and the player is told as of any research.
/// </para>
/// <para>
/// Each button has a key, named on it. The track is the one the list shows, so the list is
/// set with the mouse and the keys then raise that track. The panel stands at the right edge
/// under the top bar, where nothing else of the interface is: the column of aids at the left
/// edge is full in a window 720 high, and a fourth box there would reach the bottom panel.
/// </para>
/// </remarks>
public partial class DevelopmentTier : InterfacePart
{
    /// <summary>Raises the listed track of the local player's by a tier. Named in the input map of project.godot.</summary>
    public static readonly StringName RaiseOwnAction = "perks_dev_tier_own";

    /// <summary>Raises the listed track of the other seat's by a tier, alone. Named in the input map of project.godot.</summary>
    public static readonly StringName RaiseOtherAction = "perks_dev_tier_other";

    // Under the top bar and the two lines kept for messages, and inside the right edge.
    private const float Top = 108f;
    private const float Right = 8f;

    private PanelContainer _panel = null!;
    private OptionButton _track = null!;
    private bool _on;

    /// <summary>True when this match has a seat the aid can raise tracks for beside the local player's: alone, with a second seat.</summary>
    public bool CanRaiseForOther => View.Run is LocalRun && View.Match.Players.Count > 1;

    /// <summary>The seat the second button raises tracks for.</summary>
    public int OtherSeat => (View.Player + 1) % View.Match.Players.Count;

    protected override void Attach()
    {
        _on = View.Match.Data.Rules.Debug.Spawn;
        if (_on)
        {
            Build();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_on)
        {
            return;
        }

        if (@event.IsActionPressed(RaiseOwnAction))
        {
            Press(RaiseOwnAction);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(RaiseOtherAction))
        {
            Press(RaiseOtherAction);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Sets the list to a track, as a click on its row does.</summary>
    public void Choose(int track)
    {
        if (_on)
        {
            _track.Select(_track.GetItemIndex(track));
        }
    }

    /// <summary>
    /// Does what one of the aid's two keys does: raises the listed track of the local player's
    /// (<see cref="RaiseOwnAction"/>) or of the other seat's (<see cref="RaiseOtherAction"/>) by
    /// a tier. Returns true when a command was sent.
    /// </summary>
    public bool Press(StringName action)
    {
        if (!_on)
        {
            return false;
        }

        if (action == RaiseOwnAction)
        {
            return RaiseChosen(View.Player);
        }

        if (action != RaiseOtherAction)
        {
            return false;
        }

        if (!CanRaiseForOther)
        {
            Frame.Say("Development tier: in a shared match each player raises their own tracks.");
            return false;
        }

        return RaiseChosen(OtherSeat);
    }

    public override void EveryFrame(double delta)
    {
        // A full-screen panel has the window to itself, as with the other aids.
        if (_on)
        {
            _panel.Visible = !Frame.IsPanelOpen;
        }
    }

    /// <summary>
    /// Raises a track of a seat by one tier. Returns false when the track is as high as the
    /// match lets a track go, or the session took no command.
    /// </summary>
    public bool Raise(int player, int track)
    {
        var research = View.Match.Research;
        var tier = research.State.Tier(player, track);
        if (tier >= research.EpochCap)
        {
            return false;
        }

        var command = DebugSetTierHandler.CommandFor(player, track, tier + 1);
        return command.Player == View.Player
            ? View.Client.Submit(command)
            : View.Run is LocalRun alone && alone.Session.Host.Submit(command);
    }

    private bool RaiseChosen(int player)
    {
        if (View.Client.Phase != Net.SessionPhase.Running)
        {
            return false;
        }

        var track = _track.GetSelectedId();
        var name = View.Match.Data.Tracks[track].Name;
        var sent = Raise(player, track);
        if (!sent)
        {
            var tier = View.Match.Research.State.Tier(player, track);
            Frame.Say(string.Create(CultureInfo.InvariantCulture, $"Development tier: {name} is at tier {tier}, as high as a track goes in this match."));
        }
        else if (player != View.Player)
        {
            // The player's own research is announced as any research is. The other seat's is its own to know.
            var tier = View.Match.Research.State.Tier(player, track) + 1;
            Frame.Say(string.Create(CultureInfo.InvariantCulture, $"Development tier: the other player's {name} is now tier {tier}."));
        }

        if (OS.IsStdOutVerbose())
        {
            GD.Print(string.Create(
                CultureInfo.InvariantCulture,
                $"Development tier: {name} for player {player} at tick {View.Match.World.Tick}{(sent ? string.Empty : ", not sent")}."));
        }

        return sent;
    }

    private void Build()
    {
        var data = View.Match.Data;
        _panel = new PanelContainer { Name = "DevelopmentTier" };
        var box = UiStyle.PanelStyle(10f);
        box.BorderColor = UiStyle.Warning;
        box.SetBorderWidthAll(1);
        _panel.AddThemeStyleboxOverride("panel", box);
        Frame.AddChild(_panel);
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _panel.GrowHorizontal = Control.GrowDirection.Begin;
        _panel.OffsetTop = Top;
        _panel.OffsetBottom = Top;
        _panel.OffsetLeft = -Right;
        _panel.OffsetRight = -Right;

        var column = UiStyle.Column(6);
        _panel.AddChild(column);

        var title = new Label { Text = "Development aid: raise a track" };
        title.AddThemeColorOverride("font_color", UiStyle.Warning);
        column.AddChild(title);
        _panel.TooltipText =
            "Not part of the game. The track goes up one tier at once, free of charge, as if its research had finished.\n" +
            "A track one tier ahead of a unit's epoch gives the unit that track's accent at level I, two tiers ahead at level II.";

        // A list and buttons that never take the keyboard, so that the keys keep moving the camera after a click.
        _track = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _track.AddThemeFontSizeOverride("font_size", 13);
        _track.GetPopup().AddThemeFontSizeOverride("font_size", 13);
        for (var track = 0; track < data.Tracks.Count; track++)
        {
            _track.AddItem(data.Tracks[track].Name, track);
        }

        var row = UiStyle.Row(8);
        row.AddChild(_track);
        row.AddChild(RaiseButton($"Yours +1 ({KeyOf(RaiseOwnAction)})", () => Press(RaiseOwnAction)));
        if (CanRaiseForOther)
        {
            row.AddChild(RaiseButton($"The other's +1 ({KeyOf(RaiseOtherAction)})", () => Press(RaiseOtherAction)));
        }

        column.AddChild(row);
    }

    private static Button RaiseButton(string text, Action pressed)
    {
        var button = UiStyle.FrameButton(text);
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += pressed;
        return button;
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
