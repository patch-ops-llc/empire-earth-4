using Godot;
using Sim.Core;
using Sim.Numerics;

namespace Game;

/// <summary>
/// A development aid, not part of the game: a switch that makes the wall tool lay its run for the
/// other seat, finished and for nothing, so that a tester who plays alone has an enemy wall to
/// send units at and to see through the fog. Nobody plays the other seat, and a wall of the
/// game stands only in its owner's territory.
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
                _button.Text = _forOtherSeat ? "Walls are laid for the other player. Back to yours" : "Lay walls for the other player";
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

    protected override void Attach()
    {
        if (!IsOffered)
        {
            return;
        }

        var panel = new PanelContainer
        {
            Name = "DevelopmentWalls",
            TooltipText = "Not part of the game. While this is on, the run you draw with the Wall entry of the build menu\nis laid at once as finished wall of the other seat, free of charge, in or out of any territory.",
        };
        var box = UiStyle.PanelStyle(10f);
        box.BorderColor = UiStyle.Warning;
        box.SetBorderWidthAll(1);
        panel.AddThemeStyleboxOverride("panel", box);
        Frame.DevelopmentAids.AddChild(panel);

        var column = UiStyle.Column(4);
        panel.AddChild(column);
        var title = new Label { Text = "Development aid: an enemy wall" };
        title.AddThemeColorOverride("font_color", UiStyle.Warning);
        column.AddChild(title);

        _button = UiStyle.FrameButton(string.Empty);
        _button.AddThemeFontSizeOverride("font_size", 13);
        _button.Pressed += () => ForOtherSeat = !ForOtherSeat;
        column.AddChild(_button);
        ForOtherSeat = false;
    }
}
