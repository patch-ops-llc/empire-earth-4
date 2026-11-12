using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Production;
using Sim.Setup;
using Sim.Tests.Data;
using Sim.Tests.Map;
using Sim.Tests.Tech;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Production;

/// <summary>
/// The Light footprint perk in a real queue (design 2.3, Supplied II): "costs 1 less supply,
/// minimum 1", and it "applies to existing units at once and frees their supply".
/// </summary>
public class LightFootprintTests
{
    private static readonly int Auxiliary = Chassis("auxiliary");
    private static readonly int Peltast = Chassis("peltast");

    private static Match With(string structure, int[] tiers, out EntityId producer, GameData? data = null)
    {
        var match = Create(TestMaps.Open(24, 24), data: data);
        match.Research.SetTiers(0, tiers);
        producer = match.Place(0, structure, 10, 10);
        match.MakeRich(0);
        return match;
    }

    /// <summary>An order holds what its units will use: one less for each unit, so two less for a pair.</summary>
    [Theory]
    [InlineData("barracks", "peltast", 2, 1, 2)] // Logistics 2 gives Supplied I, which has no Light footprint.
    [InlineData("barracks", "peltast", 3, 1, 1)]
    [InlineData("works", "hippeus", 2, 1, 3)]
    [InlineData("works", "hippeus", 3, 1, 2)]
    [InlineData("barracks", "auxiliary", 4, 4, 2)] // The pair of example B: 1 supply each.
    [InlineData("barracks", "auxiliary", 1, 4, 4)] // The same pair without Light footprint: 2 each.
    public void AnOrderHoldsOneLessSupplyForEachOfItsUnits(string structure, string chassis, int logistics, int manufacturing, int held)
    {
        var unit = Chassis(chassis);
        var match = With(structure, [1, 1, 1, manufacturing, logistics, 1], out var producer);

        match.OrderTrain(0, producer, unit);
        match.Simulation.Tick();

        var order = Assert.Single(match.OrdersAt(producer));
        Assert.Equal(held, order.Supply);
        Assert.Equal(order.Count * match.BlueprintOf(0, unit).Supply, order.Supply);
        Assert.Equal(held, match.Supply.Used(0));

        // What the order held is what its units use.
        match.RunUntilAppeared();
        Assert.Empty(match.OrdersAt(producer));
        Assert.Equal(order.Count, match.Combatants.Count);
        Assert.Equal(held, match.Supply.Used(0));
    }

    /// <summary>
    /// "Minimum 1": a unit of 1 supply keeps it, and both numbers of the perk are data.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 1, 1)] // Nothing to take off.
    [InlineData(2, 5, 1, 1)] // More off than there is.
    [InlineData(4, 2, 1, 2)]
    [InlineData(4, 1, 3, 3)]
    [InlineData(4, 2, 3, 3)] // The minimum is data too.
    [InlineData(3, 2, 4, 3)] // A unit below the minimum is not raised to it.
    public void TheReductionNeverGoesBelowTheMinimum(int supply, int amount, int minimum, int held)
    {
        var files = Files()
            .Edit(DesignData.LineInfantryPath, "\"supply\": 2", $"\"supply\": {supply}")
            .Edit(DesignData.AccentsPath, "\"amount\": 1", $"\"amount\": {amount}")
            .Edit(DesignData.AccentsPath, "\"minimum\": 1", $"\"minimum\": {minimum}");
        var match = With("barracks", [1, 1, 1, 1, 3, 1], out var barracks, DataAssert.Valid(files));

        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();

        Assert.Equal(held, Assert.Single(match.OrdersAt(barracks)).Supply);
        match.RunUntilAppeared();
        Assert.Equal(held, match.Supply.Used(0));
    }

