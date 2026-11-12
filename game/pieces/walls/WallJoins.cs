using Sim.Setup;
using Sim.Vision;

namespace Game;

/// <summary>
/// Which wall tiles are pieces of one wall, as the player knows them (design 5.3): a tile joins
/// the tiles of the same owner round it, so that a line of tiles is drawn as a line.
/// </summary>
/// <remarks>
/// <para>
/// It is read from the structures the local player knows of and from nothing else. A tile the
/// player has never seen joins nothing, so the end of a wall on the screen never points at a
/// piece the fog still hides, and a tile the player remembers joins as it was last seen.
/// </para>
/// <para>
/// A tile joins the four tiles that share an edge with it. It joins a tile that shares only a
/// corner when the wall has no other way there: when neither of the two tiles that touch both
/// is a piece of the wall. A run along a diagonal is closed to units all the same (18a), and is
/// drawn closed.
/// </para>
/// <para>
/// A gate (18b) is a wall tile, and joins and is joined as one. Read for a view, this also says
/// which way the wall runs through each gate and which gates are drawn open
/// (<see cref="GateShapes"/>). A gate is open to its owner's units from the tick it is finished
/// and to nobody else's ever, and the rules have no door. The door on the screen is open while
/// a unit of the gate's owner that the player sees is on the gate's tile or on a tile beside
/// it, and shut otherwise, so that the player sees who goes through a gate by watching it. A
/// gate the player only remembers is drawn shut.
/// </para>
/// </remarks>
public sealed class WallJoins
{
    // The owner of the wall tile the player knows of on each tile, by y times this plus x.
    private const long Row = 1L << 20;

    private readonly Dictionary<long, int> _owners = [];

    // The finished gates the player sees now, by tile, with their owners; those of them that
    // are drawn open; and the tiles of the player's own wall orders that are still to be laid.
    private readonly Dictionary<long, int> _gates = [];
    private readonly HashSet<long> _open = [];
    private readonly HashSet<long> _planned = [];
    private int _planner = -1;

    /// <summary>The number of wall tiles read.</summary>
    public int Count => _owners.Count;

    /// <summary>The number of gates drawn open.</summary>
    public int OpenCount => _open.Count;

    /// <summary>
    /// Reads the wall tiles among the structures a player knows of, in place of what was read
    /// before. Read so, no gate is open, and a gate that stands alone has the wall run through
    /// it from side to side.
    /// </summary>
    public void Read(Match match, List<SeenStructure> known)
    {
        _owners.Clear();
        _gates.Clear();
        _open.Clear();
        _planned.Clear();
        _planner = -1;
        var gate = match.Data.Gate?.Index ?? -1;
        foreach (var seen in known)
        {
            if (match.WallMap.IsWall(seen.Type))
            {
                var tile = (seen.Y * Row) + seen.X;
                _owners[tile] = seen.Owner;
                if (seen.Type == gate && seen.IsFinished && !seen.IsRemembered)
                {
                    _gates[tile] = seen.Owner;
                }
            }
        }
    }

    /// <summary>
    /// Reads the wall tiles among the structures the player of a view knows of, and with them
    /// what a gate's shape needs: the tiles the player's own wall orders have yet to lay, which
    /// say which way a wall will run through a gate that has no wall beside it yet, and the
    /// units the player sees by each gate, which open its door.
    /// </summary>
    public void Read(MatchView view, List<SeenStructure> known)
    {
        var match = view.Match;
        Read(match, known);
        if (match.Data.Gate is null)
        {
            return;
        }

        _planner = view.Player;
        var plans = match.WallPlans;
        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans.ValueAt(i);
            if (plan.Owner == view.Player && match.World.Entities.IsAlive(plans.EntityAt(i)))
            {
                _planned.Add((plan.Y * Row) + plan.X);
            }
        }

        if (_gates.Count == 0)
        {
            return;
        }

