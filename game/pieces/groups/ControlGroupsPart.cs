using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The keys that select without the pointer, and the row that shows what they reach: the
/// numbered groups, the next idle worker, a look at the selection, and the second click that
/// selects every unit of one kind on screen.
/// </summary>
/// <remarks>
/// Everything here is the player's own business, as the selection is: it gives no order and
/// sends no command. It rests whenever the unit control rests, which is while a page of the menu
/// is up and while the smoke test plays, and the frame rests it while a full-screen panel is open.
/// </remarks>
public partial class ControlGroupsPart : InterfacePart
{
    // Named in the input map of project.godot. No button or key is read here.
    private static readonly StringName SetAction = "control_group_set";
    private static readonly StringName IdleWorkerAction = "select_idle_worker";
    private static readonly StringName LookAction = "camera_look_at_selection";

    private static readonly StringName[] GroupActions =
    [
        "control_group_1",
        "control_group_2",
        "control_group_3",
        "control_group_4",
        "control_group_5",
        "control_group_6",
        "control_group_7",
        "control_group_8",
        "control_group_9",
        "control_group_0",
    ];

    // A second press of a group's key within this many milliseconds of the first looks at the group.
    private const ulong SecondPressMsec = 500;

    // The row keeps the width of the camera's edge-scroll border from the window's edge, so that
    // the pointer on it never scrolls the view, and stands this far under the top bar's row.
    private const float StripLeft = 16f;
    private const float StripGap = 13f;

    private readonly ControlGroups _groups = new();
    private readonly List<EntityId> _idle = [];
    private readonly List<EntityId> _found = [];

    private GroupStrip _strip = null!;
    private int _lastGroup = -1;
    private ulong _lastGroupAt;
    private EntityId _lastIdle;
    private bool _stale = true;

    protected override void Attach()
    {
        _strip = new GroupStrip();
        _strip.Begin(KeyOf(IdleWorkerAction));
        _strip.GroupPressed += PressGroup;
        _strip.IdlePressed += NextIdleWorker;

        // The message line stands beside the row, in the middle of the window. A long message
        // reaches over a long row, and is then drawn over it: it goes away by itself.
        _strip.ZIndex = -1;
        AddChild(_strip);

        _groups.Changed += () => _stale = true;
        View.Selection.Changed += () => _stale = true;
    }

    public override void AfterTick()
    {
        var entities = View.Match.World.Entities;
        _groups.RemoveWhere(entity => !entities.IsAlive(entity));

        var before = _idle.Count;
        IdleWorkers.Find(View.Match, View.Player, _idle);
        _stale = _stale || _idle.Count != before;
    }

