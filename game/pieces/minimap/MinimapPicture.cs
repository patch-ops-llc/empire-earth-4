using Godot;
using Sim.Data;
using Sim.Economy;

namespace Game;

/// <summary>
/// The picture of the minimap: the terrain, the territories, the deposits, the structures, every
/// unit as a dot in its owner's color, and the outline of the ground the camera shows. It takes the presses on it and says
/// what they ask for, and it changes nothing itself.
/// </summary>
/// <remarks>
/// It is drawn in this order, each over those before it: terrain, territory, deposits,
/// structures, the shade of the fog when there is one, other players' units, the local player's
/// units, the outline of the view, the mark of an order. What the player may not know is left
/// out by <see cref="MinimapSight"/>, and of structures by the list <see cref="MinimapStructures"/> reads.
/// </remarks>
public partial class MinimapPicture : Control
{
    /// <summary>Held on the minimap, it moves the view to the point under the pointer. Named in the input map of project.godot.</summary>
    public static readonly StringName LookAction = "minimap_look";

    // The order button, as everywhere in the world.
    private static readonly StringName OrderAction = "unit_order";

    private const float UnitDot = 4f;
    private const float SelectedRim = 1f;
    private const float SmallestDeposit = 4f;
    private const double MarkSeconds = 0.7;

    private static readonly Color GroundColor = new(0.33f, 0.45f, 0.26f);
    private static readonly Color ForestColor = new(0.12f, 0.28f, 0.14f);
    private static readonly Color CliffColor = new(0.46f, 0.44f, 0.42f);
    private static readonly Color DepositColor = new(0.80f, 0.54f, 0.30f);
    private static readonly Color DepositEdge = new(0.30f, 0.18f, 0.10f);
    private static readonly Color ViewColor = new(1f, 1f, 1f, 0.9f);
    private static readonly Color MarkColor = new(1f, 0.95f, 0.4f);
    private static readonly Color EdgeColor = new(0.35f, 0.39f, 0.47f);

    private readonly Vector2[] _viewOutline = new Vector2[5];
    private readonly MinimapStructures _structures = new();

    private MatchView? _view;
    private MinimapGeometry _geometry;
    private MinimapSight _sight = new();
    private ImageTexture? _terrain;
    private bool _looking;
    private Vector2 _markAt;
    private double _markLeft;

    /// <summary>What the picture shows of structures and territory.</summary>
    public MinimapStructures Structures => _structures;

    /// <summary>The view is asked to look at a point of the ground, as map x and y in tiles.</summary>
    public event Action<Vector2>? Looked;

    /// <summary>An order is asked for at a point of the minimap.</summary>
    public event Action<Vector2>? Ordered;

    /// <summary>Starts showing a match. Call it once, before the picture enters the tree.</summary>
    public void Begin(MatchView view, MinimapGeometry geometry, MinimapSight sight)
    {
        _view = view;
        _geometry = geometry;
        _sight = sight;
        _terrain = TerrainPicture(view.Match.Map);

        CustomMinimumSize = geometry.Size;
        ClipContents = true;
        TextureFilter = TextureFilterEnum.Nearest;

        // A press that is not for the minimap, as a turn of the wheel, goes on to the world.
        MouseFilter = MouseFilterEnum.Pass;
        FocusMode = FocusModeEnum.None;
    }

    /// <summary>Shows for a moment that an order was given at a point of the minimap.</summary>
    public void ShowOrderAt(Vector2 point)
    {
        _markAt = point;
        _markLeft = MarkSeconds;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton press)
        {
            if (press.IsActionPressed(LookAction))
            {
                _looking = true;
                Looked?.Invoke(_geometry.ToGround(press.Position));
                AcceptEvent();
            }
            else if (press.IsActionReleased(LookAction) && _looking)
            {
                _looking = false;
                AcceptEvent();
            }
            else if (press.IsActionPressed(OrderAction))
            {
                Ordered?.Invoke(press.Position);
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion && _looking)
        {
            // While the view is dragged the pointer draws no selection box behind the minimap.
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        if (_looking)
        {
            // The release is missed when it happens over another window, or once the minimap is hidden.
            if (!Input.IsActionPressed(LookAction) || !IsVisibleInTree())
            {
                _looking = false;
            }
            else
            {
                // In every frame, and after the camera has moved itself: a pointer held still at
                // the edge of the window would otherwise scroll the view away from under it.
                Looked?.Invoke(_geometry.ToGround(GetLocalMousePosition()));
            }
        }

        _markLeft = Math.Max(0.0, _markLeft - delta);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_view is not { } view || _terrain is null)
        {
            return;
        }

        var whole = new Rect2(Vector2.Zero, _geometry.Size);
        DrawTextureRect(_terrain, whole, tile: false);
        _structures.DrawTerritory(this, view, _geometry);
        DrawDeposits(view);
        _structures.DrawStructures(this, view, _geometry);
        if (_sight.Shade is { } shade)
        {
            DrawTextureRect(shade, whole, tile: false);
        }

        DrawUnits(view, own: false);
        DrawUnits(view, own: true);
        DrawViewOutline();
        DrawOrderMark();

        // Half a pixel inside, so that the whole width of the line is in the picture.
        DrawRect(whole.Grow(-0.5f), EdgeColor, filled: false, width: 1f);
    }

