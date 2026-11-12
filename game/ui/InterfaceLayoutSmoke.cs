using System.Globalization;
using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The smoke test's look at where the interface stands: the frame is filled as full as a match
/// fills it, four times over, and asked each time whether a box reaches outside the window or
/// lies over another (<see cref="InterfaceFrame.LayoutProblems"/>).
/// </summary>
/// <remarks>
/// <para>
/// From its first tick on, the window is of 16 to 9: 1,152 by 648 of the interface's units,
/// which is the least room the game gives, since a wider or a taller window only adds units.
/// It stays so for the parts after this one.
/// </para>
/// <para>
/// Each time, every control group is set, so that their row is as long as it gets, and the
/// message line is given as many messages as it holds, none of which fits on one line. Then:
/// the tallest detail the inspection panel has, with a fighting unit selected for its command;
/// a worker with the build menu open; the tallest summary of several kinds, with a selection
/// that has every command; and a structure of the player's own, with what a piece shows for
/// one, as the buttons that train are. The detail and
/// the summary are as wide as the panel may be (<see cref="InspectionPart.WidestShown"/>),
/// which the inspection's own part holds every chassis to. The development aids and readouts
/// are shown throughout, as they are for a tester, with one aid more that is taller than the
/// window, a row stands at the right under the top bar, and two announcements stand under the
/// messages, of which there is room for one over the tallest detail. The part gives no order and
/// leaves the selection, the groups and the message line empty.
/// </para>
/// <para>
/// The smoke test plays 20 ticks in a frame, and what is shown takes its place over the frames
/// after it changes, so each look comes more than three frames after its change.
/// </para>
/// </remarks>
public sealed class InterfaceLayoutSmoke : SmokePart
{
    private const int Settle = 70;

    private const string LongMessage =
        "The Barracks cannot go there: the ground is not level enough for a structure of that size, "
        + "and a deposit lies under a part of it besides. Pick other ground.";

    // A window of 16 to 9, which the interface's 1,152 by 648 units fill exactly.
    private static readonly Vector2I Narrowest = new(1152, 648);

    private readonly List<EntityId> _fighting = [];
    private readonly List<EntityId> _workers = [];
    private EntityId _structure = EntityId.None;
    private bool _held;
    private Control? _aid;
    private Control? _row;
    private readonly List<Control> _announcements = [];

    public override string Name => "Interface layout";