    public override void EveryFrame(double delta)
    {
        // A full-screen panel covers the world, and the row with it.
        _strip.Visible = !Frame.IsPanelOpen;
        _strip.Position = new Vector2(StripLeft, Frame.TopLeft.GetGlobalRect().End.Y + StripGap);
        _strip.ResetSize();

        if (_stale)
        {
            _stale = false;
            var selected = View.Selection.Entities;
            for (var group = 0; group < ControlGroups.Count; group++)
            {
                _strip.ShowGroup(group, _groups.Of(group).Count, _groups.Holds(group, selected));
            }

            _strip.ShowIdle(_idle.Count);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!View.Units.CanProcess())
        {
            return;
        }

        for (var group = 0; group < ControlGroups.Count; group++)
        {
            if (@event.IsActionPressed(GroupActions[group]))
            {
                PressGroup(group);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event.IsActionPressed(IdleWorkerAction))
        {
            NextIdleWorker();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(LookAction))
        {
            LookAt(View.Selection.Entities);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton { DoubleClick: true }
            && @event.IsActionPressed(UnitControl.SelectAction)
            && SelectSameKind(GetViewport().GetMousePosition()))
        {
            // The unit control never hears of this press, so its release selects nothing.
            GetViewport().SetInputAsHandled();
        }
    }

    // The key of a group, or its button. With the set key held the selection becomes the group;
    // otherwise the group becomes the selection, and a second press looks at it. With the add
    // key held as well, the one is added to the other.
    private void PressGroup(int group)
    {
        var add = Input.IsActionPressed(UnitControl.SelectAddAction);
        var digit = ControlGroups.DigitOf(group);
        if (Input.IsActionPressed(SetAction))
        {
            // With nothing selected the group stays as it is, so that a slip does not lose it.
            _lastGroup = -1;
            if (View.Selection.Count == 0)
            {
                return;
            }

            if (add)
            {
                _groups.Add(group, View.Selection.Entities);
            }
            else
            {
                _groups.Set(group, View.Selection.Entities);
            }

            Report($"Group {digit} is {_groups.Of(group).Count}: {string.Join(' ', _groups.Of(group))}");
            return;
        }

        var units = _groups.Of(group);
        if (units.Count == 0)
        {
            return;
        }

        if (add)
        {
            _lastGroup = -1;
            View.Selection.Add(units);
            Report($"Group {digit} added. Selected {View.Selection.Count}: {string.Join(' ', View.Selection.Entities)}");
            return;
        }

        var now = Time.GetTicksMsec();
        var second = _lastGroup == group && now - _lastGroupAt <= SecondPressMsec;
        _lastGroup = group;
        _lastGroupAt = now;
        View.Selection.Set(units);
        Report($"Group {digit} selected. Selected {View.Selection.Count}: {string.Join(' ', View.Selection.Entities)}");
        if (second && LookAt(units))
        {
            Report($"Looked at group {digit}, at {View.Camera.Focus}.");
        }
    }

    // Selects the idle worker after the one shown last, in ID order, and looks at it.
    private void NextIdleWorker()
    {
        IdleWorkers.Find(View.Match, View.Player, _idle);
        var next = IdleWorkers.Next(_idle, _lastIdle);
        if (next == EntityId.None)
        {
            Frame.Say("No worker is idle.", 3.0);
            return;
        }

        _lastIdle = next;
        if (Input.IsActionPressed(UnitControl.SelectAddAction))
        {
            View.Selection.Add([next]);
        }
        else
        {
            View.Selection.Set([next]);
        }

        LookAt([next]);
        Report($"Idle worker {next}, one of {_idle.Count}. Selected {View.Selection.Count}: {string.Join(' ', View.Selection.Entities)}");
    }

    // Looks at the one of these units that is nearest their middle, so that there is always a
    // unit in the middle of the view, however far apart they stand.
    private bool LookAt(IReadOnlyCollection<EntityId> units)
    {
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var unit in units)
        {
            if (View.World.TryGetDrawnPosition(unit, out var position))
            {
                sum += position;
                count++;
            }
        }

        if (count == 0)
        {
            return false;
        }

        var middle = sum / count;
        var nearest = middle;
        var nearestDistance = float.MaxValue;
        foreach (var unit in units)
        {
            if (View.World.TryGetDrawnPosition(unit, out var position) && position.DistanceSquaredTo(middle) < nearestDistance)
            {
                nearest = position;
                nearestDistance = position.DistanceSquaredTo(middle);
            }
        }

        View.Camera.CenterOn(new Vector2(nearest.X, nearest.Z));
        return true;
    }

    // A second click on a unit of the player's selects every unit of its kind that is on screen.
    // False when the click was on nothing of the player's, and is then the unit control's.
    private bool SelectSameKind(Vector2 pointer)
    {
        var target = View.UnderPointer.At(pointer, PointerTargets.OwnUnit);
        var camera = GetViewport().GetCamera3D();
        if (!target.Is(PointerTargets.OwnUnit) || camera is null)
        {
            return false;
        }

        var match = View.Match;
        var kind = UnitKinds.Of(match, target.Entity);
        var screen = GetViewport().GetVisibleRect();
        _found.Clear();
        foreach (var unit in View.World.Units)
        {
            if (!match.Bodies.TryGet(unit, out var body)
                || body.Owner != View.Player
                || UnitKinds.Of(match, unit) != kind
                || !View.World.TryGetDrawnPosition(unit, out var position))
            {
                continue;
            }

            var middle = position + (Vector3.Up * (PlaceholderMeshes.UnitHeight / 2f));
            if (!camera.IsPositionBehind(middle) && screen.HasPoint(camera.UnprojectPosition(middle)))
            {
                _found.Add(unit);
            }
        }

        if (Input.IsActionPressed(UnitControl.SelectAddAction))
        {
            View.Selection.Add(_found);
        }
        else
        {
            View.Selection.Set(_found);
        }

        Report($"Same kind on screen, '{kind}'. Selected {View.Selection.Count}: {string.Join(' ', View.Selection.Entities)}");
        return true;
    }

    private static void Report(string line)
    {
        if (OS.IsStdOutVerbose())
        {
            GD.Print(line);
        }
    }

    // The key an action is given with, as the player knows it, or null when it has none.
    private static string? KeyOf(StringName action)
    {
        foreach (var input in InputMap.ActionGetEvents(action))
        {
            if (input is InputEventKey key)
            {
                var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;

                // The code of a key that prints a character is that character's.
                return (long)code is > 32 and < 127 ? ((char)(long)code).ToString() : OS.GetKeycodeString(code);
            }
        }

        return null;
    }
}
