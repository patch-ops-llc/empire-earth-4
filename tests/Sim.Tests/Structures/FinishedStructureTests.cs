using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Data;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Research;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Structures.StructureMatches;

namespace Sim.Tests.Structures;

/// <summary>
/// What a finished structure gives its owner (design 2.5), and what happens to it when the
/// structure falls: a territory, a research slot, a Generator's places, and hit points that
/// follow the owner's Protection tier.
/// </summary>
public class FinishedStructureTests
{
    private const string Academy = """
        {
          "id": "academy",
          "footprint": 2,
          "hp": 800,
          "cost": { "materials": 200, "energy": 100 },
          "build_seconds": 40,
          "sight": 6,
          "research_slots": 1,
          "max_per_player": 2
        }
        """;

    private static readonly Lazy<GameData> AcademyData = new(() =>
        DataAssert.Valid(DesignData.Files().Set("structures/academy.jsonc", Academy)));

    /// <summary>The design's data, whose research the tests of research use, with both players at their starts and rich.</summary>
    private static Match DesignMatch(GameData? data = null)
    {
        var match = WithStarts(data ?? DesignData.Data);
        match.Stockpiles.Add(0, 10_000, 10_000);
        match.Stockpiles.Add(1, 10_000, 10_000);
        return match;
    }

    [Fact]
    public void ALaidStructureGivesNothingUntilItIsFinished()
    {
        var match = Create(TestMaps.Open(24, 24));

        var capitol = match.LayStructure(0, GeneratorData.StartStructure, 4, 4);
        var generator = match.LayStructure(0, GeneratorData.StructureOf("generator"), 10, 10);

        Assert.False(match.Grid.IsPassable(4, 4));
        Assert.Equal(2_000_000, match.Health.Get(capitol).HitPoints);
        Assert.Equal((0, 0), (match.Territories.Count, match.WorkSites.Count));
        Assert.False(match.Research.State.Slot(0, 0).IsOpen);

        match.FinishStructure(capitol);
        match.FinishStructure(generator);

        Assert.Equal(Territory.Around(0, 4, 4, 2, 6), match.Territories.Get(capitol));
        Assert.Equal(
            new WorkSite { Kind = WorkSiteKind.Generator, Owner = 0, X = 10, Y = 10, Size = 2 },
            match.WorkSites.Get(generator));
        Assert.True(match.Research.State.Slot(0, 0).IsOpen);
        Assert.Throws<ArgumentException>(() => match.FinishStructure(match.SpawnUnit(0, MapGrid.TileCenter(1, 1), QuarterTile)));
    }

    [Fact]
    public void WorkersEarnEnergyBesideAGeneratorThatBlocksItsTiles()
    {
        var match = Create(TestMaps.Open(24, 24));
        var generator = match.PlaceGenerator(0, 10, 10);
        var worker = match.WorkerAt(0, 4, 10);

        match.OrderGather(0, generator, worker);
        match.RunUntilWorking(worker);
        var before = match.Stockpiles.Energy(0);
        match.Simulation.Run(SimInfo.TicksPerSecond);

        Assert.Equal((9, 10), match.StandsOn(worker));
        Assert.Equal(before + EconomyMatches.EnergyPayment, match.Stockpiles.Energy(0));
        Assert.False(match.Grid.IsPassable(10, 10));
    }

    [Fact]
    public void AGeneratorThatFallsTakesItsWorkSiteWithItAndItsWorkersStopWorking()
    {
        var match = Create(TestMaps.Open(24, 24));
        var generator = match.PlaceGenerator(0, 10, 10);
        var worker = match.WorkerAt(0, 8, 10);
        match.OrderGather(0, generator, worker);
        match.RunUntilWorking(worker);

        match.Raze(generator);
        match.Simulation.Run(2);

        Assert.Equal(WorkerState.Idle, match.StateOf(worker));
        Assert.Equal((0, 0), (match.WorkSites.Count, match.Structures.Count));
        var energy = match.Stockpiles.Energy(0);
        match.Simulation.Run(2 * SimInfo.TicksPerSecond);
        Assert.Equal(energy, match.Stockpiles.Energy(0));
    }

    /// <summary>Design 1.3: the research in the Capitol's slot is cancelled with its price back when the last Capitol falls.</summary>
    [Fact]
    public void WhenTheLastCapitolFallsItsResearchIsCancelledAndThePriceComesBack()
    {
        var match = DesignMatch();
        var before = (match.Stockpiles.Materials(0), match.Stockpiles.Energy(0));
        match.OrderResearch(0, ResearchMatches.Firepower);
        match.Simulation.Run(5);
        Assert.True(match.Slot(0).IsBusy);
        Assert.NotEqual(before, (match.Stockpiles.Materials(0), match.Stockpiles.Energy(0)));

        match.Raze(match.StructuresOf(0)[0]);
        match.Simulation.Tick();

        Assert.False(match.Slot(0).IsOpen);
        Assert.False(match.Slot(0).IsBusy);
        Assert.Equal(before, (match.Stockpiles.Materials(0), match.Stockpiles.Energy(0)));
        Assert.True(match.Slot(1).IsOpen);

        // And none can be started until a Capitol is finished again.
        match.OrderResearch(0, ResearchMatches.Firepower);
        match.Simulation.Tick();
        Assert.False(match.Slot(0).IsBusy);
        match.PlaceStructure(0, "capitol", 8, 12);
        match.OrderResearch(0, ResearchMatches.Firepower);
        match.Simulation.Tick();
        Assert.True(match.Slot(0).IsBusy);
    }

