using Godot;
using Sim.Construction;
using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Game;

/// <summary>
/// The Wall entry of the build menu (design 2.5): the player presses on the first tile of a run,
/// drags to the last and lets go, and the selected workers are sent the order to lay it. While
/// the run is drawn it is shown as the order would be carried out (<see cref="WallRunPlan"/>),
/// with its price beside the pointer.
/// </summary>
/// <remarks>
/// <para>
/// The order button or the key that closes a panel drops a run that was begun, and with no run
/// begun they go back to the menu's entries, as for a structure. An order the plan says would
/// be refused is not sent, and the player is told why. The order that is sent is judged again
/// by the simulation on the tick it runs, and what it refuses then is reported by
/// <see cref="WallMessages"/>.
/// </para>
/// <para>
/// Which tiles would be laid is asked of the wall rules themselves, so that the preview and the
/// order never disagree. Those rules judge a tile as it is, also one of the player's own
/// territory that the player has not explored yet: the preview then shows what the order will
/// do there, and with it what stands there.
/// </para>
/// </remarks>
public sealed class WallTool : BuildTool
{
    private readonly WallRunPreview _preview;
    private readonly WallPriceTag _tag;
    private readonly WallDevelopmentAid? _aid;
    private readonly GivenWallOrders _given;

    private (int X, int Y)? _pointed;
    private (int X, int Y)? _first;
    private (int X, int Y) _last;

    /// <summary>Makes the tool.</summary>
    /// <param name="preview">The layer that shows the run.</param>
    /// <param name="tag">The tag that shows its price.</param>
    /// <param name="given">Where the orders the tool sends are noted, for the messages about them.</param>
    /// <param name="aid">The development aid that lays runs for the other seat, or null.</param>
    public WallTool(WallRunPreview preview, WallPriceTag tag, GivenWallOrders given, WallDevelopmentAid? aid = null)
    {
        _preview = preview;
        _tag = tag;
        _given = given;
        _aid = aid;
    }

    public override string Id => "wall";

    public override string Name => "Wall";

    public override bool IsHeld => _first is not null;

    /// <summary>The run as it is shown now, or null while the pointer is on no tile and no run is begun.</summary>
    public WallRunPlan? Plan { get; private set; }

    /// <summary>The number of wall orders the tool has sent.</summary>
    public int Sent { get; private set; }

    public override BuildProblem Problem(MatchView view)
    {
        var match = view.Match;
        if (match.Walls.Wall is not { } wall)
        {
            return BuildProblem.NoStructure;
        }

        // The rules say whether walls are unlocked before they look at any tile.
        if (view.Sight.Seen.WallRun(0, 0, 0, 0, []) == BuildProblem.Locked)
        {
            return BuildProblem.Locked;
        }

        return match.Stockpiles.Covers(view.Player, wall.Materials, wall.Energy) ? BuildProblem.None : BuildProblem.CannotAfford;
    }

    public override string EntryLine(MatchView view, BuildProblem problem) => view.Match.Walls.Wall is not { } wall ? "None"
        : problem == BuildProblem.Locked ? BuildWords.Needs(view.Match.Data, wall)
        : $"{BuildWords.ShortPrice(wall)} a tile";

    public override string? WhyNot(MatchView view, BuildProblem problem) => problem switch
    {
        BuildProblem.None => null,
        BuildProblem.Locked => $"Locked: {WallWords.Locked(view.Match)}.",
        BuildProblem.CannotAfford when view.Match.Walls.Wall is { } wall => $"You lack {BuildWords.Lacking(view.Match, view.Player, wall)} for one tile.",
        BuildProblem.NoStructure => "This match has no walls.",
        _ => "It cannot be laid.",
    };

    public override string Detail(MatchView view) => view.Match.Walls.Wall is { } wall
        ? $"a straight run inside your territory, laid by one worker. {ResearchWords.Duration(wall.BuildTicks)} a tile."
        : "This match has no walls.";

    public override (string Text, bool Warn) Hint(MatchView view)
    {
        if (Plan is not { } plan)
        {
            return ("Press on the first tile of the wall, drag to the last and let go.", false);
        }

        if (!plan.IsAccepted)
        {
            return ($"It cannot be laid: {WallWords.WhyNot(view.Match, plan)}.", true);
        }

        return _first is null
            ? ("Press here to begin the wall, drag to its last tile and let go.", false)
            : ($"Let go to lay {WallWords.Tiles(plan.Laid)}. Shift lays more runs; a right-click drops this one.", false);
    }

