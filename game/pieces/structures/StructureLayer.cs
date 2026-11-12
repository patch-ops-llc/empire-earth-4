using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Vision;

namespace Game;

/// <summary>
/// Draws the structures the local player knows of (design 5.3): a stand-in shape over the tiles
/// each one blocks, with its roof in its owner's color, and a line round a selected one. A
/// structure the player remembers and does not see now is drawn as a memory: the same shape,
/// dimmed.
/// </summary>
/// <remarks>
/// <para>
/// What is drawn is asked of the view's <see cref="PlayerSight"/> after every tick, and the
/// match's own structures are never read here: a structure of another player that the player
/// has never seen has no node, and one that the player remembers keeps its node, as it was
/// last seen, also when it is gone, until the player sees its tiles again. Every node is made
/// from a <see cref="SeenStructure"/> and from nothing else, so whatever is drawn of a
/// structure's condition is drawn as the player last saw it.
/// </para>
/// <para>
/// A structure that is still building itself (15c) is drawn unfinished, as
/// <see cref="FoundationLook"/> has it, for as long as <see cref="SeenStructure.IsFinished"/>
/// is false, and as far built as <see cref="SeenStructure.Built"/> says. For another player's
/// structure both are as the player last saw them, so a remembered foundation stays as far
/// built as it was then, also when it has long been finished.
/// </para>
/// </remarks>
public partial class StructureLayer : WorldLayer
{
    // Above the ground by enough not to flicker against it or against the territory under it.
    private const float OutlineLift = 0.06f;

    private static readonly Color Stone = new(0.72f, 0.70f, 0.66f);
    private static readonly Color SelectedColor = new(0.55f, 1f, 0.55f);

    // How much of the owner's color the walls take, so that a structure reads as its owner's from every side.
    private const float WallTint = 0.3f;

    // A memory loses this much of its color, and then takes the share of the fog that lies on
    // explored ground, so that it is as dim as the forest beside it and grayer.
    private const float MemoryGray = 0.45f;

    private readonly Dictionary<EntityId, StructureNode> _structures = [];
    private readonly Dictionary<int, ArrayMesh> _meshes = [];
    private readonly Dictionary<int, ArrayMesh> _outlines = [];
    private readonly List<SeenStructure> _known = [];
    private readonly List<EntityId> _gone = [];
    private int _pass;
    private bool _settled = true;

    /// <summary>The number of structures drawn, memories among them.</summary>
    public int Count => _structures.Count;

    /// <summary>True when this structure is drawn, as it stands or as a memory.</summary>
    public bool Draws(EntityId entity) => _structures.ContainsKey(entity);

    /// <summary>True when this structure is drawn as a memory: dimmed, as the player last saw it.</summary>
    public bool DrawsAsMemory(EntityId entity) => _structures.TryGetValue(entity, out var node) && node.Drawn.IsRemembered;

    /// <summary>
    /// How much of a structure that is building itself is drawn as built, from 0 to 1, or null
    /// for one that is drawn finished or not drawn.
    /// </summary>
    public float? DrawnBuilt(EntityId entity) => _structures.TryGetValue(entity, out var node) ? node.Unfinished?.Shown : null;

    /// <summary>
    /// Adds every structure that is drawn unfinished to a list, for what is drawn over one, as
    /// the bar of its progress: nothing then shows a foundation this layer does not.
    /// </summary>
    public void CollectUnfinished(List<UnfinishedStructure> into)
    {
        foreach (var (entity, node) in _structures)
        {
            if (node.Unfinished is { } look)
            {
                into.Add(new UnfinishedStructure(entity, node.Body.Position, look.Height, look.Shown, node.Dimmed));
            }
        }
    }

    protected override void Attach() => View.Selection.Changed += ShowSelection;

