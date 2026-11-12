using Godot;
using Sim.Construction;

namespace Game;

/// <summary>
/// The run of wall the player is drawing, shown tile by tile as the order for it would be
/// carried out (<see cref="WallRunPlan"/>): a see-through piece of wall on every tile that
/// would be laid, and a cross on every tile that would be left out.
/// </summary>
/// <remarks>
/// <para>
/// Green is wall the order lays and the player pays for. A red tile with a cross is one where
/// no wall can stand: the order leaves it out and it costs nothing. A pale tile is one where
/// the player's wall stands already, which costs nothing either, so that a run can start from
/// an old wall. When the order would not be taken at all, the tiles it would have laid are
/// amber, as when the player cannot pay for them.
/// </para>
/// <para>
/// The layer judges nothing: the wall tool says what to show.
/// </para>
/// </remarks>
public partial class WallRunPreview : WorldLayer
{
    private static readonly Color Good = new(0.45f, 1f, 0.5f);
    private static readonly Color Blocked = new(1f, 0.32f, 0.26f);
    private static readonly Color Warned = new(1f, 0.74f, 0.25f);
    private static readonly Color There = new(0.9f, 0.92f, 1f);

    private const float PieceAlpha = 0.5f;
    private const float TileAlpha = 0.42f;
    private const float LeftOutAlpha = 0.62f;
    private const float CrossAlpha = 0.95f;
    private const float ThereAlpha = 0.22f;

    private WallSketch _sketch = null!;
    private WallRunPlan? _plan;

    /// <summary>True while a run is shown.</summary>
    public bool IsShown => _plan is not null;

    /// <summary>The number of tiles drawn as wall that would be laid, in the last frame.</summary>
    public int LaidShown { get; private set; }

    /// <summary>The number of tiles marked with a cross, in the last frame.</summary>
    public int LeftOutShown { get; private set; }

    /// <summary>The number of tiles marked as wall that stands already, in the last frame.</summary>
    public int StandingShown { get; private set; }

    /// <summary>Shows a run, in place of what was shown.</summary>
    public void Show(WallRunPlan plan) => _plan = plan;

    /// <summary>Shows nothing.</summary>
    public void Clear() => _plan = null;

    /// <summary>The color of the tiles a run would lay: green when the order would be taken, amber when not.</summary>
    public static Color ColorOf(WallRunPlan plan) => plan.Problem == BuildProblem.None ? Good : Warned;

    protected override void Attach() => _sketch = new WallSketch(this, View.Match.Map);

    public override void EveryFrame(double delta)
    {
        _sketch.Begin();
        LaidShown = 0;
        LeftOutShown = 0;
        StandingShown = 0;
        if (_plan is { } plan)
        {
            var laid = ColorOf(plan);
            for (var i = 0; i < plan.Tiles.Count; i++)
            {
                var (x, y) = plan.Tiles[i];
                switch (plan.Kinds[i])
                {
                    case WallRunTile.Laid:
                        LaidShown++;
                        _sketch.Tile(x, y, new Color(laid, TileAlpha));
                        _sketch.Post(x, y, new Color(laid, PieceAlpha));
                        Join(plan, i, i - 1, laid);
                        Join(plan, i, i + 1, laid);
                        break;

                    case WallRunTile.Standing:
                        StandingShown++;
                        _sketch.Tile(x, y, new Color(There, ThereAlpha));
                        break;

                    default:
                        LeftOutShown++;
                        _sketch.Tile(x, y, new Color(Blocked, LeftOutAlpha));
                        _sketch.Cross(x, y, new Color(Blocked, CrossAlpha));
                        break;
                }
            }
        }

        _sketch.End();
    }

    // The arm of a tile that would be laid toward the tile before or after it in the run, when
    // there will be wall on that one too.
    private void Join(WallRunPlan plan, int from, int to, Color color)
    {
        if (to < 0 || to >= plan.Tiles.Count || plan.Kinds[to] == WallRunTile.LeftOut)
        {
            return;
        }

        var step = (plan.Tiles[to].X - plan.Tiles[from].X, plan.Tiles[to].Y - plan.Tiles[from].Y);
        var direction = Array.IndexOf(WallShapes.Directions, step);
        if (direction >= 0)
        {
            _sketch.Arm(plan.Tiles[from].X, plan.Tiles[from].Y, direction, new Color(color, PieceAlpha));
        }
    }
}