    public override void Point(MatchView view, Vector2? ground, IReadOnlyList<EntityId> workers)
    {
        var grid = view.Match.Grid;
        if (ground is { } point)
        {
            var x = Mathf.FloorToInt(point.X);
            var y = Mathf.FloorToInt(point.Y);
            if (_first is not null)
            {
                // A run that is dragged past the edge of the map ends on its last tile there.
                _last = (Math.Clamp(x, 0, grid.Width - 1), Math.Clamp(y, 0, grid.Height - 1));
                _pointed = _last;
            }
            else
            {
                _pointed = grid.Contains(x, y) ? (x, y) : null;
            }
        }
        else if (_first is null)
        {
            _pointed = null;
        }

        Show(view, workers);
    }

    public override void Press(MatchView view)
    {
        if (_pointed is { } tile)
        {
            _first = tile;
            _last = tile;
        }
    }

    public override bool Release(MatchView view, IReadOnlyList<EntityId> workers)
    {
        if (_first is not { } first)
        {
            return false;
        }

        var last = _last;
        _first = null;
        var plan = PlanOf(view, workers, first, last);
        Show(view, workers);
        if (!plan.IsAccepted)
        {
            view.Interface.Say($"The wall cannot be laid: {WallWords.WhyNot(view.Match, plan)}.");
            return false;
        }

        return plan.AidFor is null ? Order(view, workers, first, last) : _aid is not null && _aid.Lay(plan.LaidTiles()) > 0;
    }

    public override bool Back(MatchView view)
    {
        if (_first is null)
        {
            return false;
        }

        // The run is gone from the screen with the next frame, which shows the tile under the pointer.
        _first = null;
        return true;
    }

    public override void Leave(MatchView view)
    {
        _first = null;
        _pointed = null;
        Plan = null;
        _preview.Clear();
        _tag.Clear();
    }

    /// <summary>
    /// Sends the selected workers the order to lay the run from one tile to another, as letting
    /// go of a run does. Returns true when the session took the order.
    /// </summary>
    public bool Order(MatchView view, IReadOnlyList<EntityId> workers, (int X, int Y) first, (int X, int Y) last)
    {
        var point = MapGrid.TileCenter(first.X, first.Y);
        var command = new Command(
            CommandType.BuildWall,
            view.Player,
            [.. workers],
            point.X,
            point.Y,
            contentId: (last.Y * view.Match.Grid.Width) + last.X);
        if (!view.Client.Submit(command))
        {
            return false;
        }

        _given.Note(point, view.Match.World.Tick);
        Sent++;
        if (OS.IsStdOutVerbose())
        {
            GD.Print($"Wall order for {workers.Count} from tile {first.X}, {first.Y} to tile {last.X}, {last.Y} at tick {view.Match.World.Tick}.");
        }

        return true;
    }

    private WallRunPlan PlanOf(MatchView view, IReadOnlyList<EntityId> workers, (int X, int Y) first, (int X, int Y) last) =>
        _aid is { ForOtherSeat: true } aid
            ? WallRunPlan.ForAid(view, aid.OtherSeat, first.X, first.Y, last.X, last.Y)
            : WallRunPlan.Of(view, workers, first.X, first.Y, last.X, last.Y);

    // Shows the run that is begun, or the one tile under the pointer before a run is begun.
    private void Show(MatchView view, IReadOnlyList<EntityId> workers)
    {
        if ((_first ?? _pointed) is not { } first)
        {
            Plan = null;
            _preview.Clear();
            _tag.Clear();
            return;
        }

        var plan = PlanOf(view, workers, first, _first is null ? first : _last);
        Plan = plan;
        _preview.Show(plan);
        // While a run is drawn the menu, and its line of what to do, are out of the way.
        var note = WallWords.Note(view.Match, plan) ?? (_first is null ? null : "Let go to lay it. A right-click drops it.");
        _tag.Show(WallWords.Price(plan), note, warn: !plan.IsAccepted);
    }
}
