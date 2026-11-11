using System.Collections.Immutable;
using Godot;
using Net;
using Sim.Core;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;

namespace Game;

/// <summary>
/// Lets the player select units of the player's own seat, by a click or by dragging a box, and send
/// the selected ones somewhere with a move order.
/// </summary>
/// <remarks>
/// <para>
/// It reads the match and never changes it. An order leaves as a command through the session,
/// and the simulation carries it out a tick or more later, on every machine alike.
/// </para>
/// <para>
/// Nothing here uses physics. A click or a box is first turned into an area of the ground, the
/// simulation's spatial index names the entities in it, and each is then tested again where it
/// appears on screen. The second test is needed because the camera looks down at a slant: a
/// rectangle on screen is not a rectangle on the ground, and a unit is clicked on its body, which
/// stands above the spot of ground behind it.
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

    // How many pixels beside a unit a click still selects it.
    private const float ClickSlack = 5f;

    // How far around a click or a box, in tiles, the spatial index is asked. It covers the slant of
    // the camera and the distance a unit is drawn from where it stood at the end of the last tick.
    private const float GroundMargin = 2f;

    private readonly Selection _selection = new();
    private readonly List<EntityId> _candidates = [];
    private readonly List<EntityId> _hits = [];
    private readonly List<Vector3> _ringPoints = [];

    private Match? _match;
    private ClientSession _client = null!;
    private WorldView _world = null!;
    private Panel _box = null!;
    private SelectionRings _rings = null!;
    private OrderMarker _marker = null!;

    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressedAt;
    private PendingOrder? _order;

    /// <summary>What the player has selected.</summary>
    public Selection Selection => _selection;

    public override void _Ready()
    {
        _rings = new SelectionRings { Name = "SelectionRings" };
        AddChild(_rings);

        _marker = new OrderMarker { Name = "OrderMarker" };
        AddChild(_marker);

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
    /// <param name="match">The match, to read.</param>
    /// <param name="client">The local player's side of the session, which orders go through.</param>
    /// <param name="world">The view, which knows where each unit is drawn.</param>
    public void Attach(Match match, ClientSession client, WorldView world)
    {
        if (_match is not null)
        {
            throw new InvalidOperationException("The unit control already has a match.");
        }

        _match = match;
        _client = client;
        _world = world;
        _rings.SetArea(new Vector2(match.Map.Width, match.Map.Height));
        client.CommandsScheduled += OnCommandsScheduled;
        client.TickCompleted += OnTickCompleted;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_match is null)
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
                SelectInBox(BoxTo(GetViewport().GetMousePosition()), add);
            }
            else
            {
                SelectAt(_pressedAt, add);
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
            OrderMove(GetViewport().GetMousePosition());
        }
    }

    public override void _Notification(int what)
    {
        // A release that happens while another window has the focus never arrives.
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut)
        {
            EndPress();
        }
    }

    public override void _Process(double delta)
    {
        if (_match is null)
        {
            return;
        }

        // This node comes after the world view in the scene, so the units have been placed for
        // this frame and the rings go exactly under them.
        _ringPoints.Clear();
        foreach (var entity in _selection.Entities)
        {
            if (_world.TryGetDrawnPosition(entity, out var position))
            {
                _ringPoints.Add(position);
            }
        }

        _rings.Place(_ringPoints);
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

    private void SelectAt(Vector2 pointer, bool add)
    {
        var camera = GetViewport().GetCamera3D();
        var best = EntityId.None;
        var bestDistance = float.MaxValue;

        if (camera is not null && TryGroundPoint(camera, pointer, out var ground))
        {
            _match!.SpatialIndex.QueryCircle(ToMapPoint(ground), ToFixed(GroundMargin), _candidates);
            foreach (var entity in _candidates)
            {
                if (!IsOwn(entity) || !TryDrawnPosition(entity, out var position) || camera.IsPositionBehind(position))
                {
                    continue;
                }

                // On screen a unit is roughly a capsule: the line from its foot to its top, as thick as it is wide.
                var foot = camera.UnprojectPosition(position);
                var top = camera.UnprojectPosition(position + (Vector3.Up * PlaceholderMeshes.UnitHeight));
                var side = camera.UnprojectPosition(position + (camera.GlobalBasis.X * PlaceholderMeshes.UnitRadius));
                var reach = foot.DistanceTo(side) + ClickSlack;
                var distance = Geometry2D.GetClosestPointToSegment(pointer, foot, top).DistanceTo(pointer);
                if (distance <= reach && distance < bestDistance)
                {
                    best = entity;
                    bestDistance = distance;
                }
            }
        }

        if (best == EntityId.None)
        {
            if (!add)
            {
                _selection.Clear();
            }
        }
        else if (add)
        {
            _selection.Toggle(best);
        }
        else
        {
            _selection.Set([best]);
        }

        ReportSelection();
    }

    private void SelectInBox(Rect2 box, bool add)
    {
        var camera = GetViewport().GetCamera3D();
        _hits.Clear();

        if (camera is not null)
        {
            // The ground the box covers is a four-sided shape. The index is asked for the upright
            // rectangle around it, and the test on screen below throws out what is not in the box.
            var map = _match!.Map;
            var min = Vector2.Zero;
            var max = new Vector2(map.Width, map.Height);
            if (TryGroundPoint(camera, box.Position, out var a)
                && TryGroundPoint(camera, new Vector2(box.End.X, box.Position.Y), out var b)
                && TryGroundPoint(camera, box.End, out var c)
                && TryGroundPoint(camera, new Vector2(box.Position.X, box.End.Y), out var d))
            {
                min = a.Min(b).Min(c).Min(d) - (Vector2.One * GroundMargin);
                max = a.Max(b).Max(c).Max(d) + (Vector2.One * GroundMargin);
            }

            _match.SpatialIndex.QueryBox(ToMapPoint(min), ToMapPoint(max), _candidates);
            foreach (var entity in _candidates)
            {
                if (!IsOwn(entity) || !TryDrawnPosition(entity, out var position))
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
            _selection.Add(_hits);
        }
        else
        {
            _selection.Set(_hits);
        }

        ReportSelection();
    }

    private void OrderMove(Vector2 pointer)
    {
        var camera = GetViewport().GetCamera3D();
        if (_selection.Count == 0 || camera is null || !TryGroundPoint(camera, pointer, out var ground))
        {
            return;
        }

        // The simulation drops an order whose target is off the map, so a click beyond the edge
        // is an order to the nearest point on it (design 5.3).
        var target = ToMapPoint(ground);
        EntityId[] entities = [.. _selection.Entities];
        var command = new Command(CommandType.Move, _client.Player, entities, target.X, target.Y);
        if (!_client.Submit(command))
        {
            return;
        }

        // The mark appears at once, where the player clicked. The order itself runs a tick or more
        // later, and only then is it known where the units really go.
        _order = new PendingOrder(target, entities, RunsInTick: -1);
        _marker.ShowAt(WorldView.ToWorld(target));

        if (OS.IsStdOutVerbose())
        {
            GD.Print($"Move order for {entities.Length} to {WorldView.ToWorld(target)} at tick {_match!.World.Tick}.");
        }
    }

    // The session says which tick each command runs in. This finds the tick of the order the mark stands for.
    private void OnCommandsScheduled(int tick, ImmutableArray<Command> commands)
    {
        if (_order is not { RunsInTick: < 0 } order)
        {
            return;
        }

        foreach (var command in commands)
        {
            if (command.Type == CommandType.Move
                && command.Player == _client.Player
                && command.TargetX.Raw == order.Target.X.Raw
                && command.TargetY.Raw == order.Target.Y.Raw)
            {
                _order = order with { RunsInTick = tick };
                return;
            }
        }
    }

    private void OnTickCompleted(int tick)
    {
        var entities = _match!.World.Entities;
        _selection.RemoveWhere(entity => !entities.IsAlive(entity));

        if (_order is not { } order || order.RunsInTick != tick)
        {
            return;
        }

        // The order has run. Where a unit was sent can differ from the click: beside blocked
        // ground it is moved off it, and a place that cannot be reached becomes the nearest that can.
        _order = null;
        foreach (var entity in order.Entities)
        {
            if (_match.Movers.TryGet(entity, out var mover) && mover.State != MoveState.Idle)
            {
                _marker.MoveTo(WorldView.ToWorld(mover.Destination));
                return;
            }
        }
    }

    private bool IsOwn(EntityId entity) =>
        _match!.Bodies.TryGet(entity, out var body) && body.Owner == _client.Player;

    // Where the unit is drawn, since that is what the player aims at, or where it stands if it has no node yet.
    private bool TryDrawnPosition(EntityId entity, out Vector3 position)
    {
        if (_world.TryGetDrawnPosition(entity, out position))
        {
            return true;
        }

        if (_match!.Bodies.TryGet(entity, out var body))
        {
            position = WorldView.ToWorld(body.Position);
            return true;
        }

        return false;
    }

    // The point of the ground under a point of the screen, as map x and y. False when the ray from
    // the camera through that point never comes down.
    private static bool TryGroundPoint(Camera3D camera, Vector2 screen, out Vector2 ground)
    {
        var origin = camera.ProjectRayOrigin(screen);
        var direction = camera.ProjectRayNormal(screen);
        if (direction.Y > -0.0001f)
        {
            ground = default;
            return false;
        }

        var hit = origin + (direction * (-origin.Y / direction.Y));
        ground = new Vector2(hit.X, hit.Z);
        return true;
    }

    // Floats end here: this is the one place a point from the screen becomes a point of the
    // simulation. The point is brought onto the map on the way.
    private FixedVector2 ToMapPoint(Vector2 point)
    {
        var map = _match!.Map;
        return new FixedVector2(ToMapCoordinate(point.X, map.Width), ToMapCoordinate(point.Y, map.Height));
    }

    // The last point of a map 96 tiles wide is one distance unit short of 96.
    private static Fixed ToMapCoordinate(float value, int tiles)
    {
        if (float.IsNaN(value))
        {
            return default;
        }

        // In double, because a float cannot hold every distance unit of a large map.
        var last = ((double)tiles * Fixed.RawOne) - 1.0;
        return Fixed.FromRaw((int)Math.Clamp(Math.Round((double)value * Fixed.RawOne), 0.0, last));
    }

    private static Fixed ToFixed(float value) => Fixed.FromRaw(Mathf.RoundToInt(value * Fixed.RawOne));

    private void ReportSelection()
    {
        if (OS.IsStdOutVerbose())
        {
            GD.Print($"Selected {_selection.Count}: {string.Join(' ', _selection.Entities)}");
        }
    }

    private sealed record PendingOrder(FixedVector2 Target, EntityId[] Entities, int RunsInTick);
}
