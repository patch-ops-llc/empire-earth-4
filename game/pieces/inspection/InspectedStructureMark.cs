using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// A line round the structure of another player that the inspection panel is about, so that the
/// player sees which structure the panel describes. It lies where the player knows the structure
/// to be, also when that is a memory. A structure of the player's own that is selected has the
/// line every selected structure has.
/// </summary>
public partial class InspectedStructureMark : WorldLayer
{
    // The same height as the line round a selected structure.
    private const float Lift = 0.06f;

    private static readonly Color MarkColor = new(1f, 0.95f, 0.85f);

    private readonly Dictionary<int, ArrayMesh> _outlines = [];
    private MeshInstance3D? _line;
    private EntityId _structure;

    /// <summary>The structure the line is round, or <see cref="EntityId.None"/> for no line.</summary>
    public EntityId Structure
    {
        get => _structure;
        set
        {
            _structure = value;
            Place();
        }
    }

    /// <summary>The lowest tile and the side of the square the line is round now, or null while there is no line.</summary>
    public (int X, int Y, int Size)? Marked { get; private set; }

    protected override void Attach()
    {
        _line = new MeshInstance3D
        {
            Name = "Inspected",
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_line);
    }

    public override void AfterTick() => Place();

    public override void EveryFrame(double delta) => Place();

    private void Place()
    {
        if (_line is null)
        {
            return;
        }

        if (Structure == EntityId.None || !View.Sight.TryGetStructure(Structure, out var seen))
        {
            Marked = null;
            _line.Visible = false;
            return;
        }

        if (!_outlines.TryGetValue(seen.Size, out var outline))
        {
            outline = StructureShapes.Outline(seen.Size, MarkColor);
            _outlines.Add(seen.Size, outline);
        }

        Marked = (seen.X, seen.Y, seen.Size);
        _line.Mesh = outline;
        _line.Position = new Vector3(seen.X + (seen.Size / 2f), Lift, seen.Y + (seen.Size / 2f));
        _line.Visible = true;
    }
}
