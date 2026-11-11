using Sim.Core;
using Sim.Numerics;
using Sim.Setup;

namespace Sim.Combat;

/// <summary>Places units that can fight and be damaged.</summary>
public static class CombatSpawn
{
    /// <summary>
    /// Places a unit of a chassis for a player: an entity that walks at the speed of the player's
    /// blueprint of that chassis, with its full hit points, idle at its post. Call it before the
    /// first tick, or inside a tick from a system or a command handler, as
    /// <see cref="Match.SpawnUnit"/> asks.
    /// </summary>
    /// <param name="match">The match.</param>
    /// <param name="player">The owner, a position in <see cref="Match.Players"/>.</param>
    /// <param name="chassis">The chassis, as its <see cref="Data.ChassisDef.Index"/>. It need not be unlocked.</param>
    /// <param name="position">Where its center goes. The tile there must be passable.</param>
    public static EntityId SpawnChassis(this Match match, int player, int chassis, FixedVector2 position)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentOutOfRangeException.ThrowIfNegative(chassis);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chassis, match.Data.Chassis.Count);

        var entity = match.SpawnUnit(player, position, match.Combat.TechOf(player).Blueprints[chassis].SpeedPerTick);
        match.Combatants.Add(entity, new Combatant { Chassis = chassis, Anchor = position, Stood = position });
        match.Health.Add(entity, Health.Full(match.Combat.TechOf(player).Blueprints[chassis].HitPoints));
        return entity;
    }

    /// <summary>
    /// Places a worker for a player: an entity that walks, with a worker's hit points (design 4.1).
    /// It is not of a chassis, so it never fights.
    /// </summary>
    public static EntityId SpawnWorker(this Match match, int player, FixedVector2 position, Fixed speedPerTick)
    {
        ArgumentNullException.ThrowIfNull(match);
        var entity = match.SpawnUnit(player, position, speedPerTick);
        match.Health.Add(entity, match.Combat.WorkerHealth(player));
        return entity;
    }
}