    public override int Ticks => (4 * Settle) + 10;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        var view = orders.View;
        var frame = view.Interface;
        var panel = frame.Parts.OfType<InspectionPart>().FirstOrDefault();
        var menu = frame.Parts.OfType<BuildMenu>().FirstOrDefault();
        switch (tick)
        {
            case 0:
                UseNarrowestWindow(frame);
                FindUnits(orders);
                FillGroups(frame);
                AddTallAid(frame);
                AddRowAtRight(frame);
                AddAnnouncements(frame);

                // As many as the line holds: it has to give some up to stay above the panel.
                for (var i = 0; i < 5; i++)
                {
                    frame.Say(LongMessage, seconds: 30.0);
                }

                if (_fighting.Count > 0)
                {
                    view.Selection.Set([_fighting[0]]);
                }

                if (panel is not null && InspectionSmoke.LongestDetail(orders.Match.Data, panel) is { } tallest)
                {
                    panel.Hold([tallest], widest: true);
                    _held = true;
                }

                break;

            case Settle:
                Look(orders, "the tallest detail of a unit", _held || _fighting.Count > 0 ? "BottomPanel" : null, _held ? "/UnderMessages/" : null);
                CheckHeldWidth(orders);
                CheckAnnouncements(orders);
                panel?.Release();
                _held = false;
                if (_workers.Count > 0 && menu is not null)
                {
                    view.Selection.Set([_workers[0]]);
                    menu.Open();
                }

                break;

            case 2 * Settle:
                Look(orders, "a worker and the open build menu", _workers.Count > 0 ? "BottomPanel" : null, menu is { IsOpen: true } ? "BuildMenuPanel" : null);
                menu?.Close();
                view.Selection.Set([.. _fighting, .. _workers]);
                if (panel is not null && InspectionSmoke.MostKinds(orders.Match.Data) is { Count: > 1 } kinds)
                {
                    panel.Hold(kinds, widest: true);
                    _held = true;
                }

                break;

            case 3 * Settle:
                Look(orders, "the widest summary of several kinds", _held || _fighting.Count + _workers.Count > 0 ? "BottomPanel" : null);
                CheckHeldWidth(orders);
                panel?.Release();
                _held = false;
                _structure = OwnStructure(orders);
                if (_structure != EntityId.None)
                {
                    view.Selection.Set([_structure]);
                }
                else
                {
                    view.Selection.Clear();
                }

                break;

            case 4 * Settle:
                Look(orders, "a structure of the player's own", _structure != EntityId.None ? "BottomPanel" : null);
                view.Selection.Clear();
                EmptyGroups(frame);
                frame.DropMessages();
                _aid?.QueueFree();
                _aid = null;
                _row?.QueueFree();
                _row = null;
                foreach (var announcement in _announcements)
                {
                    announcement.QueueFree();
                }

                _announcements.Clear();
                break;
        }
    }

    // The first structure of the local player's: what a selected structure shows, its card and
    // whatever a piece adds for it, as the buttons that train are, stands in the bottom panel.
    private static EntityId OwnStructure(SmokeOrders orders)
    {
        var match = orders.Match;
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var entity = match.Structures.EntityAt(i);
            if (match.Bodies.TryGet(entity, out var body) && body.Owner == orders.Player)
            {
                return entity;
            }
        }

        return EntityId.None;
    }

    /// <summary>
    /// Gives the game's window the shape with the least room for the interface, 16 to 9, from
    /// now on. The smoke test's own window is square, which is 504 units taller. What is shown
    /// takes its new place over the next frames.
    /// </summary>
    public static void UseNarrowestWindow(Node anything) => anything.GetWindow().Size = Narrowest;

    // What the panel was held to show was as wide as the panel may be, so the look was at the
    // least room the rest can be left with.
    private void CheckHeldWidth(SmokeOrders orders)
    {
        if (!_held)
        {
            return;
        }

        foreach (var (name, rect) in orders.View.Interface.Boxes())
        {
            if (name.EndsWith("BottomPanel", StringComparison.Ordinal) && rect.Size.X < InspectionPart.WidestShown)
            {
                orders.Fail(string.Create(
                    CultureInfo.InvariantCulture,
                    $"the bottom panel is {rect.Size.X:F0} wide with the inspection panel held at {InspectionPart.WidestShown:F0}."));
            }
        }
    }

    // An aid that is taller than the window, so that the column of the aids has to end where
    // the game's own things begin, whatever the aids of the match need.
    private void AddTallAid(InterfaceFrame frame)
    {
        var lines = frame.AddDevelopmentAid("Development aid: smoke test", "Only the smoke test shows this. It is as tall as it is to fill the column.");
        for (var line = 1; line <= 40; line++)
        {
            lines.AddChild(new Label { Text = string.Create(CultureInfo.InvariantCulture, $"Line {line} of 40") });
        }

        _aid = lines.GetParent().GetParent<Control>();
    }

    // Two announcements that pass, each as high as the banner of a new epoch on three lines.
    // Under one message and over the tallest detail there is room for one of them.
    private void AddAnnouncements(InterfaceFrame frame)
    {
        for (var i = 1; i <= 2; i++)
        {
            var announcement = new PanelContainer
            {
                Name = "SmokeAnnouncement",
                CustomMinimumSize = new Vector2(400f, 100f),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            announcement.AddChild(new Label { Text = string.Create(CultureInfo.InvariantCulture, $"Smoke test: announcement {i} of 2") });
            frame.Announce(announcement);
            _announcements.Add(announcement);
        }
    }

    // With the tallest detail shown there is no room for both announcements under the newest
    // message: the older has given way, as the older messages have, and the newer is shown.
    private void CheckAnnouncements(SmokeOrders orders)
    {
        if (!_held || _announcements.Count < 2)
        {
            return;
        }

        if (_announcements[0].Visible || !_announcements[1].Visible)
        {
            orders.Fail(
                $"with the tallest detail of a unit shown, the older announcement is {(_announcements[0].Visible ? "shown" : "hidden")} and the newer is {(_announcements[1].Visible ? "shown" : "hidden")}. " +
                "The older gives way and the newer stays.");
        }
    }

    // A row at the right under the top bar, as wide as the host's pause and speed buttons are,
    // so that the two ends of the rows are shown at once.
    private void AddRowAtRight(InterfaceFrame frame)
    {
        _row = UiStyle.FrameButton("Smoke test: a row at the right");
        _row.CustomMinimumSize = new Vector2(340f, 0f);
        frame.RowsUnderBarAtRight.AddChild(_row);
    }

    private void FindUnits(SmokeOrders orders)
    {
        var match = orders.Match;
        foreach (var entity in orders.OwnUnits())
        {
            if (match.Workers.Contains(entity))
            {
                _workers.Add(entity);
            }
            else if (match.Combatants.Contains(entity))
            {
                _fighting.Add(entity);
            }
        }
    }

    // Every group holds something, so that the row has all its buttons.
    private void FillGroups(InterfaceFrame frame)
    {
        if (frame.Parts.OfType<ControlGroupsPart>().FirstOrDefault() is not { } groups)
        {
            return;
        }

        EntityId[] units = [.. _fighting, .. _workers];
        for (var group = 0; group < ControlGroups.Count && units.Length > 0; group++)
        {
            groups.Groups.Set(group, units.Take(1 + (group % units.Length)));
        }
    }

    private static void EmptyGroups(InterfaceFrame frame)
    {
        if (frame.Parts.OfType<ControlGroupsPart>().FirstOrDefault() is not { } groups)
        {
            return;
        }

        for (var group = 0; group < ControlGroups.Count; group++)
        {
            groups.Groups.Set(group, []);
        }
    }

    // Fails the test for everything that is wrong with where the boxes stand, and when something
    // that should be among them is not shown, so that an empty screen does not pass.
    private static void Look(SmokeOrders orders, string showing, params string?[] expected)
    {
        var frame = orders.View.Interface;
        foreach (var problem in frame.LayoutProblems())
        {
            orders.Fail($"with {showing} shown, {problem}");
        }

        var boxes = frame.Boxes();
        bool Shows(string part) => boxes.Any(box => box.Name.Contains(part, StringComparison.Ordinal));

        var needed = new List<string?>(expected) { "/Messages/" };
        if (frame.Parts.OfType<ControlGroupsPart>().Any())
        {
            needed.Add("/RowsUnderBar/");
        }

        needed.Add("/RowsUnderBarAtRight/");

        if (frame.DevelopmentAids.GetChildCount() > 0)
        {
            needed.Add("/DevelopmentAids/");
        }

        if (frame.DevelopmentReadouts.GetChildCount() > 0)
        {
            needed.Add("/DevelopmentReadouts/");
        }

        foreach (var part in needed)
        {
            if (part is not null && !Shows(part))
            {
                orders.Fail($"with {showing} shown, the interface shows nothing of '{part.Trim('/')}'. It shows: {string.Join(", ", boxes.Select(box => box.Name))}");
            }
        }

        // The message is longer than the middle of the window is wide, so it is on more lines than one.
        var middle = frame.MiddleWidth;
        foreach (var (name, rect) in boxes)
        {
            if (name.Contains("/Messages/", StringComparison.Ordinal) && rect.Size.X > middle + 0.5f)
            {
                orders.Fail(string.Create(
                    CultureInfo.InvariantCulture,
                    $"with {showing} shown, a message is {rect.Size.X:F0} wide and the middle of the window is {middle:F0}."));
            }
        }
    }
}