    // Draws what the player knows of and nothing else, whatever was drawn before.
    public override void AfterTick()
    {
        View.Sight.CollectStructures(_known);
        _pass++;
        foreach (var seen in _known)
        {
            // A structure drawn finished that the player knows as unfinished is made anew: that
            // happens only when the development switch has shown it as it stands and the fog is
            // laid back over a memory of its foundation.
            if (_structures.TryGetValue(seen.Entity, out var node)
                && (!SameShape(node.Drawn, seen) || (node.Unfinished is null && !seen.IsFinished)))
            {
                Remove(seen.Entity);
                node = null;
            }

            node ??= Add(seen);
            if (node is not null)
            {
                ShowProgress(node, in seen);
                node.Drawn = seen;
                node.Pass = _pass;
                _settled &= node.Dimmed == Wanted(seen);
            }
        }

        foreach (var (entity, node) in _structures)
        {
            if (node.Pass != _pass)
            {
                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            Remove(entity);
        }

        _gone.Clear();
    }

    // A structure that goes out of sight dims as the fog comes over the ground round it, and
    // one that comes into sight brightens with it.
    public override void EveryFrame(double delta)
    {
        if (_settled)
        {
            return;
        }

        var step = (float)delta / FogPicture.FadeSeconds;
        var settled = true;
        foreach (var node in _structures.Values)
        {
            var wanted = Wanted(node.Drawn);
            if (node.Dimmed != wanted)
            {
                node.Dimmed = Mathf.MoveToward(node.Dimmed, wanted, step);
                Paint(node);
                settled &= node.Dimmed == wanted;
            }
        }

        _settled = settled;
    }

    private static float Wanted(in SeenStructure seen) => seen.IsRemembered ? 1f : 0f;

    // Whatever the shape and the place of a node are made from. A structure never changes them,
    // so this is true for every structure that is drawn already.
    private static bool SameShape(in SeenStructure drawn, in SeenStructure seen) =>
        drawn.Type == seen.Type && drawn.Owner == seen.Owner && drawn.X == seen.X && drawn.Y == seen.Y && drawn.Size == seen.Size;

    private StructureNode? Add(in SeenStructure seen)
    {
        if (View.Match.Data.StructureOf(seen.Type) is not { } structure)
        {
            return null;
        }

        var node = Create(structure, in seen);
        node.Outline.Visible = View.Selection.Contains(seen.Entity);
        _structures.Add(seen.Entity, node);
        return node;
    }

    // Fills an unfinished structure as far as the player knows it to be built, and gives one
    // the player knows to be finished the look of a finished one.
    private static void ShowProgress(StructureNode node, in SeenStructure seen)
    {
        if (node.Unfinished is not { } look)
        {
            return;
        }

        if (!seen.IsFinished)
        {
            look.Show(seen.Built);
            return;
        }

        node.Unfinished = null;
        node.Body.SetSurfaceOverrideMaterial(StructureShapes.WallsSurface, node.Walls);
        node.Body.SetSurfaceOverrideMaterial(StructureShapes.RoofSurface, node.Roof);
        node.Body.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        Paint(node);
    }

    private void Remove(EntityId entity)
    {
        if (_structures.Remove(entity, out var node))
        {
            node.Body.QueueFree();
        }
    }

    private void ShowSelection()
    {
        foreach (var (entity, node) in _structures)
        {
            node.Outline.Visible = View.Selection.Contains(entity);
        }
    }

    private StructureNode Create(StructureDef structure, in SeenStructure seen)
    {
        if (!_meshes.TryGetValue(structure.Index, out var mesh))
        {
            mesh = StructureShapes.MeshOf(structure);
            _meshes.Add(structure.Index, mesh);
        }

        if (!_outlines.TryGetValue(seen.Size, out var outline))
        {
            outline = StructureShapes.Outline(seen.Size, SelectedColor);
            _outlines.Add(seen.Size, outline);
        }

        // The middle of the footprint, from the tiles themselves: a whole or a half number of tiles.
        var body = new MeshInstance3D
        {
            Name = $"Structure{seen.Entity.Value}",
            Mesh = mesh,
            Position = new Vector3(seen.X + (seen.Size / 2f), 0f, seen.Y + (seen.Size / 2f)),
        };

        // Each structure has materials of its own, because each dims by itself.
        var color = PlayerColors.Of(seen.Owner);
        var walls = PlaceholderMeshes.Flat(Stone.Lerp(color, WallTint));
        var roof = PlaceholderMeshes.Flat(color);
        FoundationLook? unfinished = null;
        if (seen.IsFinished)
        {
            body.SetSurfaceOverrideMaterial(StructureShapes.WallsSurface, walls);
            body.SetSurfaceOverrideMaterial(StructureShapes.RoofSurface, roof);
        }
        else
        {
            unfinished = FoundationLook.On(body, structure, walls.AlbedoColor, color);
            unfinished.Show(seen.Built);
        }

        var line = new MeshInstance3D
        {
            Name = "Selected",
            Mesh = outline,
            Position = Vector3.Up * OutlineLift,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        body.AddChild(line);
        AddChild(body);

        // A structure that is first drawn as a memory, as after the development switch, is dim from the start.
        var node = new StructureNode(body, line, walls, roof, walls.AlbedoColor, color) { Dimmed = Wanted(seen), Unfinished = unfinished };
        Paint(node);
        return node;
    }

    private static void Paint(StructureNode node)
    {
        node.Unfinished?.Dim(node.Dimmed);
        node.Walls.AlbedoColor = Dim(node.WallColor, node.Dimmed);
        node.Roof.AlbedoColor = Dim(node.RoofColor, node.Dimmed);
    }

    /// <summary>A color of a structure as a memory shows it, by how far the structure has dimmed: 0 for seen now, 1 for remembered.</summary>
    public static Color Dim(Color color, float dimmed)
    {
        var gray = color.Luminance;
        var memory = color.Lerp(new Color(gray, gray, gray), MemoryGray).Lerp(FogPicture.Unknown, FogPicture.ExploredShade);
        return color.Lerp(memory, dimmed);
    }

    private sealed class StructureNode(MeshInstance3D body, MeshInstance3D outline, StandardMaterial3D walls, StandardMaterial3D roof, Color wallColor, Color roofColor)
    {
        public MeshInstance3D Body { get; } = body;

        public MeshInstance3D Outline { get; } = outline;

        public StandardMaterial3D Walls { get; } = walls;

        public StandardMaterial3D Roof { get; } = roof;

        public Color WallColor { get; } = wallColor;

        public Color RoofColor { get; } = roofColor;

        /// <summary>The look of a structure that is drawn unfinished, or null for one drawn finished.</summary>
        public FoundationLook? Unfinished { get; set; }

        /// <summary>What the node was last made or checked against.</summary>
        public SeenStructure Drawn { get; set; }

        /// <summary>How far it has dimmed, from 0 for a structure seen now to 1 for a memory.</summary>
        public float Dimmed { get; set; }

        public int Pass { get; set; }
    }
}

/// <summary>A structure the <see cref="StructureLayer"/> draws unfinished.</summary>
/// <param name="Entity">The structure.</param>
/// <param name="Middle">The middle of its footprint, on the ground.</param>
/// <param name="Height">How tall its shape is, in tiles.</param>
/// <param name="Built">How much of it is drawn as built, from 0 to 1.</param>
/// <param name="Dimmed">How far it is drawn as a memory, from 0 for a structure seen now to 1 for one remembered.</param>
public readonly record struct UnfinishedStructure(EntityId Entity, Vector3 Middle, float Height, float Built, float Dimmed);