    private void DrawDeposits(MatchView view)
    {
        var sites = view.Match.WorkSites;
        var entities = view.Match.World.Entities;
        for (var i = 0; i < sites.Count; i++)
        {
            var site = sites.ValueAt(i);
            if (site.Kind != WorkSiteKind.Deposit || !entities.IsAlive(sites.EntityAt(i)) || !_sight.ShowsDeposit(site))
            {
                continue;
            }

            // A deposit is drawn at least this large, round its own middle, so that it is found on a large map.
            var middle = _geometry.ToMinimap(new Vector2(site.X + (site.Size / 2f), site.Y + (site.Size / 2f)));
            var side = Math.Max(SmallestDeposit, site.Size * _geometry.PixelsPerTile);
            var square = new Rect2(middle - (Vector2.One * (side / 2f)), Vector2.One * side);
            DrawRect(square.Grow(1f), DepositEdge);
            DrawRect(square, DepositColor);
        }
    }

    private void DrawUnits(MatchView view, bool own)
    {
        var match = view.Match;
        var bodies = match.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var entity = bodies.EntityAt(i);
            var body = bodies.ValueAt(i);
            if ((body.Owner == view.Player) != own || !match.Movers.Contains(entity))
            {
                continue;
            }

            // Where the world draws it in this frame, so that the dot moves as the unit does.
            var drawn = view.World.TryGetDrawnPosition(entity, out var position) ? position : WorldView.ToWorld(body.Position);
            var ground = new Vector2(drawn.X, drawn.Z);
            if (!_sight.ShowsUnit(body.Owner, ground))
            {
                continue;
            }

            var dot = new Rect2(_geometry.ToMinimap(ground) - (Vector2.One * (UnitDot / 2f)), Vector2.One * UnitDot);
            if (own && view.Selection.Contains(entity))
            {
                DrawRect(dot.Grow(SelectedRim), Colors.White);
            }

            DrawRect(dot, PlayerColors.Of(body.Owner));
        }
    }

    // The ground the camera shows is a four-sided shape, wider at its far side, because the
    // camera looks down at a slant. Its corners are the ground under the corners of the window.
    private void DrawViewOutline()
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null)
        {
            return;
        }

        var window = GetViewportRect().Size;
        for (var corner = 0; corner < 4; corner++)
        {
            var screen = new Vector2(corner is 1 or 2 ? window.X : 0f, corner is 2 or 3 ? window.Y : 0f);
            if (!UnderPointer.TryGroundPoint(camera, screen, out var ground))
            {
                return;
            }

            _viewOutline[corner] = _geometry.ToMinimap(ground);
        }

        _viewOutline[4] = _viewOutline[0];
        DrawPolyline(_viewOutline, ViewColor, width: 1.5f, antialiased: true);
    }

    // A ring that closes on the ordered point, as the mark of an order does on the ground.
    private void DrawOrderMark()
    {
        if (_markLeft <= 0.0)
        {
            return;
        }

        var left = (float)(_markLeft / MarkSeconds);
        DrawArc(_markAt, 3f + (7f * left), 0f, Mathf.Tau, 24, MarkColor, width: 1.5f, antialiased: true);
    }

    // A pixel for a tile. Forest and cliffs are what blocks the way, so they are what is drawn.
    private static ImageTexture TerrainPicture(MapDef map)
    {
        var image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgb8);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                image.SetPixel(x, y, map.TerrainAt(x, y) switch
                {
                    TerrainKind.Forest => ForestColor,
                    TerrainKind.Cliff => CliffColor,
                    _ => GroundColor,
                });
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
