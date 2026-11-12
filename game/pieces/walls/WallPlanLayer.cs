using Godot;

namespace Game;

/// <summary>
/// The tiles of the local player's wall orders that are paid for and not yet laid (18a), drawn
/// as a plan: a see-through piece of wall in the player's color on each, joined to the tiles
/// beside it that are planned too or stand already, so that the player sees the wall to come.
/// </summary>
/// <remarks>
/// Nothing stands on such a tile yet and it is open ground, so it is drawn as a plan and not
/// as a structure. Only the local player's own orders are drawn: where another player means to
/// build is that player's to know until a tile is laid. A tile leaves the plan when it is laid,
/// when its worker has to leave it out, and when the order ends.
/// </remarks>
public partial class WallPlanLayer : WorldLayer
{
    private const float PieceAlpha = 0.3f;
    private const float TileAlpha = 0.22f;

    // The planned tiles of the player, by y times this plus x.
    private const long Row = 1L << 20;

    private readonly StructureLayer _structures;
    private readonly HashSet<long> _planned = [];
    private readonly List<(int X, int Y)> _tiles = [];
    private WallSketch _sketch = null!;
    private Color _color;

    /// <summary>Makes the layer, which joins its plans to the wall tiles this layer draws.</summary>
    public WallPlanLayer(StructureLayer structures) => _structures = structures;

    /// <summary>The number of planned tiles drawn.</summary>
    public int Count => _tiles.Count;

    /// <summary>True when a planned tile is drawn on a tile of the map.</summary>
    public bool Draws(int x, int y) => _planned.Contains((y * Row) + x);

    protected override void Attach()
    {
        _sketch = new WallSketch(this, View.Match.Map);
        _color = PlayerColors.Of(View.Player).Lerp(Colors.White, 0.35f);
    }

    public override void AfterTick()
    {
        var plans = View.Match.WallPlans;
        _planned.Clear();
        _tiles.Clear();
        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans.ValueAt(i);
            if (plan.Owner == View.Player && View.Match.World.Entities.IsAlive(plans.EntityAt(i)) && _planned.Add((plan.Y * Row) + plan.X))
            {
                _tiles.Add((plan.X, plan.Y));
            }
        }

        _sketch.Begin();
        foreach (var (x, y) in _tiles)
        {
            _sketch.Tile(x, y, new Color(_color, TileAlpha));
            _sketch.Post(x, y, new Color(_color, PieceAlpha));
            for (var direction = 0; direction < WallShapes.Directions.Length; direction++)
            {
                var (dx, dy) = WallShapes.Directions[direction];
                if (!IsPiece(x + dx, y + dy))
                {
                    continue;
                }

                // Round a corner the wall goes by the tile between, as a wall that stands does.
                if (dx != 0 && dy != 0 && (IsPiece(x + dx, y) || IsPiece(x, y + dy)))
                {
                    continue;
                }

                _sketch.Arm(x, y, direction, new Color(_color, PieceAlpha));
            }
        }

        _sketch.End();
    }

    // True for a tile that is planned, or where a piece of the player's wall stands.
    private bool IsPiece(int x, int y) => _planned.Contains((y * Row) + x) || _structures.Walls.IsWallOf(View.Player, x, y);
}
