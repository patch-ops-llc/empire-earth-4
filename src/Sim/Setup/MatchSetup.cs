using Sim.Combat;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Production;
using Sim.Research;
using Sim.Structures;
using Sim.Vision;

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
        var buildOrders = world.AddStore(new ComponentStore<BuildOrder>(BuildOrder.StoreName));
        var combatants = world.AddStore(new ComponentStore<Combatant>(Combatant.StoreName));
        var foundations = world.AddStore(new ComponentStore<Foundation>(Foundation.StoreName));
        var health = world.AddStore(new ComponentStore<Health>(Health.StoreName));
        var movers = world.AddStore(new ComponentStore<Mover>(Mover.StoreName));
        var producers = world.AddStore(new ComponentStore<Producer>(Producer.StoreName));
        var researchState = world.AddStore(new ResearchState(players.Count, data.Tracks.Count, data.TierCount, data.Rules.Match.StartEpoch));
        var structureMemory = world.AddStore(new StructureMemory(players.Count, map.Width, map.Height));
        var shots = world.AddStore(new ComponentStore<Shot>(Shot.StoreName));
        var stockpiles = world.AddStore(new Stockpiles(players.Count));
        var structures = world.AddStore(new ComponentStore<PlacedStructure>(PlacedStructure.StoreName));
        var territories = world.AddStore(new ComponentStore<Territory>(Territory.StoreName));
        var turrets = world.AddStore(new ComponentStore<Turret>(Turret.StoreName));
        var visionGrid = world.AddStore(new VisionGrid(players.Count, map.Width, map.Height));
        var workers = world.AddStore(new ComponentStore<Worker>(Worker.StoreName));
        var workSites = world.AddStore(new ComponentStore<WorkSite>(WorkSite.StoreName));

        // Worked out from the state and not state itself. Match.RefreshDerivedState renews it.
        var research = new MatchResearch(data, world, researchState, stockpiles, data.Rules.Match.EpochCap);
        var routes = new RouteCache(grid);
        var spatialIndex = new SpatialIndex(grid, world.Entities, bodies);

        var supply = new SupplyAccount(data, research, world.Entities, bodies, workers);
        supply.AddUser(new CombatSupply(research, world.Entities, bodies, combatants));

        // What each player sees and remembers. The grid and the memories are state; this works
        // them out and answers who sees whom.
        var vision = new MatchVision(
            data, world, grid, visionGrid, structureMemory, bodies, combatants, foundations, health, structures, workers,
            researchState, research.Tech);

        // The combat numbers are worked out from the data and from each player's tech as research has it.
        var combat = new CombatRules(data, players.Count, research.Tech);
        var targeting = new Targeting(grid, bodies, combatants, health, spatialIndex, combat, vision) { Structures = structures };
        var approach = new Approach(grid);
        var fighters = new Fighters(data, combat, combatants, bodies, movers, spatialIndex);

        var simulation = new Simulation(world);

        // The orders in the queues hold supply from the moment they are placed.
        var production = new MatchProduction(data, world.Entities, bodies, producers, structures, stockpiles, supply, research, simulation.Events, players.Count);
        supply.AddUser(production);

        // Command handlers, in alphabetical order of their types. The order here changes nothing.
        // The four orders of a unit share one handler, which walks a move through the move order's
        // own. A worker walks to its work by the move order, and an order that makes it walk
        // elsewhere takes it off its work: a move, and an attack-move, which is a move to a worker.
        var move = new MoveCommandHandler(grid, bodies, movers, players.Count);
        var structureTiles = new StructureTiles(grid, world.Entities, structures, routes, new Rerouting(grid, bodies, movers, move));
        research.AddTechChangeHandler(new StructureHealth(bodies, health, structures));
        var work = new WorkAssignments(data.Resources, grid, bodies, workers, workSites, move);
        var orders = new OrderCommandHandler(move, grid, bodies, movers, combatants, targeting, approach, simulation.Events, players.Count);
        var walkOrders = new LeaveWorkHandler(orders, bodies, work);

        // A worker walks to the site of its build order by the move order too, and any other
        // order it is given ends the build order: the handlers of those orders are wrapped once
        // more. An attack order is not, because a worker never takes one.
        var structureFactory = new StructureFactory(
            data, world, bodies, health, structures, territories, turrets, workSites, structureTiles, research, production,
            simulation.Events, players.Count);
        var construction = new ConstructionRules(
            data, world, grid, bodies, buildOrders, combatants, foundations, movers, structures, territories, workers,
            stockpiles, research, work, structureFactory, spatialIndex, move, simulation.Events, players.Count)
        {
            Vision = vision,
        };
        simulation.SetCommandHandler(CommandType.Attack, orders);
        simulation.SetCommandHandler(CommandType.AttackMove, new OtherOrderHandler(walkOrders, construction));
        simulation.SetCommandHandler(CommandType.Build, new BuildCommandHandler(construction));
        simulation.SetCommandHandler(CommandType.CancelBuild, new CancelBuildCommandHandler(construction));
        simulation.SetCommandHandler(CommandType.CancelResearch, new CancelResearchHandler(research));
        simulation.SetCommandHandler(CommandType.CancelTrain, new CancelTrainHandler(production, simulation.Events));
        simulation.SetCommandHandler(
            CommandType.Gather,
            new OtherOrderHandler(new GatherCommandHandler(bodies, workSites, work, players.Count), construction));
        simulation.SetCommandHandler(CommandType.Move, new OtherOrderHandler(walkOrders, construction));
        simulation.SetCommandHandler(CommandType.SetRallyPoint, new SetRallyPointHandler(production, grid, simulation.Events));
        simulation.SetCommandHandler(CommandType.StartResearch, new StartResearchHandler(research));
        simulation.SetCommandHandler(CommandType.Stop, new OtherOrderHandler(orders, construction));
        simulation.SetCommandHandler(CommandType.Train, new TrainHandler(production));

        // Systems. Their order is a rule of the game: a pull request that adds one says where it
        // goes and why.
        //
        // Production comes first, before routes are found, so that a unit that appears in this
        // tick takes its rally order and its first step in this tick.
        // Routes are found before anyone moves, so that a unit ordered in this tick can take its
        // first step in this tick. Units step apart right after they have walked, from where the
        // walk left them, except a worker that holds a place and a unit that is fighting, which
        // stay where they are. The spatial index is rebuilt after both, so that every system after it
        // sees where things stand now. A system that moves bodies goes before it.
        // Vision comes after movement and the spatial index and before combat: what a player sees
        // is worked out from where things stand in this tick, before any unit picks a target.
        // Combat comes after movement, so that it sees where everything stands in this tick and
        // which units have moved in it: a unit attacks only in a tick in which it does not move.
        // Shots land before the attacks, so a shot fired in one tick flies from the next. Towers
        // attack right after the units: theirs are attacks of the tick like any other. Deaths
        // come after all three: every attack due in a tick is made before any death of that tick
        // takes effect.
        // Structures leave the map right after the deaths: a structure destroyed in this tick
        // frees its tiles in this tick, so income, research and the orders of the next tick see
        // the tiles, the territory and the research slots as they are without it.
        // Construction comes after movement, the deaths and the structures that leave: a worker
        // lays its foundation in the tick it arrives, on the tiles as they are in this tick, and
        // the order of a worker killed in this tick ends in it. It comes before income and
        // research, which see what a structure finished in this tick gives.
        // The orders of a production structure destroyed in this tick are given back after that,
        // before income and research, so the stockpile has them when the tick ends.
        // Income comes after movement, so that a worker that reaches its place in this tick has
        // arrived in this tick; after the deaths, so that a worker killed in this tick earns
        // nothing in it and its place is given out in it; and before research, so that a tier
        // raised in this tick pays from the next. It moves nobody.
        // Research goes last: after everything that can destroy the holder of a research slot, so
        // that a research cannot finish in the tick in which its building falls.
        var producing = new ProductionSystem(production, walkOrders, simulation.Events);
        simulation.AddSystem(producing);
        simulation.AddSystem(new RoutePlanningSystem(movers, routes));
        simulation.AddSystem(new MovementSystem(grid, bodies, movers, routes));
        simulation.AddSystem(new SeparationSystem(grid, bodies, movers, routes, new PlaceHolders(workers), fighters));
        simulation.AddSystem(new SpatialIndexSystem(spatialIndex));
        simulation.AddSystem(new VisionSystem(vision));
        simulation.AddSystem(new ShotSystem(bodies, shots, targeting, simulation.Events));
        simulation.AddSystem(new AttackSystem(bodies, movers, combatants, shots, targeting, approach, fighters, simulation.Events));
        simulation.AddSystem(new TurretSystem(bodies, structures, turrets, shots, targeting, simulation.Events));
        simulation.AddSystem(new DeathSystem(bodies, health, simulation.Events));
        simulation.AddSystem(new StructureSystem(data, bodies, structures, foundations, structureTiles, research, simulation.Events));
        simulation.AddSystem(new ConstructionSystem(construction, data));
        simulation.AddSystem(new ProductionRefundSystem(production, bodies));
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
            BuildOrders = buildOrders,
            Combat = combat,
            Combatants = combatants,
            Construction = construction,
            Foundations = foundations,
            Health = health,
            Movers = movers,
            Production = production,
            Research = research,
            Routes = routes,
            Shots = shots,
            SpatialIndex = spatialIndex,
            Stockpiles = stockpiles,
            StructureFactory = structureFactory,
            StructureTiles = structureTiles,
            Structures = structures,
            Supply = supply,
            Territories = territories,
            Turrets = turrets,
            Vision = vision,
            Work = work,
            Workers = workers,
            WorkSites = workSites,
        };

        // Production places its units through the match, which exists only now.
        producing.Attach(match);

        // The development spawn and placement put their units and structures on the map through
        // the match, so their handlers come after it. Only while the rules switch them on:
        // otherwise the commands have no handler and are dropped.
        if (data.Rules.Debug.Spawn)
        {
            simulation.SetCommandHandler(CommandType.DebugPlace, new DebugPlaceHandler(match, simulation.Events));
            simulation.SetCommandHandler(CommandType.DebugSpawn, new DebugSpawnHandler(match, simulation.Events));
        }

        for (var player = 0; player < players.Count; player++)
        {
            if (players[player].HasStart)
            {
                // Design 5.2: what every player starts with.
                stockpiles.Add(player, data.Rules.Start.Materials, data.Rules.Start.Energy);
                PlaceStartingWorkers(match, player, map.Starts[players[player].Start]);
            }
        }

        // The Capitols come after the workers of every player, so that the workers keep the
        // numbers they have always had, and before the deposits, which keep theirs too: a Capitol
        // takes the number of the territory that stood alone here before structures existed.
        for (var player = 0; player < players.Count; player++)
        {
            if (players[player].HasStart)
            {
                PlaceStartingCapitol(match, player, map.Starts[players[player].Start]);
            }
        }

        EconomySetup.PlaceDeposits(match);

        // Design 5.3: at the start everything is unexplored except what the players' own see.
        vision.Refresh();

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
    /// Places a player's finished Capitol on its start (design 5.2). It blocks its tiles, holds
    /// its territory and gives the player their research slot.
    /// </summary>
    private static void PlaceStartingCapitol(Match match, int player, MapStart start)
    {
        var capitol = match.Data.StartStructure;
        if (!match.HasRoomFor(capitol, start.X, start.Y))
        {
            throw new ArgumentException(
                $"Map '{match.Map.Id}' has no room for the {capitol.Footprint} by {capitol.Footprint} tiles of the Capitol of player {player} at its start ({start.X}, {start.Y}).",
                nameof(match));
        }

        match.PlaceStructure(player, capitol, start.X, start.Y);
    }

    /// <summary>
    /// Places a player's starting workers (design 5.2) on the passable tiles nearest the footprint
    /// of the starting Capitol, one to a tile: the row below it first, then the row above, then the
    /// columns beside it, then the same one tile further out.
    /// </summary>
    /// <remarks>
    /// "Below" and "left" are turned around for a start in the mirrored half of the map, so that the
    /// two players of a mirrored map stand as mirror images of each other, also when terrain is in
    /// the way. The tiles of the footprint are left free for the Capitol, which is placed after the
    /// workers of every player.
    /// </remarks>
    private static void PlaceStartingWorkers(Match match, int player, MapStart start)
    {
        var map = match.Map;
        var footprint = match.Data.StartStructure.Footprint;
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
