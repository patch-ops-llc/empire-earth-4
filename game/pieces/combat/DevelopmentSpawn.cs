using System.Globalization;
using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;

namespace Game;

/// <summary>
/// A development aid, not part of the game: a small panel and two keys that make fighting units
/// from nothing at the pointer, so that a fight can be tried before a match can train a unit.
/// It sends the development spawn (<see cref="CommandType.DebugSpawn"/>), which the simulation
/// carries out only while the rules have <c>debug.spawn</c> on. With the switch off this part
/// shows nothing and reads no key.
/// </summary>
/// <remarks>
/// <para>
/// One key makes units for the local player. The command goes through the player's own side of
/// the session, alone and over the network alike, so both machines of a shared match make the
/// same units.
/// </para>
/// <para>
/// The other key makes units for the other seat, so that there is someone to fight. A command
/// names the player it is from, and a session takes a player's commands from that player only.
/// So this works alone, where nobody sits at the other seat and the host of the match, which is
/// this same game, gives that seat's commands as it would give an AI player's. In a shared
/// match the other player makes their own.
/// </para>
/// <para>
/// One command makes one unit, so a group is as many commands as it has units. A point where no
/// unit can stand is left out, since the simulation would refuse it.
/// </para>
/// </remarks>
public partial class DevelopmentSpawn : InterfacePart
{
    /// <summary>Makes units of the local player at the pointer. Named in the input map of project.godot.</summary>
    public static readonly StringName SpawnOwnAction = "combat_dev_spawn_own";

    /// <summary>Makes units of the other seat at the pointer, alone. Named in the input map of project.godot.</summary>
    public static readonly StringName SpawnOtherAction = "combat_dev_spawn_other";

    private static readonly int[] Counts = [1, 3, 5, 10];
    private const int DefaultCount = 2;

    private OptionButton _chassis = null!;
    private OptionButton _count = null!;
    private bool _on;

    /// <summary>True when this match has a seat the development spawn can make units for beside the local player's: alone, with a second seat.</summary>
    public bool CanSpawnForOther => View.Run is LocalRun && View.Match.Players.Count > 1;

    /// <summary>The seat the second key makes units for.</summary>
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

        if (@event.IsActionPressed(SpawnOwnAction))
        {
            SpawnAtPointer(View.Player);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(SpawnOtherAction))
        {
            if (CanSpawnForOther)
            {
                SpawnAtPointer(OtherSeat);
            }
            else
            {
                Frame.Say("Development spawn: in a shared match each player makes their own units.");
            }

            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Makes a group of units of a chassis for a seat, in rows round a point of the map. Returns
    /// the number of spawn commands the session took, one for each unit.
    /// </summary>
    public int Spawn(int player, int chassis, int count, FixedVector2 center)
    {
        var match = View.Match;
        var spacing = match.Data.LineOf(match.Data.Chassis[chassis].Line)?.Base.Spacing.ToFraction().ToFixed() ?? Fixed.One;
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (count + columns - 1) / columns;
        var sent = 0;
        for (var i = 0; i < count; i++)
        {
            // Half steps, so that an even number of columns is centered on the point too.
            var x = center.X + (spacing * ((2 * (i % columns)) - (columns - 1)) / 2);
            var y = center.Y + (spacing * ((2 * (i / columns)) - (rows - 1)) / 2);
            var point = new FixedVector2(x, y);
            if (!match.Grid.IsPassable(point))
            {
                continue;
            }

            var command = new Command(CommandType.DebugSpawn, player, targetX: point.X, targetY: point.Y, contentId: chassis);
            sent += Submit(command) ? 1 : 0;
        }

        return sent;
    }

    /// <summary>The chassis of the earliest epoch that a test accepts, the first by its place in the data, or null.</summary>
    public static ChassisDef? FirstChassis(GameData data, Func<ChassisDef, bool> wanted) =>
        data.Chassis.Where(wanted).OrderBy(chassis => chassis.Epoch).ThenBy(chassis => chassis.Index).FirstOrDefault();

    // The local player's command goes through the player's side of the session. Another seat's
    // is given by the host of a match played alone, which is this game.
    private bool Submit(in Command command)
    {
        if (command.Player == View.Player)
        {
            return View.Client.Submit(command);
        }

        return View.Run is LocalRun alone && alone.Session.Host.Submit(command);
    }

    private void SpawnAtPointer(int player)
    {
        var target = View.UnderPointer.Current(PointerTargets.Ground);
        if (!target.IsOnWorld || View.Client.Phase != Net.SessionPhase.Running)
        {
            return;
        }

        var chassis = _chassis.GetSelectedId();
        var count = _count.GetSelectedId();
        var sent = Spawn(player, chassis, count, target.Point);
        if (sent == 0)
        {
            Frame.Say("Development spawn: no unit can stand there.");
        }

        if (OS.IsStdOutVerbose())
        {
            GD.Print(string.Create(
                CultureInfo.InvariantCulture,
                $"Development spawn: {sent} of {count} {View.Match.Data.Chassis[chassis].Id} for player {player} at {WorldView.ToWorld(target.Point)} at tick {View.Match.World.Tick}."));
        }
    }

    private void Build()
    {
        var data = View.Match.Data;
        var panel = new PanelContainer { Name = "DevelopmentSpawn" };
        var box = UiStyle.PanelStyle(10f);
        box.BorderColor = UiStyle.Warning;
        box.SetBorderWidthAll(1);
        panel.AddThemeStyleboxOverride("panel", box);
        Frame.DevelopmentAids.AddChild(panel);

        var column = UiStyle.Column(6);
        panel.AddChild(column);

        var title = new Label { Text = "Development aid: make units" };
        title.AddThemeColorOverride("font_color", UiStyle.Warning);
        column.AddChild(title);
        column.AddChild(Small("Not part of the game. Units appear from nothing,\nfree of charge, so that a fight can be tried."));

        _chassis = Choice();
        foreach (var line in data.Lines.OrderBy(line => line.Index))
        {
            foreach (var chassis in line.Chassis.OrderBy(chassis => chassis.Epoch))
            {
                _chassis.AddItem(CombatWords.Describe(data, chassis), chassis.Index);
            }
        }

        // The list opens on a unit that shoots, so that the first fight a tester makes has shots in it.
        if (FirstChassis(data, chassis => chassis.Attack.Kind == AttackKind.Ranged && chassis.Attack.MinRange is null) is { } first)
        {
            _chassis.Select(_chassis.GetItemIndex(first.Index));
        }

        column.AddChild(Row("Unit", _chassis));

        _count = Choice();
        foreach (var count in Counts)
        {
            _count.AddItem(count.ToString(CultureInfo.InvariantCulture), count);
        }

        _count.Select(DefaultCount);
        column.AddChild(Row("At a time", _count));

        // One line, so that the column of aids keeps clear of the corners in a small window.
        column.AddChild(Small(CanSpawnForOther
            ? $"At the pointer. {KeyOf(SpawnOwnAction)}: yours.  {KeyOf(SpawnOtherAction)}: the other player's."
            : $"At the pointer. {KeyOf(SpawnOwnAction)}: yours. The other player makes their own."));
    }

    private static HBoxContainer Row(string name, Control control)
    {
        var row = UiStyle.Row(8);
        row.AddChild(Small(name));
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(control);
        return row;
    }

    private static Label Small(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }

    // A list to choose from that never takes the keyboard, so that the keys keep moving the camera after a click on it.
    private static OptionButton Choice()
    {
        var choice = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        choice.AddThemeFontSizeOverride("font_size", 13);
        choice.GetPopup().AddThemeFontSizeOverride("font_size", 13);
        return choice;
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
