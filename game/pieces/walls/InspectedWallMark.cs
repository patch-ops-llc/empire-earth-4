using Godot;

namespace Game;

/// <summary>
/// A line round the tile of another player's wall that the wall panel is about, so that the
/// player sees which tile the panel describes. A wall of the player's own that is selected has
/// the line every selected structure has.
/// </summary>
public partial class InspectedWallMark : WorldLayer
{
    // The same height as the line round a selected structure.
    private const float Lift = 0.06f;

    private static readonly Color MarkColor = new(1f, 0.95f, 0.85f);

    private MeshInstance3D _line = null!;

    /// <summary>The tile to mark, or null for none.</summary>
    public (int X, int Y)? Tile { get; set; }

    protected override void Attach()
    {
        _line = new MeshInstance3D
        {
            Name = "Inspected",
            Mesh = StructureShapes.Outline(1, MarkColor),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_line);
    }

    public override void EveryFrame(double delta)
    {
        _line.Visible = Tile is not null;
        if (Tile is { } tile)
        {
            _line.Position = new Vector3(tile.X + 0.5f, Lift, tile.Y + 0.5f);
        }
    }
}
