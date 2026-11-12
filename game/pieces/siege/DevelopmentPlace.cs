using System.Globalization;
using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Structures;

namespace Game;

/// <summary>
/// A development aid, not part of the game: a small panel and two keys that lay a finished
/// structure from nothing at the pointer, so that a Tower can be seen shooting and a structure
/// can be attacked without building one first, and for the other seat, which nobody plays.
/// It sends the development placement (<see cref="CommandType.DebugPlace"/>), which the
/// simulation carries out only while the rules have <c>debug.spawn</c> on. With the switch off
/// this part shows nothing and reads no key.
/// </summary>
/// <remarks>
/// It goes the way the aid that makes units goes (<see cref="DevelopmentSpawn"/>): the local
/// player's command through the player's own side of the session, and the other seat's through
/// the host of a match played alone. The structure is laid with the middle of its footprint
/// under the pointer, costs nothing, and need not be unlocked nor stand in territory. It is
/// refused where its tiles are not open ground, under another structure, or stood on by a unit.
/// </remarks>
public partial class DevelopmentPlace : InterfacePart
{
    /// <summary>Lays a structure of the local player's at the pointer. Named in the input map of project.godot.</summary>
    public static readonly StringName PlaceOwnAction = "siege_dev_place_own";

    /// <summary>Lays a structure of the other seat's at the pointer, alone. Named in the input map of project.godot.</summary>
    public static readonly StringName PlaceOtherAction = "siege_dev_place_other";

    private OptionButton _structure = null!;
    private bool _on;

    /// <summary>True when this match has a seat the aid can lay structures for beside the local player's: alone, with a second seat.</summary>
    public bool CanPlaceForOther => View.Run is LocalRun && View.Match.Players.Count > 1;

    /// <summary>The seat the second key lays structures for.</summary>
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

        if (@event.IsActionPressed(PlaceOwnAction))
        {
            PlaceAtPointer(View.Player);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(PlaceOtherAction))
        {
            if (CanPlaceForOther)
            {
                PlaceAtPointer(OtherSeat);
            }
            else
            {
                Frame.Say("Development placement: in a shared match each player lays their own structures.");
            }

            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// The lowest tile of a structure that is laid with the middle of its footprint as near a
    /// point of the map as whole tiles allow.
    /// </summary>
    public static (int X, int Y) LowestTile(StructureDef structure, FixedVector2 middle)
    {
        var back = Fixed.FromRaw((structure.Footprint - 1) * (Fixed.RawOne / 2));
        return ((middle.X - back).FloorToInt(), (middle.Y - back).FloorToInt());
    }

    /// <summary>
    /// Lays a finished structure for a seat with its lowest tile on a tile of the map. Returns
    /// false when the structure has no room there or the session took no command. A unit that
    /// stands on the tiles is found by the simulation, which then refuses the command.
    /// </summary>
    public bool Place(int player, StructureDef structure, int x, int y)
    {
        if (!View.Match.HasRoomFor(structure, x, y))
        {
            return false;
        }

        // The command's point is on the lowest tile of the footprint.
        var command = new Command(CommandType.DebugPlace, player, targetX: Fixed.FromInt(x), targetY: Fixed.FromInt(y), contentId: structure.Index);
        return command.Player == View.Player
            ? View.Client.Submit(command)
            : View.Run is LocalRun alone && alone.Session.Host.Submit(command);
    }

    private void PlaceAtPointer(int player)
    {
        var target = View.UnderPointer.Current(PointerTargets.Ground);
        if (!target.IsOnWorld
            || View.Client.Phase != Net.SessionPhase.Running
            || View.Match.Data.StructureOf(_structure.GetSelectedId()) is not { } structure)
        {
            return;
        }

        var (x, y) = LowestTile(structure, target.Point);
        var sent = Place(player, structure, x, y);
        if (!sent)
        {
            Frame.Say("Development placement: no structure can stand there.");
        }

        if (OS.IsStdOutVerbose())
        {
            GD.Print(string.Create(
                CultureInfo.InvariantCulture,
                $"Development placement: {structure.Id} for player {player} at tile {x}, {y} at tick {View.Match.World.Tick}{(sent ? string.Empty : ", not sent")}."));
        }
    }

    private void Build()
    {
        var data = View.Match.Data;
        var panel = new PanelContainer { Name = "DevelopmentPlace" };
        var box = UiStyle.PanelStyle(10f);
        box.BorderColor = UiStyle.Warning;
        box.SetBorderWidthAll(1);
        panel.AddThemeStyleboxOverride("panel", box);
        Frame.DevelopmentAids.AddChild(panel);

        var column = UiStyle.Column(6);
        panel.AddChild(column);

        // The column of aids has little room, so this box says less than the others and keeps
        // the rest for the pointer resting on it.
        var title = new Label { Text = "Development aid: lay a structure" };
        title.AddThemeColorOverride("font_color", UiStyle.Warning);
        column.AddChild(title);
        panel.TooltipText = "Not part of the game. A finished structure appears from nothing, free of charge,\nwith the middle of its footprint under the pointer.";

        // A list that never takes the keyboard, so that the keys keep moving the camera after a click on it.
        _structure = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _structure.AddThemeFontSizeOverride("font_size", 13);
        _structure.GetPopup().AddThemeFontSizeOverride("font_size", 13);
        foreach (var structure in data.Structures.OrderBy(structure => structure.Index))
        {
            _structure.AddItem(StructureWords.NameOf(structure), structure.Index);
        }

        // The list opens on the structure that shoots, which is what the aid is mostly for.
        if (data.Structures.FirstOrDefault(structure => structure.Attack is not null) is { } shooter)
        {
            _structure.Select(_structure.GetItemIndex(shooter.Index));
        }

        var row = UiStyle.Row(8);
        row.AddChild(_structure);
        row.AddChild(Small(CanPlaceForOther
            ? $"{KeyOf(PlaceOwnAction)}: yours.  {KeyOf(PlaceOtherAction)}: the other's."
            : $"{KeyOf(PlaceOwnAction)}: yours."));
        column.AddChild(row);
    }

    private static Label Small(string text)
    {
        var label = new Label { Text = text };
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
