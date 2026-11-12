using Sim.Core;
using Sim.Numerics;
using Sim.Setup;

namespace Sim.Combat;

/// <summary>Places units that can fight and be damaged.</summary>
public static class CombatSpawn
{
    /// <summary>
    /// Places a unit of a chassis for a player: an entity that walks at the speed of the player's
    /// blueprint of that chassis and keeps the spacing of its line, with its full hit points, idle
    /// at its post. Call it before the
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

        var blueprint = match.Research.Tech(player).Blueprints[chassis];
        var entity = match.SpawnUnit(player, position, blueprint.SpeedPerTick, blueprint.Line.Base.Spacing.ToFraction().ToFixed());
        match.Combatants.Add(entity, new Combatant { Chassis = chassis, Anchor = position, Stood = position });
        match.Health.Add(entity, Health.Full(blueprint.HitPoints));
        return entity;
    }
}
