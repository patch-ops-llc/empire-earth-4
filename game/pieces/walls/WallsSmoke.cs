using Godot;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Vision;
using Sim.Walls;

namespace Game;

/// <summary>
/// The smoke test's walls: the local player lays a run of wall through the build menu as a
/// player does, and the part checks what the view shows at each step. A second order is
/// cancelled while its worker is on the way, and one tile of the first after it is laid. A
/// third order has a tile left out. A wall of the other seat that the player has never seen is
/// not shown anywhere.
/// </summary>
/// <remarks>
/// <para>
/// The orders leave through the menu's wall tool and the cancel command themselves, so that the
/// path a drag takes is the path tested. The rules of walls are tested with the simulation.
/// What is checked here is that the preview and the price are those of the rules, that the
/// tiles to come and the tiles laid are drawn as what they are, joined as one wall, that the
/// panel and the minimap know a wall, and that the player is told what went wrong.
/// </para>
/// <para>
/// The pointer is not used, as in the part for construction: the part points the tool at a
/// tile, which is what a press and a drag come to.
/// </para>
/// <para>
/// The tile that is left out, and the wall that is not seen, are the other seat's, laid with
/// the development placement, since nobody plays that seat. While the rules have that switch
/// off, the part leaves those steps out and says so.
/// </para>
/// <para>
/// No order of the script is one the simulation refuses, which the smoke test does not allow.
/// So the message for a refused wall order is checked up to what tells it from a refused build
/// order: a refusal at the first tile of a wall order just given is known as the wall's.
/// </para>
/// </remarks>
public sealed class WallsSmoke : SmokePart
{
    private const int Length = 420;

    private const int RunLength = 5;
    private const int ShortRun = 3;

    // How long an order may take to be carried out, and how long a worker may take to lay a run.
    private const int TicksToTake = 60;
    private const int TicksToLay = 320;

    // How long what the view draws may be behind the match.
    private const int TicksToDraw = 3;

    // How far from its worker a run may be looked for, and how far the worker walks at least to
    // a run that something is done to on the way.
    private const int FarthestRun = 24;
    private const int LeastWalk = 6;

    // A wall of the other seat is laid at least this far from every tile the player sees.
    private const int OutOfSight = 5;

    // The bits of a tile's joins toward the next tile of a run that goes east, and toward the one before.
    private const int East = 1 << 2;
    private const int West = 1 << 6;

    private readonly List<(int X, int Y)> _tiles = [];
    private readonly List<SeenStructure> _listed = [];
    private readonly List<EntityId> _laid = [];

    private Step _step = Step.Open;
    private int _deadline = Length;
    private int _drawnBy;
    private StructureDef _wall = null!;
    private EntityId _firstWorker;
    private EntityId _secondWorker;
    private int _other;
    private ((int X, int Y) First, (int X, int Y) Last) _firstRun;
    private ((int X, int Y) First, (int X, int Y) Last) _secondRun;
    private ((int X, int Y) First, (int X, int Y) Last) _thirdRun;
    private ((int X, int Y) First, (int X, int Y) Last) _unseenRun;
    private EntityId _cancelledTile;
    private int _buildMessages;
    private bool _sawUnfinished;
    private bool _sawBar;
    private bool _sawMark;
    private bool _withOtherSeat;

    private enum Step
    {
        Open,
        FirstTaken,
        SecondTaken,
        SecondCancelled,
        FirstLaid,
        TileCancelled,
        Finished,
        ThirdTaken,
        TileSkipped,
        Unseen,
        Done,
    }

    public override string Name => "Walls";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (orders.Failed || _step == Step.Done)
        {
            return;
        }

        if (Parts.Of(orders) is not { } parts)
        {
            return;
        }

        if (tick == Ticks - 1 || tick > _deadline)
        {
            orders.Fail($"walls did not get past the step {_step} in time: at tick {tick} of its part.");
            return;
        }

