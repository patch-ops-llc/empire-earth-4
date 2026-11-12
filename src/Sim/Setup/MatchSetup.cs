using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Research;

namespace Sim.Setup;

/// <summary>
/// Builds a match. This is the one place that registers every store, sets every command handler and
/// adds every system, so that single-player, network play, replays and tests all run the same game.
/// </summary>
public static class MatchSetup
{
    /// <summary>The most players a match can have.</summary>
    public const int MaxPlayers = 8;

    /// <summary>
    /// Builds the world and the tick loop for a match, ready for its first tick. The same arguments
    /// give the same match on every machine.
    /// </summary>
    /// <param name="data">The loaded game data.</param>
    /// <param name="map">The map. It need not be one of the data's maps: tests and the battle harness pass one made in code.</param>
    /// <param name="seed">The seed of the world's random generator.</param>
    /// <param name="players">The players, in the order that numbers them from 0.</param>
    public static Match Create(GameData data, MapDef map, ulong seed, IReadOnlyList<PlayerSetup> players)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(players);
        CheckPlayers(map, players);

        var world = new World(seed);
        var grid = new MapGrid(map, map.Deposits.Count == 0 ? 0 : data.Rules.Map.DepositSize);

        // Stores, in alphabetical order of their names. The world sorts them by name, so the order
        // here changes nothing. A name is never changed once it has merged: it places the store in
        // the state hash.
        var bodies = world.AddStore(new ComponentStore<Body>(Body.StoreName));
        var combatants = world.AddStore(new ComponentStore<Combatant>(Combatant.StoreName));
        var health = world.AddStore(new ComponentStore<Health>(Health.StoreName));
        var movers = world.AddStore(new ComponentStore<Mover>(Mover.StoreName));
        var researchState = world.AddStore(new ResearchState(players.Count, data.Tracks.Count, data.TierCount, data.Rules.Match.StartEpoch));
        var shots = world.AddStore(new ComponentStore<Shot>(Shot.StoreName));
        var stockpiles = world.AddStore(new Stockpiles(players.Count));
        var territories = world.AddStore(new ComponentStore<Territory>(Territory.StoreName));
        var workers = world.AddStore(new ComponentStore<Worker>(Worker.StoreName));
        var workSites = world.AddStore(new ComponentStore<WorkSite>(WorkSite.StoreName));

        // Worked out from the state and not state itself. Match.RefreshDerivedState renews it.
        var research = new MatchResearch(data, world, researchState, stockpiles, data.Rules.Match.EpochCap);
        var routes = new RouteCache(grid);
        var spatialIndex = new SpatialIndex(grid, world.Entities, bodies);

        var supply = new SupplyAccount(data, research, world.Entities, bodies, workers);

        // The combat numbers are worked out from the data and from each player's tech as research has it.
        var combat = new CombatRules(data, players.Count, research.Tech);
        var targeting = new Targeting(grid, bodies, combatants, health, spatialIndex, combat);
        var approach = new Approach(grid);
        var fighters = new Fighters(data, combat, combatants, bodies, movers, spatialIndex);

        var simulation = new Simulation(world);

        // Command handlers, in alphabetical order of their types. The order here changes nothing.
        // The four orders of a unit share one handler, which walks a move through the move order's
        // own. A worker walks to its work by the move order, and an order that makes it walk
        // elsewhere takes it off its work: a move, and an attack-move, which is a move to a worker.
        var move = new MoveCommandHandler(grid, bodies, movers, players.Count);
        var work = new WorkAssignments(data.Resources, grid, bodies, workers, workSites, move);
        var orders = new OrderCommandHandler(move, grid, bodies, movers, combatants, targeting, approach, players.Count);
        var walkOrders = new LeaveWorkHandler(orders, bodies, work);
        simulation.SetCommandHandler(CommandType.Attack, orders);
        simulation.SetCommandHandler(CommandType.AttackMove, walkOrders);
        simulation.SetCommandHandler(CommandType.CancelResearch, new CancelResearchHandler(research));
        simulation.SetCommandHandler(CommandType.Gather, new GatherCommandHandler(bodies, workSites, work, players.Count));
        simulation.SetCommandHandler(CommandType.Move, walkOrders);
        simulation.SetCommandHandler(CommandType.StartResearch, new StartResearchHandler(research));
        simulation.SetCommandHandler(CommandType.Stop, orders);

