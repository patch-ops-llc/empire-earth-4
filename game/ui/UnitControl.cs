using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// Takes the clicks that select and the clicks that give orders. The player selects units of the
/// player's own seat, by a click or by dragging a box, and gives the selected ones an order with
/// the order button.
/// </summary>
/// <remarks>
/// <para>
/// It reads the match and never changes it. It decides nothing about what a click means: the
/// <see cref="UnderPointer"/> says what is under it, and the <see cref="OrderTable"/> turns the
/// selection and that answer into a command, which leaves through the session and is carried
/// out a tick or more later, on every machine alike. A piece that adds a kind of thing to click
/// on adds a <see cref="PointerPicker"/>, and a piece that adds an order adds a row of the table.
/// Neither changes this file.
/// </para>
/// <para>
/// Nothing here uses physics. A box is first turned into an area of the ground, the simulation's
/// spatial index names the entities in it, and each is then tested again where it appears on
/// screen. The second test is needed because the camera looks down at a slant: a rectangle on
/// screen is not a rectangle on the ground.
/// </para>
/// </remarks>
public partial class UnitControl : Node
{
    // Named in the input map of project.godot. No button or key is read here.
    private static readonly StringName SelectAction = "unit_select";
    private static readonly StringName SelectAddAction = "unit_select_add";
    private static readonly StringName OrderAction = "unit_order";

    // A press that moves less than this many pixels before its release is a click and not a box.
    private const float DragThreshold = 6f;

    private readonly List<EntityId> _candidates = [];
    private readonly List<EntityId> _hits = [];

    private MatchView? _view;
    private Panel _box = null!;
    private OrderMarker _marker = null!;

    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressedAt;