    [Fact]
    public void APlayerWithASecondCapitolKeepsTheSlotWhenOneFalls()
    {
        var match = DesignMatch();
        var first = match.StructuresOf(0)[0];
        var second = match.PlaceStructure(0, "capitol", 8, 12);
        match.OrderResearch(0, ResearchMatches.Firepower);
        match.Simulation.Run(5);

        match.Raze(first);
        match.Simulation.Tick();

        Assert.True(match.Slot(0).IsBusy);
        Assert.Equal([second], match.StructuresOf(0));
        match.Simulation.Run(ResearchMatches.TicksToTier2);
        Assert.Equal(2, match.Research.State.Tier(0, ResearchMatches.Firepower));
    }

    /// <summary>Design 2.5: an Academy adds a slot of its own, which closes when the Academy falls.</summary>
    [Fact]
    public void AStructureWithAResearchSlotOfItsOwnOpensOneThatClosesWhenItFalls()
    {
        var match = DesignMatch(AcademyData.Value);
        var academy = match.PlaceStructure(0, "academy", 8, 12);
        Assert.Equal(academy, match.Slot(0, 1).Holder);
        Assert.True(match.Slot(0, 1).IsOpen);

        match.OrderResearch(0, ResearchMatches.Firepower);
        match.OrderResearch(0, ResearchMatches.Protection);
        match.Simulation.Run(5);
        Assert.True(match.Slot(0, 0).IsBusy && match.Slot(0, 1).IsBusy);

        match.Raze(academy);
        match.Simulation.Tick();

        Assert.False(match.Slot(0, 1).IsOpen);
        Assert.True(match.Slot(0, 0).IsBusy);

        // A third Academy has no slot to give: the order that places structures refuses it first.
        match.PlaceStructure(0, "academy", 8, 12);
        match.PlaceStructure(0, "academy", 16, 12);
        Assert.Throws<InvalidOperationException>(() => { match.PlaceStructure(0, "academy", 22, 10); });
    }

    /// <summary>Design 2.3 and 2.5: structure hit points scale by 1.25 per Protection tier, and a damaged structure keeps its fraction.</summary>
    [Fact]
    public void AStructureKeepsTheSameFractionOfItsHitPointsWhenItsOwnersProtectionRises()
    {
        var match = DesignMatch();
        var (mine, theirs) = (match.StructuresOf(0)[0], match.StructuresOf(1)[0]);
        match.SetHitPoints(mine, 500_000);
        match.SetHitPoints(theirs, 500_000);

        match.OrderResearch(0, ResearchMatches.Protection);
        match.Simulation.Run(ResearchMatches.TicksToTier2 - 1);
        Assert.Equal((500_000, 2_000_000), (match.Health.Get(mine).HitPoints, match.Health.Get(mine).Maximum));
        match.Simulation.Tick();

        Assert.Equal((625_000, 2_500_000), (match.Health.Get(mine).HitPoints, match.Health.Get(mine).Maximum));
        Assert.Equal((500_000, 2_000_000), (match.Health.Get(theirs).HitPoints, match.Health.Get(theirs).Maximum));

        // One placed from now on has the new maximum from the start.
        var second = match.PlaceStructure(0, "capitol", 8, 12);
        Assert.Equal(2_500_000, match.Health.Get(second).HitPoints);
    }

    /// <summary>
    /// A structure is an enemy like a unit (design 2.5): an order to attack one is carried out. A
    /// structure that does not attack is picked by no unit that stands idle (design 2.4). The rest
    /// of structures in combat is in the tests of <c>Sim.Tests.Combat</c>.
    /// </summary>
    [Fact]
    public void NoIdleUnitPicksACapitolAndAnOrderToAttackOneIsCarriedOut()
    {
        var match = CombatMatches.Create(TestMaps.Open(24, 24));
        var capitol = match.PlaceStructure(1, "capitol", 10, 10);
        var archer = match.Spawn(0, CombatMatches.Ballista, "6.5", "11.5");
        var guard = match.Spawn(0, CombatMatches.Hoplite, "9.5", "11.5");

        match.Simulation.Run(60);
        Assert.Equal(2_000_000, match.Health.Get(capitol).HitPoints);
        Assert.Empty(match.EventsOf(SimEventKind.ShotFired));

        match.OrderAttack(0, capitol, archer, guard);
        match.Simulation.Tick();

        Assert.Empty(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Single(match.EventsOf(SimEventKind.ShotFired));
        Assert.Equal(2_000_000 - 8_000, match.Health.Get(capitol).HitPoints);
        Assert.True(match.Targeting().IsEnemy(match.World, 0, capitol, out _));
        Assert.False(match.Targeting().IsEnemy(match.World, 1, capitol, out _));
        Assert.False(match.Targeting().IsEnemy(match.World, 0, guard, out _));
    }
}
