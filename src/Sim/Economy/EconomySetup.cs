using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;

namespace Sim.Economy;

/// <summary>
/// Places what the economy needs in a match: the deposits and the workers, and for tests a
/// territory without its structure. A Generator is a structure and is placed as one.
/// </summary>
public static class EconomySetup
{
    /// <summary>
    /// Makes a work site of each deposit of the map. The match setup calls it once, after it has
    /// placed the starting workers and the starting Capitols, whose territory decides who earns
    /// at a deposit.
    /// </summary>
    public static void PlaceDeposits(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);

        foreach (var deposit in match.Map.Deposits)
        {
            match.WorkSites.Add(
                match.World.Entities.Create(),
                new WorkSite
                {
                    Kind = WorkSiteKind.Deposit,
                    Owner = WorkSite.NoOwner,
                    X = deposit.X,
                    Y = deposit.Y,
                    Size = match.Data.Rules.Map.DepositSize,
                });
        }
    }

    /// <summary>
    /// Places a worker: a unit with the speed and spacing of the worker profile that can be assigned to work
    /// sites, and with a worker's hit points under its owner's Protection tier, so that it can be
    /// attacked. Call it before the first tick, or inside a tick from a system or a command handler.
    /// </summary>
    /// <param name="match">The match.</param>
    /// <param name="player">The owner.</param>
    /// <param name="position">Where its center goes. The tile there must be passable.</param>
    public static EntityId SpawnWorker(this Match match, int player, FixedVector2 position)
    {
        ArgumentNullException.ThrowIfNull(match);

        var profile = match.Data.Resources.Worker;
        var worker = match.SpawnUnit(player, position, Mover.SpeedPerTick(profile.Speed.ToFraction()), profile.Spacing.ToFraction().ToFixed());
        match.Workers.Add(worker, default);
        match.Health.Add(worker, match.Combat.WorkerHealth(player));
        return worker;
    }

    /// <summary>
    /// Places the territory a Capitol or an Outpost gives, without the structure: a circle of
    /// <paramref name="radius"/> tiles around the center of a square footprint whose lowest tile
    /// is (x, y). Destroying the entity ends the territory.
    /// </summary>
    public static EntityId PlaceTerritory(this Match match, int player, int x, int y, int footprint, int radius)
    {
        ArgumentNullException.ThrowIfNull(match);
        CheckPlayer(match, player);
        ArgumentOutOfRangeException.ThrowIfLessThan(footprint, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        var entity = match.World.Entities.Create();
        match.Territories.Add(entity, Territory.Around(player, x, y, footprint, radius));
        return entity;
    }

    private static void CheckPlayer(Match match, int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, match.Players.Count);
    }
}