    public override void _Ready()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.55f, 1f, 0.55f, 0.12f),
            BorderColor = new Color(0.55f, 1f, 0.55f, 0.9f),
        };
        style.SetBorderWidthAll(1);
        _box = new Panel { Name = "SelectionBox", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _box.AddThemeStyleboxOverride("panel", style);
        AddChild(_box);
    }

    /// <summary>Starts taking input for a match. Call it once, after the world view draws the match.</summary>
    public void Attach(MatchView view)
    {
        if (_view is not null)
        {
            throw new InvalidOperationException("The unit control already has a match.");
        }

        _view = view;
        view.World.AddLayer(new SelectionRings());
        _marker = new OrderMarker();
        view.World.AddLayer(_marker);
    }

    /// <summary>Drops from the selection what is no longer in the match. Call it after every tick.</summary>
    public void AfterTick()
    {
        if (_view is { } view)
        {
            var entities = view.Match.World.Entities;
            view.Selection.RemoveWhere(entity => !entities.IsAlive(entity));
        }
    }

    /// <summary>
    /// Selects what a box dragged over the whole window selects, exactly as the drag does. The
    /// frame test selects so, to time what a player does many times in a match.
    /// </summary>
    public void SelectWholeWindow()
    {
        if (_view is { } view)
        {
            SelectInBox(view, GetViewport().GetVisibleRect(), add: false);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_view is not { } view)
        {
            return;
        }

        if (@event.IsActionPressed(SelectAction))
        {
            _pressed = true;
            _dragging = false;
            _pressedAt = GetViewport().GetMousePosition();
        }
        else if (@event.IsActionReleased(SelectAction) && _pressed)
        {
            var add = Input.IsActionPressed(SelectAddAction);
            if (_dragging)
            {
                SelectInBox(view, BoxTo(GetViewport().GetMousePosition()), add);
            }
            else
            {
                SelectAt(view, _pressedAt, add);
            }

            EndPress();
        }
        else if (@event is InputEventMouseMotion && _pressed)
        {
            var pointer = GetViewport().GetMousePosition();
            _dragging = _dragging || pointer.DistanceTo(_pressedAt) >= DragThreshold;
            if (_dragging)
            {
                var box = BoxTo(pointer);
                _box.Position = box.Position;
                _box.Size = box.Size;
                _box.Visible = true;
            }
        }
        else if (@event.IsActionPressed(OrderAction))
        {
            Order(view, GetViewport().GetMousePosition());
        }
    }

    public override void _Notification(int what)
    {
        // A release that happens while another window has the focus, or while a page or a panel
        // has taken the pointer, never arrives.
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut || what == NotificationDisabled)
        {
            EndPress();
        }
    }

    private void EndPress()
    {
        _pressed = false;
        _dragging = false;
        if (_box is not null)
        {
            _box.Visible = false;
        }
    }

    private Rect2 BoxTo(Vector2 pointer) => new Rect2(_pressedAt, pointer - _pressedAt).Abs();

    // A click selects what is the player's own under the pointer, and nothing when it is on
    // anything else.
    private static void SelectAt(MatchView view, Vector2 pointer, bool add)
    {
        var selection = view.Selection;
        var target = view.UnderPointer.At(pointer, PointerTargets.Own);
        if (!target.Is(PointerTargets.Own))
        {
            if (!add)
            {
                selection.Clear();
            }
        }
        else if (add)
        {
            selection.Toggle(target.Entity);
        }
        else
        {
            selection.Set([target.Entity]);
        }

        ReportSelection(selection);
    }

    // A box selects the player's own units in it.
    private void SelectInBox(MatchView view, Rect2 box, bool add)
    {
        var camera = GetViewport().GetCamera3D();
        var match = view.Match;
        _hits.Clear();

        if (camera is not null)
        {
            // The ground the box covers is a four-sided shape. The index is asked for the upright
            // rectangle around it, and the test on screen below throws out what is not in the box.
            var map = match.Map;
            var min = Vector2.Zero;
            var max = new Vector2(map.Width, map.Height);
            if (UnderPointer.TryGroundPoint(camera, box.Position, out var a)
                && UnderPointer.TryGroundPoint(camera, new Vector2(box.End.X, box.Position.Y), out var b)
                && UnderPointer.TryGroundPoint(camera, box.End, out var c)
                && UnderPointer.TryGroundPoint(camera, new Vector2(box.Position.X, box.End.Y), out var d))
            {
                min = a.Min(b).Min(c).Min(d) - (Vector2.One * UnitPicker.GroundMargin);
                max = a.Max(b).Max(c).Max(d) + (Vector2.One * UnitPicker.GroundMargin);
            }

            match.SpatialIndex.QueryBox(view.UnderPointer.ToMapPoint(min), view.UnderPointer.ToMapPoint(max), _candidates);
            foreach (var entity in _candidates)
            {
                if (!match.Bodies.TryGet(entity, out var body)
                    || body.Owner != view.Player
                    || !view.World.TryGetDrawnPosition(entity, out var position))
                {
                    continue;
                }

                var middle = position + (Vector3.Up * (PlaceholderMeshes.UnitHeight / 2f));
                if (!camera.IsPositionBehind(middle) && box.HasPoint(camera.UnprojectPosition(middle)))
                {
                    _hits.Add(entity);
                }
            }
        }

        if (add)
        {
            view.Selection.Add(_hits);
        }
        else
        {
            view.Selection.Set(_hits);
        }

        ReportSelection(view.Selection);
    }

    private void Order(MatchView view, Vector2 pointer)
    {
        var target = view.UnderPointer.At(pointer, view.Orders.Wanted);
        if (view.Orders.Give(view, target) is not { } row)
        {
            return;
        }

        // The mark appears at once, at the point the player ordered, and stays there. The order
        // itself runs a tick or more later, and each unit may end up somewhere else: beside
        // blocked ground it is moved off it, and a place that cannot be reached becomes the
        // nearest that can.
        _marker.ShowAt(WorldView.ToWorld(target.Point), row.Mark);

        if (OS.IsStdOutVerbose())
        {
            GD.Print($"{row.Name} order for {view.Selection.Count} to {WorldView.ToWorld(target.Point)} at tick {view.Match.World.Tick}.");
        }
    }

    private static void ReportSelection(Selection selection)
    {
        if (OS.IsStdOutVerbose())
        {
            GD.Print($"Selected {selection.Count}: {string.Join(' ', selection.Entities)}");
        }
    }
}
