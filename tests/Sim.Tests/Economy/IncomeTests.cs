using Sim.Core;
using Sim.Economy;
using Sim.Movement;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Economy.EconomyMatches;

namespace Sim.Tests.Economy;

/// <summary>Design 4.1: what a worker standing at its place is paid, and when.</summary>
public class IncomeTests
{
    [Fact]
    public void AWorkerWalksToItsPlaceOnceAndIsPaidOneSecondAfterItArrivesAndEverySecondAfter()
    {
        var match = WithOneDeposit(out var deposit);
        var worker = match.WorkerAt(0, 4, 10);

        match.OrderGather(0, deposit, worker);
        match.Simulation.Tick();
        Assert.Equal(WorkerState.Walking, match.StateOf(worker));
        Assert.Equal((9, 10), match.TileOf(worker));

        var arrival = match.RunUntilWorking(worker);
        Assert.Equal(Sim.Map.MapGrid.TileCenter(9, 10), match.PositionOf(worker));
        Assert.Equal(MoveState.Idle, match.Movers.Get(worker).State);
        Assert.Equal(0, match.WorkerOf(worker).Timer);

        match.RunThrough(arrival + 19);
        Assert.Equal(0, match.Stockpiles.Materials(0));
        Assert.Equal(19, match.WorkerOf(worker).Timer);

        match.Simulation.Tick();
        Assert.Equal(MaterialsPayment, match.Stockpiles.Materials(0));
        Assert.Equal(0, match.WorkerOf(worker).Timer);

        match.RunThrough(arrival + 39);
        Assert.Equal(MaterialsPayment, match.Stockpiles.Materials(0));
        match.Simulation.Tick();
        Assert.Equal(2 * MaterialsPayment, match.Stockpiles.Materials(0));

        // 45 a minute, exactly, with nothing lost to rounding or to walking.
        match.RunThrough(arrival + (60 * 20));
        Assert.Equal(45 * Stockpiles.PerResource, match.Stockpiles.Materials(0));
        Assert.Equal(0, match.Stockpiles.Energy(0));
        Assert.Equal(0, match.Stockpiles.Materials(1));
        Assert.Equal(45 * Stockpiles.PerResource, match.WorkSites.Get(deposit).Delivered);
    }

    [Fact]
    public void EachWorkerCountsItsOwnTicksFromItsOwnArrival()
    {
        var match = WithOneDeposit(out var deposit);
        var near = match.WorkerAt(0, 8, 10);
        var far = match.WorkerAt(0, 10, 3);

        match.OrderGather(0, deposit, near, far);
        var nearArrival = match.RunUntilWorking(near);
        var farArrival = match.RunUntilWorking(far);
        Assert.NotEqual(0, (farArrival - nearArrival) % 20);

        var paid = new List<int>();
        var before = match.Stockpiles.Materials(0);
        while (match.World.Tick <= farArrival + 45)
        {
            match.Simulation.Tick();
            var now = match.Stockpiles.Materials(0);
            Assert.True(now == before || now == before + MaterialsPayment, "Two workers were paid in one tick, or a payment was not 0.75.");
            if (now != before)
            {
                paid.Add(match.World.Tick - 1);
            }

            before = now;
        }

        var expected = new List<int>();
        for (var second = 1; nearArrival + (20 * second) <= farArrival + 45; second++)
        {
            if (nearArrival + (20 * second) > farArrival)
            {
                expected.Add(nearArrival + (20 * second));
            }
        }

        expected.Add(farArrival + 20);
        expected.Add(farArrival + 40);
        expected.Sort();
        Assert.Equal(expected, paid);
    }

