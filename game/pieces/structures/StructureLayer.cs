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
/// A structure is drawn the same from the moment it is laid. Construction (15c) brings
/// structures that are not finished yet, and their look is decided here, in
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

    /// <summary>The number of structures drawn.</summary>
    public int Count => _structures.Count;

    /// <summary>True when this structure is drawn.</summary>
    public bool Draws(EntityId entity) => _structures.ContainsKey(entity);

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

        if (!_materials.TryGetValue(owner, out var materials))
        {
            var color = PlayerColors.Of(owner);
            materials = (PlaceholderMeshes.Flat(Stone.Lerp(color, WallTint)), PlaceholderMeshes.Flat(color));
            _materials.Add(owner, materials);
        }

        // The middle of the footprint, from the tiles themselves: a whole or a half number of tiles.
        var body = new MeshInstance3D
        {
            Name = $"Structure{entity.Value}",
            Mesh = mesh,
            Position = new Vector3(placed.X + (placed.Size / 2f), 0f, placed.Y + (placed.Size / 2f)),
        };
        body.SetSurfaceOverrideMaterial(StructureShapes.WallsSurface, materials.Walls);
        body.SetSurfaceOverrideMaterial(StructureShapes.RoofSurface, materials.Roof);

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
