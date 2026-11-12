using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// Draws the banner of the track in research (design 3.2): a finished structure that gives a
/// research slot and shows no track of its own, the Academy, carries a pole, and while a
/// research runs in its slot a banner hangs from the pole in that track's color.
/// </summary>
/// <remarks>
/// Like the tier's look it is read from <see cref="StructureLooks"/> after every tick. The pole
/// stands also while the slot is idle, so that an Academy with nothing in research can be told
/// from one whose banner is hidden behind something.
/// </remarks>
public partial class BannerLayer : WorldLayer
{
    // How far the pole rises over the structure's shape, and how wide and tall the cloth is, in tiles.
    private const float PoleOver = 1.35f;
    private const float PoleWidth = 0.08f;
    private const float BarWidth = 0.86f;
    private const float ClothWidth = 0.74f;
    private const float ClothHeight = 1f;
    private const float ClothPoint = 0.22f;

    // The cloth is blown toward the camera, which looks down from the south, so that its face
    // is seen from above, and it sways a little. A turn about the x axis by a negative angle
    // brings its lower edge south.
    private const float Hang = -0.6f;
    private const float Sway = 0.07f;
    private const float SwaySpeed = 1.6f;

    private static readonly Color PoleColor = new(0.93f, 0.92f, 0.88f);

    private readonly List<StructureLook> _looks = [];
    private readonly Dictionary<EntityId, Banner> _banners = [];
    private readonly Dictionary<int, Material> _paints = [];
    private readonly List<EntityId> _gone = [];
    private readonly Dictionary<int, ArrayMesh> _poles = [];
    private ArrayMesh _cloth = null!;
    private double _time;
    private int _pass;

    /// <summary>The number of structures that carry a pole.</summary>
    public int Count => _banners.Count;

    /// <summary>The number of banners that hang now.</summary>
    public int Flown { get; private set; }

    /// <summary>The track whose banner hangs on a structure, or <see cref="StructureLook.NoTrack"/>.</summary>
    public int TrackShown(EntityId structure) =>
        _banners.TryGetValue(structure, out var banner) ? banner.Track : StructureLook.NoTrack;

    protected override void Attach() => _cloth = ClothMesh();

    public override void AfterTick()
    {
        StructureLooks.Collect(View, _looks);
        _pass++;
        Flown = 0;
        foreach (var look in _looks)
        {
            if (!look.FliesBanner)
            {
                continue;
            }

            if (!_banners.TryGetValue(look.Entity, out var banner))
            {
                banner = Create(look);
                _banners.Add(look.Entity, banner);
            }

            if (banner.Track != look.Banner)
            {
                banner.Track = look.Banner;
                banner.Cloth.Visible = look.Banner != StructureLook.NoTrack;
                if (banner.Cloth.Visible)
                {
                    banner.Cloth.MaterialOverride = PaintOf(look.Banner);
                }
            }

            banner.Pole.Position = look.Middle;
            banner.Pass = _pass;
            Flown += banner.Cloth.Visible ? 1 : 0;
        }

        foreach (var (entity, banner) in _banners)
        {
            if (banner.Pass != _pass)
            {
                banner.Pole.QueueFree();
                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            _banners.Remove(entity);
        }

        _gone.Clear();
    }

    public override void EveryFrame(double delta)
    {
        _time += delta;
        foreach (var (entity, banner) in _banners)
        {
            if (banner.Cloth.Visible)
            {
                // Each a little out of step with the others, by its own number.
                var swing = Hang + (Sway * Mathf.Sin((float)(_time * SwaySpeed) + entity.Value));
                banner.Cloth.Rotation = new Vector3(swing, 0f, 0f);
            }
        }
    }

    private Banner Create(in StructureLook look)
    {
        var top = StructureShapes.HeightOf(look.Def) + PoleOver;
        if (!_poles.TryGetValue(look.Def.Index, out var mesh))
        {
            mesh = PoleMesh(top);
            _poles.Add(look.Def.Index, mesh);
        }

        var pole = new MeshInstance3D
        {
            Name = $"Banner{look.Entity.Value}",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var cloth = new MeshInstance3D
        {
            Name = "Cloth",
            Mesh = _cloth,
            Position = new Vector3(0f, top, PoleWidth),
            Rotation = new Vector3(Hang, 0f, 0f),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        pole.AddChild(cloth);
        AddChild(pole);
        return new Banner(pole, cloth);
    }

    private Material PaintOf(int track)
    {
        if (!_paints.TryGetValue(track, out var paint))
        {
            paint = new StandardMaterial3D
            {
                AlbedoColor = TrackColors.Of(View.Match.Data, track),
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            _paints.Add(track, paint);
        }

        return paint;
    }

    // A pole on the origin, as two upright rectangles that cross, with a bar across its top. It
    // takes no light and is seen from every side.
    private static ArrayMesh PoleMesh(float top)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetNormal(Vector3.Up);
        tool.SetColor(PoleColor);

        var half = PoleWidth / 2f;
        AddQuad(tool, new(-half, 0f, 0f), new(half, 0f, 0f), new(half, top, 0f), new(-half, top, 0f));
        AddQuad(tool, new(0f, 0f, -half), new(0f, 0f, half), new(0f, top, half), new(0f, top, -half));

        // The bar, upright and lying flat, so that it is seen from the side and from above.
        var bar = BarWidth / 2f;
        AddQuad(tool, new(-bar, top - PoleWidth, 0f), new(bar, top - PoleWidth, 0f), new(bar, top, 0f), new(-bar, top, 0f));
        AddQuad(tool, new(-bar, top, -half), new(bar, top, -half), new(bar, top, half), new(-bar, top, half));

        var mesh = tool.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        return mesh;
    }

    // The cloth, hanging down from the origin: a rectangle that ends in a point, with a darker
    // band along its lower edge. White, so that the material's color is the track's.
    private static ArrayMesh ClothMesh()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetNormal(Vector3.Back);

        var half = ClothWidth / 2f;
        var straight = ClothHeight - ClothPoint;
        var band = straight - 0.14f;
        tool.SetColor(Colors.White);
        AddQuad(tool, new(-half, -band, 0f), new(half, -band, 0f), new(half, 0f, 0f), new(-half, 0f, 0f));

        tool.SetColor(new Color(0.62f, 0.62f, 0.62f));
        AddQuad(tool, new(-half, -straight, 0f), new(half, -straight, 0f), new(half, -band, 0f), new(-half, -band, 0f));

        tool.SetColor(Colors.White);
        tool.AddVertex(new Vector3(-half, -straight, 0f));
        tool.AddVertex(new Vector3(0f, -ClothHeight, 0f));
        tool.AddVertex(new Vector3(half, -straight, 0f));
        return tool.Commit();
    }

    private static void AddQuad(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        tool.AddVertex(a);
        tool.AddVertex(b);
        tool.AddVertex(c);

        tool.AddVertex(a);
        tool.AddVertex(c);
        tool.AddVertex(d);
    }

    private sealed class Banner(MeshInstance3D pole, MeshInstance3D cloth)
    {
        public MeshInstance3D Pole { get; } = pole;

        public MeshInstance3D Cloth { get; } = cloth;

        public int Track { get; set; } = StructureLook.NoTrack;

        public int Pass { get; set; }
    }
}