    [Fact]
    public void ADepositPaysOnlyWhileAllFourTilesAreInsideTheTerritoryOfTheWorkersOwner()
    {
        var match = Create(OneDeposit);
        var deposit = match.SiteAt(10, 10);
        var worker = match.WorkerAt(0, 8, 10);

        // Sent to a deposit outside its owner's territory, it stands at its place and earns nothing.
        match.OrderGather(0, deposit, worker);
        var arrival = match.RunUntilWorking(worker);
        match.RunThrough(arrival + 45);
        Assert.Equal(WorkerState.Working, match.StateOf(worker));
        Assert.Equal(0, match.Stockpiles.Materials(0));

        // A territory that leaves one tile of the deposit out changes nothing: tile (11, 11) has its
        // center 5.7 tiles from the center of a footprint of one tile at (7, 7).
        var short1 = match.PlaceTerritory(0, 7, 7, 1, 5);
        match.RunThrough(arrival + 65);
        Assert.Equal(0, match.Stockpiles.Materials(0));
        match.World.Entities.Destroy(short1);

        // Another player's territory is not the worker's owner's.
        var enemy = match.PlaceTerritory(1, 10, 10, 2, 8);
        match.RunThrough(arrival + 85);
        Assert.Equal(0, match.Stockpiles.Materials(0));
        Assert.Equal(0, match.Stockpiles.Materials(1));
        match.World.Entities.Destroy(enemy);

        // The count of ticks ran on all the while: the first payment comes at the worker's next
        // whole second, not a second after the territory.
        var outpost = match.PlaceTerritory(0, 7, 7, 1, 6);
        match.RunThrough(arrival + 99);
        Assert.Equal(0, match.Stockpiles.Materials(0));
        match.Simulation.Tick();
        Assert.Equal(MaterialsPayment, match.Stockpiles.Materials(0));
        match.RunThrough(arrival + 120);
        Assert.Equal(2 * MaterialsPayment, match.Stockpiles.Materials(0));

        // Destroying the Outpost stops the income. The worker keeps its place and its count.
        match.World.Entities.Destroy(outpost);
        match.RunThrough(arrival + 175);
        Assert.Equal(2 * MaterialsPayment, match.Stockpiles.Materials(0));
        Assert.Equal(WorkerState.Working, match.StateOf(worker));

        match.PlaceTerritory(0, 7, 7, 1, 6);
        match.RunThrough(arrival + 180);
        Assert.Equal(3 * MaterialsPayment, match.Stockpiles.Materials(0));
    }

    [Fact]
    public void TheTilesOfADepositMayLieInDifferentTerritoriesOfTheSameOwner()
    {
        var match = Create(OneDeposit);
        var deposit = match.SiteAt(10, 10);
        var worker = match.WorkerAt(0, 8, 10);

        // Each holds one column of the deposit and not the other.
        match.PlaceTerritory(0, 6, 10, 2, 4);
        Assert.False(Territory.Holds(match.Territories, match.World.Entities, 0, 10, 10, 2));
        match.PlaceTerritory(0, 14, 10, 2, 4);
        Assert.True(Territory.Holds(match.Territories, match.World.Entities, 0, 10, 10, 2));
        Assert.False(Territory.Holds(match.Territories, match.World.Entities, 1, 10, 10, 2));

        match.OrderGather(0, deposit, worker);
        var arrival = match.RunUntilWorking(worker);
        match.RunThrough(arrival + 20);
        Assert.Equal(MaterialsPayment, match.Stockpiles.Materials(0));
    }

    /// <summary>
    /// Design 4.1: full rate for the first 8,000 Materials delivered, half rate once the count had
    /// reached 8,000 before the payment. The payment that crosses 8,000 is paid in full.
    /// </summary>
    [Theory]
    [InlineData(7_999_250, 750, 375)]
    [InlineData(7_999_999, 750, 375)]
    [InlineData(7_998_000, 750, 750)]
    [InlineData(8_000_000, 375, 375)]
    public void ADepositPaysHalfOnceItHadDeliveredItsFullRateAmountBeforeThePayment(long delivered, long first, long second)
    {
        var match = WithOneDeposit(out var deposit);
        var worker = match.WorkerAt(0, 8, 10);
        match.WorkSites.Get(deposit).Delivered = delivered;

        match.OrderGather(0, deposit, worker);
        var arrival = match.RunUntilWorking(worker);

        match.RunThrough(arrival + 20);
        Assert.Equal(first, match.Stockpiles.Materials(0));
        Assert.Equal(delivered + first, match.WorkSites.Get(deposit).Delivered);

        match.RunThrough(arrival + 40);
        Assert.Equal(first + second, match.Stockpiles.Materials(0));
        Assert.Equal(delivered + first + second, match.WorkSites.Get(deposit).Delivered);
    }

    [Fact]
    public void TheCountOfADepositIsOfEveryPlayersMaterials()
    {
        var match = WithOneDeposit(out var deposit);
        match.PlaceTerritory(1, 10, 10, 2, 40);
        var own = match.WorkerAt(0, 8, 10);
        var other = match.WorkerAt(1, 13, 10);
        match.WorkSites.Get(deposit).Delivered = 7_999_900;

        match.OrderGather(0, deposit, own);
        match.OrderGather(1, deposit, other);
        match.RunUntilWorking(own, other);
        match.Simulation.Run(60);

        // One of the two got the last full payment. Every payment after it is half, for both.
        var total = match.Stockpiles.Materials(0) + match.Stockpiles.Materials(1);
        Assert.True(match.Stockpiles.Materials(0) > 0 && match.Stockpiles.Materials(1) > 0);
        Assert.True(total >= 750 + (4 * 375), $"Too few payments were made: {total}.");
        Assert.Equal(0, (total - 750) % 375);
        Assert.Equal(7_999_900 + total, match.WorkSites.Get(deposit).Delivered);
    }

