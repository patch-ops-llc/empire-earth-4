using Sim.Construction;
using Sim.Data;

namespace Ai;

/// <summary>A site at which an order to build was not carried out, and until when the computer player leaves it alone.</summary>
internal readonly record struct RefusedSite(int Type, int X, int Y, int UntilTick);

/// <summary>
/// Finds the tiles for a structure. A site is taken only when the ground around it is open too,
/// so that structures never close a way through the base, and never cover the tiles on which
/// workers stand around a deposit or a Generator.
/// </summary>
internal sealed class AiSites
{
    /// <summary>
    /// The open tiles kept on every side of a structure: one for the workers and new units that
    /// stand beside it, and one to walk past them.
    /// </summary>
    public const int Margin = 2;

    private readonly AiSight _sight;
    private readonly List<RefusedSite> _refused = [];

    public AiSites(AiSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        _sight = sight;
    }

    /// <summary>Notes a site at which an order to build was not carried out, so that it is not ordered again at once.</summary>
    public void Refuse(int type, int x, int y, int untilTick) => _refused.Add(new RefusedSite(type, x, y, untilTick));

    /// <summary>Forgets the refused sites whose time is over.</summary>
    public void Forget(int tick) => _refused.RemoveAll(site => site.UntilTick <= tick);

    /// <summary>True when an order for a structure near the tile was refused not long ago.</summary>
    public bool WasRefusedNear(int type, int x, int y, int within)
    {
        foreach (var site in _refused)
        {
            if (site.Type == type && Math.Abs(site.X - x) <= within && Math.Abs(site.Y - y) <= within)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The site nearest a tile, searched outward ring by ring, each ring a square around the tile.
    /// Of the sites of one ring the one nearest (<paramref name="preferX"/>,
    /// <paramref name="preferY"/>) is taken, and of those the first in the order of the search.
    /// </summary>
    /// <param name="structure">The structure to place.</param>
    /// <param name="aroundX">The x of the tile to search around.</param>
    /// <param name="aroundY">The y of the tile to search around.</param>
    /// <param name="rings">How many rings to search.</param>
    /// <param name="preferX">The x of the tile that breaks a tie within a ring.</param>
    /// <param name="preferY">The y of that tile.</param>
    /// <param name="alsoNeeds">A further rule a site must satisfy, or null.</param>
    /// <param name="x">The x of the lowest tile of the site found.</param>
    /// <param name="y">The y of the lowest tile of the site found.</param>
    public bool TryFind(
        StructureDef structure,
        int aroundX,
        int aroundY,
        int rings,
        int preferX,
        int preferY,
        Func<int, int, bool>? alsoNeeds,
        out int x,
        out int y)
    {
        ArgumentNullException.ThrowIfNull(structure);

        // The lowest tile that puts the middle of the footprint on the tile searched around.
        var originX = aroundX - (structure.Footprint / 2);
        var originY = aroundY - (structure.Footprint / 2);
        for (var ring = 0; ring <= rings; ring++)
        {
            var found = false;
            var best = long.MaxValue;
            x = 0;
            y = 0;
            for (var offsetY = -ring; offsetY <= ring; offsetY++)
            {
                // Only the edge of the square: the rings inside it have been searched.
                var step = Math.Abs(offsetY) == ring ? 1 : 2 * ring;
                for (var offsetX = -ring; offsetX <= ring; offsetX += Math.Max(1, step))
                {
                    var candidateX = originX + offsetX;
                    var candidateY = originY + offsetY;
                    if (!IsGood(structure, candidateX, candidateY) || (alsoNeeds is not null && !alsoNeeds(candidateX, candidateY)))
                    {
                        continue;
                    }

                    var distance = DistanceSquared(candidateX, candidateY, structure.Footprint, preferX, preferY);
                    if (distance < best)
                    {
                        best = distance;
                        x = candidateX;
                        y = candidateY;
                        found = true;
                    }
                }
            }

            if (found)
            {
                return true;
            }
        }

        x = 0;
        y = 0;
        return false;
    }

    /// <summary>
    /// True when the structure may be ordered with its lowest tile at (x, y) and the ground around
    /// it is open: no cliff, deposit, structure or planned structure within <see cref="Margin"/>
    /// tiles, and no order refused there not long ago.
    /// </summary>
    public bool IsGood(StructureDef structure, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(structure);

        var size = structure.Footprint;
        if (_sight.CheckSite(structure, x, y) != BuildProblem.None || WasRefusedNear(structure.Index, x, y, within: 0))
        {
            return false;
        }

        for (var tileY = y - Margin; tileY < y + size + Margin; tileY++)
        {
            for (var tileX = x - Margin; tileX < x + size + Margin; tileX++)
            {
                if (!_sight.CanStandOn(tileX, tileY))
                {
                    return false;
                }
            }
        }

        // A build order blocks nothing until its foundation is laid, so the planned ones are kept clear of here.
        foreach (var order in _sight.BuildOrders)
        {
            if (x < order.X + order.Size + Margin
                && order.X < x + size + Margin
                && y < order.Y + order.Size + Margin
                && order.Y < y + size + Margin)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The square of the distance from the middle of a footprint to the middle of a tile, in half tiles.</summary>
    public static long DistanceSquared(int x, int y, int size, int tileX, int tileY)
    {
        long dx = (2L * x) + size - ((2L * tileX) + 1);
        long dy = (2L * y) + size - ((2L * tileY) + 1);
        return (dx * dx) + (dy * dy);
    }
}
