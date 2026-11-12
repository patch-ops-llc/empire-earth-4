using Sim.Core;
using Sim.Economy;
using Sim.Setup;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using static Sim.Tests.Economy.EconomyMatches;

namespace Sim.Tests.Economy;

/// <summary>
/// The snapshot round trip for the economy's stores: stockpiles, territories, workers and work
/// sites.
/// </summary>
public class EconomySnapshotTests
{
    private const int LastTick = 420;

    /// <summary>
    /// One worker of another player sent to a deposit it does not hold, nine of the player who
    /// holds it sent after it, so that two wait, and three sent to a Generator. The deposit is
    /// close to its full-rate amount. One worker is moved off and sent back on the way. Snapshots: before any order, with the orders waiting, with every worker
    /// walking, with some arrived, just after a place was freed, with a waiting worker on its way
    /// to a place, after the deposit went to half rate, and at the end.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(45)]
    [InlineData(151)]
    [InlineData(158)]
    [InlineData(231)]
    [InlineData(300)]
    [InlineData(LastTick)]
    public void AMatchWithWorkersAtWorkRestoresAndContinues(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(Build, Script(), snapshotTick, LastTick);

    /// <summary>The run does what the test above says it does, so that the test means something.</summary>
    [Fact]
    public void TheRunHasWorkersInEveryStateAtTheSnapshots()
    {
        var match = Build();
        foreach (var (tick, command) in Script())
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        match.Simulation.Run(2);
        Assert.Equal((0, 11, 0, 2), Counts(match));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);

        match.Simulation.Run(43);
        var (_, walking, working, _) = Counts(match);
        Assert.InRange(walking, 1, 10);
        Assert.InRange(working, 1, 10);

        match.Simulation.Run(105);
        Assert.Equal((0, 0, 11, 2), Counts(match));
        var paidAtFullRate = match.Stockpiles.Materials(0);
        Assert.True(match.WorkSites.Get(match.SiteAt(10, 10)).Delivered < 8_000_000);
        Assert.True(match.Stockpiles.Energy(0) > 0);

        // A worker is moved off the deposit: the waiting one has its place and is on its way.
        match.Simulation.Run(8);
        Assert.Equal((1, 1, 10, 1), Counts(match));

        // The one moved off was sent back and now waits in its turn.
        match.Simulation.Run(73);
        Assert.Equal((0, 0, 11, 2), Counts(match));

        match.Simulation.Run(LastTick - 231);
        var site = match.WorkSites.Get(match.SiteAt(10, 10));
        Assert.True(site.Delivered > 8_000_000);
        Assert.NotEqual(0, (match.Stockpiles.Materials(0) - paidAtFullRate) % MaterialsPayment);
        Assert.Equal(0, match.Stockpiles.Materials(1));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    private static (int Idle, int Walking, int Working, int Waiting) Counts(Match match)
    {
        var counts = new int[4];
        for (var i = 0; i < match.Workers.Count; i++)
        {
            counts[(int)match.Workers.ValueAt(i).State]++;
        }

        return (counts[0], counts[1], counts[2], counts[3]);
    }

    /// <summary>
    /// The deposit is entity 1, as the match setup made it. Entities 2 to 10 are the miners, 11 to
    /// 13 the stokers, 14 the other player's worker and 17 the Generator.
    /// </summary>
    private static Match Build()
    {
        var match = Create(OneDeposit);
        for (var i = 0; i < 9; i++)
        {
            match.WorkerAt(0, 2 + (2 * i), 3);
        }

        for (var i = 0; i < 3; i++)
        {
            match.WorkerAt(0, 20, 14 + i);
        }

        match.WorkerAt(1, 3, 20);
        match.PlaceTerritory(0, 10, 10, 2, 12);
        match.PlaceTerritory(1, 0, 22, 2, 4);
        match.PlaceGenerator(0, 16, 16);
        match.WorkSites.Get(match.SiteAt(10, 10)).Delivered = 7_950_000;
        match.RefreshDerivedState();
        return match;
    }

    private static List<(int Tick, Command Command)> Script()
    {
        var deposit = new EntityId(1);
        var generator = new EntityId(17);
        var miners = Enumerable.Range(2, 9).Select(id => new EntityId(id)).ToArray();
        var stokers = Enumerable.Range(11, 3).Select(id => new EntityId(id)).ToArray();
        return
        [
            (1, Gather(1, deposit, new EntityId(14))),
            (1, Gather(0, deposit, miners[..8])),
            (1, Gather(0, generator, stokers)),
            (1, Gather(0, deposit, miners[8])),
            (150, new Command(CommandType.Move, 0, [miners[2]], TestMatches.Point("6.5", "6.5").X, TestMatches.Point("6.5", "6.5").Y)),
            (170, Gather(0, deposit, miners[2])),
        ];
    }
}