    /// <summary>
    /// Design 4.2: Extraction multiplies both resources by 1.12 per tier. The product is exact and
    /// rounded once: 0.75 x 1.2544 = 0.9408, 0.5 x 1.2544 = 0.6272, and half of the first is 0.4704.
    /// </summary>
    [Fact]
    public void ExtractionMultipliesEveryPaymentAndTheBonusCountsTowardTheDepositsAmount()
    {
        var match = WithOneDeposit(out var deposit, DataStartingAtTier(3));
        var generator = match.PlaceGenerator(0, 16, 16);
        var miner = match.WorkerAt(0, 8, 10);
        var stoker = match.WorkerAt(0, 14, 16);
        match.WorkSites.Get(deposit).Delivered = 7_999_500;

        match.OrderGather(0, deposit, miner);
        match.OrderGather(0, generator, stoker);
        var arrival = match.RunUntilWorking(miner, stoker);

        match.RunThrough(arrival + 20);
        Assert.Equal(941, match.Stockpiles.Materials(0));
        Assert.Equal(627, match.Stockpiles.Energy(0));
        Assert.Equal(7_999_500 + 941, match.WorkSites.Get(deposit).Delivered);

        match.RunThrough(arrival + 40);
        Assert.Equal(941 + 470, match.Stockpiles.Materials(0));
        Assert.Equal(2 * 627, match.Stockpiles.Energy(0));
        Assert.Equal(0, match.WorkSites.Get(generator).Delivered);
    }

    [Fact]
    public void AGeneratorPaysItsOwnersWorkersEnergyAndNeedsNoTerritory()
    {
        var match = Create(TestMaps.Open(24, 24));
        var generator = match.PlaceGenerator(1, 10, 10);
        var workers = Enumerable.Range(0, 4).Select(i => match.WorkerAt(1, 4 + i, 4)).ToArray();

        match.OrderGather(1, generator, workers);
        var arrival = match.RunUntilWorking(workers);
        match.RunThrough(arrival + (60 * 20));

        // 30 a minute for each of four workers. The first three had arrived a little earlier.
        Assert.InRange(match.Stockpiles.Energy(1), 4 * 30 * Stockpiles.PerResource, (4 * 30 * Stockpiles.PerResource) + (3 * 3 * EnergyPayment));
        Assert.Equal(0, match.Stockpiles.Energy(1) % EnergyPayment);
        Assert.Equal(0, match.Stockpiles.Materials(1));
        Assert.Equal(0, match.Stockpiles.Energy(0));
    }

    /// <summary>
    /// The gather order refuses another player's Generator. A worker assigned to one all the same,
    /// as a later rule might, holds a place there and is paid nothing.
    /// </summary>
    [Fact]
    public void AGeneratorPaysNobodyButItsOwnersWorkers()
    {
        var match = Create(TestMaps.Open(24, 24));
        var generator = match.PlaceGenerator(1, 10, 10);
        var stranger = match.WorkerAt(0, 8, 10);

        match.Simulation.Tick();
        Assert.True(match.Work.Assign(match.World, stranger, generator));
        var arrival = match.RunUntilWorking(stranger);
        match.RunThrough(arrival + 45);

        Assert.Equal(1, match.Work.HoldersOf(match.World, generator));
        Assert.Equal((0L, 0L), (match.Stockpiles.Energy(0), match.Stockpiles.Energy(1)));
    }

    /// <summary>
    /// The sample map with both players' starts: the six starting workers sent to the main deposit
    /// earn six payments a second, on top of the starting stockpile.
    /// </summary>
    [Fact]
    public void TheStartingWorkersWorkTheMainDepositInsideTheStartingTerritory()
    {
        var match = MatchSetup.Create(TestMatches.Data, TestMatches.Data.Maps[0], 1, [new(0), new(1)]);
        var deposit = match.SiteAt(2, 4);
        var workers = Enumerable.Range(1, 6).Select(id => new EntityId(id)).ToArray();
        Assert.Equal(400 * Stockpiles.PerResource, match.Stockpiles.Materials(0));

        match.OrderGather(0, deposit, workers);
        var arrival = match.RunUntilWorking(workers);
        match.RunThrough(arrival + 20);
        var before = match.Stockpiles.Materials(0);
        match.Simulation.Run(200);

        Assert.Equal(before + (10 * 6 * MaterialsPayment), match.Stockpiles.Materials(0));
        Assert.Equal(100 * Stockpiles.PerResource, match.Stockpiles.Energy(0));
        Assert.Equal(400 * Stockpiles.PerResource, match.Stockpiles.Materials(1));
    }
}
