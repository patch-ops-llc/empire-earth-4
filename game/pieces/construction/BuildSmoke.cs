using Godot;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Setup;

namespace Game;

/// <summary>
/// The smoke test's construction: the local player builds through the build menu as a player
/// does, and the part checks what the view shows at each step. One structure is built to the
/// end. While it builds itself, a second order is cancelled while its worker is on the way, and
/// a third after its foundation is laid. A finished Generator is then given a worker.
/// </summary>
/// <remarks>
/// <para>
/// The orders leave through the menu and the cancel command themselves, not through the smoke
/// test's own way of giving an order, so that the path a click takes is the path tested. The
/// rules of construction are tested with the simulation. What is checked here is that the
/// menu's entries say what the rules say, that the preview marks the tiles the rules refuse,
/// that a site, a foundation and its progress are drawn while they are there and not after,
/// and that a finished structure is drawn finished.
/// </para>
/// <para>
/// The pointer is not used: a headless run has none worth trusting. The part chooses in the
/// menu and places at a tile, which is what a key and a click come to.
/// </para>
/// </remarks>
public sealed class BuildSmoke : SmokePart
{
    private const int Length = 680;

    // The menu is left open for more than a frame's ticks before the first structure is placed,
    // so that it and the preview are drawn at least once.
    private const int TicksOpen = 25;

    // How long a worker may take to reach a site and lay the foundation, and how long a cancel
    // may take to be carried out.
    private const int TicksToLay = 320;
    private const int TicksToCancel = 60;

    // The second site is at least this far from its worker, so that the order is seen on the way.
    private const int LeastWalk = 6;
    private const int FarthestSite = 22;

    private Step _step = Step.Open;
    private int _deadline = Length;
    private int _openedAt;
    private StructureDef _structure = null!;
    private EntityId _firstWorker;
    private EntityId _secondWorker;
    private (int X, int Y) _firstSite;
    private (int X, int Y) _secondSite;
    private EntityId _first;
    private EntityId _third;
    private bool _sawBar;
    private bool _sawProgress;

    private enum Step
    {
        Open,
        Place,
        FirstFoundation,
        SecondOrder,
        SecondCancelled,
        ThirdFoundation,
        ThirdCancelled,
        FirstFinished,
        AtWork,
        Done,
    }

