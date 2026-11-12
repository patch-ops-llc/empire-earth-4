using System.Runtime.InteropServices;
using Sim.Core;
using Sim.Data;
using Sim.Map;

namespace Ai;

/// <summary>
/// A reason that must hold for the reaction delay of the difficulty before it is acted on
/// (design 5.5). It counts from the round the reason was first found, and starts again when the
/// reason is gone.
/// </summary>
internal struct AiReaction
{
    private bool _counting;
    private int _since;

    /// <summary>True when the reason has held for the delay, this round included.</summary>
    public bool Holds(bool reason, int tick, int delay)
    {
        if (!reason)
        {
            _counting = false;
            return false;
        }

        if (!_counting)
        {
            _counting = true;
            _since = tick;
        }

        return tick - _since >= delay;
    }

    public void Reset() => _counting = false;
}

/// <summary>What the parts that lead units share: where a group is, and the commands that send it.</summary>
internal static class AiOrders
{
    public static int TileX(in OwnFighter unit) => unit.Position.X.FloorToInt();

    public static int TileY(in OwnFighter unit) => unit.Position.Y.FloorToInt();

    /// <summary>The tile in the middle of a group: the mean of where its units stand.</summary>
    public static (int X, int Y) MiddleOf(List<OwnFighter> units)
    {
        if (units.Count == 0)
        {
            return (0, 0);
        }

        long x = 0;
        long y = 0;
        foreach (var unit in units)
        {
            x += TileX(unit);
            y += TileY(unit);
        }

        return ((int)(x / units.Count), (int)(y / units.Count));
    }

    /// <summary>The units of a group that stand within so many tiles of a tile.</summary>
    public static int CountNear(List<OwnFighter> units, int x, int y, int within)
    {
        var count = 0;
        foreach (var unit in units)
        {
            if (AiSearch.DistanceSquared(TileX(unit), TileY(unit), x, y) <= (long)within * within)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The middle of the marker of a kind that lies best on the way from one tile to another:
    /// see <see cref="TryFindMarker"/>.
    /// </summary>
    public static bool TryFindPassage(AiSight sight, MarkerKind kind, int fromX, int fromY, int toX, int toY, out int x, out int y)
    {
        var found = TryFindMarker(sight, kind, fromX, fromY, toX, toY, out var marker);
        x = marker is null ? 0 : MiddleX(marker);
        y = marker is null ? 0 : MiddleY(marker);
        return found;
    }

    /// <summary>
    /// The marker of a kind that lies best on the way from one tile to another: the one for
    /// which the way through its middle is shortest. False when the map has none of the kind,
    /// and when the way through the best is more than twice the straight way.
    /// </summary>
    public static bool TryFindMarker(AiSight sight, MarkerKind kind, int fromX, int fromY, int toX, int toY, out MapMarker? marker)
    {
        marker = null;
        var best = long.MaxValue;
        foreach (var candidate in sight.Markers)
        {
            if (candidate.Kind != kind)
            {
                continue;
            }

            var middleX = MiddleX(candidate);
            var middleY = MiddleY(candidate);
            var way = Length(fromX, fromY, middleX, middleY) + Length(middleX, middleY, toX, toY);
            if (way < best)
            {
                best = way;
                marker = candidate;
            }
        }

        return best <= 2 * Math.Max(1, Length(fromX, fromY, toX, toY));
    }

    public static int MiddleX(MapMarker marker) => marker.Area.X + (marker.Area.SizeX / 2);

    public static int MiddleY(MapMarker marker) => marker.Area.Y + (marker.Area.SizeY / 2);

    /// <summary>Sends units to a tile with a move or an attack-move. Nothing is added for an empty list.</summary>
    public static void Send(CommandType type, int player, List<EntityId> units, int x, int y, List<Command> commands)
    {
        if (units.Count == 0)
        {
            return;
        }

        var point = MapGrid.TileCenter(x, y);
        commands.Add(new Command(type, player, CollectionsMarshal.AsSpan(units), point.X, point.Y));
    }

    /// <summary>Orders units to attack one entity. Nothing is added for an empty list.</summary>
    public static void Attack(int player, List<EntityId> units, EntityId target, List<Command> commands)
    {
        if (units.Count > 0)
        {
            commands.Add(new Command(CommandType.Attack, player, CollectionsMarshal.AsSpan(units), targetEntity: target));
        }
    }

    private static long Length(int x, int y, int otherX, int otherY) =>
        Sim.Numerics.IntegerMath.Sqrt((ulong)AiSearch.DistanceSquared(x, y, otherX, otherY));
}
