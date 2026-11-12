using Sim.Core;
using Sim.Economy;
using Sim.Research;
using Sim.Setup;
using Sim.Tests.Map;
using static Sim.Tests.Economy.EconomyMatches;

namespace Sim.Tests.Economy;

/// <summary>
/// The economy reads a player's tiers from the research of the match (design 4.1 and 4.2): a
/// research that finishes changes what the workers are paid and how much supply the player has.
/// </summary>
public class ResearchAndEconomyTests
{
    /// <summary>A second of one worker's Materials at Extraction 2: 0.75 x 1.12.</summary>
    private const long MaterialsPaymentAtTier2 = 840;

    [Fact]
    public void AnExtractionResearchRaisesTheNextPayment()
    {
        var (finished, payments) = ResearchExtractionWhileAWorkerEarns(ticksBeforeTheOrder: 7);

        var before = payments.Where(payment => payment.Tick <= finished).ToArray();
        var after = payments.Where(payment => payment.Tick > finished).ToArray();
        Assert.True(before.Length >= 2 && after.Length >= 2);
        Assert.All(before, payment => Assert.Equal(MaterialsPayment, payment.Amount));
        Assert.All(after, payment => Assert.Equal(MaterialsPaymentAtTier2, payment.Amount));

        // No payment was lost or delayed by the research: they stay a second apart.
        Assert.Equal(SimInfo.TicksPerSecond, after[0].Tick - before[^1].Tick);
    }

    /// <summary>
    /// Income runs before research in a tick, so a payment made in the tick in which an
    /// Extraction research finishes is still at the old tier. The new tier pays from the next
    /// tick on.
    /// </summary>
    [Fact]
    public void AnExtractionResearchThatFinishesInATickDoesNotRaiseAPaymentOfThatTick()
    {
        // Find how far the end of the research is from a payment, and order it that much later.
        var (probeFinished, probePayments) = ResearchExtractionWhileAWorkerEarns(ticksBeforeTheOrder: 0);
        var second = SimInfo.TicksPerSecond;
        var delay = (((probePayments[0].Tick - probeFinished) % second) + second) % second;

        var (finished, payments) = ResearchExtractionWhileAWorkerEarns(delay);

        var inTheTick = Assert.Single(payments, payment => payment.Tick == finished);
        Assert.Equal(MaterialsPayment, inTheTick.Amount);
        var next = Assert.Single(payments, payment => payment.Tick == finished + second);
        Assert.Equal(MaterialsPaymentAtTier2, next.Amount);
    }

    [Fact]
    public void ALogisticsResearchRaisesTheSupplyCap()
    {
        var match = Create(TestMaps.Open(16, 16));
        var logistics = Track(match, "logistics");
        match.Research.OpenPlayerSlot(0);
        match.Stockpiles.Add(0, 10_000, 10_000);
        for (var i = 0; i < 5; i++)
        {
            match.WorkerAt(0, i, 3);
        }

        Assert.Equal(55, match.Supply.Cap(0));
        Assert.False(match.Supply.HasRoomFor(0, 51));
        var ticks = match.Research.Tech(0).Research[logistics].Price!.Value.Ticks;

        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.StartResearch, 0, contentId: logistics));
        match.Simulation.Run(ticks - 1);
        Assert.Equal(55, match.Supply.Cap(0));
        match.Simulation.Run(2);

        Assert.Equal(2, match.Research.State.Tier(0, logistics));
        Assert.Equal(70, match.Supply.Cap(0));
        Assert.Equal(65, match.Supply.Free(0));
        Assert.True(match.Supply.HasRoomFor(0, 65));
        Assert.Equal(55, match.Supply.Cap(1));
    }

    /// <summary>
    /// A worker of player 0 earns at a deposit, and the player researches Extraction 2, ordered
    /// some ticks after the worker's first payment. Returns the tick in which the research
    /// finished and every payment to the player, each with the tick in which it was made.
    /// </summary>
    private static (int Finished, (int Tick, long Amount)[] Payments) ResearchExtractionWhileAWorkerEarns(int ticksBeforeTheOrder)
    {
        var match = WithOneDeposit(out var deposit);
        var extraction = Track(match, "extraction");
        match.Research.OpenPlayerSlot(0);
        var finished = -1;
        match.Research.Reported += reported =>
        {
            if (reported.Kind == ResearchEventKind.Finished)
            {
                finished = reported.Tick;
            }
        };
        var miner = match.WorkerAt(0, 8, 10);
        match.OrderGather(0, deposit, miner);
        var arrival = match.RunUntilWorking(miner);

        // The price is added after the first payment and paid at once, so that every change to
        // the Materials but those two is a payment.
        var payments = new List<(int Tick, long Amount)>();
        var price = match.Research.Tech(0).Research[extraction].Price!.Value;
        var order = arrival + SimInfo.TicksPerSecond + 1 + ticksBeforeTheOrder;
        var end = order + price.Ticks + (3 * SimInfo.TicksPerSecond);
        while (match.World.Tick <= end)
        {
            var tick = match.World.Tick;
            if (tick == order)
            {
                match.Stockpiles.Add(0, price.Materials, price.Energy);
                match.Simulation.Commands.Enqueue(tick, new Command(CommandType.StartResearch, 0, contentId: extraction));
            }

            var before = match.Stockpiles.Materials(0);
            match.Simulation.Tick();
            var paid = match.Stockpiles.Materials(0) - before + (tick == order ? price.Materials * Stockpiles.PerResource : 0);
            if (paid != 0)
            {
                payments.Add((tick, paid));
            }
        }

        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.True(finished > order, "The research did not finish.");
        Assert.Equal(2, match.Research.State.Tier(0, extraction));
        Assert.Equal(0, match.Stockpiles.Energy(0));
        return (finished, [.. payments]);
    }

    private static int Track(Match match, string id) => match.Data.Tracks.ToList().FindIndex(track => track.Id == id);
}
