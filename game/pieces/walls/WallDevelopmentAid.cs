using Godot;
using Sim.Core;
using Sim.Numerics;
using Sim.Structures;

namespace Game;

/// <summary>
/// A development aid, not part of the game: a switch that makes the wall tool lay its run for the
/// other seat, finished and for nothing, and the build menu place its gate for that seat, so
/// that a tester who plays alone has an enemy wall and an enemy gate to send units at and to
/// see through the fog. Nobody plays the other seat, and a wall of the game stands only in its
/// owner's territory.
/// </summary>
/// <remarks>
/// It sends the development placement (<see cref="CommandType.DebugPlace"/>), one command for
/// each tile, which the simulation carries out only while the rules have <c>debug.spawn</c>
/// on. With the switch off, and in a shared match, where each seat has its own player, this
/// part shows nothing and the wall tool lays walls as the game has them. The commands go the
/// way the development spawn's go for the other seat: through the host of a match played
/// alone, which is this same game.
/// </remarks>
public partial class WallDevelopmentAid : InterfacePart
{
    private Button? _button;
    private bool _forOtherSeat;

    /// <summary>True when this match has another seat the aid can lay walls for: alone, with a second seat, with the rules' switch on.</summary>
    public bool IsOffered => View.Match.Data.Rules.Debug.Spawn && View.Run is LocalRun && View.Match.Players.Count > 1 && View.Match.Walls.Wall is not null;

    /// <summary>The seat the aid lays walls for.</summary>
    public int OtherSeat => (View.Player + 1) % View.Match.Players.Count;

    /// <summary>
    /// DEVELOPMENT ONLY. True while the wall tool lays its runs for the other seat. Setting it
    /// does nothing in a match the aid is not offered in.
    /// </summary>
    public bool ForOtherSeat
    {
        get => _forOtherSeat;
        set
        {
            _forOtherSeat = value && IsOffered;
            if (_button is not null)
            {
                var whose = MatchPanel.PlayerName(OtherSeat);
                var what = View.Match.Data.Gate is null ? "walls" : "walls and gates";
                _button.Text = _forOtherSeat ? $"Aid: {what} for {whose}: ON" : $"Aid: {what} for {whose}: off";
                _button.AddThemeColorOverride("font_color", _forOtherSeat ? UiStyle.Warning : ResearchColors.Ink);
            }
        }
    }

    /// <summary>
    /// Lays a finished tile of wall for the other seat on every one of these tiles. Returns the
    /// number of commands the session took. The simulation refuses a tile a unit stands on.
    /// </summary>
    public int Lay(IEnumerable<(int X, int Y)> tiles)
    {
        if (!IsOffered || View.Run is not LocalRun alone || View.Match.Walls.Wall is not { } wall)
        {
            return 0;
        }

        var sent = 0;
        foreach (var (x, y) in tiles)
        {
            var command = new Command(CommandType.DebugPlace, OtherSeat, targetX: Fixed.FromInt(x), targetY: Fixed.FromInt(y), contentId: wall.Index);
            sent += alone.Session.Host.Submit(command) ? 1 : 0;
        }

        return sent;
    }

    /// <summary>
    /// Places a finished gate for the other seat on a tile, if a gate has room there. Returns
    /// true when the session took the command.
    /// </summary>
    public bool PlaceGate(int x, int y)
    {
        if (!IsOffered || View.Run is not LocalRun alone || View.Match.Data.Gate is not { } gate || !View.Match.HasRoomFor(gate, x, y))
        {
            return false;
        }

        var command = new Command(CommandType.DebugPlace, OtherSeat, targetX: Fixed.FromInt(x), targetY: Fixed.FromInt(y), contentId: gate.Index);
        return alone.Session.Host.Submit(command);
    }

    protected override void Attach()
    {
        if (!IsOffered)
        {
            return;
        }

        // No wider than its button, so that a third aid in the column leaves the panel at the
        // bottom of a small window its room.
        var panel = new PanelContainer
        {
            Name = "DevelopmentWalls",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            TooltipText = "Not part of the game. While this is on, the run you draw with the Wall entry of the build menu\nis laid at once as finished wall of the other seat, and a click with the Gate entry places a finished\ngate of the other seat, free of charge, in or out of any territory.",
        };
        var box = UiStyle.PanelStyle(6f);
        box.BorderColor = UiStyle.Warning;
        box.SetBorderWidthAll(1);
        panel.AddThemeStyleboxOverride("panel", box);
        Frame.DevelopmentAids.AddChild(panel);

        _button = UiStyle.FrameButton(string.Empty);
        _button.AddThemeFontSizeOverride("font_size", 13);
        _button.Pressed += () => ForOtherSeat = !ForOtherSeat;
        panel.AddChild(_button);
        ForOtherSeat = false;
    }
}
