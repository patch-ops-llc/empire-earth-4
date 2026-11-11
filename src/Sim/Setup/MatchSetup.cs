using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
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
    /// The speed of a starting worker, as the distance per tick. The design gives workers no speed
    /// yet, so this stands in until it does and the data carries it: 3 tiles per second, the speed
    /// of Line infantry.
    /// </summary>
    public static Fixed StartingWorkerSpeed { get; } = Mover.SpeedPerTick(3);

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
        var grid = new MapGrid(map);

        // Stores, in alphabetical order of their names. The world sorts them by name, so the order
        // here changes nothing. A name is never changed once it has merged: it places the store in
        // the state hash.
        var bodies = world.AddStore(new ComponentStore<Body>(Body.StoreName));
        var movers = world.AddStore(new ComponentStore<Mover>(Mover.StoreName));
        var researchState = world.AddStore(new ResearchState(players.Count, data.Tracks.Count, data.TierCount, data.Rules.Match.StartEpoch));
        var stockpiles = world.AddStore(new Stockpiles(players.Count));

        // Worked out from the state and not state itself. Match.RefreshDerivedState renews it.
        var research = new MatchResearch(data, world, researchState, stockpiles, data.Rules.Match.EpochCap);
        var routes = new RouteCache(grid);
        var spatialIndex = new SpatialIndex(grid, world.Entities, bodies);

        var simulation = new Simulation(world);

        // Command handlers, in alphabetical order of their types. The order here changes nothing.
        simulation.SetCommandHandler(CommandType.CancelResearch, new CancelResearchHandler(research));
        simulation.SetCommandHandler(CommandType.Move, new MoveCommandHandler(grid, bodies, movers, players.Count));
        simulation.SetCommandHandler(CommandType.StartResearch, new StartResearchHandler(research));

        // Systems. Their order is a rule of the game: a pull request that adds one says where it
        // goes and why.
        //
        // Routes are found before anyone moves, so that a unit ordered in this tick can take its
        // first step in this tick. The spatial index is rebuilt right after movement, so that every
        // system after it sees where things stand now. A system that moves bodies goes before it.
        // Research goes last: after everything that can destroy the holder of a research slot, so
        // that a research cannot finish in the tick in which its building falls.
        simulation.AddSystem(new RoutePlanningSystem(movers, routes));
        simulation.AddSystem(new MovementSystem(grid, bodies, movers, routes));
        simulation.AddSystem(new SpatialIndexSystem(spatialIndex));
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
            Movers = movers,
            Research = research,
            Routes = routes,
            SpatialIndex = spatialIndex,
            Stockpiles = stockpiles,
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

            match.SpawnUnit(player, MapGrid.TileCenter(tileX, tileY), StartingWorkerSpeed);
            return 1;
        }
    }
}
