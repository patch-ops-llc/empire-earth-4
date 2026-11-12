using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Structures;

namespace Game;

/// <summary>
/// Draws the structures where the match has them: a stand-in shape over the tiles each one
/// blocks, with its roof in its owner's color, and a line round a selected one.
/// </summary>
/// <remarks>
/// <para>
/// A structure appears with the simulation's <see cref="SimEventKind.StructurePlaced"/> and goes
/// with <see cref="SimEventKind.StructureRemoved"/>. Those that stood before the view opened,
/// as the starting Capitols, are read from the match once. After every tick the layer counts
/// what it draws against the match, and says so as an error when the two differ, which fails
/// the smoke test: an event that was missed must not leave a structure that is not there.
/// </para>
/// <para>
/// A structure that is still building itself (15c) is drawn unfinished, as
/// <see cref="FoundationLook"/> has it, from the moment it is laid until the match no longer
/// has it among its foundations. Which of the two looks a structure gets is decided here, in
/// <see cref="Create"/>.
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

    private readonly Dictionary<EntityId, StructureNode> _structures = [];
    private readonly Dictionary<int, ArrayMesh> _meshes = [];
    private readonly Dictionary<int, ArrayMesh> _outlines = [];
    private readonly Dictionary<int, (Material Walls, Material Roof)> _materials = [];
    private readonly Dictionary<EntityId, FoundationLook> _unfinished = [];
    private readonly List<EntityId> _finished = [];

    /// <summary>The number of structures drawn.</summary>
    public int Count => _structures.Count;

    /// <summary>True when this structure is drawn.</summary>
    public bool Draws(EntityId entity) => _structures.ContainsKey(entity);

    /// <summary>
    /// How much of a structure that is building itself is drawn as built, from 0 to 1, or null
    /// for one that is drawn finished or not drawn.
    /// </summary>
    public float? DrawnBuilt(EntityId entity) => _unfinished.TryGetValue(entity, out var look) ? look.Shown : null;

    /// <summary>
    /// Adds every structure that is drawn unfinished to a list, for what is drawn over one, as
    /// the bar of its progress: nothing then shows a foundation this layer does not.
    /// </summary>
    public void CollectUnfinished(List<UnfinishedStructure> into)
    {
        foreach (var (entity, look) in _unfinished)
        {
            if (_structures.TryGetValue(entity, out var node))
            {
                into.Add(new UnfinishedStructure(entity, node.Body.Position, look.Height, look.Shown));
            }
        }
    }

    protected override void Attach()
    {
        ReadAll();
        View.Selection.Changed += ShowSelection;
    }

    public override void AfterTick()
    {
        foreach (var happened in View.Match.Simulation.Events)
        {
            if (happened.Kind == SimEventKind.StructurePlaced)
            {
                Add(happened.Entity);
            }
            else if (happened.Kind == SimEventKind.StructureRemoved)
            {
                Remove(happened.Entity);
            }
        }

        ShowProgress();
        var standing = View.Match.Structures.Count;
        if (_structures.Count != standing)
        {
            GD.PushError($"The view draws {_structures.Count} structures and the match has {standing} at tick {View.Match.World.Tick}.");
            ReadAll();
        }
    }

    // Draws what the match has and nothing else, whatever was drawn before.
    private void ReadAll()
    {
        var structures = View.Match.Structures;
        foreach (var entity in _structures.Keys.ToArray())
        {
            if (!structures.Contains(entity))
            {
                Remove(entity);
            }
        }

        for (var i = 0; i < structures.Count; i++)
        {
            Add(structures.EntityAt(i));
        }
    }

    private void Add(EntityId entity)
    {
        var match = View.Match;
        if (_structures.ContainsKey(entity)
            || !match.World.Entities.IsAlive(entity)
            || !match.Structures.TryGet(entity, out var placed)
            || !match.Bodies.TryGet(entity, out var body)
            || match.Data.StructureOf(placed.Type) is not { } structure)
        {
            return;
        }

        var node = Create(entity, structure, placed, body.Owner);
        node.Outline.Visible = View.Selection.Contains(entity);
        _structures.Add(entity, node);
    }

    private void Remove(EntityId entity)
    {
        _unfinished.Remove(entity);
        if (_structures.Remove(entity, out var node))
        {
            node.Body.QueueFree();
        }
    }

    // Fills every unfinished structure as far as it is built, and gives one the match no longer
    // has among its foundations the look of a finished one.
    private void ShowProgress()
    {
        var foundations = View.Match.Foundations;
        foreach (var (entity, look) in _unfinished)
        {
            if (foundations.TryGet(entity, out var foundation))
            {
                look.Show(foundation.Built);
            }
            else
            {
                _finished.Add(entity);
            }
        }

        foreach (var entity in _finished)
        {
            _unfinished.Remove(entity);
            if (_structures.TryGetValue(entity, out var node) && View.Match.Bodies.TryGet(entity, out var body))
            {
                var (walls, roof) = MaterialsOf(body.Owner);
                node.Body.SetSurfaceOverrideMaterial(StructureShapes.WallsSurface, walls);
                node.Body.SetSurfaceOverrideMaterial(StructureShapes.RoofSurface, roof);
                node.Body.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            }
        }

        _finished.Clear();
    }

    private (Material Walls, Material Roof) MaterialsOf(int owner)
    {
        if (!_materials.TryGetValue(owner, out var materials))
        {
            var color = PlayerColors.Of(owner);
            materials = (PlaceholderMeshes.Flat(WallColorOf(color)), PlaceholderMeshes.Flat(color));
            _materials.Add(owner, materials);
        }

        return materials;
    }

    private static Color WallColorOf(Color owner) => Stone.Lerp(owner, WallTint);

    private void ShowSelection()
    {
        foreach (var (entity, node) in _structures)
        {
            node.Outline.Visible = View.Selection.Contains(entity);
        }
    }

    private StructureNode Create(EntityId entity, StructureDef structure, in PlacedStructure placed, int owner)
    {
        if (!_meshes.TryGetValue(structure.Index, out var mesh))
        {
            mesh = StructureShapes.MeshOf(structure);
            _meshes.Add(structure.Index, mesh);
        }

        if (!_outlines.TryGetValue(placed.Size, out var outline))
        {
            outline = StructureShapes.Outline(placed.Size, SelectedColor);
            _outlines.Add(placed.Size, outline);
        }

        // The middle of the footprint, from the tiles themselves: a whole or a half number of tiles.
        var body = new MeshInstance3D
        {
            Name = $"Structure{entity.Value}",
            Mesh = mesh,
            Position = new Vector3(placed.X + (placed.Size / 2f), 0f, placed.Y + (placed.Size / 2f)),
        };
        if (View.Match.Foundations.TryGet(entity, out var foundation))
        {
            var color = PlayerColors.Of(owner);
            var look = FoundationLook.On(body, structure, WallColorOf(color), color);
            look.Show(foundation.Built);
            _unfinished[entity] = look;
        }
        else
        {
            var (walls, roof) = MaterialsOf(owner);
            body.SetSurfaceOverrideMaterial(StructureShapes.WallsSurface, walls);
            body.SetSurfaceOverrideMaterial(StructureShapes.RoofSurface, roof);
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
        return new StructureNode(body, line);
    }

    private readonly record struct StructureNode(MeshInstance3D Body, MeshInstance3D Outline);
}

/// <summary>A structure the <see cref="StructureLayer"/> draws unfinished.</summary>
/// <param name="Entity">The structure.</param>
/// <param name="Middle">The middle of its footprint, on the ground.</param>
/// <param name="Height">How tall its shape is, in tiles.</param>
/// <param name="Built">How much of it is drawn as built, from 0 to 1.</param>
public readonly record struct UnfinishedStructure(EntityId Entity, Vector3 Middle, float Height, float Built);
