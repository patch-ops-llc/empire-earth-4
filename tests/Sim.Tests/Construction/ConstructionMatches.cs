using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Data;
using Sim.Tests.Economy;
using Sim.Tests.Map;

namespace Sim.Tests.Construction;

/// <summary>
/// Matches for the scenario tests of construction: the sample data with the structures of design
/// 2.5 that the rules of construction tell apart, at the design's prices and build times, on
/// maps made in code. Players start with nothing; a test gives them what its scenario needs.
/// </summary>
internal static class ConstructionMatches
{
    /// <summary>3 by 3 tiles, 250 Materials, 45 seconds. Its territory has a radius of 5 tiles here, so that maps can be small.</summary>
    public const string Outpost = """
        {
          "id": "outpost",
          "footprint": 3,
          "hp": 1200,
          "cost": { "materials": 250, "energy": 0 },
          "build_seconds": 45,
          "sight": 8,
          "territory_radius": 5
        }
        """;

    /// <summary>2 by 2 tiles, 200 Materials and 100 Energy, 45 seconds, at most two.</summary>
    public const string Academy = """
        {
          "id": "academy",
          "footprint": 2,
          "hp": 800,
          "cost": { "materials": 200, "energy": 100 },
          "build_seconds": 45,
          "sight": 6,
          "research_slots": 1,
          "max_per_player": 2
        }
        """;

    /// <summary>A structure that needs a tier no player starts with, and whose price has an odd number in each resource.</summary>
    public const string Works = """
        {
          "id": "works",
          "track": "mobility",
          "unlock_tier": 2,
          "footprint": 3,
          "hp": 1000,
          "cost": { "materials": 205, "energy": 51 },
          "build_seconds": 45,
          "sight": 6
        }
        """;

    public const string Wall = """
        {
          "id": "wall",
          "track": "protection",
          "footprint": 1,
          "hp": 400,
          "cost": { "materials": 20, "energy": 0 },
          "build_seconds": 5,
          "sight": 2,
          "wall": {
            "gate": { "cost": { "materials": 40, "energy": 0 }, "build_seconds": 10 }
          }
        }
        """;

    private static readonly Lazy<GameData> Loaded = new(() => DataAssert.Valid(Files()));

    /// <summary>The sample data with a Generator, an Outpost, an Academy, a structure that is locked at the start, and the wall.</summary>
    public static GameData Data => Loaded.Value;

    public static int Generator => Data.StructureOf("generator").Index;

    public static InMemoryDataFiles Files() => EconomyMatches.Files()
        .Edit(
            SampleData.RulesPath,
            "\"structures\": [\"capitol\", \"barracks\", \"generator\"]",
            "\"structures\": [\"capitol\", \"barracks\", \"generator\", \"outpost\", \"academy\", \"works\", \"wall\"]")
        .Set("structures/outpost.jsonc", Outpost)
        .Set("structures/academy.jsonc", Academy)
        .Set("structures/works.jsonc", Works)
        .Set("structures/wall.jsonc", Wall);

    /// <summary>The data of these matches with every track starting at this tier.</summary>
    public static GameData DataStartingAtTier(int tier) =>
        DataAssert.Valid(Files().Edit(SampleData.RulesPath, "\"start_epoch\": 1", $"\"start_epoch\": {tier}"));

    /// <summary>A match of two players without starts and with nothing in their stockpiles.</summary>
    public static Match Create(MapDef map, GameData? data = null) => EconomyMatches.Create(map, data ?? Data);

    /// <summary>
    /// A match on open ground of 40 by 24 tiles. Player 0 has a finished Capitol on the tiles
    /// (6, 10) to (7, 11), so its territory is the tiles within 6 of the point (7, 11), and
    /// 1,000 of each resource.
    /// </summary>
    public static Match WithACapitol(GameData? data = null)
    {
        var match = Create(TestMaps.Open(40, 24), data);
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 1000, 1000);
        return match;
    }

    public static StructureDef Def(this Match match, string id) => match.Data.StructureOf(id);

    /// <summary>A build order for a structure with its lowest tile at (x, y). The point is the middle of that tile.</summary>
    public static Command Build(this Match match, int player, string structure, int x, int y, params EntityId[] workers)
    {
        var point = MapGrid.TileCenter(x, y);
        return new Command(CommandType.Build, player, workers, point.X, point.Y, contentId: match.Def(structure).Index);
    }

    /// <summary>Queues a build order for the next tick to run.</summary>
    public static void OrderBuild(this Match match, int player, string structure, int x, int y, params EntityId[] workers) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, match.Build(player, structure, x, y, workers));

    /// <summary>Queues an order to cancel builds for the next tick to run.</summary>
    public static void OrderCancelBuild(this Match match, int player, params EntityId[] entities) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.CancelBuild, player, entities));

    /// <summary>The Materials and Energy of a player, in whole resources. Fails when either has a fraction.</summary>
    public static (long Materials, long Energy) StockOf(this Match match, int player)
    {
        var materials = match.Stockpiles.Materials(player);
        var energy = match.Stockpiles.Energy(player);
        Assert.Equal((0, 0), (materials % 1000, energy % 1000));
        return (materials / 1000, energy / 1000);
    }

    /// <summary>The living structure whose lowest tile is (x, y), or none.</summary>
    public static EntityId StructureAt(this Match match, int x, int y)
    {
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var structure = match.Structures.ValueAt(i);
            if (structure.X == x && structure.Y == y && match.World.Entities.IsAlive(match.Structures.EntityAt(i)))
            {
                return match.Structures.EntityAt(i);
            }
        }

        return EntityId.None;
    }

    /// <summary>The events of the last tick of one kind, each as its entity and the reason in its amount.</summary>
    public static (EntityId Entity, BuildProblem Why)[] Problems(this Match match, SimEventKind kind) =>
        [.. match.Simulation.Events.Where(simEvent => simEvent.Kind == kind).Select(simEvent => (simEvent.Entity, (BuildProblem)simEvent.Amount))];

    /// <summary>Runs ticks until the worker has no build order, and returns the number of the last tick run.</summary>
    public static int RunUntilOrderEnds(this Match match, EntityId worker, int limit = 2000)
    {
        for (var ticks = 0; ticks < limit; ticks++)
        {
            match.Simulation.Tick();
            if (!match.BuildOrders.Contains(worker))
            {
                return match.World.Tick - 1;
            }
        }

        throw new InvalidOperationException($"The worker still had its build order after {limit} ticks.");
    }
}
