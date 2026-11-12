using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Data;
using Sim.Tests.Map;
using Sim.Tests.Setup;

namespace Sim.Tests.Economy;

/// <summary>
/// Matches for the economy's scenario tests: a map made in code with deposits on it, players who
/// start with nothing, and the numbers of design 4.1 from the sample data.
/// </summary>
internal static class EconomyMatches
{
    /// <summary>A whole second of one worker's Materials at Extraction 1, in thousandths: 45 a minute.</summary>
    public const long MaterialsPayment = 750;

    /// <summary>A whole second of one worker's Energy at Extraction 1, in thousandths: 30 a minute.</summary>
    public const long EnergyPayment = 500;

    public const string GeneratorPath = "structures/generator.jsonc";

    /// <summary>A Generator as design 2.5 has it: 2 by 2 tiles, 500 hit points.</summary>
    public const string Generator = """
        {
          "id": "generator",
          "track": "extraction",
          "footprint": 2,
          "hp": 500,
          "cost": { "materials": 100, "energy": 0 },
          "build_seconds": 25,
          "sight": 6,
          "work_site": "energy"
        }
        """;

    private static readonly Lazy<GameData> Loaded = new(() => DataAssert.Valid(Files()));

    /// <summary>The sample data with a Generator in its roster and its file.</summary>
    public static GameData Data => Loaded.Value;

    public static InMemoryDataFiles Files() => SampleData
        .With(SampleData.RulesPath, "\"structures\": [\"capitol\", \"barracks\"]", "\"structures\": [\"capitol\", \"barracks\", \"generator\"]")
        .Set(GeneratorPath, Generator);

    /// <summary>The data of these matches with every track starting at this tier.</summary>
    public static GameData DataStartingAtTier(int tier) =>
        DataAssert.Valid(Files().Edit(SampleData.RulesPath, "\"start_epoch\": 1", $"\"start_epoch\": {tier}"));

    /// <summary>
    /// Places a finished Generator whose lowest tile is (x, y). The match's data must have one,
    /// as <see cref="Data"/> has.
    /// </summary>
    public static EntityId PlaceGenerator(this Match match, int player, int x, int y) =>
        match.PlaceStructure(player, "generator", x, y);

    public static MapDef WithDeposits(this MapDef map, params (int X, int Y)[] deposits) =>
        map with { Deposits = [.. deposits.Select(deposit => new MapDeposit(DepositKind.Contested, deposit.X, deposit.Y))] };

    /// <summary>Open ground of 24 by 24 tiles with one deposit on the tiles (10, 10) to (11, 11).</summary>
    public static MapDef OneDeposit => TestMaps.Open(24, 24).WithDeposits((10, 10));

    /// <summary>A match on <see cref="OneDeposit"/> in which player 0's territory holds the whole map.</summary>
    public static Match WithOneDeposit(out EntityId deposit, GameData? data = null)
    {
        var match = Create(OneDeposit, data);
        match.PlaceTerritory(0, 10, 10, 2, 40);
        deposit = match.SiteAt(10, 10);
        return match;
    }

    public static Match Create(MapDef map, GameData? data = null, int players = 2) =>
        MatchSetup.Create(data ?? Data, map, 1, [.. Enumerable.Repeat(PlayerSetup.WithoutStart, players)]);

    /// <summary>A worker of a player on the center of a tile.</summary>
    public static EntityId WorkerAt(this Match match, int player, int x, int y) => match.SpawnWorker(player, MapGrid.TileCenter(x, y));

    /// <summary>The work site whose lowest tile is (x, y).</summary>
    public static EntityId SiteAt(this Match match, int x, int y)
    {
        for (var i = 0; i < match.WorkSites.Count; i++)
        {
            var site = match.WorkSites.ValueAt(i);
            if (site.X == x && site.Y == y)
            {
                return match.WorkSites.EntityAt(i);
            }
        }

        throw new InvalidOperationException($"No work site at ({x}, {y}).");
    }

    /// <summary>Queues a gather order for the next tick to run.</summary>
    public static void OrderGather(this Match match, int player, EntityId site, params EntityId[] workers) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, Gather(player, site, workers));

    public static Command Gather(int player, EntityId site, params EntityId[] workers) =>
        new(CommandType.Gather, player, workers, targetEntity: site);

    public static Worker WorkerOf(this Match match, EntityId worker) => match.Workers.Get(worker);

    public static WorkerState StateOf(this Match match, EntityId worker) => match.Workers.Get(worker).State;

    /// <summary>The tile a worker holds or waits on.</summary>
    public static (int X, int Y) TileOf(this Match match, EntityId worker)
    {
        var tile = match.Workers.Get(worker).Tile;
        return (tile % match.Grid.Width, tile / match.Grid.Width);
    }

    /// <summary>The tile a unit stands on.</summary>
    public static (int X, int Y) StandsOn(this Match match, EntityId unit)
    {
        var position = match.Bodies.Get(unit).Position;
        return (position.X.FloorToInt(), position.Y.FloorToInt());
    }

    /// <summary>
    /// Runs ticks until every listed worker is at work and returns the number of the tick in which
    /// the last of them arrived.
    /// </summary>
    public static int RunUntilWorking(this Match match, params EntityId[] workers)
    {
        for (var ticks = 0; ticks < 2000; ticks++)
        {
            match.Simulation.Tick();
            if (workers.All(worker => match.StateOf(worker) == WorkerState.Working))
            {
                return match.World.Tick - 1;
            }
        }

        throw new InvalidOperationException("The workers did not all get to work.");
    }

    /// <summary>Runs ticks until the tick with this number has been run.</summary>
    public static void RunThrough(this Match match, int tick) => match.Simulation.Run(tick + 1 - match.World.Tick);
}
