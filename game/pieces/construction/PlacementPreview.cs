using Godot;
using Sim.Data;

namespace Game;

/// <summary>
/// The structure the player is about to place, drawn where a click would put it: its shape,
/// see-through, and a mark on each tile of its footprint.
/// </summary>
/// <remarks>
/// <para>
/// Green is a site the order would be accepted for, and red one it would be refused for, with
/// the tiles that are the trouble in red and the others in green, so that the player sees which
/// way to move. Amber is a site the order is accepted for but where, as things stand, the worker
/// would be turned away on arrival because the structure's territory would overlap an enemy's
/// (design 5.4): a warning, since the enemy's structure may be gone by then.
/// </para>
/// <para>
/// The layer judges nothing. The build menu says what to show and where, with what
/// <see cref="BuildChecks"/> said of the site, and the layer asks the same about each tile.
/// </para>
/// </remarks>
public partial class PlacementPreview : WorldLayer
{
    // Above the territory's tint and line, and under the line round a selected structure.
    private const float TileLift = 0.045f;
    private const float TileSize = 0.9f;

    private static readonly Color Good = new(0.45f, 1f, 0.5f);
    private static readonly Color Blocked = new(1f, 0.32f, 0.26f);
    private static readonly Color Warned = new(1f, 0.74f, 0.25f);
    private const float ShapeAlpha = 0.5f;
    private const float TileAlpha = 0.42f;

    // Over a site that is refused the shape is fainter and the tiles stronger, so that the
    // tiles that are the trouble are told from the others through it.
    private const float RefusedShapeAlpha = 0.16f;
    private const float RefusedTileAlpha = 0.62f;

    private readonly Dictionary<int, ArrayMesh> _meshes = [];
    private MeshInstance3D _shape = null!;
    private StandardMaterial3D _material = null!;
    private InstancedShapes _tiles = null!;
    private StructureDef? _structure;
    private Placement _site;

    /// <summary>True while a structure is shown.</summary>
    public bool IsShown => _structure is not null;

    /// <summary>The number of tiles marked as no good in the last frame.</summary>
    public int BlockedTiles { get; private set; }

    /// <summary>
    /// What gives the shape of a structure whose shape turns on where it stands, as a gate's
    /// turns on the wall beside it, or null. It returns null for every structure with one
    /// shape, which is then drawn as <see cref="StructureShapes"/> has it.
    /// </summary>
    public Func<StructureDef, Placement, ArrayMesh?>? ShapeAt { get; set; }

    /// <summary>The shape that was drawn in the last frame, or null when none was.</summary>
    public Mesh? ShapeShown => _shape.Visible ? _shape.Mesh : null;

    /// <summary>Shows a structure at a site, in place of what was shown.</summary>
    public void Show(StructureDef structure, in Placement site)
    {
        _structure = structure;
        _site = site;
    }

    /// <summary>Shows nothing.</summary>
    public void Clear() => _structure = null;

    /// <summary>The color a site is drawn in.</summary>
    public static Color ColorOf(in Placement site) => !site.IsAccepted ? Blocked : site.NearEnemy ? Warned : Good;

    protected override void Attach()
    {
        _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 1f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _shape = new MeshInstance3D
        {
            Name = "Shape",
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_shape);

        // With a shape from the start, though unseen, the renderer has what it needs to draw the
        // material before the first structure is placed, and that one appears at once.
        foreach (var structure in View.Match.Data.Structures)
        {
            _shape.Mesh = MeshOf(structure);
            break;
        }

        _tiles = InstancedShapes.Under(
            this,
            "Tiles",
            new PlaneMesh { Size = new Vector2(TileSize, TileSize) },
            InstancedShapes.Material(seeThrough: true),
            InstancedShapes.MapArea(View.Match.Map));
    }

    public override void EveryFrame(double delta)
    {
        _tiles.Begin();
        BlockedTiles = 0;
        _shape.Visible = _structure is not null;
        if (_structure is { } structure)
        {
            var color = ColorOf(_site);
            _shape.Mesh = ShapeAt?.Invoke(structure, _site) ?? MeshOf(structure);
            _shape.Position = new Vector3(_site.X + (structure.Footprint / 2f), 0f, _site.Y + (structure.Footprint / 2f));
            _material.AlbedoColor = _site.IsAccepted ? new Color(color, ShapeAlpha) : new Color(color.Lerp(Colors.White, 0.5f), RefusedShapeAlpha);
            MarkTiles(structure, _site.IsAccepted ? color : Good);
        }

        _tiles.End();
    }

    private ArrayMesh MeshOf(StructureDef structure)
    {
        if (!_meshes.TryGetValue(structure.Index, out var mesh))
        {
            mesh = StructureShapes.MeshOf(structure);
            _meshes.Add(structure.Index, mesh);
        }

        return mesh;
    }

    private void MarkTiles(StructureDef structure, Color good)
    {
        var checks = BuildChecks.For(View);
        for (var y = _site.Y; y < _site.Y + structure.Footprint; y++)
        {
            for (var x = _site.X; x < _site.X + structure.Footprint; x++)
            {
                var free = checks.Tile(structure, x, y);
                BlockedTiles += free ? 0 : 1;
                _tiles.Add(
                    new Transform3D(Basis.Identity, new Vector3(x + 0.5f, TileLift, y + 0.5f)),
                    new Color(free ? good : Blocked, _site.IsAccepted ? TileAlpha : RefusedTileAlpha));
            }
        }
    }
}
