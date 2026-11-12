using Godot;
using Sim.Numerics;

namespace Game;

/// <summary>
/// A red cross for a moment on a tile of the player's wall order that its worker could not lay
/// (<see cref="Sim.Core.SimEventKind.WallTileSkipped"/>), so that the player sees where the gap
/// in the wall is. <see cref="WallMessages"/> says why.
/// </summary>
public partial class WallSkipMarks : WorldLayer
{
    private const double Seconds = 4.0;
    private const double FadeSeconds = 1.0;

    private static readonly Color Blocked = new(1f, 0.32f, 0.26f);

    private readonly List<(int X, int Y, double Left)> _marks = [];
    private WallSketch _sketch = null!;

    /// <summary>The number of marks shown now.</summary>
    public int Count => _marks.Count;

    /// <summary>Marks the tile that holds a point of the map.</summary>
    public void ShowAt(FixedVector2 point) => _marks.Add((point.X.FloorToInt(), point.Y.FloorToInt(), Seconds));

    protected override void Attach() => _sketch = new WallSketch(this, View.Match.Map);

    public override void EveryFrame(double delta)
    {
        _sketch.Begin();
        for (var i = _marks.Count - 1; i >= 0; i--)
        {
            var (x, y, left) = _marks[i];
            left -= delta;
            if (left <= 0.0)
            {
                _marks.RemoveAt(i);
                continue;
            }

            _marks[i] = (x, y, left);
            var solid = (float)Math.Min(1.0, left / FadeSeconds);
            _sketch.Tile(x, y, new Color(Blocked, 0.5f * solid));
            _sketch.Cross(x, y, new Color(Blocked, 0.95f * solid));
        }

        _sketch.End();
    }
}