    public override string Name => "Construction";

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
            orders.Fail($"construction did not get past the step {_step} in time: at tick {tick} of its part.");
            return;
        }

        if (_first != EntityId.None)
        {
            _sawBar |= parts.Bars.BarCount > 0;
            _sawProgress |= parts.Structures.DrawnBuilt(_first) is > 0.2f;
        }

        switch (_step)
        {
            case Step.Open:
                Open(tick, orders, parts);
                break;
            case Step.Place:
                PlaceFirst(tick, orders, parts);
                break;
            case Step.FirstFoundation:
                FirstFoundation(tick, orders, parts);
                break;
            case Step.SecondOrder:
                SecondOrder(tick, orders, parts);
                break;
            case Step.SecondCancelled:
                SecondCancelled(tick, orders, parts);
                break;
            case Step.ThirdFoundation:
                ThirdFoundation(tick, orders, parts);
                break;
            case Step.ThirdCancelled:
                ThirdCancelled(orders, parts);
                break;
            case Step.FirstFinished:
                FirstFinished(tick, orders, parts);
                break;
            case Step.AtWork:
                AtWork(tick, orders);
                break;
        }
    }

    // Selects a worker, opens the menu and starts placing the structure that is quickest to build.
    private void Open(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        var workers = Array.FindAll(orders.OwnUnits(), match.Workers.Contains);
        if (workers.Length < 2)
        {
            orders.Fail("the local player has fewer than two workers to build with.");
            return;
        }

        _firstWorker = workers[0];
        _secondWorker = workers[1];
        view.Selection.Set([_firstWorker]);
        parts.Menu.Open();
        if (!parts.Menu.IsOpen)
        {
            orders.Fail("the build menu did not open with a worker selected.");
            return;
        }

        var checks = BuildChecks.For(orders.View);
        StructureDef? quickest = null;
        var listed = 0;
        foreach (var structure in match.Data.Structures)
        {
            if (!checks.Lists(structure))
            {
                continue;
            }

            listed++;
            var problem = checks.Structure(structure);
            if (parts.Menu.ProblemShownFor(structure) != problem)
            {
                orders.Fail($"the build menu shows the {structure.Id} as {parts.Menu.ProblemShownFor(structure)} and the rules say {problem}.");
                return;
            }

            if (problem == BuildProblem.None
                && ConstructionRules.NeedsTerritory(structure)
                && (quickest is null || structure.BuildTicks < quickest.BuildTicks))
            {
                quickest = structure;
            }
        }

        if (listed != parts.Menu.EntryCount)
        {
            orders.Fail($"the build menu lists {parts.Menu.EntryCount} structures and a worker places {listed}.");
            return;
        }

        if (quickest is null)
        {
            orders.Fail("the local player can order no structure that stands in territory.");
            return;
        }

        _structure = quickest;
        if (!match.Bodies.TryGet(_firstWorker, out var body)
            || FindSite(match, orders.Player, _structure, body.Position.X.FloorToInt(), body.Position.Y.FloorToInt(), 2, null) is not { } site)
        {
            orders.Fail($"no site near the local player's first worker takes a {_structure.Id}.");
            return;
        }

        _firstSite = site;
        if (!parts.Menu.Choose(_structure) || parts.Menu.Placing != _structure)
        {
            orders.Fail($"the build menu did not start placing the {_structure.Id}.");
            return;
        }

        if (!CheckPreview(orders, parts))
        {
            return;
        }

        _openedAt = tick;
        _step = Step.Place;
    }

    // The preview marks no tile of a good site, and every tile of a site on the player's own Capitol.
    private bool CheckPreview(SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var checks = BuildChecks.For(orders.View);
        var good = Placement.Of(checks, _structure, _firstSite.X, _firstSite.Y);
        parts.Preview.Show(_structure, good);
        parts.Preview.EveryFrame(0.0);
        if (!good.IsAccepted || parts.Preview.BlockedTiles != 0)
        {
            orders.Fail($"the preview marks {parts.Preview.BlockedTiles} tiles of a site the rules accept.");
            return false;
        }

        for (var i = 0; i < match.Structures.Count; i++)
        {
            var entity = match.Structures.EntityAt(i);
            var placed = match.Structures.ValueAt(i);
            if (!match.Bodies.TryGet(entity, out var body) || body.Owner != orders.Player || placed.Size < _structure.Footprint)
            {
                continue;
            }

            var taken = Placement.Of(checks, _structure, placed.X, placed.Y);
            parts.Preview.Show(_structure, taken);
            parts.Preview.EveryFrame(0.0);
            var tiles = _structure.Footprint * _structure.Footprint;
            if (taken.IsAccepted || parts.Preview.BlockedTiles != tiles)
            {
                orders.Fail($"the preview marks {parts.Preview.BlockedTiles} of the {tiles} tiles of a site on a structure, which the rules refuse.");
                return false;
            }

            break;
        }

        parts.Preview.Clear();
        return true;
    }

    private void PlaceFirst(int tick, SmokeOrders orders, Parts parts)
    {
        if (tick < _openedAt + TicksOpen)
        {
            return;
        }

        if (parts.Menu.Placing != _structure)
        {
            orders.Fail($"the build menu stopped placing the {_structure.Id} by itself.");
            return;
        }

        if (!parts.Menu.Place(_structure, _firstSite.X, _firstSite.Y) || parts.Menu.IsOpen)
        {
            orders.Fail($"the build menu did not send the order for the {_structure.Id}, or stayed open after it.");
            return;
        }

        _deadline = tick + TicksToLay;
        _step = Step.FirstFoundation;
    }

    // The first foundation is laid: it is drawn unfinished and its line says how far it is.
    // Then the second worker is sent to a site of its own.
    private void FirstFoundation(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        var view = orders.View;
        if (FoundationAt(match, orders.Player, _firstSite) is not { } foundation)
        {
            return;
        }

        _first = foundation;
        if (parts.Structures.DrawnBuilt(_first) is null)
        {
            orders.Fail("a structure that is building itself is drawn as finished.");
            return;
        }

        view.Selection.Set([_first]);
        if (parts.Readout.Shown is null || !CancelBuild.Applies(view))
        {
            orders.Fail("a selected foundation has no line about its progress, or cannot be cancelled.");
            return;
        }

        if (!match.Bodies.TryGet(_secondWorker, out var body)
            || FindSite(match, orders.Player, _structure, body.Position.X.FloorToInt(), body.Position.Y.FloorToInt(), LeastWalk, _firstSite) is not { } site)
        {
            orders.Fail($"no second site takes a {_structure.Id}.");
            return;
        }

        _secondSite = site;
        if (!PlaceSecond(orders, parts))
        {
            return;
        }

        _deadline = tick + TicksToLay;
        _step = Step.SecondOrder;
    }

    // Sends the second worker to the second site, through the menu.
    private bool PlaceSecond(SmokeOrders orders, Parts parts)
    {
        orders.View.Selection.Set([_secondWorker]);
        parts.Menu.Open();
        if (!parts.Menu.Choose(_structure) || !parts.Menu.Place(_structure, _secondSite.X, _secondSite.Y))
        {
            orders.Fail($"the build menu did not send a second order for the {_structure.Id}.");
            return false;
        }

        return true;
    }

    // The second worker is on its way: the site is drawn, and the order is cancelled.
    private void SecondOrder(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        if (FoundationAt(match, orders.Player, _secondSite) is not null)
        {
            orders.Fail("the second foundation was laid before its site was seen.");
            return;
        }

        if (!match.BuildOrders.TryGet(_secondWorker, out var order))
        {
            return;
        }

        if (!parts.Sites.Draws(_secondWorker, order))
        {
            orders.Fail("the site a worker is walking to is not drawn.");
            return;
        }

        if (!CancelBuild.Give(orders.View))
        {
            orders.Fail("the cancel command gave no order for a selected worker with a build order.");
            return;
        }

        _deadline = tick + TicksToCancel;
        _step = Step.SecondCancelled;
    }

    // The order is gone and so is its site. The same worker is sent again, and this time arrives.
    private void SecondCancelled(int tick, SmokeOrders orders, Parts parts)
    {
        if (orders.Match.BuildOrders.Contains(_secondWorker))
        {
            return;
        }

        if (parts.Sites.Count != OwnOrders(orders))
        {
            orders.Fail("the site of a cancelled build order is still drawn.");
            return;
        }

        if (!PlaceSecond(orders, parts))
        {
            return;
        }

        _deadline = tick + TicksToLay;
        _step = Step.ThirdFoundation;
    }

    // The second site has a foundation: it is selected and cancelled.
    private void ThirdFoundation(int tick, SmokeOrders orders, Parts parts)
    {
        if (FoundationAt(orders.Match, orders.Player, _secondSite) is not { } foundation)
        {
            return;
        }

        _third = foundation;
        if (parts.Structures.DrawnBuilt(_third) is null)
        {
            orders.Fail("the second structure that is building itself is drawn as finished.");
            return;
        }

        orders.View.Selection.Set([_third]);
        if (!CancelBuild.Give(orders.View))
        {
            orders.Fail("the cancel command gave no order for a selected foundation.");
            return;
        }

        _deadline = tick + TicksToCancel;
        _step = Step.ThirdCancelled;
    }

    private void ThirdCancelled(SmokeOrders orders, Parts parts)
    {
        if (orders.Match.Structures.Contains(_third))
        {
            return;
        }

        if (parts.Structures.Draws(_third))
        {
            orders.Fail("a cancelled foundation is still drawn.");
            return;
        }

        _deadline = Length;
        _step = Step.FirstFinished;
    }

    // The first structure has built itself: it is drawn finished, and it was seen filling on the way.
    private void FirstFinished(int tick, SmokeOrders orders, Parts parts)
    {
        var match = orders.Match;
        if (!match.Structures.Contains(_first))
        {
            orders.Fail("the structure that was building itself is gone.");
            return;
        }

        if (!match.Construction.IsFinished(_first))
        {
            return;
        }

        if (!parts.Structures.Draws(_first) || parts.Structures.DrawnBuilt(_first) is not null)
        {
            orders.Fail("a structure that has built itself is still drawn unfinished.");
            return;
        }

        if (!_sawBar || !_sawProgress)
        {
            orders.Fail("a structure built itself without a bar over it, or without being drawn any further than laid.");
            return;
        }

        if (parts.Messages.Count != 0)
        {
            orders.Fail($"construction that went as ordered gave {parts.Messages.Count} messages about orders that did not.");
            return;
        }

        if (!_structure.IsEnergyWorkSite)
        {
            Finish(tick, orders, worked: false);
            return;
        }

        // The order button on the finished Generator, with its worker selected.
        var view = orders.View;
        view.Selection.Set([_firstWorker]);
        var target = new PointerTarget(PointerTargets.OwnStructure, _first, match.Structures.TryGet(_first, out var placed) ? placed.Tiles.Center : default);
        if (view.Orders.Give(view, target)?.Name != GeneratorWork.Row().Name)
        {
            orders.Fail("the order button on a finished Generator did not send the selected worker to work there.");
            return;
        }

        _deadline = tick + TicksToCancel;
        _step = Step.AtWork;
    }

    private void AtWork(int tick, SmokeOrders orders)
    {
        if (orders.Match.Workers.TryGet(_firstWorker, out var worker) && worker.Site == _first)
        {
            Finish(tick, orders, worked: true);
        }
    }

    private void Finish(int tick, SmokeOrders orders, bool worked)
    {
        orders.View.Selection.Clear();
        _step = Step.Done;
        var work = worked ? ", and a worker was sent to work at it" : string.Empty;
        GD.Print($"Smoke test, construction: a {_structure.Id} built itself by tick {tick} of the part{work}; one order was cancelled on the way and one after its foundation was laid.");
    }

    private static int OwnOrders(SmokeOrders orders)
    {
        var match = orders.Match;
        var count = 0;
        for (var i = 0; i < match.BuildOrders.Count; i++)
        {
            if (match.Bodies.TryGet(match.BuildOrders.EntityAt(i), out var body) && body.Owner == orders.Player)
            {
                count++;
            }
        }

        return count;
    }

    // The player's structure that is building itself with its lowest tile here, or null.
    private static EntityId? FoundationAt(Match match, int player, (int X, int Y) site)
    {
        for (var i = 0; i < match.Foundations.Count; i++)
        {
            var entity = match.Foundations.EntityAt(i);
            if (match.Structures.TryGet(entity, out var placed)
                && placed.X == site.X
                && placed.Y == site.Y
                && match.Bodies.TryGet(entity, out var body)
                && body.Owner == player)
            {
                return entity;
            }
        }

        return null;
    }

    // The nearest site to a tile, at least so many tiles from it, where a worker that arrived
    // now would lay the foundation, and which leaves a tile free round another site.
    private static (int X, int Y)? FindSite(Match match, int player, StructureDef structure, int nearX, int nearY, int least, (int X, int Y)? apartFrom)
    {
        for (var away = least; away <= FarthestSite; away++)
        {
            for (var dy = -away; dy <= away; dy++)
            {
                for (var dx = -away; dx <= away; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != away)
                    {
                        continue;
                    }

                    var x = nearX + dx;
                    var y = nearY + dy;
                    if (apartFrom is { } other
                        && Math.Abs(x - other.X) <= structure.Footprint
                        && Math.Abs(y - other.Y) <= structure.Footprint)
                    {
                        continue;
                    }

                    if (match.Construction.CheckArrival(player, structure, x, y) == BuildProblem.None)
                    {
                        return (x, y);
                    }
                }
            }
        }

        return null;
    }

    // What the piece added to the view.
    private sealed record Parts(
        BuildMenu Menu,
        PlacementPreview Preview,
        BuildSiteLayer Sites,
        FoundationBars Bars,
        StructureLayer Structures,
        FoundationReadout Readout,
        BuildMessages Messages)
    {
        public static Parts? Of(SmokeOrders orders)
        {
            var view = orders.View;
            var layers = view.World.GetChildren();
            if (view.Interface.Parts.OfType<BuildMenu>().FirstOrDefault() is { } menu
                && layers.OfType<PlacementPreview>().FirstOrDefault() is { } preview
                && layers.OfType<BuildSiteLayer>().FirstOrDefault() is { } sites
                && layers.OfType<FoundationBars>().FirstOrDefault() is { } bars
                && layers.OfType<StructureLayer>().FirstOrDefault() is { } structures
                && view.Interface.Parts.OfType<FoundationReadout>().FirstOrDefault() is { } readout
                && view.Interface.Parts.OfType<BuildMessages>().FirstOrDefault() is { } messages)
            {
                return new Parts(menu, preview, sites, bars, structures, readout, messages);
            }

            orders.Fail("the view has no build menu, or lacks one of the layers that draw construction.");
            return null;
        }
    }
}
