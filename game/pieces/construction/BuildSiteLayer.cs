using Godot;
using Sim.Construction;
using Sim.Core;
using Sim.Data;

namespace Game;

/// <summary>
/// The sites the local player's workers are walking to: for every build order, the shape of the
/// structure to come, see-through in the player's color, with a line round its tiles.
/// </summary>
/// <remarks>
/// Nothing stands on such a site yet and its tiles are open, so it is drawn as a plan and not as
/// a structure. Only the local player's own orders are drawn: where another player means to
/// build is that player's to know until the foundation is laid.
/// </remarks>
public partial class BuildSiteLayer : WorldLayer
{
    // The same height as the line round a selected structure.
    private const float OutlineLift = 0.06f;
    private const float ShapeAlpha = 0.3f;

    private readonly Dictionary<EntityId, Site> _sites = [];
    private readonly Dictionary<int, ArrayMesh> _meshes = [];
    private readonly Dictionary<int, ArrayMesh> _outlines = [];
    private readonly List<EntityId> _gone = [];
    private StandardMaterial3D _material = null!;

    /// <summary>The number of sites drawn.</summary>
    public int Count => _sites.Count;

    /// <summary>True when the site of this worker's build order is drawn on these tiles.</summary>
    public bool Draws(EntityId worker, in BuildOrder order) =>
        _sites.TryGetValue(worker, out var site) && site.Order.Type == order.Type && site.Order.Tiles == order.Tiles;

    protected override void Attach()
    {
        _material = new StandardMaterial3D
        {
            AlbedoColor = new Color(PlayerColors.Of(View.Player).Lerp(Colors.White, 0.35f), ShapeAlpha),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 1f,
        };

        // An unseen site from the start, so that the renderer has what it needs to draw one
        // before the first is ordered, and that one appears without a pause.
        foreach (var structure in View.Match.Data.Structures)
        {
            var site = NodeFor(structure, structure.Footprint);
            site.Name = "Unseen";
            site.Visible = false;
            AddChild(site);
            break;
        }
    }

    public override void AfterTick()
    {
        var match = View.Match;
        var orders = match.BuildOrders;
        foreach (var (worker, site) in _sites)
        {
            if (!IsOwn(worker) || !orders.TryGet(worker, out var order) || order.Type != site.Order.Type || order.Tiles != site.Order.Tiles)
            {
                _gone.Add(worker);
            }
        }

        foreach (var worker in _gone)
        {
            if (_sites.Remove(worker, out var site))
            {
                site.Node.QueueFree();
            }
        }

        _gone.Clear();
        for (var i = 0; i < orders.Count; i++)
        {
            var worker = orders.EntityAt(i);
            if (!_sites.ContainsKey(worker) && IsOwn(worker))
            {
                Add(worker, orders.ValueAt(i));
            }
        }
    }

    private bool IsOwn(EntityId worker) =>
        View.Match.World.Entities.IsAlive(worker)
        && View.Match.Bodies.TryGet(worker, out var body)
        && body.Owner == View.Player;

    private void Add(EntityId worker, in BuildOrder order)
    {
        if (View.Match.Data.StructureOf(order.Type) is not { } structure)
        {
            return;
        }

        var node = NodeFor(structure, order.Size);
        node.Name = $"Site{worker.Value}";
        node.Position = new Vector3(order.X + (order.Size / 2f), 0f, order.Y + (order.Size / 2f));
        AddChild(node);
        _sites.Add(worker, new Site(order, node));
    }

    // The shape of a structure as a plan, with a line round the tiles it will take.
    private MeshInstance3D NodeFor(StructureDef structure, int size)
    {
        if (!_meshes.TryGetValue(structure.Index, out var mesh))
        {
            mesh = StructureShapes.MeshOf(structure);
            _meshes.Add(structure.Index, mesh);
        }

        if (!_outlines.TryGetValue(size, out var outline))
        {
            outline = StructureShapes.Outline(size, PlayerColors.Of(View.Player).Lerp(Colors.White, 0.35f));
            _outlines.Add(size, outline);
        }

        var node = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        node.AddChild(new MeshInstance3D
        {
            Name = "Tiles",
            Mesh = outline,
            Position = Vector3.Up * OutlineLift,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return node;
    }

    private readonly record struct Site(BuildOrder Order, MeshInstance3D Node);
}
