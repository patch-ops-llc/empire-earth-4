using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Production;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Data;
using Sim.Tests.Economy;
using Sim.Tests.Tech;

namespace Sim.Tests.Production;

/// <summary>
/// Matches for the scenario tests of production: the design's own numbers
/// (<see cref="DesignData"/>) with a Barracks, a Works and a Generator as design 2.5 has them, on
/// maps made in code, with players who start with nothing.
/// </summary>
/// <remarks>
/// Every player has every track at tier 1 until a test says otherwise, so the Barracks trains the
/// Peltast (Line infantry) and the Hoplite (Guard), and the Works the Hippeus and the Ballista.
/// </remarks>
internal static class ProductionMatches
{
    public const int AWorker = TrainOrder.Worker;

    /// <summary>The worker of design 4.1: 50 Materials, 1 supply, 12 seconds.</summary>
    public const int WorkerTicks = 240;

    private const string Barracks = """
        {
          "id": "barracks",
          "track": "manufacturing",
          "footprint": 3,
          "hp": 1000,
          "cost": { "materials": 150, "energy": 0 },
          "build_seconds": 35,
          "sight": 6,
          "trains": ["line_infantry", "guard"]
        }
        """;

    private const string Works = """
        {
          "id": "works",
          "track": "mobility",
          "footprint": 3,
          "hp": 1000,
          "cost": { "materials": 200, "energy": 50 },
          "build_seconds": 45,
          "sight": 6,
          "trains": ["cavalry_armor", "artillery"]
        }
        """;

    private static readonly Lazy<GameData> Loaded = new(() => DataAssert.Valid(DesignData.Files()
        .Set("structures/barracks.jsonc", Barracks)
        .Set("structures/works.jsonc", Works)
        .Set(EconomyMatches.GeneratorPath, EconomyMatches.Generator)));

    public static GameData Data => Loaded.Value;

    public static Match Create(MapDef map, int players = 2) =>
        MatchSetup.Create(Data, map, 1, [.. Enumerable.Repeat(PlayerSetup.WithoutStart, players)]);

    public static int Chassis(string id) => Data.Chassis.Single(chassis => chassis.Id == id).Index;

    /// <summary>Gives a player more than any test spends.</summary>
    public static void MakeRich(this Match match, int player) => match.Stockpiles.Add(player, 100_000, 100_000);

    /// <summary>Whole Materials and Energy in a player's stockpile.</summary>
    public static (long Materials, long Energy) Stock(this Match match, int player) =>
        (match.Stockpiles.Materials(player) / 1000, match.Stockpiles.Energy(player) / 1000);

    public static Command TrainCommand(int player, EntityId structure, int unit) =>
        new(CommandType.Train, player, [structure], contentId: unit);

    public static Command CancelCommand(int player, EntityId structure, int position) =>
        new(CommandType.CancelTrain, player, [structure], contentId: position);

    public static Command RallyCommand(int player, FixedVector2 point, params EntityId[] structures) =>
        new(CommandType.SetRallyPoint, player, structures, point.X, point.Y);

    /// <summary>Queues an order to train for the next tick to run.</summary>
    public static void OrderTrain(this Match match, int player, EntityId structure, int unit) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, TrainCommand(player, structure, unit));

    /// <summary>Queues an order to cancel for the next tick to run.</summary>
    public static void OrderCancel(this Match match, int player, EntityId structure, int position) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, CancelCommand(player, structure, position));

    /// <summary>Queues an order that sets rally points for the next tick to run.</summary>
    public static void OrderRally(this Match match, int player, FixedVector2 point, params EntityId[] structures) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, RallyCommand(player, point, structures));

    /// <summary>The orders in a structure's queue, the first at position 0.</summary>
    public static TrainOrder[] OrdersAt(this Match match, EntityId structure)
    {
        var producer = match.Production.Producers.Get(structure);
        return [.. Enumerable.Range(0, producer.Count).Select(position => producer[position])];
    }

    public static Producer ProducerOf(this Match match, EntityId structure) => match.Production.Producers.Get(structure);

    /// <summary>What production reported in the tick that ran last, in its order.</summary>
    public static TrainEvent[] TrainEvents(this Match match)
    {
        var events = new List<TrainEvent>();
        foreach (var simEvent in match.Simulation.Events)
        {
            if (TrainEvent.TryRead(simEvent, out var train))
            {
                events.Add(train);
            }
        }

        return [.. events];
    }

    /// <summary>The units that appeared in the tick that ran last, in the order they appeared.</summary>
    public static EntityId[] Appeared(this Match match) =>
        [.. match.TrainEvents().Where(train => train.Kind == SimEventKind.UnitTrained).Select(train => train.Trained)];

    /// <summary>
    /// Runs ticks until a unit appears and returns how many ran. The first tick it runs is the
    /// one that carries out the commands queued just before.
    /// </summary>
    public static int RunUntilAppeared(this Match match, int limit = 2000)
    {
        for (var ticks = 1; ticks <= limit; ticks++)
        {
            match.Simulation.Tick();
            if (match.Appeared().Length > 0)
            {
                return ticks;
            }
        }

        throw new InvalidOperationException($"Nothing appeared in {limit} ticks.");
    }

    /// <summary>The tile a unit or a point is on.</summary>
    public static (int X, int Y) Tile(FixedVector2 point) => (point.X.FloorToInt(), point.Y.FloorToInt());

    public static (int X, int Y) TileOn(this Match match, EntityId unit) => Tile(match.Bodies.Get(unit).Position);

    /// <summary>Brings a structure's hit points to zero, so that the deaths of the next tick take it.</summary>
    public static void Destroy(this Match match, EntityId structure) => match.Health.Get(structure).HitPoints = 0;

    /// <summary>Places units of a chassis on the tiles of the map's last columns, away from what the tests build.</summary>
    public static void SpawnMany(this Match match, int player, int chassis, int count)
    {
        for (var unit = 0; unit < count; unit++)
        {
            match.SpawnChassis(player, chassis, MapGrid.TileCenter(match.Grid.Width - 1 - (unit / match.Grid.Height), unit % match.Grid.Height));
        }
    }

    public static EntityId Place(this Match match, int player, string structure, int x, int y) =>
        match.PlaceStructure(player, structure, x, y);
}
