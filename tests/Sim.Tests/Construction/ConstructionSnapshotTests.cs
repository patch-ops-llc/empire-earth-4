using Sim.Construction;
using Sim.Core;
using Sim.Map;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Structures;
using static Sim.Tests.Construction.ConstructionMatches;

namespace Sim.Tests.Construction;

/// <summary>
/// Build orders and foundations are state: they are in the state hash, and a match restored from
/// a snapshot taken at any point of a build carries it on as the first match does.
/// </summary>
public class ConstructionSnapshotTests
{
    private static Match Build()
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((2, 10)));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 700, 100);
        match.Stockpiles.Add(1, 300, 0);
        match.WorkerAt(0, 4, 9);
        match.WorkerAt(0, 10, 4);
        match.WorkerAt(0, 3, 13);
        match.WorkerAt(1, 30, 2);
        match.SpawnUnit(1, MapGrid.TileCenter(10, 13), StructureMatches.QuarterTile);
        match.RefreshDerivedState();
        return match;
    }

    /// <summary>
    /// The run has every way a build goes: an order under way, one given up for another, one
    /// cancelled, one that ends on arrival, a foundation building, one cancelled, and one finished.
    /// </summary>
    [Fact]
    public void AMatchWithBuildsUnderWayRestoresAndContinues()
    {
        // The deposit is the first thing in the match, the Capitol the second, and the first structure laid the eighth.
        var (gatherer, builder, third, theirs) = (new EntityId(3), new EntityId(4), new EntityId(5), new EntityId(6));
        var generator = new EntityId(8);
        var probe = Build();
        var deposit = probe.SiteAt(2, 10);
        (int Tick, Command Command)[] script =
        [
            (0, EconomyMatches.Gather(0, deposit, gatherer)),
            (0, probe.Build(0, "generator", 10, 10, builder)),
            (2, probe.Build(1, "outpost", 30, 18, theirs)),
            (40, probe.Build(0, "academy", 8, 6, gatherer, third)),
            (60, probe.Build(0, "generator", 4, 13, gatherer)),
            (90, new Command(CommandType.CancelBuild, 1, [theirs])),
            (120, probe.Build(0, "generator", 10, 12, builder)),
            (200, new Command(CommandType.CancelBuild, 0, [generator])),
            (260, probe.Build(0, "outpost", 13, 14, third)),
        ];

        var seen = new List<(SimEventKind Kind, long Amount)>();
        foreach (var (tick, command) in script)
        {
            probe.Simulation.Commands.Enqueue(tick, command);
        }

        for (var tick = 0; tick < 700; tick++)
        {
            probe.Simulation.Tick();
            seen.AddRange(probe.Simulation.Events
                .Where(simEvent => simEvent.Kind is SimEventKind.BuildOrderEnded or SimEventKind.StructureRemoved or SimEventKind.StructureFinished)
                .Select(simEvent => (simEvent.Kind, simEvent.Kind == SimEventKind.BuildOrderEnded ? simEvent.Amount : 0L)));
        }

        Assert.Equal(0, probe.Simulation.DroppedCommandCount);
        Assert.Equal(
            [
                (SimEventKind.BuildOrderEnded, (long)BuildProblem.OtherOrder),
                (SimEventKind.BuildOrderEnded, (long)BuildProblem.Cancelled),
                (SimEventKind.BuildOrderEnded, (long)BuildProblem.EnemyOnSite),
                (SimEventKind.StructureRemoved, 0L),
                (SimEventKind.StructureFinished, 0L),
            ],
            seen);
        Assert.Equal((3, 1, 0), (probe.Structures.Count, probe.Foundations.Count, probe.BuildOrders.Count));
        Assert.Equal(100_000, probe.Stockpiles.Energy(0));

        foreach (var snapshotTick in new[] { 0, 1, 3, 41, 61, 91, 150, 201, 300 })
        {
            SnapshotAssert.RestoresAndContinues(Build, script, snapshotTick, lastTick: 700);
        }
    }

    [Fact]
    public void BuildOrdersAndFoundationsAreInTheStateHash()
    {
        var match = Build();
        var same = Build();
        var (walking, laid) = (new EntityId(4), new EntityId(5));
        match.OrderBuild(0, "generator", 10, 10, walking);
        match.OrderBuild(0, "generator", 3, 14, laid);
        same.OrderBuild(0, "generator", 10, 10, walking);
        same.OrderBuild(0, "generator", 3, 14, laid);
        match.Simulation.Tick();
        same.Simulation.Tick();
        Assert.Equal((1, 1), (match.BuildOrders.Count, match.Foundations.Count));
        Assert.Equal(same.World.ComputeHash(), match.World.ComputeHash());

        match.BuildOrders.Get(walking).Energy++;
        Assert.NotEqual(same.World.ComputeHash(), match.World.ComputeHash());
        match.BuildOrders.Get(walking).Energy--;
        match.Foundations.Get(match.StructureAt(3, 14)).Built++;
        Assert.NotEqual(same.World.ComputeHash(), match.World.ComputeHash());

        Assert.Contains(match.World.Stores, store => ReferenceEquals(store, match.BuildOrders) && store.Name == "buildorders");
        Assert.Contains(match.World.Stores, store => ReferenceEquals(store, match.Foundations) && store.Name == "foundations");
    }
}
