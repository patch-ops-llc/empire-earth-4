using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;

namespace Sim.Economy;

/// <summary>
/// Places what the economy needs in a match: what the match setup gives every player at the start,
/// and what tests and the map fixture place until workers build structures.
/// </summary>
public static class EconomySetup
{
    /// <summary>
    /// The tiles a Generator covers along each side (design 2.5). It stands here until structures
    /// have data files.
    /// </summary>
    public const int GeneratorSize = 2;

    /// <summary>
    /// Gives every player with a start the territory of their starting Capitol (design 5.2), and
    /// makes a work site of each deposit of the map. The match setup calls it once, after it has
    /// placed the starting workers.
    /// </summary>
    /// <remarks>
    /// The Capitol itself arrives with a later milestone. Until then its territory stands alone,
    /// with the footprint and radius of <c>map.start_territory</c> in the rules.
    /// </remarks>
    public static void PlaceStart(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);

        var rules = match.Data.Rules;
        for (var player = 0; player < match.Players.Count; player++)
        {
            if (!match.Players[player].HasStart)
            {
                continue;
            }

            var start = match.Map.Starts[match.Players[player].Start];
            match.PlaceTerritory(player, start.X, start.Y, rules.Map.StartFootprint, rules.Map.StartTerritoryRadius);
        }

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
                    Size = rules.Map.DepositSize,
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

    /// <summary>
    /// Places a finished Generator of <see cref="GeneratorSize"/> tiles a side whose lowest tile
    /// is (x, y). Every tile must be one a unit can stand on, and no other work site may cover it.
    /// </summary>
    /// <remarks>
    /// Until structures block their tiles, units can still walk across it. Workers take their
    /// places beside it all the same.
    /// </remarks>
    public static EntityId PlaceGenerator(this Match match, int player, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(match);
        CheckPlayer(match, player);

        for (var tileY = y; tileY < y + GeneratorSize; tileY++)
        {
            for (var tileX = x; tileX < x + GeneratorSize; tileX++)
            {
                if (!match.Grid.IsPassable(tileX, tileY))
                {
                    throw new ArgumentException($"A Generator cannot stand on tile ({tileX}, {tileY}).", nameof(x));
                }

                for (var i = 0; i < match.WorkSites.Count; i++)
                {
                    if (match.WorkSites.ValueAt(i).Covers(tileX, tileY) && match.World.Entities.IsAlive(match.WorkSites.EntityAt(i)))
                    {
                        throw new ArgumentException($"Tile ({tileX}, {tileY}) already belongs to a work site.", nameof(x));
                    }
                }
            }
        }

        var entity = match.World.Entities.Create();
        match.WorkSites.Add(
            entity,
            new WorkSite { Kind = WorkSiteKind.Generator, Owner = player, X = x, Y = y, Size = GeneratorSize });
        return entity;
    }

    private static void CheckPlayer(Match match, int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, match.Players.Count);
    }
}
