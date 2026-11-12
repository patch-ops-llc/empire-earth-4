using Sim.Core;
using Sim.Economy;
using Sim.Tests.Research;

namespace Sim.Tests.Economy;

public class StockpilesTests
{
    [Fact]
    public void AStockpileStartsEmptyAndCountsInThousandths()
    {
        var stockpiles = new Stockpiles(2);

        stockpiles.Add(1, 400, 100);

        Assert.Equal((0, 0), (stockpiles.Materials(0), stockpiles.Energy(0)));
        Assert.Equal((400_000, 100_000), (stockpiles.Materials(1), stockpiles.Energy(1)));
        Assert.Equal(1000, Stockpiles.PerResource);
        Assert.Equal(2, stockpiles.PlayerCount);
    }

    [Theory]
    [InlineData(400, 100, true)]
    [InlineData(0, 0, true)]
    [InlineData(401, 100, false)]
    [InlineData(400, 101, false)]
    public void APriceIsPaidInFullOrNotAtAll(int materials, int energy, bool paid)
    {
        var stockpiles = new Stockpiles(1);
        stockpiles.Add(0, 400, 100);

        Assert.Equal(paid, stockpiles.Covers(0, materials, energy));
        Assert.Equal(paid, stockpiles.TryPay(0, materials, energy));

        var left = paid ? (400 - materials, 100 - energy) : (400, 100);
        Assert.Equal((left.Item1 * 1000L, left.Item2 * 1000L), (stockpiles.Materials(0), stockpiles.Energy(0)));
    }

    [Fact]
    public void AmountsBelowZeroAndPlayersNotInTheMatchAreRefused()
    {
        var stockpiles = new Stockpiles(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Stockpiles(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stockpiles.Add(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stockpiles.Add(0, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stockpiles.TryPay(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stockpiles.Covers(0, 0, -1));
        Assert.Throws<IndexOutOfRangeException>(() => stockpiles.Add(1, 1, 1));
        Assert.Throws<IndexOutOfRangeException>(() => stockpiles.Materials(-1));
    }

    [Fact]
    public void EveryAmountIsInTheStateHash()
    {
        var hashes = new HashSet<ulong>();
        for (var player = 0; player < 2; player++)
        {
            foreach (var (materials, energy) in new[] { (1, 0), (0, 1) })
            {
                var world = new World(1);
                var stockpiles = world.AddStore(new Stockpiles(2));
                stockpiles.Add(player, materials, energy);
                hashes.Add(world.ComputeHash());
            }
        }

        Assert.Equal(4, hashes.Count);
        Assert.Equal("stockpiles", new Stockpiles(1).Name);
    }

    [Theory]
    [InlineData("_materials[player]")]
    [InlineData("_energy[player]")]
    public void ARestoreRefusesAnAmountBelowZero(string field)
    {
        var stockpiles = new Stockpiles(2);
        stockpiles.Add(0, 5, 5);

        Assert.Throws<InvalidDataException>(() => stockpiles.Walk(new ChangingWalker(field, occurrence: 1, replacement: -1)));
    }
}