    /// <summary>
    /// With one supply left a Light-footprint unit still fits where a full one does not, and a
    /// pair falls back to one unit.
    /// </summary>
    [Theory]
    [InlineData(4, 99, TrainCheck.Allowed)]
    [InlineData(1, 54, TrainCheck.NoSupply)]
    public void TheLastSupplyTakesALightUnitAndNoFullOne(int logistics, int used, TrainCheck expected)
    {
        var match = With("barracks", [1, 1, 2, 4, logistics, 2], out var barracks);
        // Workers fill the supply: one each, whatever the mix.
        for (var worker = 0; worker < used; worker++)
        {
            match.SpawnWorker(0, MapGrid.TileCenter(23 - (worker / 24), worker % 24));
        }

        Assert.Equal((used + 1, used), (match.Supply.Cap(0), match.Supply.Used(0)));
        Assert.Equal(expected, match.Production.CheckTrain(0, barracks, Auxiliary));

        match.OrderTrain(0, barracks, Auxiliary);
        match.Simulation.Tick();

        if (expected == TrainCheck.Allowed)
        {
            Assert.Equal((1, 1), (match.OrdersAt(barracks)[0].Count, match.OrdersAt(barracks)[0].Supply));
            Assert.Equal(match.Supply.Cap(0), match.Supply.Used(0));
        }
        else
        {
            Assert.Empty(match.OrdersAt(barracks));
            Assert.Equal(TrainCheck.NoSupply, Assert.Single(match.TrainEvents()).Reason);
        }
    }

    /// <summary>
    /// "Applies to existing units at once and frees their supply": in the tick the research
    /// finishes every unit on the map uses one less. An order placed before it keeps what it
    /// held (design 2.3: "research that finishes later changes none of these for orders already
    /// in the queue") until its unit appears, which then uses one less too.
    /// </summary>
    [Fact]
    public void AResearchThatBringsLightFootprintFreesTheSupplyOfExistingUnitsAtOnce()
    {
        // Logistics 2: a cap of 70, and a Peltast uses 2.
        var match = With("barracks", [1, 1, 1, 1, 2, 1], out var barracks);
        match.Research.OpenPlayerSlot(0);
        var army = match.SpawnMany(0, Peltast, 33);
        match.OrderResearch(0, "logistics");
        match.Simulation.Tick();

        match.RunUntilResearchHas(0, ticksLeft: 2);
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();

        // 66 for the army and 2 for each of two orders. The third does not fit.
        Assert.Equal((70, 70), (match.Supply.Cap(0), match.Supply.Used(0)));
        Assert.Equal(2, match.OrdersAt(barracks).Length);
        Assert.Equal(TrainCheck.NoSupply, match.TrainEvents()[^1].Reason);
        Assert.Equal(TrainCheck.NoSupply, match.Production.CheckTrain(0, barracks, Peltast));

        match.Simulation.Tick();

        // The research finished in that tick: 33 for the army, and the two orders still hold 2 each.
        Assert.Equal(3, match.Research.State.Tier(0, DesignData.Track("logistics")));
        Assert.Equal(1, match.BlueprintOf(0, Peltast).Supply);
        Assert.Equal((85, 37), (match.Supply.Cap(0), match.Supply.Used(0)));
        Assert.All(match.OrdersAt(barracks), order => Assert.Equal(2, order.Supply));
        Assert.Equal(33, army.Count(match.World.Entities.IsAlive));

        // The order that was refused is taken now, and holds 1.
        Assert.Equal(TrainCheck.Allowed, match.Production.CheckTrain(0, barracks, Peltast));
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();
        Assert.Equal([2, 2, 1], match.OrdersAt(barracks).Select(order => order.Supply));
        Assert.Equal(38, match.Supply.Used(0));

        // Each unit that appears uses 1, whatever its order held.
        match.RunUntilAppeared();
        Assert.Equal(37, match.Supply.Used(0));
        match.RunUntilAppeared();
        Assert.Equal(36, match.Supply.Used(0));
        match.RunUntilAppeared();
        Assert.Equal(36, match.Supply.Used(0));
        Assert.Equal(36, match.Combatants.Count);
    }
}
