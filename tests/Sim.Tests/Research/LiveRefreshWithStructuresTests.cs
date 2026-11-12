using Sim.Combat;
using Sim.Core;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Map;
using static Sim.Tests.Research.ResearchMatches;

namespace Sim.Tests.Research;

/// <summary>
/// Design 2.3, "when a research finishes, every existing unit updates", where its two halves
/// meet: the units and workers of a player (<see cref="Sim.Research.UnitRefresh"/>) and the
/// structures of that player (<see cref="StructureHealth"/>) are brought up to date by the same
/// research, in the tick it finishes. Each half has its own tests; this is the match that has
/// both.
/// </summary>
/// <remarks>
/// The numbers are the design's: a Hoplite has 130 hit points, a worker 50 and a Capitol 2,000,
/// and a tier of Protection multiplies each by 1.25.
/// </remarks>
public class LiveRefreshWithStructuresTests
{
    [Fact]
    public void OneResearchBringsAUnitAWorkerAndAStructureUpToDateInTheTickItFinishes()
    {
        var match = CombatMatches.Create(TestMaps.Open(32, 24));
        var capitol = match.PlaceStructure(0, "capitol", 4, 4);
        var unit = match.Spawn(0, CombatMatches.Hoplite, "9.5", "5.5");
        var worker = match.SpawnWorker(0, "9.5", "7.5");
        var theirCapitol = match.PlaceStructure(1, "capitol", 24, 16);
        var theirUnit = match.Spawn(1, CombatMatches.Hoplite, "22.5", "17.5");
        match.Stockpiles.Add(0, Plenty, Plenty);
        match.SetHitPoints(capitol, 500_000);
        match.SetHitPoints(unit, 65_000);
        match.SetHitPoints(worker, 25_000);
        match.SetHitPoints(theirCapitol, 500_000);
        match.SetHitPoints(theirUnit, 65_000);

        // The research runs in the slot of the Capitol that is brought up to date by it.
        match.OrderResearch(0, Protection);
        match.Simulation.Run(TicksToTier2 - 1);
        Assert.Equal(Health(500_000, 2_000_000), match.Health.Get(capitol));
        Assert.Equal(Health(65_000, 130_000), match.Health.Get(unit));
        Assert.Equal(Health(25_000, 50_000), match.Health.Get(worker));
        Assert.Empty(match.EventsOf(SimEventKind.ResearchFinished));

        match.Simulation.Tick();

        // A quarter of 2,000 is a quarter of 2,500; half of 130 is half of 162.5; half of 50 is half of 62.5.
        Assert.Single(match.EventsOf(SimEventKind.ResearchFinished));
        Assert.Equal(Health(625_000, 2_500_000), match.Health.Get(capitol));
        Assert.Equal(Health(81_250, 162_500), match.Health.Get(unit));
        Assert.Equal(Health(31_250, 62_500), match.Health.Get(worker));

        // Nothing of the other player changes, and nothing was dropped or refused on the way.
        Assert.Equal(Health(500_000, 2_000_000), match.Health.Get(theirCapitol));
        Assert.Equal(Health(65_000, 130_000), match.Health.Get(theirUnit));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);

        // What is placed from now on starts whole under the new tier, as the two halves each say.
        var second = match.PlaceStructure(0, "capitol", 4, 16);
        var recruit = match.Spawn(0, CombatMatches.Hoplite, "9.5", "9.5");
        Assert.Equal(Health(2_500_000, 2_500_000), match.Health.Get(second));
        Assert.Equal(Health(162_500, 162_500), match.Health.Get(recruit));
    }

    private static Health Health(long hitPoints, long maximum) => new() { HitPoints = hitPoints, Maximum = maximum };
}