        _sawBar |= _step is Step.FirstLaid or Step.TileCancelled or Step.Finished && parts.Bars.BarCount > 0;
        _sawMark |= parts.Marks.Count > 0;
        switch (_step)
        {
            case Step.Open:
                Open(tick, orders, parts);
                break;
            case Step.FirstTaken:
                FirstTaken(tick, orders, parts);
                break;
            case Step.SecondTaken:
                SecondTaken(tick, orders);
                break;
            case Step.SecondCancelled:
                SecondCancelled(tick, orders, parts);
                break;
            case Step.FirstLaid:
                FirstLaid(tick, orders, parts);
                break;
            case Step.TileCancelled:
                TileCancelled(tick, orders, parts);
                break;
            case Step.Finished:
                Finished(tick, orders, parts);
                break;
            case Step.ThirdTaken:
                ThirdTaken(tick, orders, parts);
                break;
            case Step.TileSkipped:
                TileSkipped(tick, orders, parts);
                break;
            case Step.Unseen:
                Unseen(tick, orders, parts);
                break;
        }
    }

    // Selects a worker, opens the menu, takes the wall in hand, looks at what the tool shows
    // for a run and lays it.
    private void Open(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        if (match.Walls.Wall is not { } wall)
        {
            orders.Fail("the match has no walls.");
            return;
        }

        _wall = wall;
        _other = (orders.Player + 1) % match.Players.Count;
        _withOtherSeat = match.Data.Rules.Debug.Spawn && match.Players.Count > 1;
        _buildMessages = parts.BuildMessages.Count;
        var workers = Array.FindAll(orders.OwnUnits(), match.Workers.Contains);
        if (workers.Length < 2)
        {
            orders.Fail("the local player has fewer than two workers to lay walls with.");
            return;
        }

        _firstWorker = workers[0];
        _secondWorker = workers[1];
        view.Selection.Set([_firstWorker]);
        parts.Menu.Open();
        var tool = parts.Tool;
        var problem = tool.Problem(view);
        if (!parts.Menu.IsOpen || parts.Menu.ProblemShownFor(tool) != problem || problem != BuildProblem.None)
        {
            orders.Fail($"the build menu shows the wall as {parts.Menu.ProblemShownFor(tool)} and the rules say {problem}, with {match.Stockpiles.Materials(orders.Player) / 1000} Materials.");
            return;
        }

        if (!parts.Menu.Choose(tool) || parts.Menu.Tool != tool)
        {
            orders.Fail("the build menu did not take the wall in hand.");
            return;
        }

        if (!match.Bodies.TryGet(_firstWorker, out var body)
            || FindRun(match, orders.Player, body.Position, 3, RunLength, null) is not { } run)
        {
            orders.Fail($"no run of {RunLength} tiles near the local player's first worker takes a wall.");
            return;
        }

        _firstRun = run;
        if (!CheckDrag(orders, parts) || !CheckTilesLeftOut(orders, parts))
        {
            return;
        }

        if (!parts.Menu.PlaceWithTool(Middle(run.First), Middle(run.Last)) || parts.Menu.IsOpen)
        {
            orders.Fail("the build menu did not send the order for the wall, or stayed open after it.");
            return;
        }

        if (parts.Preview.IsShown || parts.Tag.Price is not null)
        {
            orders.Fail("the run and its price are still shown after the order was sent.");
            return;
        }

        _deadline = tick + TicksToTake;
        _step = Step.FirstTaken;
    }

    // A press at the first tile and a drag to the last: the tool, the preview and the price tag
    // show the tiles the rules would lay. Going back drops the run and keeps the wall in hand.
    private bool CheckDrag(SmokeOrders orders, Parts parts)
    {
        var view = orders.View;
        var tool = parts.Tool;
        EntityId[] workers = [_firstWorker];
        tool.Point(view, Middle(_firstRun.First), workers);
        tool.Press(view);
        tool.Point(view, Middle(_firstRun.Last), workers);
        parts.Preview.EveryFrame(0.0);
        var price = RunLength * _wall.Materials;
        if (tool.Plan is not { IsAccepted: true, Laid: RunLength, LeftOut: 0, Standing: 0 } plan
            || plan.Materials != price
            || parts.Preview.LaidShown != RunLength
            || parts.Preview.LeftOutShown != 0)
        {
            orders.Fail($"a run of {RunLength} free tiles is shown as {tool.Plan?.Laid} tiles to lay for {tool.Plan?.Materials} Materials, {parts.Preview.LaidShown} of them drawn.");
            return false;
        }

        if (parts.Tag.Price != WallWords.Price(plan) || !parts.Tag.Price.Contains(ResearchWords.Number(price), StringComparison.Ordinal))
        {
            orders.Fail($"the price beside the pointer reads '{parts.Tag.Price}' for a run of {price} Materials.");
            return false;
        }

        if (!tool.IsHeld || !tool.Back(view) || tool.IsHeld || tool.Back(view) || parts.Menu.Tool != tool)
        {
            orders.Fail("going back while a run is drawn did not drop the run and keep the wall in hand.");
            return false;
        }

        return true;
    }

    // A run into a structure of the player's own: the tiles under the structure are left out,
    // and are drawn as left out.
    private bool CheckTilesLeftOut(SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var entity = match.Structures.EntityAt(i);
            var placed = match.Structures.ValueAt(i);
            if (placed.Size < 2 || !match.Bodies.TryGet(entity, out var body) || body.Owner != orders.Player || !match.World.Entities.IsAlive(entity))
            {
                continue;
            }

            var plan = WallRunPlan.Of(orders.View, [_firstWorker], placed.X - 2, placed.Y, placed.X + 1, placed.Y);
            _tiles.Clear();
            var problem = match.Walls.TilesToLay(orders.Player, placed.X - 2, placed.Y, placed.X + 1, placed.Y, _tiles);
            parts.Preview.Show(plan);
            parts.Preview.EveryFrame(0.0);
            var shown = (parts.Preview.LaidShown, parts.Preview.LeftOutShown);
            parts.Preview.Clear();
            parts.Preview.EveryFrame(0.0);
            if (plan.Laid != _tiles.Count || plan.LeftOut < 2 || shown != (plan.Laid, plan.LeftOut) || (problem == BuildProblem.None) != plan.IsAccepted)
            {
                orders.Fail($"a run into a structure is shown as {shown.LaidShown} tiles to lay and {shown.LeftOutShown} left out, and the rules lay {_tiles.Count} of its 4.");
                return false;
            }

            return true;
        }

        orders.Fail("the local player has no structure to draw a run of wall into.");
        return false;
    }

    // The first order is taken: its tiles are drawn as a plan and the worker's line counts them.
    // Then the second worker is sent to a run of its own.
    private void FirstTaken(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        if (!match.Walls.Has(_firstWorker) || !IsDrawn(tick, PlansDraw(parts, _firstRun, RunLength)))
        {
            return;
        }

        if (!UnitFacts.TryRead(match, _firstWorker, out var facts) || facts.WallTilesLeft == 0 || match.Walls.HeldBy(_firstWorker).Materials == 0)
        {
            orders.Fail("a worker that is laying a wall is not described as laying one.");
            return;
        }

        view.Selection.Set([_firstWorker]);
        if (!CancelBuild.Applies(view))
        {
            orders.Fail("a selected worker with a wall order cannot be told to cancel it.");
            return;
        }

        if (!match.Bodies.TryGet(_secondWorker, out var body)
            || FindRun(match, orders.Player, body.Position, LeastWalk, ShortRun, _firstRun.First.Y) is not { } run)
        {
            orders.Fail($"no second run of {ShortRun} tiles takes a wall.");
            return;
        }

        _secondRun = run;
        if (!Lay(orders, parts, _secondWorker, run))
        {
            return;
        }

        _deadline = tick + TicksToTake;
        _step = Step.SecondTaken;
    }

    // The second order is taken, and is cancelled while its worker is on the way.
    private void SecondTaken(int tick, SmokeOrders orders)
    {
        if (!orders.Match.Walls.Has(_secondWorker))
        {
            return;
        }

        orders.View.Selection.Set([_secondWorker]);
        if (!CancelBuild.Give(orders.View))
        {
            orders.Fail("the cancel command gave no order for a selected worker with a wall order.");
            return;
        }

        _deadline = tick + TicksToTake;
        _step = Step.SecondCancelled;
    }

    private void SecondCancelled(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        if (match.Walls.Has(_secondWorker) || !IsDrawn(tick, PlansDraw(parts, _secondRun, 0)))
        {
            return;
        }

        if (CountLaid(match, _secondRun, ShortRun) != 0)
        {
            orders.Fail("a tile of a cancelled wall order was laid.");
            return;
        }

        _deadline = tick + TicksToLay;
        _step = Step.FirstLaid;
    }

    // Every tile of the first run is laid. The last is still building itself: it is selected,
    // shown in the panel, and cancelled.
    private void FirstLaid(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        WatchUnfinished(match, parts);
        if (CountLaid(match, _firstRun, RunLength) < RunLength)
        {
            return;
        }

        _cancelledTile = match.WallMap.WallAt(_firstRun.Last.X, _firstRun.Last.Y);
        if (!match.Foundations.Contains(_cancelledTile) || !IsDrawn(tick, parts.Structures.DrawnBuilt(_cancelledTile) is not null))
        {
            return;
        }

        view.Selection.Set([_cancelledTile]);
        if (parts.Inspection.Mode != WallInspectionMode.One
            || !parts.Inspection.ShownText().Contains(StructureWords.NameOf(_wall), StringComparison.Ordinal)
            || !CancelBuild.Applies(view))
        {
            orders.Fail($"a selected tile of wall that is building itself is shown as {parts.Inspection.Mode}, or cannot be cancelled: '{parts.Inspection.ShownText().ReplaceLineEndings(" / ")}'.");
            return;
        }

        if (!CancelBuild.Give(view))
        {
            orders.Fail("the cancel command gave no order for a selected tile of wall.");
            return;
        }

        _deadline = tick + TicksToTake;
        _step = Step.TileCancelled;
    }

    private void TileCancelled(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        WatchUnfinished(match, parts);
        if (match.WallMap.WallAt(_firstRun.Last.X, _firstRun.Last.Y) != EntityId.None
            || !IsDrawn(tick, parts.Structures.ShapeOf(_cancelledTile) is null))
        {
            return;
        }

        _deadline = tick + _wall.BuildTicks + TicksToTake;
        _step = Step.Finished;
    }

    // The four tiles that are left have built themselves: they are drawn finished and whole,
    // each joined to the next, and the panel and the minimap know them as one wall.
    private void Finished(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        WatchUnfinished(match, parts);
        var length = RunLength - 1;
        _laid.Clear();
        for (var i = 0; i < length; i++)
        {
            var tile = match.WallMap.WallAt(_firstRun.First.X + i, _firstRun.First.Y);
            if (tile == EntityId.None)
            {
                orders.Fail("a tile of the wall is gone.");
                return;
            }

            if (match.Foundations.Contains(tile) || parts.Structures.DrawnBuilt(tile) is not null)
            {
                return;
            }

            _laid.Add(tile);
        }

        if (!_sawUnfinished || !_sawBar)
        {
            orders.Fail("the wall built itself without a tile drawn unfinished, or without a bar over one.");
            return;
        }

        for (var i = 0; i < length; i++)
        {
            var joins = (i > 0 ? West : 0) | (i < length - 1 ? East : 0);
            if (parts.Structures.ShapeOf(_laid[i]) is not { } shape
                || WallShapes.JoinsOf(shape) != joins
                || WallShapes.StageOf(shape) != WallShapes.Whole)
            {
                orders.Fail($"tile {i + 1} of a finished wall of {length} is not drawn whole and joined to the tiles beside it.");
                return;
            }
        }

        view.Selection.Set([.. _laid]);
        var several = parts.Inspection.Mode;
        view.Selection.Set([_laid[1]]);
        var text = parts.Inspection.ShownText();
        if (several != WallInspectionMode.Several
            || parts.Inspection.Mode != WallInspectionMode.One
            || !text.Contains(WallWords.Tiles(length), StringComparison.Ordinal)
            || CancelBuild.Applies(view))
        {
            orders.Fail($"the panel shows {length} selected tiles of wall as {several}, or one of them not as a piece of {length}, or as one to cancel: '{text.ReplaceLineEndings(" / ")}'.");
            return;
        }

        new FogOnMinimap(view).CollectStructures(view, _listed);
        if (_laid.Exists(tile => !_listed.Exists(listed => listed.Entity == tile)))
        {
            orders.Fail("the minimap does not list a tile of the local player's wall.");
            return;
        }

        var again = WallRunPlan.Of(view, [_firstWorker], _firstRun.First.X, _firstRun.First.Y, _firstRun.Last.X, _firstRun.Last.Y);
        if (again.Standing != length || again.Laid != RunLength - length)
        {
            orders.Fail($"a run over a wall of {length} tiles is shown as {again.Laid} tiles to lay and {again.Standing} that stand.");
            return;
        }

        if (parts.Messages.Count != 0 || parts.BuildMessages.Count != _buildMessages)
        {
            orders.Fail($"walls that went as ordered, or were cancelled, gave a message: '{parts.Messages.Last}'.");
            return;
        }

        if (!_withOtherSeat)
        {
            Finish(tick, orders);
            return;
        }

        if (!match.Bodies.TryGet(_firstWorker, out var body)
            || FindRun(match, orders.Player, body.Position, LeastWalk, ShortRun, _firstRun.First.Y) is not { } run)
        {
            orders.Fail($"no third run of {ShortRun} tiles takes a wall.");
            return;
        }

        _thirdRun = run;
        if (!Lay(orders, parts, _firstWorker, run))
        {
            return;
        }

        _deadline = tick + TicksToTake;
        _step = Step.ThirdTaken;
    }

    // The third order is taken. A refusal at its first tile would be known as a wall order's,
    // and one elsewhere as a build order's. Before its worker is there, the other seat's wall
    // stands on its last tile.
    private void ThirdTaken(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        if (!match.Walls.Has(_firstWorker))
        {
            return;
        }

        var here = new SimEvent(
            SimEventKind.BuildRefused,
            match.World.Tick,
            orders.Player,
            EntityId.None,
            EntityId.None,
            MapGrid.TileCenter(_thirdRun.First.X, _thirdRun.First.Y),
            (long)BuildProblem.SiteNotFree);
        var elsewhere = here with { Position = MapGrid.TileCenter(_thirdRun.Last.X, _thirdRun.Last.Y) };
        if (!parts.Messages.IsOfWallOrder(here) || parts.Messages.IsOfWallOrder(elsewhere))
        {
            orders.Fail("a refusal at the first tile of a wall order just given is not known as a wall order's, or one elsewhere is.");
            return;
        }

        PlaceForOther(orders, _thirdRun.Last);
        _deadline = tick + TicksToLay;
        _step = Step.TileSkipped;
    }

    // The worker has laid what it could. The player is told of the tile that was left out, and
    // sees the other seat's tile as that seat's, joined to nothing of the player's own. Then
    // the other seat gets a wall where the player does not see.
    private void TileSkipped(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        if (match.Walls.Has(_firstWorker))
        {
            return;
        }

        var theirs = match.WallMap.WallAt(_thirdRun.Last.X, _thirdRun.Last.Y);
        var beside = match.WallMap.WallAt(_thirdRun.Last.X - 1, _thirdRun.Last.Y);
        if (theirs == EntityId.None || beside == EntityId.None || CountLaid(match, _thirdRun, ShortRun) != ShortRun)
        {
            orders.Fail("the third run was not laid up to the other seat's tile.");
            return;
        }

        if (!IsDrawn(tick, parts.Structures.ShapeOf(beside) is not null && parts.Structures.ShapeOf(theirs) is not null))
        {
            return;
        }

        if (parts.Messages.Count != 1 || !_sawMark || parts.Messages.Last != WallWords.Skipped(1, BuildProblem.SiteNotFree) || parts.BuildMessages.Count != _buildMessages)
        {
            orders.Fail($"a tile that was left out gave {parts.Messages.Count} messages, the last '{parts.Messages.Last}', and {(_sawMark ? "a" : "no")} mark on the ground.");
            return;
        }

        if (!view.Sight.TryGetStructure(theirs, out var seen)
            || seen.Owner != _other
            || WallShapes.JoinsOf(parts.Structures.ShapeOf(theirs) ?? -1) != 0
            || WallShapes.JoinsOf(parts.Structures.ShapeOf(beside) ?? -1) != West)
        {
            orders.Fail("a tile of the other seat's wall beside the local player's is not seen as that seat's, or is drawn joined to the local player's wall.");
            return;
        }

        view.Selection.Clear();
        parts.Inspection.Inspect(theirs);
        var text = parts.Inspection.ShownText();
        var mode = parts.Inspection.Mode;
        parts.Inspection.Inspect(EntityId.None);
        if (mode != WallInspectionMode.Other || !text.Contains(InspectionWords.Owner(_other), StringComparison.Ordinal) || parts.Inspection.Mode != WallInspectionMode.Nothing)
        {
            orders.Fail($"a tile of the other seat's wall is shown as {mode}: '{text.ReplaceLineEndings(" / ")}'.");
            return;
        }

        if (FindUnseenRun(orders) is not { } run)
        {
            orders.Fail($"no run of {ShortRun} free tiles is out of the local player's sight.");
            return;
        }

        _unseenRun = run;
        for (var i = 0; i < ShortRun; i++)
        {
            PlaceForOther(orders, (run.First.X + i, run.First.Y));
        }

        _deadline = tick + TicksToTake;
        _step = Step.Unseen;
    }

    // A wall the player has not seen is not drawn, not on the minimap, and joins nothing.
    private void Unseen(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        if (CountLaid(match, _unseenRun, ShortRun) < ShortRun)
        {
            return;
        }

        if (tick < _drawnBy)
        {
            return;
        }

        if (_drawnBy == 0)
        {
            _drawnBy = tick + TicksToDraw;
            return;
        }

        new FogOnMinimap(view).CollectStructures(view, _listed);
        for (var i = 0; i < ShortRun; i++)
        {
            var (x, y) = (_unseenRun.First.X + i, _unseenRun.First.Y);
            var tile = match.WallMap.WallAt(x, y);
            if (view.Sight.FogAt(x, y) == FogState.Visible)
            {
                orders.Fail("the wall that was laid out of the local player's sight came into it.");
                return;
            }

            if (view.Sight.TryGetStructure(tile, out _)
                || parts.Structures.ShapeOf(tile) is not null
                || parts.Structures.Walls.IsWallOf(_other, x, y)
                || _listed.Exists(listed => listed.Entity == tile))
            {
                orders.Fail("a tile of the other seat's wall that the local player has never seen is drawn, or is on the minimap.");
                return;
            }
        }

        Finish(tick, orders);
    }

    private void Finish(int tick, SmokeOrders orders)
    {
        orders.View.Selection.Clear();
        _step = Step.Done;
        var rest = _withOtherSeat
            ? "a tile was left out for the other seat's, with its message and its mark, and the other seat's unseen wall was not shown"
            : "the steps with the other seat's wall were left out, since the rules have the development placement off";
        GD.Print($"Smoke test, walls: a run of {RunLength} was laid through the build menu and drawn as one wall of {RunLength - 1} after a tile was cancelled, by tick {tick} of the part; an order was cancelled on the way; {rest}.");
    }

    // Sends a worker to lay a run, through the menu.
    private static bool Lay(SmokeOrders orders, Parts parts, EntityId worker, ((int X, int Y) First, (int X, int Y) Last) run)
    {
        orders.View.Selection.Set([worker]);
        parts.Menu.Open();
        if (!parts.Menu.Choose(parts.Tool) || !parts.Menu.PlaceWithTool(Middle(run.First), Middle(run.Last)))
        {
            orders.Fail("the build menu did not send another order for a wall.");
            return false;
        }

        return true;
    }

    private void PlaceForOther(SmokeOrders orders, (int X, int Y) tile) =>
        orders.SubmitFor(new Command(CommandType.DebugPlace, _other, targetX: Fixed.FromInt(tile.X), targetY: Fixed.FromInt(tile.Y), contentId: _wall.Index));

    private void WatchUnfinished(Match match, Parts parts)
    {
        for (var i = 0; i < RunLength; i++)
        {
            var tile = match.WallMap.WallAt(_firstRun.First.X + i, _firstRun.First.Y);
            _sawUnfinished |= tile != EntityId.None && parts.Structures.DrawnBuilt(tile) is not null;
        }
    }

    // What the view draws follows the match by a tick: true once it is so, and never true when
    // it has not become so in time, which the deadline of the step then reports.
    private bool IsDrawn(int tick, bool drawn)
    {
        if (drawn)
        {
            _drawnBy = 0;
            return true;
        }

        if (_drawnBy == 0)
        {
            _drawnBy = tick + TicksToDraw;
        }

        _deadline = Math.Min(_deadline, _drawnBy);
        return false;
    }

    // True when exactly so many tiles of a run are drawn as planned, counted from its first.
    private static bool PlansDraw(Parts parts, ((int X, int Y) First, (int X, int Y) Last) run, int tiles)
    {
        var drawn = 0;
        for (var x = run.First.X; x <= run.Last.X; x++)
        {
            drawn += parts.Plans.Draws(x, run.First.Y) ? 1 : 0;
        }

        return drawn == tiles;
    }

    private static int CountLaid(Match match, ((int X, int Y) First, (int X, int Y) Last) run, int length)
    {
        var laid = 0;
        for (var i = 0; i < length; i++)
        {
            laid += match.WallMap.WallAt(run.First.X + i, run.First.Y) != EntityId.None ? 1 : 0;
        }

        return laid;
    }

    private static Vector2 Middle((int X, int Y) tile) => new(tile.X + 0.5f, tile.Y + 0.5f);

    // The nearest run of so many tiles from west to east, at least so far from a point, of which
    // the rules would lay every tile now, and which is not within two rows of another run.
    private ((int X, int Y) First, (int X, int Y) Last)? FindRun(Match match, int player, FixedVector2 near, int least, int length, int? apartFromRow)
    {
        var nearX = near.X.FloorToInt();
        var nearY = near.Y.FloorToInt();
        for (var away = least; away <= FarthestRun; away++)
        {
            for (var dy = -away; dy <= away; dy++)
            {
                for (var dx = -away; dx <= away; dx++)
                {
                    var x = nearX + dx;
                    var y = nearY + dy;
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != away
                        || Math.Max(Math.Abs(dx + length - 1), Math.Abs(dy)) < least
                        || (apartFromRow is { } row && Math.Abs(y - row) < 3))
                    {
                        continue;
                    }

                    _tiles.Clear();
                    if (match.Walls.TilesToLay(player, x, y, x + length - 1, y, _tiles) == BuildProblem.None && _tiles.Count == length)
                    {
                        return ((x, y), (x + length - 1, y));
                    }
                }
            }
        }

        return null;
    }

    // A run of free tiles that the local player does not see, and that no tile it sees is near:
    // the one of them that is farthest to the side of the map the other seat starts on.
    private ((int X, int Y) First, (int X, int Y) Last)? FindUnseenRun(SmokeOrders orders)
    {
        var match = orders.Match;
        var sight = orders.View.Sight;
        for (var x = match.Grid.Width - ShortRun - OutOfSight; x >= OutOfSight; x--)
        {
            for (var y = OutOfSight; y < match.Grid.Height - OutOfSight; y++)
            {
                var free = true;
                for (var i = 0; i < ShortRun && free; i++)
                {
                    free = match.HasRoomFor(_wall, x + i, y) && match.WallMap.WallAt(x + i, y) == EntityId.None;
                }

                for (var dy = -OutOfSight; dy <= OutOfSight && free; dy++)
                {
                    for (var dx = -OutOfSight; dx < ShortRun + OutOfSight && free; dx++)
                    {
                        free = sight.FogAt(x + dx, y + dy) != FogState.Visible;
                    }
                }

                if (free)
                {
                    return ((x, y), (x + ShortRun - 1, y));
                }
            }
        }

        return null;
    }

    private sealed record Parts(
        BuildMenu Menu,
        WallTool Tool,
        WallRunPreview Preview,
        WallPriceTag Tag,
        WallPlanLayer Plans,
        WallSkipMarks Marks,
        WallMessages Messages,
        BuildMessages BuildMessages,
        WallInspection Inspection,
        StructureLayer Structures,
        FoundationBars Bars)
    {
        public static Parts? Of(SmokeOrders orders)
        {
            var view = orders.View;
            var layers = view.World.GetChildren();
            var parts = view.Interface.Parts;
            if (parts.OfType<BuildMenu>().FirstOrDefault() is { } menu
                && menu.ToolOf<WallTool>() is { } tool
                && layers.OfType<WallRunPreview>().FirstOrDefault() is { } preview
                && parts.OfType<WallPriceTag>().FirstOrDefault() is { } tag
                && layers.OfType<WallPlanLayer>().FirstOrDefault() is { } plans
                && layers.OfType<WallSkipMarks>().FirstOrDefault() is { } marks
                && parts.OfType<WallMessages>().FirstOrDefault() is { } messages
                && parts.OfType<BuildMessages>().FirstOrDefault() is { } buildMessages
                && parts.OfType<WallInspection>().FirstOrDefault() is { } inspection
                && layers.OfType<StructureLayer>().FirstOrDefault() is { } structures
                && layers.OfType<FoundationBars>().FirstOrDefault() is { } bars)
            {
                return new Parts(menu, tool, preview, tag, plans, marks, messages, buildMessages, inspection, structures, bars);
            }

            orders.Fail("the view has no wall in its build menu, or lacks one of the parts that show walls.");
            return null;
        }
    }
}
