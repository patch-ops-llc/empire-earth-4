using Sim.Core;
using Sim.Economy;
using Sim.Serialization;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Economy.EconomyMatches;

namespace Sim.Tests.Economy;

public class StockpilesAndSupplyTests
{
    [Fact]
    public void APlayerWithAStartBeginsWithTheStartingStockpileAndOneWithoutBeginsWithNothing()
    {
        var data = TestMatches.Data;
        var match = MatchSetup.Create(data, data.Maps[0], 1, [new(1), PlayerSetup.WithoutStart, new(0)]);

        Assert.Equal(3, match.Stockpiles.PlayerCount);
        Assert.Equal((400_000L, 100_000L), (match.Stockpiles.Materials(0), match.Stockpiles.Energy(0)));
        Assert.Equal((0L, 0L), (match.Stockpiles.Materials(1), match.Stockpiles.Energy(1)));
        Assert.Equal((400_000L, 100_000L), (match.Stockpiles.Materials(2), match.Stockpiles.Energy(2)));
        Assert.Contains(match.World.Stores, store => ReferenceEquals(store, match.Stockpiles) && store.Name == "stockpiles");
    }

    [Fact]
    public void APriceIsTakenWholeOrNotAtAllFromWhatIncomeAddedInThousandths()
    {
        var stockpiles = new Stockpiles(2);
        stockpiles.AddThousandths(0, Resource.Materials, 100_500);
        stockpiles.AddThousandths(0, Resource.Energy, 49_999);

        Assert.True(stockpiles.Covers(0, 100, 49));
        Assert.False(stockpiles.Covers(0, 101, 0));
        Assert.False(stockpiles.Covers(0, 0, 50));
        Assert.False(stockpiles.TryPay(0, 100, 50));
        Assert.Equal((100_500L, 49_999L), (stockpiles.Materials(0), stockpiles.Energy(0)));

        Assert.True(stockpiles.TryPay(0, 70, 10));
        Assert.Equal((30_500L, 39_999L), (stockpiles.Materials(0), stockpiles.Energy(0)));
        Assert.True(stockpiles.TryPay(0, 0, 0));
        Assert.False(stockpiles.TryPay(1, 1, 0));
        stockpiles.Add(1, 2, 3);
        Assert.Equal((2000L, 3000L), (stockpiles.Materials(1), stockpiles.Energy(1)));

        Assert.Throws<ArgumentOutOfRangeException>(() => stockpiles.AddThousandths(0, Resource.Materials, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stockpiles.Add(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stockpiles.TryPay(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stockpiles.Covers(0, 0, -1));
        Assert.Throws<IndexOutOfRangeException>(() => stockpiles.Materials(2));
        Assert.Throws<OverflowException>(() => stockpiles.AddThousandths(0, Resource.Energy, long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Stockpiles(0));
    }

    [Fact]
    public void TheStockpilesAreInTheStateHashAndInASnapshot()
    {
        var match = Create(TestMaps.Open(8, 8));
        var before = match.World.ComputeHash();
        match.Stockpiles.AddThousandths(1, Resource.Energy, 1);
        var after = match.World.ComputeHash();
        Assert.NotEqual(before, after);
        match.Stockpiles.AddThousandths(1, Resource.Materials, 1);
        Assert.NotEqual(after, match.World.ComputeHash());

        var writer = new ByteWriter();
        Snapshot.Write(match.World, writer);
        var restored = Create(TestMaps.Open(8, 8));
        Snapshot.Restore(restored, new ByteReader(writer.ToArray()));
        Assert.Equal((1L, 1L), (restored.Stockpiles.Materials(1), restored.Stockpiles.Energy(1)));
        Assert.Equal(match.World.ComputeHash(), restored.World.ComputeHash());
    }

    /// <summary>Design 4.1: the cap is 40 + 15 per Logistics tier, and a worker costs 1.</summary>
    [Theory]
    [InlineData(1, 55)]
    [InlineData(2, 70)]
    [InlineData(4, 100)]
    public void TheSupplyCapFollowsTheLogisticsTierAndEveryLivingWorkerUsesOne(int tier, int cap)
    {
        var match = Create(TestMaps.Open(16, 16), DataStartingAtTier(tier));
        var workers = Enumerable.Range(0, 7).Select(i => match.WorkerAt(0, i, 3)).ToArray();
        match.WorkerAt(1, 0, 5);
        match.SpawnUnit(0, Sim.Map.MapGrid.TileCenter(0, 7), Sim.Numerics.Fixed.Half);

        Assert.Equal(cap, match.Supply.Cap(0));
        Assert.Equal(7, match.Supply.Used(0));
        Assert.Equal(1, match.Supply.Used(1));
        Assert.Equal(cap - 7, match.Supply.Free(0));
        Assert.True(match.Supply.HasRoomFor(0, cap - 7));
        Assert.False(match.Supply.HasRoomFor(0, cap - 6));

        match.World.Entities.Destroy(workers[2]);
        Assert.Equal(6, match.Supply.Used(0));
        match.Simulation.Tick();
        Assert.Equal(6, match.Supply.Used(0));
    }

    [Fact]
    public void WhatOtherUsersReportCountsAndUsedSupplyMayPassTheCap()
    {
        var match = Create(TestMaps.Open(16, 16));
        match.WorkerAt(0, 1, 1);
        match.Supply.AddUser(new Army(player: 0, supply: 60));

        Assert.Equal(55, match.Supply.Cap(0));
        Assert.Equal(61, match.Supply.Used(0));
        Assert.Equal(0, match.Supply.Free(0));
        Assert.False(match.Supply.HasRoomFor(0, 1));
        Assert.Equal(0, match.Supply.Used(1));
        Assert.True(match.Supply.HasRoomFor(1, 55));
    }

    private sealed class Army(int player, int supply) : ISupplyUser
    {
        public int SupplyUsedBy(int asked) => asked == player ? supply : 0;
    }
}
