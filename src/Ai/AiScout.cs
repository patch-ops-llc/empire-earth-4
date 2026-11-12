using Sim.Combat;
using Sim.Core;
using Sim.Data;

namespace Ai;

/// <summary>What its scout is doing.</summary>
internal enum ScoutPhase
{
    /// <summary>No scout is away.</summary>
    None,

    /// <summary>The scout is on its way to the enemy.</summary>
    Out,

    /// <summary>The scout has looked and is on its way home.</summary>
    Back,
}

/// <summary>
/// The scout (design 5.5, item 4): at the interval of its personality's file it sends one
/// fighting unit to look at the enemy, and the unit comes home when it has. What the unit sees
/// on the way is remembered like everything else its player sees, by <see cref="AiIntel"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which unit.</b> Of its units at home, the one that sees furthest, of those the fastest,
/// and of those the oldest. A worker sees less than any fighting unit (design 4.1), so it sends
/// none, and while it has no unit at home the scout waits for one.
/// </para>
/// <para>
/// <b>Where.</b> To the nearest structure of the enemy's that holds territory, of those it sees
/// or remembers; with none, to the nearest other structure; and with none of those, to the
/// nearest start of another seat. It walks on a move order, which fights nothing, and turns for
/// home when the place is a tile inside its sight, when it can get no nearer, or when the walk
/// has taken too long.
/// </para>
/// </remarks>
internal sealed class AiScout
{

    private readonly AiSight _sight;
    private readonly AiScoutingDef _scouting;
    private readonly List<EntityId> _one = [];
    private int _nextAt;
    private int _deadline;
    private int _x;
    private int _y;
    private int _size;

    public AiScout(AiSight sight, AiScoutingDef scouting)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(scouting);
        _sight = sight;
        _scouting = scouting;
        _nextAt = scouting.EveryTicks;
    }

    public ScoutPhase Phase { get; private set; }

    /// <summary>The scouts it has sent.</summary>
    public int Sent { get; private set; }

    /// <summary>The tick in which it sent its first scout, or -1.</summary>
    public int FirstSentTick { get; private set; } = -1;

    /// <summary>The scouts that came home.</summary>
    public int Returned { get; private set; }

    /// <param name="fight">Its army, which says which units are away with a force or a raid.</param>
    /// <param name="hasMuster">False when it has no tile to gather on, and so none for a scout to come home to.</param>
    /// <param name="musterX">The tile its army gathers on.</param>
    /// <param name="musterY">The tile its army gathers on.</param>
    /// <param name="commands">The list its orders are added to.</param>
    public void Think(AiFight fight, bool hasMuster, int musterX, int musterY, List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(fight);
        ArgumentNullException.ThrowIfNull(commands);
        if (!_scouting.SendsScouts)
        {
            return;
        }

        if (Phase == ScoutPhase.None)
        {
            if (hasMuster && _sight.Tick >= _nextAt && TryChoose(fight, out var chosen) && TryFindPlace(AiOrders.TileX(chosen), AiOrders.TileY(chosen)))
            {
                _sight.Scout = chosen.Entity;
                Phase = ScoutPhase.Out;
                Sent++;
                if (FirstSentTick < 0)
                {
                    FirstSentTick = _sight.Tick;
                }

                _nextAt = _sight.Tick + _scouting.EveryTicks;
                Send(chosen, _x + (_size / 2), _y + (_size / 2), commands);
            }

            return;
        }

        if (!TryFindScout(out var scout))
        {
            // It did not come back.
            End();
            return;
        }

        var tileX = AiOrders.TileX(scout);
        var tileY = AiOrders.TileY(scout);
        if (Phase == ScoutPhase.Out)
        {
            // From one tile inside its sight it sees the place, and need go no nearer.
            var look = Math.Max(1, _sight.Tech.Blueprints[scout.Chassis].Sight - 1);
            if (AiSearch.DistanceToSquare(_x, _y, _size, tileX, tileY) <= (long)look * look
                || scout.Order == UnitOrder.Idle
                || _sight.Tick >= _deadline)
            {
                if (!hasMuster)
                {
                    End();
                    return;
                }

                Phase = ScoutPhase.Back;
                _x = musterX;
                _y = musterY;
                _size = 1;
                Send(scout, musterX, musterY, commands);
            }

            return;
        }

        if (AiSearch.DistanceToSquare(_x, _y, _size, tileX, tileY) <= AiFight.Near * AiFight.Near
            || scout.Order == UnitOrder.Idle
            || _sight.Tick >= _deadline)
        {
            Returned++;
            End();
        }
    }

    private void End()
    {
        Phase = ScoutPhase.None;
        _sight.Scout = EntityId.None;
    }

    private void Send(in OwnFighter scout, int x, int y, List<Command> commands)
    {
        _deadline = AiSearch.DeadlineFor(_sight.Tick, AiSearch.DistanceSquared(AiOrders.TileX(scout), AiOrders.TileY(scout), x, y));
        _one.Clear();
        _one.Add(scout.Entity);
        AiOrders.Send(CommandType.Move, _sight.Player, _one, x, y, commands);
    }

    private bool TryFindScout(out OwnFighter scout)
    {
        foreach (var fighter in _sight.Fighters)
        {
            if (fighter.Entity == _sight.Scout)
            {
                scout = fighter;
                return true;
            }
        }

        scout = default;
        return false;
    }

    private bool TryChoose(AiFight fight, out OwnFighter chosen)
    {
        chosen = default;
        var found = false;
        foreach (var fighter in _sight.Fighters)
        {
            if (fight.IsAway(fighter.Entity))
            {
                continue;
            }

            if (!found || IsBetter(fighter, chosen))
            {
                chosen = fighter;
                found = true;
            }
        }

        return found;
    }

    private bool IsBetter(in OwnFighter candidate, in OwnFighter than)
    {
        var blueprint = _sight.Tech.Blueprints[candidate.Chassis];
        var other = _sight.Tech.Blueprints[than.Chassis];
        if (blueprint.Sight != other.Sight)
        {
            return blueprint.Sight > other.Sight;
        }

        // Units are listed in the order of their IDs, so of two that are level the earlier stays.
        return blueprint.SpeedPerTick > other.SpeedPerTick;
    }

    /// <summary>Sets the square it looks at: (x, y) is its lowest tile.</summary>
    private bool TryFindPlace(int fromX, int fromY)
    {
        var best = long.MaxValue;
        var bestHoldsTerritory = false;
        foreach (var structure in _sight.EnemyStructures)
        {
            var holdsTerritory = _sight.Data.StructureOf(structure.Type)?.TerritoryRadius is not null;
            var distance = AiSearch.DistanceToSquare(structure.X, structure.Y, structure.Size, fromX, fromY);
            if (best == long.MaxValue || (holdsTerritory != bestHoldsTerritory ? holdsTerritory : distance < best))
            {
                best = distance;
                bestHoldsTerritory = holdsTerritory;
                _x = structure.X;
                _y = structure.Y;
                _size = structure.Size;
            }
        }

        if (best != long.MaxValue)
        {
            return true;
        }

        foreach (var (startX, startY) in _sight.OtherStarts)
        {
            var distance = AiSearch.DistanceSquared(startX, startY, fromX, fromY);
            if (distance < best)
            {
                best = distance;
                _x = startX;
                _y = startY;
                _size = 1;
            }
        }

        return best != long.MaxValue;
    }
}