        // Systems. Their order is a rule of the game: a pull request that adds one says where it
        // goes and why.
        //
        // Routes are found before anyone moves, so that a unit ordered in this tick can take its
        // first step in this tick. Units step apart right after they have walked, from where the
        // walk left them, except a worker that holds a place and a unit that is fighting, which
        // stay where they are. The spatial index is rebuilt after both, so that every system after it
        // sees where things stand now. A system that moves bodies goes before it.
        // Combat comes after movement, so that it sees where everything stands in this tick and
        // which units have moved in it: a unit attacks only in a tick in which it does not move.
        // Shots land before the attacks, so a shot fired in one tick flies from the next. Deaths
        // come after both: every attack due in a tick is made before any death of that tick takes
        // effect.
        // Income comes after movement, so that a worker that reaches its place in this tick has
        // arrived in this tick; after the deaths, so that a worker killed in this tick earns
        // nothing in it and its place is given out in it; and before research, so that a tier
        // raised in this tick pays from the next. It moves nobody.
        // Research goes last: after everything that can destroy the holder of a research slot, so
        // that a research cannot finish in the tick in which its building falls.
        simulation.AddSystem(new RoutePlanningSystem(movers, routes));
        simulation.AddSystem(new MovementSystem(grid, bodies, movers, routes));
        simulation.AddSystem(new SeparationSystem(grid, bodies, movers, routes, new PlaceHolders(workers), fighters));
        simulation.AddSystem(new SpatialIndexSystem(spatialIndex));
        simulation.AddSystem(new ShotSystem(bodies, shots, targeting));
        simulation.AddSystem(new AttackSystem(bodies, movers, combatants, shots, targeting, approach, fighters));
        simulation.AddSystem(new DeathSystem(health));
        simulation.AddSystem(new IncomeSystem(data, grid, bodies, movers, workers, workSites, territories, stockpiles, work, research));
        simulation.AddSystem(new ResearchSystem(research));

        var match = new Match
        {
            Data = data,
            Map = map,
            Players = [.. players],
            World = world,
            Simulation = simulation,
            Grid = grid,
            Bodies = bodies,
            Combat = combat,
            Combatants = combatants,
            Health = health,
            Movers = movers,
            Research = research,
            Routes = routes,
            Shots = shots,
            SpatialIndex = spatialIndex,
            Stockpiles = stockpiles,
            Supply = supply,
            Territories = territories,
            Work = work,
            Workers = workers,
            WorkSites = workSites,
        };

        for (var player = 0; player < players.Count; player++)
        {
            if (players[player].HasStart)
            {
                // Design 5.2: what every player starts with. The research slot stands in for the
                // starting Capitol's until the setup places one.
                stockpiles.Add(player, data.Rules.Start.Materials, data.Rules.Start.Energy);
                research.OpenPlayerSlot(player);
                PlaceStartingWorkers(match, player, map.Starts[players[player].Start]);
            }
        }

        EconomySetup.PlaceStart(match);

        match.RefreshDerivedState();
        return match;
    }

    private static void CheckPlayers(MapDef map, IReadOnlyList<PlayerSetup> players)
    {
        if (players.Count is < 1 or > MaxPlayers)
        {
            throw new ArgumentException($"A match has 1 to {MaxPlayers} players, not {players.Count}.", nameof(players));
        }

        for (var player = 0; player < players.Count; player++)
        {
            var setup = players[player] ?? throw new ArgumentException($"Player {player} is null.", nameof(players));
            if (!setup.HasStart)
            {
                continue;
            }

            if (setup.Start < 0 || setup.Start >= map.Starts.Count)
            {
                throw new ArgumentException(
                    $"Player {player} has start {setup.Start}, but map '{map.Id}' has {map.Starts.Count} starts.", nameof(players));
            }

            for (var other = 0; other < player; other++)
            {
                if (players[other].Start == setup.Start)
                {
                    throw new ArgumentException($"Players {other} and {player} share start {setup.Start}.", nameof(players));
                }
            }
        }
    }

    /// <summary>
    /// Places a player's starting workers (design 5.2) on the passable tiles nearest the footprint
    /// of the starting Capitol, one to a tile: the row below it first, then the row above, then the
    /// columns beside it, then the same one tile further out.
    /// </summary>
    /// <remarks>
    /// "Below" and "left" are turned around for a start in the mirrored half of the map, so that the
    /// two players of a mirrored map stand as mirror images of each other, also when terrain is in
    /// the way. The Capitol itself arrives with a later milestone; its tiles are left free here.
    /// </remarks>
    private static void PlaceStartingWorkers(Match match, int player, MapStart start)
    {
        var map = match.Map;
        var footprint = match.Data.Rules.Map.StartFootprint;
        var flipX = map.Symmetry == MapSymmetry.MirrorX && (2 * start.X) + footprint > map.Width;
        var flipY = map.Symmetry == MapSymmetry.MirrorY && (2 * start.Y) + footprint > map.Height;

        var remaining = match.Data.Rules.Start.Workers;
        for (var ring = 1; remaining > 0; ring++)
        {
            if (ring > map.Width + map.Height)
            {
                throw new InvalidOperationException(
                    $"Map '{map.Id}' has too little open ground for the starting workers of player {player}.");
            }

            // Offsets from the footprint's lowest tile, before any turning around.
            var low = -ring;
            var high = footprint - 1 + ring;
            for (var x = low; x <= high && remaining > 0; x++)
            {
                remaining -= Place(x, high);
            }

            for (var x = low; x <= high && remaining > 0; x++)
            {
                remaining -= Place(x, low);
            }

            for (var y = low + 1; y < high && remaining > 0; y++)
            {
                remaining -= Place(low, y);
            }

            for (var y = low + 1; y < high && remaining > 0; y++)
            {
                remaining -= Place(high, y);
            }
        }

        int Place(int x, int y)
        {
            var tileX = start.X + (flipX ? footprint - 1 - x : x);
            var tileY = start.Y + (flipY ? footprint - 1 - y : y);
            if (!match.Grid.IsPassable(tileX, tileY))
            {
                return 0;
            }

            match.SpawnWorker(player, MapGrid.TileCenter(tileX, tileY));
            return 1;
        }
    }
}
