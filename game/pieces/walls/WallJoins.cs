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
/// </remarks>
public sealed class WallJoins
{
    // The owner of the wall tile the player knows of on each tile, by y times this plus x.
    private const long Row = 1L << 20;

    private readonly Dictionary<long, int> _owners = [];

    /// <summary>The number of wall tiles read.</summary>
    public int Count => _owners.Count;

    /// <summary>Reads the wall tiles among the structures a player knows of, in place of what was read before.</summary>
    public void Read(Match match, List<SeenStructure> known)
    {
        _owners.Clear();
        foreach (var seen in known)
        {
            if (match.WallMap.IsWall(seen.Type))
            {
                _owners[(seen.Y * Row) + seen.X] = seen.Owner;
            }
        }
    }

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

        return WallShapes.ShapeOf(JoinsOf(seen.Owner, seen.X, seen.Y), WallShapes.StageOf(seen.HitPoints, seen.MaxHitPoints));
    }

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