        var movers = match.Movers;
        for (var i = 0; i < movers.Count; i++)
        {
            var unit = movers.EntityAt(i);
            if (!match.World.Entities.IsAlive(unit) || !match.Bodies.TryGet(unit, out var body) || !view.Sight.ShowsUnit(unit))
            {
                continue;
            }

            var x = body.Position.X.FloorToInt();
            var y = body.Position.Y.FloorToInt();
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var tile = ((y + dy) * Row) + x + dx;
                    if (_gates.TryGetValue(tile, out var owner) && owner == body.Owner)
                    {
                        _open.Add(tile);
                    }
                }
            }
        }
    }

    /// <summary>True when the gate on a tile is drawn open: a unit of its owner that the player sees is on it or beside it.</summary>
    public bool IsOpen(int x, int y) => _open.Contains((y * Row) + x);

    /// <summary>True when the player knows of a wall tile of this owner on a tile.</summary>
    public bool IsWallOf(int owner, int x, int y) => _owners.TryGetValue((y * Row) + x, out var found) && found == owner;

    /// <summary>
    /// The shape a structure is drawn with (<see cref="WallShapes"/>): its joins and how worn it
    /// is for a wall tile, and 0 for every other structure, which has one shape.
    /// </summary>
    public int ShapeOf(Match match, in SeenStructure seen)
    {
        if (!match.WallMap.IsWall(seen.Type))
        {
            return 0;
        }

        var joins = JoinsOf(seen.Owner, seen.X, seen.Y);
        var stage = WallShapes.StageOf(seen.HitPoints, seen.MaxHitPoints);
        return seen.Type == match.Data.Gate?.Index
            ? GateShapes.ShapeOf(joins, stage, RunsAlongY(seen.Owner, seen.X, seen.Y, joins), IsOpen(seen.X, seen.Y))
            : WallShapes.ShapeOf(joins, stage);
    }

    /// <summary>
    /// Which way the wall runs through a gate of an owner on a tile: true for up and down the
    /// map. The wall tiles beside the gate say it. Where there are none yet, the tiles the
    /// owner's wall orders have still to lay say it, for the player whose orders were read.
    /// A gate with neither has the wall run from side to side.
    /// </summary>
    public bool RunsAlongY(int owner, int x, int y, int joins)
    {
        if (GateShapes.RunOf(joins) is { } run)
        {
            return run;
        }

        if (owner != _planner || IsPlanned(x - 1, y) || IsPlanned(x + 1, y))
        {
            return false;
        }

        return IsPlanned(x, y - 1) || IsPlanned(x, y + 1);
    }

    /// <summary>
    /// Which way a wall would run through a gate of the player whose orders were read, were
    /// one placed on a tile now: true for up and down the map. What a placement preview asks.
    /// </summary>
    public bool WouldRunAlongY(int x, int y) => _planner >= 0 && RunsAlongY(_planner, x, y, JoinsOf(_planner, x, y));

    /// <summary>The joins of a wall tile of an owner on a tile, a bit for each of <see cref="WallShapes.Directions"/>.</summary>
    public int JoinsOf(int owner, int x, int y)
    {
        var joins = 0;
        for (var i = 0; i < WallShapes.Directions.Length; i++)
        {
            var (dx, dy) = WallShapes.Directions[i];
            if (!IsWallOf(owner, x + dx, y + dy))
            {
                continue;
            }

            // Round a corner the wall goes by the tile between, and not across it as well.
            if (dx != 0 && dy != 0 && (IsWallOf(owner, x + dx, y) || IsWallOf(owner, x, y + dy)))
            {
                continue;
            }

            joins |= 1 << i;
        }

        return joins;
    }

    private bool IsPlanned(int x, int y) => _planned.Contains((y * Row) + x);

    /// <summary>
    /// The wall tiles that are one wall with a tile: every tile of the owner that the player
    /// knows of and that is reached from it tile by tile, the tile itself among them. Zero when
    /// the player knows of no wall of the owner there.
    /// </summary>
    public int LengthOfWallAt(int owner, int x, int y)
    {
        if (!IsWallOf(owner, x, y))
        {
            return 0;
        }

        var seen = new HashSet<long> { (y * Row) + x };
        var open = new Stack<(int X, int Y)>();
        open.Push((x, y));
        while (open.Count > 0)
        {
            var (atX, atY) = open.Pop();
            foreach (var (dx, dy) in WallShapes.Directions)
            {
                if (IsWallOf(owner, atX + dx, atY + dy) && seen.Add(((atY + dy) * Row) + atX + dx))
                {
                    open.Push((atX + dx, atY + dy));
                }
            }
        }

        return seen.Count;
    }
}
