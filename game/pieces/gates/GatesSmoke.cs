using Godot;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Vision;

namespace Game;

/// <summary>
/// The smoke test's gates: the local player orders a gate through the build menu as a player
/// does and draws a run of wall across it, and the part checks what the view shows at each
/// step. A worker is sent into the gate and away again, and the door is open and shut with it.
/// A gate of the other seat is seen shut.
/// </summary>
/// <remarks>
/// <para>
/// The rules of gates are tested with the simulation. What is checked here is that the menu
/// offers the gate though the roster does not list it, that the gate is drawn as a gate in its
/// wall, turned the way the wall runs, that the panel and the minimap know it, that the view
/// asks the player's own grid where the player's units can go, and that the door on the screen
/// follows the units the player sees.
/// </para>
/// <para>
/// The pointer is not used, as in the parts for construction and for walls. The gate of the
/// other seat is laid with the development placement, since nobody plays that seat. While the
/// rules have that switch off, the part leaves that step out and says so.
/// </para>
/// </remarks>
public sealed class GatesSmoke : SmokePart
{
    private const int Length = 600;

    // How long an order may take to be carried out, how long a worker may take to walk to its
    // work, and how long the gate and the wall beside it may take to stand.
    private const int TicksToTake = 60;
    private const int TicksToWalk = 320;
    private const int TicksToBuild = 640;

    // How long what the view draws may be behind the match.
    private const int TicksToDraw = 3;

    // How far from its worker a site may be looked for, and how far a worker is sent from the gate.
    private const int FarthestSite = 24;
    private const int Away = 5;

    // The bits of a tile's joins toward the tile east of it and toward the one west.
    private const int East = 1 << 2;
    private const int West = 1 << 6;

    private readonly List<(int X, int Y)> _tiles = [];
    private readonly List<SeenStructure> _listed = [];

    private Step _step = Step.Open;
    private int _deadline = Length;
    private int _drawnBy;
    private StructureDef _gate = null!;
    private StructureDef _wall = null!;
    private EntityId _first;
    private EntityId _second;
    private int _other;
    private (int X, int Y) _site;
    private (int X, int Y) _theirs;
    private bool _sawUnfinished;
    private bool _withOtherSeat;

    private enum Step
    {
        Open,
        Ordered,
        Building,
        InGate,
        Left,
        Theirs,
        Done,
    }

    public override string Name => "Gates";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (orders.Failed || _step == Step.Done)
        {
            return;
        }

        if (orders.Match.Data.Gate is not { } gate || orders.Match.Walls.Wall is not { } wall)
        {
            _step = Step.Done;
            GD.Print("Smoke test, gates: left out, since the data has no wall and so no gate.");
            return;
        }

        if (Parts.Of(orders) is not { } parts)
        {
            return;
        }

        if (tick == Ticks - 1 || tick > _deadline)
        {
            orders.Fail($"gates did not get past the step {_step} in time: at tick {tick} of its part.");
            return;
        }

