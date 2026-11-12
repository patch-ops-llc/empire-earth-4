using Godot;
using Sim.Core;
using Sim.Economy;

namespace Game;

/// <summary>
/// Draws the deposits the local player knows of: a block of ore on the tiles each one covers, on
/// which nobody can stand. It also shows the mark of a work order, a ring that closes round the
/// deposit the workers were sent to.
/// </summary>
/// <remarks>
/// <para>
/// Under the fog of war (design 5.3) a deposit is known once the player has explored a tile
/// under it, which is what <see cref="PlayerSight.ShowsDeposit"/> answers and what the minimap
/// draws by. Until then it is not drawn. A deposit none of whose tiles the player sees now is
/// drawn dimmed, as the ground round it is.
/// </para>
/// <para>
/// The shape is a stand-in until there is art. It is as wide as the deposit's tiles, since the
/// places where workers stand are the tiles beside them.
/// </para>
/// </remarks>
public partial class DepositLayer : WorldLayer
{
    /// <summary>How tall a deposit is drawn, which is also how tall it is to the pointer.</summary>
    public const float Height = 0.7f;

    // How much of its tiles a deposit fills at the ground and at its top, from the middle outward.
    private const float FootShare = 0.94f;
    private const float TopShare = 0.5f;

    private const double MarkSeconds = 1.2;
    private const float MarkLift = 0.05f;

    private static readonly Color OreColor = new(0.56f, 0.36f, 0.22f);

    private readonly Dictionary<EntityId, DepositView> _deposits = [];
    private readonly List<EntityId> _gone = [];
    private Mesh _mesh = null!;
    private Material _material = null!;
    private Material _dimmed = null!;
    private MeshInstance3D _mark = null!;
    private StandardMaterial3D _markMaterial = null!;
    private double _markLeft;
    private float _markSize;
    private int _pass;

    protected override void Attach()
    {
        _mesh = OreBlock();
        _material = PlaceholderMeshes.Flat(OreColor);
        _dimmed = PlaceholderMeshes.Flat(OreColor.Lerp(FogPicture.Unknown, FogPicture.ExploredShade));

        // The ring has a radius of one, and is sized to the deposit it is shown round.
        var ring = PlaceholderMeshes.Ring(1f, Colors.White);
        _markMaterial = (StandardMaterial3D)ring.Material;
        _mark = new MeshInstance3D { Name = "WorkOrderMark", Mesh = ring, Visible = false };
        AddChild(_mark);
    }

    public override void AfterTick()
    {
        _pass++;
        var sites = View.Match.WorkSites;
        var entities = View.Match.World.Entities;
        for (var i = 0; i < sites.Count; i++)
        {
            var entity = sites.EntityAt(i);
            var site = sites.ValueAt(i);
            if (site.Kind != WorkSiteKind.Deposit || !entities.IsAlive(entity) || !View.Sight.ShowsDeposit(site))
            {
                continue;
            }

            if (!_deposits.TryGetValue(entity, out var deposit))
            {
                deposit = new DepositView(Create(entity, site));
                _deposits.Add(entity, deposit);
            }

            deposit.Node.MaterialOverride = IsSeenNow(site) ? _material : _dimmed;
            deposit.Pass = _pass;
        }

        foreach (var (entity, deposit) in _deposits)
        {
            if (deposit.Pass != _pass)
            {
                deposit.Node.QueueFree();
                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            _deposits.Remove(entity);
        }

        _gone.Clear();
    }

    public override void EveryFrame(double delta)
    {
        if (!_mark.Visible)
        {
            return;
        }

        _markLeft -= delta;
        if (_markLeft <= 0.0)
        {
            _mark.Visible = false;
            return;
        }

        Shrink();
    }

    /// <summary>The number of deposits drawn.</summary>
    public int Count => _deposits.Count;

    /// <summary>True when this deposit is drawn.</summary>
    public bool Draws(EntityId deposit) => _deposits.ContainsKey(deposit);

    private bool IsSeenNow(in WorkSite site)
    {
        for (var y = site.Y; y < site.Y + site.Size; y++)
        {
            for (var x = site.X; x < site.X + site.Size; x++)
            {
                if (View.Sight.FogAt(x, y) == Sim.Vision.FogState.Visible)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The middle of a work site on the ground.</summary>
    public static Vector3 CenterOf(in WorkSite site) => new(site.X + (site.Size / 2f), 0f, site.Y + (site.Size / 2f));

    /// <summary>Shows the mark of a work order round a deposit, from its beginning.</summary>
    public void ShowOrder(in WorkSite site, Color color)
    {
        // The ring ends just outside the corners of the deposit's tiles.
        _markSize = site.Size * 0.75f;
        _markMaterial.AlbedoColor = color;
        _mark.Position = CenterOf(site) + (Vector3.Up * MarkLift);
        _markLeft = MarkSeconds;
        _mark.Visible = true;
        Shrink();
    }

    // From half as large again down to its size, and flat all the way.
    private void Shrink()
    {
        var size = _markSize * Mathf.Lerp(1f, 1.5f, (float)(_markLeft / MarkSeconds));
        _mark.Scale = new Vector3(size, 0.25f, size);
    }

    private MeshInstance3D Create(EntityId entity, in WorkSite site)
    {
        var node = new MeshInstance3D
        {
            Name = $"Deposit{entity.Value}",
            Mesh = _mesh,
            MaterialOverride = _material,
            Position = CenterOf(site),
            Scale = new Vector3(site.Size, 1f, site.Size),
        };
        AddChild(node);
        return node;
    }

    // A block with sloping sides, one tile wide and standing on the ground, that is scaled to the
    // tiles of its deposit. The sides are flat, so that it reads as cut rock.
    private static ArrayMesh OreBlock()
    {
        var foot = FootShare / 2f;
        var top = TopShare / 2f;
        Vector2[] corners = [new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(-1f, 1f)];

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);

        tool.SetNormal(Vector3.Up);
        tool.AddVertex(Top(0));
        tool.AddVertex(Top(1));
        tool.AddVertex(Top(2));
        tool.AddVertex(Top(0));
        tool.AddVertex(Top(2));
        tool.AddVertex(Top(3));

        // The sides. There is no underside, since nothing sees it.
        for (var i = 0; i < corners.Length; i++)
        {
            var next = (i + 1) % corners.Length;
            var along = Bottom(next) - Bottom(i);
            var up = Top(i) - Bottom(i);
            tool.SetNormal(up.Cross(along).Normalized());

            tool.AddVertex(Top(next));
            tool.AddVertex(Top(i));
            tool.AddVertex(Bottom(i));

            tool.AddVertex(Top(next));
            tool.AddVertex(Bottom(i));
            tool.AddVertex(Bottom(next));
        }

        return tool.Commit();

        Vector3 Top(int corner) => new(corners[corner].X * top, Height, corners[corner].Y * top);
        Vector3 Bottom(int corner) => new(corners[corner].X * foot, 0f, corners[corner].Y * foot);
    }

    private sealed class DepositView(MeshInstance3D node)
    {
        public MeshInstance3D Node { get; } = node;

        public int Pass { get; set; }
    }
}