        _gate = gate;
        _wall = wall;
        switch (_step)
        {
            case Step.Open:
                Open(tick, orders, parts);
                break;
            case Step.Ordered:
                Ordered(tick, orders, parts);
                break;
            case Step.Building:
                Building(tick, orders, parts);
                break;
            case Step.InGate:
                InGate(tick, orders, parts);
                break;
            case Step.Left:
                Left(tick, orders, parts);
                break;
            case Step.Theirs:
                Theirs(tick, orders, parts);
                break;
        }
    }

    // Selects a worker, opens the menu, finds the gate in it though the roster does not list
    // it, and orders one on the middle tile of three that a wall can be drawn across.
    private void Open(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        _other = (orders.Player + 1) % match.Players.Count;
        _withOtherSeat = match.Data.Rules.Debug.Spawn && match.Players.Count > 1;
        var workers = Array.FindAll(orders.OwnUnits(), match.Workers.Contains);
        if (workers.Length < 2)
        {
            orders.Fail("the local player has fewer than two workers to build a gate and its wall with.");
            return;
        }

        _first = workers[0];
        _second = workers[1];
        view.Selection.Set([_first]);
        parts.Menu.Open();
        var checks = BuildChecks.For(view);
        var problem = checks.Structure(_gate);
        if (!parts.Menu.IsOpen || parts.Menu.AddedCount == 0 || parts.Menu.ProblemShownFor(_gate) != problem || problem != BuildProblem.None)
        {
            orders.Fail($"the build menu shows the gate as {parts.Menu.ProblemShownFor(_gate)} and the rules say {problem}, with {match.Stockpiles.Materials(orders.Player) / 1000} Materials.");
            return;
        }

        if (!match.Bodies.TryGet(_first, out var body) || FindSite(match, orders.Player, body.Position) is not { } site)
        {
            orders.Fail("no three tiles near the local player's first worker take a gate with a wall on either side.");
            return;
        }

        _site = site;
        var placement = Placement.Of(checks, _gate, site.X, site.Y);
        if (!placement.IsAccepted || parts.Placement.ShapeAt?.Invoke(_gate, placement) is null)
        {
            orders.Fail($"the site of the gate is shown as {placement.Problem}, or the gate under the pointer has no shape of its own.");
            return;
        }

        if (!parts.Menu.Choose(_gate) || parts.Menu.Placing?.Index != _gate.Index)
        {
            orders.Fail("the build menu did not take the gate in hand.");
            return;
        }

        if (!parts.Menu.Place(_gate, site.X, site.Y) || parts.Menu.IsOpen)
        {
            orders.Fail("the build menu did not send the order for the gate, or stayed open after it.");
            return;
        }

        _deadline = tick + TicksToWalk;
        _step = Step.Ordered;
    }

    // The foundation of the gate is laid: it is drawn as a gate that is not built yet, shut to
    // its owner too, and the panel says so. Then the second worker draws a run across it, and
    // the run pays for the two tiles beside the gate.
    private void Ordered(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        var gate = match.WallMap.WallAt(_site.X, _site.Y);
        if (gate == EntityId.None)
        {
            return;
        }

        if (!IsDrawn(tick, parts.Structures.ShapeOf(gate) is { } drawn && GateShapes.IsGate(drawn) && parts.Structures.DrawnBuilt(gate) is not null))
        {
            return;
        }

        if (GateShapes.IsOpen(parts.Structures.ShapeOf(gate) ?? 0) || match.Grid.For(orders.Player).IsPassable(_site.X, _site.Y))
        {
            orders.Fail("a gate that is still building itself is drawn open, or is ground its owner's units can walk on.");
            return;
        }

        view.Selection.Set([gate]);
        var text = parts.Inspection.ShownText();
        if (parts.Inspection.Mode != WallInspectionMode.One
            || parts.Inspection.Gates != 1
            || !view.Sight.TryGetStructure(gate, out var seen)
            || !text.Contains(StructureWords.NameOf(_gate), StringComparison.Ordinal)
            || !text.Contains(GateWords.OwnFacts(_gate, seen), StringComparison.Ordinal)
            || !CancelBuild.Applies(view))
        {
            orders.Fail($"a selected gate that is building itself is shown as {parts.Inspection.Mode}: '{text.ReplaceLineEndings(" / ")}', or cannot be cancelled.");
            return;
        }

        view.Selection.Set([_second]);
        parts.Menu.Open();
        var tool = parts.Tool;
        var from = Middle((_site.X - 1, _site.Y));
        var to = Middle((_site.X + 1, _site.Y));
        if (!parts.Menu.Choose(tool))
        {
            orders.Fail("the build menu did not take the wall in hand for a run across the gate.");
            return;
        }

        EntityId[] workers = [_second];
        tool.Point(view, from, workers);
        tool.Press(view);
        tool.Point(view, to, workers);
        var plan = tool.Plan;
        tool.Back(view);
        if (plan is not { IsAccepted: true, Laid: 2, Standing: 1, LeftOut: 0 } || plan.Materials != 2 * _wall.Materials)
        {
            orders.Fail($"a run across a gate is shown as {plan?.Laid} tiles to lay, {plan?.Standing} standing and {plan?.LeftOut} left out, for {plan?.Materials} Materials.");
            return;
        }

        if (!parts.Menu.PlaceWithTool(from, to))
        {
            orders.Fail("the build menu did not send the order for the wall across the gate.");
            return;
        }

        _deadline = tick + TicksToBuild;
        _step = Step.Building;
    }

    // The gate and the wall on either side of it stand: one wall, with the gate turned the way
    // it runs. The gate is ground to its owner's units and to nobody else's. Then one worker is
    // sent into the gate and the other away from it.
    private void Building(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        var gate = match.WallMap.WallAt(_site.X, _site.Y);
        var west = match.WallMap.WallAt(_site.X - 1, _site.Y);
        var east = match.WallMap.WallAt(_site.X + 1, _site.Y);
        if (gate == EntityId.None)
        {
            orders.Fail("the gate is gone before it was built.");
            return;
        }

        _sawUnfinished |= parts.Structures.DrawnBuilt(gate) is not null;
        if (west == EntityId.None || east == EntityId.None || IsUnfinished(match, gate) || IsUnfinished(match, west) || IsUnfinished(match, east))
        {
            return;
        }

        var shape = parts.Structures.ShapeOf(gate) ?? 0;
        if (!IsDrawn(
            tick,
            GateShapes.IsGate(shape)
                && WallShapes.JoinsOf(shape) == (East | West)
                && parts.Structures.DrawnBuilt(gate) is null
                && WallShapes.JoinsOf(parts.Structures.ShapeOf(west) ?? 0) == East
                && WallShapes.JoinsOf(parts.Structures.ShapeOf(east) ?? 0) == West))
        {
            return;
        }

        if (!_sawUnfinished || GateShapes.RunsAlongY(shape) || GateShapes.IsGate(parts.Structures.ShapeOf(west) ?? 0))
        {
            orders.Fail("the gate was never drawn unfinished, or stands across the wall that runs through it, or a wall tile beside it is drawn as a gate.");
            return;
        }

        if (!match.Grid.For(orders.Player).IsPassable(_site.X, _site.Y)
            || match.Grid.For(_other).IsPassable(_site.X, _site.Y)
            || match.Grid.IsPassable(_site.X, _site.Y))
        {
            orders.Fail("a finished gate is not ground to its owner's units alone.");
            return;
        }

        new FogOnMinimap(view).CollectStructures(view, _listed);
        if (!_listed.Exists(listed => listed.Entity == gate && listed.Type == _gate.Index))
        {
            orders.Fail("a finished gate of the local player's is not among what the minimap shows.");
            return;
        }

        view.Selection.Set([gate]);
        var one = parts.Inspection.ShownText();
        var isOne = parts.Inspection.Mode == WallInspectionMode.One
            && view.Sight.TryGetStructure(gate, out var seen)
            && one.Contains(GateWords.OwnFacts(_gate, seen), StringComparison.Ordinal)
            && one.Contains(GateWords.PieceOf(3, own: true), StringComparison.Ordinal);
        view.Selection.Set([gate, west]);
        var two = parts.Inspection.ShownText();
        if (!isOne
            || parts.Inspection.Mode != WallInspectionMode.Several
            || parts.Inspection.Gates != 1
            || !two.Contains(GateWords.TitleOf(_wall, 2, 1), StringComparison.Ordinal))
        {
            orders.Fail($"a finished gate is shown as '{one.ReplaceLineEndings(" / ")}', and with a tile of its wall as '{two.ReplaceLineEndings(" / ")}'.");
            return;
        }

        view.Selection.Clear();
        if (FindFar(match, orders.Player, _second) is not { } far)
        {
            orders.Fail("no ground away from the gate for the workers to go to.");
            return;
        }

        var inGate = MapGrid.TileCenter(_site.X, _site.Y);
        orders.Submit(new Command(CommandType.Move, orders.Player, [_first], inGate.X, inGate.Y));
        orders.Submit(new Command(CommandType.Move, orders.Player, [_second], far.X, far.Y));
        _deadline = tick + TicksToWalk;
        _step = Step.InGate;
    }

    // The worker stands in its owner's gate, and the door is drawn open. Then it is sent away.
    private void InGate(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var gate = match.WallMap.WallAt(_site.X, _site.Y);
        if (!match.Bodies.TryGet(_first, out var body) || TileOf(body.Position) != _site)
        {
            return;
        }

        if (!IsDrawn(tick, parts.Structures.Walls.IsOpen(_site.X, _site.Y) && GateShapes.IsOpen(parts.Structures.ShapeOf(gate) ?? 0)))
        {
            return;
        }

        if (FindFar(match, orders.Player, _first) is not { } far)
        {
            orders.Fail("no ground away from the gate for the worker in it to go to.");
            return;
        }

        orders.Submit(new Command(CommandType.Move, orders.Player, [_first], far.X, far.Y));
        _deadline = tick + TicksToWalk;
        _step = Step.Left;
    }

    // No unit of the owner's is by the gate any more, and the door is drawn shut. Then the
    // other seat gets a gate where the player sees it.
    private void Left(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var gate = match.WallMap.WallAt(_site.X, _site.Y);
        if (IsUnitNear(match, _site, 1, orders.Player))
        {
            return;
        }

        if (!IsDrawn(tick, !parts.Structures.Walls.IsOpen(_site.X, _site.Y) && !GateShapes.IsOpen(parts.Structures.ShapeOf(gate) ?? -1)))
        {
            return;
        }

        if (!_withOtherSeat)
        {
            Finish(tick, orders);
            return;
        }

        if (FindSeenTile(orders) is not { } tile)
        {
            orders.Fail("no free tile in the local player's sight takes a gate of the other seat's.");
            return;
        }

        _theirs = tile;
        orders.SubmitFor(new Command(CommandType.DebugPlace, _other, targetX: Fixed.FromInt(tile.X), targetY: Fixed.FromInt(tile.Y), contentId: _gate.Index));
        _deadline = tick + TicksToTake;
        _step = Step.Theirs;
    }

    // The other seat's gate is seen as that seat's: a gate, shut, no ground to the local
    // player's units, and the panel says whose units it lets through.
    private void Theirs(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        var theirs = match.WallMap.WallAt(_theirs.X, _theirs.Y);
        if (theirs == EntityId.None)
        {
            return;
        }

        if (!IsDrawn(tick, GateShapes.IsGate(parts.Structures.ShapeOf(theirs) ?? 0)))
        {
            return;
        }

        if (!view.Sight.TryGetStructure(theirs, out var seen)
            || seen.Owner != _other
            || GateShapes.IsOpen(parts.Structures.ShapeOf(theirs) ?? 0)
            || match.Grid.For(orders.Player).IsPassable(_theirs.X, _theirs.Y)
            || !match.Grid.For(_other).IsPassable(_theirs.X, _theirs.Y))
        {
            orders.Fail("a gate of the other seat's is not seen as that seat's, or is drawn open with none of its owner's units by it, or is ground to the local player's units.");
            return;
        }

        view.Selection.Clear();
        parts.Inspection.Inspect(theirs);
        var text = parts.Inspection.ShownText();
        var mode = parts.Inspection.Mode;
        var gates = parts.Inspection.Gates;
        parts.Inspection.Inspect(EntityId.None);
        if (mode != WallInspectionMode.Other
            || gates != 1
            || !text.Contains(InspectionWords.Owner(_other), StringComparison.Ordinal)
            || !text.Contains(GateWords.OthersFacts(seen), StringComparison.Ordinal))
        {
            orders.Fail($"a gate of the other seat's is shown as {mode}: '{text.ReplaceLineEndings(" / ")}'.");
            return;
        }

        Finish(tick, orders);
    }

    private void Finish(int tick, SmokeOrders orders)
    {
        orders.View.Selection.Clear();
        _step = Step.Done;
        var rest = _withOtherSeat
            ? "the other seat's gate was seen shut, with its panel"
            : "the step with the other seat's gate was left out, since the rules have the development placement off";
        GD.Print($"Smoke test, gates: a gate was ordered through the build menu and built with a run of wall drawn across it, as one wall of 3; it opened to a worker of its owner's and shut behind it, by tick {tick} of the part; {rest}.");
    }

    private static bool IsUnfinished(Match match, EntityId structure) => match.Foundations.Contains(structure);

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

    private static Vector2 Middle((int X, int Y) tile) => new(tile.X + 0.5f, tile.Y + 0.5f);

    private static (int X, int Y) TileOf(FixedVector2 position) => (position.X.FloorToInt(), position.Y.FloorToInt());

    // True when a unit stands within so many tiles of a tile: of this player, or of anybody for -1.
    private static bool IsUnitNear(Match match, (int X, int Y) tile, int reach, int player = -1)
    {
        var movers = match.Movers;
        for (var i = 0; i < movers.Count; i++)
        {
            var unit = movers.EntityAt(i);
            if (!match.World.Entities.IsAlive(unit) || !match.Bodies.TryGet(unit, out var body) || (player >= 0 && body.Owner != player))
            {
                continue;
            }

            var (x, y) = TileOf(body.Position);
            if (Math.Max(Math.Abs(x - tile.X), Math.Abs(y - tile.Y)) <= reach)
            {
                return true;
            }
        }

        return false;
    }

    // True when a wall tile of anybody's stands within so many tiles of a tile.
    private static bool IsWallNear(Match match, (int X, int Y) tile, int reach)
    {
        for (var dy = -reach; dy <= reach; dy++)
        {
            for (var dx = -reach; dx <= reach; dx++)
            {
                if (match.WallMap.WallAt(tile.X + dx, tile.Y + dy) != EntityId.None)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The nearest tile for a gate with a free tile west and east of it: three tiles of which
    // the rules would lay a wall on each now, open ground above and below the middle one for
    // whoever goes through, and no wall and no unit near, so that what is drawn there is this
    // part's alone.
    private (int X, int Y)? FindSite(Match match, int player, FixedVector2 near)
    {
        var (nearX, nearY) = TileOf(near);
        var grid = match.Grid;
        for (var away = 3; away <= FarthestSite; away++)
        {
            for (var dy = -away; dy <= away; dy++)
            {
                for (var dx = -away; dx <= away; dx++)
                {
                    var x = nearX + dx;
                    var y = nearY + dy;
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != away
                        || !grid.IsPassable(x, y - 1)
                        || !grid.IsPassable(x, y + 1)
                        || match.Construction.CheckSite(player, _gate, x, y) != BuildProblem.None
                        || IsWallNear(match, (x, y), 3)
                        || IsUnitNear(match, (x, y), 2))
                    {
                        continue;
                    }

                    _tiles.Clear();
                    if (match.Walls.TilesToLay(player, x - 1, y, x + 1, y, _tiles) == BuildProblem.None && _tiles.Count == 3)
                    {
                        return (x, y);
                    }
                }
            }
        }

        return null;
    }

    // A point the unit can walk to, a few tiles from the gate and from every tile beside it.
    private FixedVector2? FindFar(Match match, int player, EntityId unit)
    {
        if (!match.Bodies.TryGet(unit, out var body))
        {
            return null;
        }

        var grid = match.Grid.For(player);
        var region = grid.RegionOf(body.Position);
        for (var away = Away; away <= Away + 6; away++)
        {
            for (var dy = -away; dy <= away; dy++)
            {
                for (var dx = -away; dx <= away; dx++)
                {
                    var x = _site.X + dx;
                    var y = _site.Y + dy;
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == away && grid.IsPassable(x, y) && grid.RegionOf(x, y) == region)
                    {
                        return MapGrid.TileCenter(x, y);
                    }
                }
            }
        }

        return null;
    }

    // A free tile the local player sees now, with no wall and no unit near it, for a gate of
    // the other seat's that stands alone.
    private (int X, int Y)? FindSeenTile(SmokeOrders orders)
    {
        var match = orders.Match;
        var sight = orders.View.Sight;
        for (var away = 4; away <= FarthestSite; away++)
        {
            for (var dy = -away; dy <= away; dy++)
            {
                for (var dx = -away; dx <= away; dx++)
                {
                    var tile = (X: _site.X + dx, Y: _site.Y + dy);
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == away
                        && sight.FogAt(tile.X, tile.Y) == FogState.Visible
                        && match.HasRoomFor(_gate, tile.X, tile.Y)
                        && !IsWallNear(match, tile, 2)
                        && !IsUnitNear(match, tile, 2))
                    {
                        return tile;
                    }
                }
            }
        }

        return null;
    }

    private sealed record Parts(BuildMenu Menu, WallTool Tool, PlacementPreview Placement, WallInspection Inspection, StructureLayer Structures)
    {
        public static Parts? Of(SmokeOrders orders)
        {
            var view = orders.View;
            var layers = view.World.GetChildren();
            var parts = view.Interface.Parts;
            if (parts.OfType<BuildMenu>().FirstOrDefault() is { } menu
                && menu.ToolOf<WallTool>() is { } tool
                && layers.OfType<PlacementPreview>().FirstOrDefault() is { } placement
                && parts.OfType<WallInspection>().FirstOrDefault() is { } inspection
                && layers.OfType<StructureLayer>().FirstOrDefault() is { } structures)
            {
                return new Parts(menu, tool, placement, inspection, structures);
            }

            orders.Fail("the view has no wall in its build menu, or lacks one of the parts that show gates.");
            return null;
        }
    }
}
