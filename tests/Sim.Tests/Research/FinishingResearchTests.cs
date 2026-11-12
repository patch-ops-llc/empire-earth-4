using Sim.Core;
using Sim.Map;
using Sim.Numerics;
using Sim.Research;
using Sim.Setup;
using Sim.Tech;
using Sim.Tests.Tech;
using static Sim.Tests.Research.ResearchMatches;

namespace Sim.Tests.Research;

/// <summary>What a finished research changes: the tier, the player's table, the epoch, and what stands on the map.</summary>
public class FinishingResearchTests
{
    [Fact]
    public void AResearchRaisesItsTrackByOneTierAfterExactlyItsTime()
    {
        var match = Create();
        match.Simulation.Run(7);
        var events = match.Events();
        match.OrderResearch(0, Firepower);

        match.Simulation.Run(TicksToTier2 - 1);
        Assert.Equal(1, match.Research.State.Tier(0, Firepower));
        Assert.Equal(1, match.Slot(0).TicksLeft);
        Assert.Single(events);

        match.Simulation.Tick();
        Assert.Equal(7 + TicksToTier2, match.World.Tick);
        Assert.Equal([2, 1, 1, 1, 1, 1], match.Research.State.Tiers(0).ToArray());
        Assert.Equal([1, 1, 1, 1, 1, 1], match.Research.State.Tiers(1).ToArray());
        Assert.True(match.Slot(0).IsFree);
        Assert.Equal(new ResearchEvent(ResearchEventKind.Finished, 7 + TicksToTier2 - 1, 0, Firepower, 2, 1), events[^1]);
        Assert.Equal(2, events.Count);

        // Tiers are permanent and the price is spent.
        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(0));
        Assert.Equal(3, match.Research.Tech(0).Research[Firepower].Tier);
    }

    [Fact]
    public void ThePlayersTableIsReplacedAndNobodyElsesIs()
    {
        var match = Create();
        var (mine, theirs) = (match.Research.Tech(0), match.Research.Tech(1));
        match.OrderResearch(0, Logistics);

        match.Simulation.Run(TicksToTier2 - 1);
        Assert.Same(mine, match.Research.Tech(0));
        match.Simulation.Tick();

        Assert.NotSame(mine, match.Research.Tech(0));
        Assert.Same(theirs, match.Research.Tech(1));
        TechAssert.Same(TechResolver.Resolve(DesignData.Data, DesignData.Mix(1, 1, 1, 1, 2, 1)), match.Research.Tech(0));
        Assert.Equal((55, 70), (mine.SupplyCap, match.Research.Tech(0).SupplyCap));

        // The old table is not changed: whoever still holds it sees the player as they were.
        Assert.Equal(1, mine.Tiers[Logistics]);
    }

    /// <summary>
    /// The plan's scenario: two players with different mixes get different stats for the same
    /// chassis. One researches quality and the other quantity, and each then has their own
    /// Hoplite (130 hit points, 8 damage, 70 Materials and 10 Energy, 18 seconds at tier 1).
    /// </summary>
    [Fact]
    public void TwoPlayersWithDifferentMixesGetDifferentStatsForTheSameChassis()
    {
        var match = Create();
        match.Research.OpenSlot(0, match.PlaceHolder(0));
        var hoplite = DesignData.Chassis("hoplite").Index;
        var start = match.Research.Tech(0).Blueprints[hoplite];
        Assert.Equal((130_000, 8_000, 70, 10, 360), (start.HitPoints, start.DamagePerSecond, start.Materials, start.Energy, start.BuildTicks));

        match.OrderResearch(0, Firepower);
        match.OrderResearch(0, Protection);
        match.OrderResearch(1, Manufacturing);
        match.Simulation.Run(TicksToTier2);

        var elite = match.Research.Tech(0).Blueprints[hoplite];
        var mass = match.Research.Tech(1).Blueprints[hoplite];
        Assert.Equal((162_500, 10_000, 93, 13, 360, 1), (elite.HitPoints, elite.DamagePerSecond, elite.Materials, elite.Energy, elite.BuildTicks, elite.BatchSize));
        Assert.Equal((130_000, 8_000, 63, 9, 288, 2), (mass.HitPoints, mass.DamagePerSecond, mass.Materials, mass.Energy, mass.BuildTicks, mass.BatchSize));
        Assert.Equal([1, 1], elite.Accents.Select(accent => accent.Level));
        Assert.Equal([new Accent(Manufacturing, 1)], mass.Accents);
    }

    /// <summary>
    /// Design 1.4: the epoch is the highest tier three tracks have reached, and its change is
    /// announced. Three researches that finish in one tick finish in the order of their slots.
    /// </summary>
    [Fact]
    public void TheEpochRisesWithTheThirdTrackAndIsReportedOnce()
    {
        var match = Create();
        match.Research.OpenSlot(0, match.PlaceHolder(0, 3, 3));
        match.Research.OpenSlot(0, match.PlaceHolder(0, 5, 3));
        var events = match.Events();

        match.OrderResearch(0, Mobility);
        match.OrderResearch(0, Extraction);
        match.OrderResearch(0, Firepower);
        match.Simulation.Run(TicksToTier2);

        var last = TicksToTier2 - 1;
        Assert.Equal(
            [
                new ResearchEvent(ResearchEventKind.Started, 0, 0, Mobility, 2, 1),
                new ResearchEvent(ResearchEventKind.Started, 0, 0, Extraction, 2, 1),
                new ResearchEvent(ResearchEventKind.Started, 0, 0, Firepower, 2, 1),
                new ResearchEvent(ResearchEventKind.Finished, last, 0, Mobility, 2, 1),
                new ResearchEvent(ResearchEventKind.Finished, last, 0, Extraction, 2, 1),
                new ResearchEvent(ResearchEventKind.Finished, last, 0, Firepower, 2, 2),
                new ResearchEvent(ResearchEventKind.EpochChanged, last, 0, Firepower, 2, 2),
            ],
            events);
        Assert.Equal((2, 1), (match.Research.Tech(0).Epoch, match.Research.Tech(1).Epoch));

        // A fourth track at the same tier is breadth, not a new epoch.
        match.OrderResearch(0, Protection);
        match.Simulation.Run(TicksToTier2 / 2);
        Assert.Equal(new ResearchEvent(ResearchEventKind.Finished, last + (TicksToTier2 / 2), 0, Protection, 2, 2), events[^1]);
        Assert.Equal(9, events.Count);
    }

    /// <summary>Design 1.3 and 1.4: once the epoch has risen, the tracks left behind cost half and take half as long.</summary>
    [Fact]
    public void AfterTheEpochRisesTheNextResearchIsPricedInItsNewBand()
    {
        var match = Create();
        match.Research.SetTiers(0, DesignData.Mix(2, 2, 1, 1, 1, 1));
        Assert.Equal(ResearchBand.Frontier, match.Research.Tech(0).Research[Logistics].Price!.Value.Band);

        match.OrderResearch(0, Mobility);
        match.Simulation.Run(TicksToTier2);

        Assert.Equal(new ResearchPrice(ResearchBand.CatchUp, 50, 50, 450), match.Research.Tech(0).Research[Logistics].Price);
        Assert.Equal(ResearchStatus.Available, match.Research.Tech(0).Research[Firepower].Status);
    }

    [Fact]
    public void WhoListensChangesNothing()
    {
        var quiet = Create();
        var heard = Create();
        heard.Events();
        foreach (var match in new[] { quiet, heard })
        {
            match.OrderResearch(0, Firepower);
            match.OrderResearch(1, Protection);
            match.Simulation.Run(500);
            match.OrderCancel(1, Protection);
            match.Simulation.Run(500);
        }

        Assert.Equal(quiet.World.ComputeHash(), heard.World.ComputeHash());
    }

    /// <summary>
    /// Design 2.3: "when a research finishes, every existing unit updates", and a unit whose
    /// maximum hit points change keeps the same fraction of them. The store of hit points is not
    /// built yet, so this test brings one of its own, and brings it up to date through the
    /// handler the real one will use.
    /// </summary>
    [Fact]
    public void UnitsOnTheMapKeepTheirFractionOfHitPointsWhenProtectionIsResearched()
    {
        var match = Create();
        var health = match.World.AddStore(new ComponentStore<TestHealth>("test-health"));
        match.Research.AddTechChangeHandler(new HealthRefresh(match.Bodies, health));
        var hoplite = DesignData.Chassis("hoplite").Index;
        var whole = Place(match, health, 0, 3, hoplite, 130_000);
        var hurt = Place(match, health, 0, 5, hoplite, 52_000);
        var nearlyDead = Place(match, health, 0, 7, hoplite, 1);
        var theirs = Place(match, health, 1, 9, hoplite, 52_000);

        match.OrderResearch(0, Firepower);
        match.Simulation.Run(TicksToTier2);
        Assert.Equal((130_000, 52_000), (health.Get(whole).HitPoints, health.Get(hurt).HitPoints));

        match.OrderResearch(0, Protection);
        match.Simulation.Run(TicksToTier2);

        Assert.Equal(162_500, match.Research.Tech(0).Blueprints[hoplite].HitPoints);
        Assert.Equal((162_500, 162_500), (health.Get(whole).HitPoints, health.Get(whole).Maximum));
        Assert.Equal((65_000, 162_500), (health.Get(hurt).HitPoints, health.Get(hurt).Maximum));
        Assert.Equal((1, 162_500), (health.Get(nearlyDead).HitPoints, health.Get(nearlyDead).Maximum));
        Assert.Equal((52_000, 130_000), (health.Get(theirs).HitPoints, health.Get(theirs).Maximum));
    }

    [Fact]
    public void HandlersRunInTheOrderTheyWereAddedOnceForEachFinishedResearch()
    {
        var match = Create();
        match.Research.OpenSlot(0, match.PlaceHolder(0));
        var calls = new List<string>();
        match.Research.AddTechChangeHandler(new Recorder("first", calls, match));
        match.Research.AddTechChangeHandler(new Recorder("second", calls, match));
        match.Events().Clear();
        match.Research.Reported += reported => calls.Add($"reported {reported.Kind}");

        match.OrderResearch(0, Firepower);
        match.OrderResearch(0, Protection);
        match.OrderResearch(1, Mobility);
        match.Simulation.Tick();
        calls.Clear();
        match.OrderCancel(1, Mobility);
        match.Simulation.Run(TicksToTier2 - 1);

        Assert.Equal(
            [
                "reported Cancelled",
                "first: player 0, 1/1/1/1/1/1 -> 2/1/1/1/1/1 in tick 899",
                "second: player 0, 1/1/1/1/1/1 -> 2/1/1/1/1/1 in tick 899",
                "reported Finished",
                "first: player 0, 2/1/1/1/1/1 -> 2/2/1/1/1/1 in tick 899",
                "second: player 0, 2/1/1/1/1/1 -> 2/2/1/1/1/1 in tick 899",
                "reported Finished",
            ],
            calls);
    }

    [Fact]
    public void HandlersAreAddedBeforeTheFirstTickOnly()
    {
        var match = Create();
        match.Simulation.Tick();

        Assert.Throws<InvalidOperationException>(() => match.Research.AddTechChangeHandler(new Recorder("late", [], match)));
        Assert.Throws<ArgumentNullException>(() => Create().Research.AddTechChangeHandler(null!));
    }

    private static EntityId Place(Match match, ComponentStore<TestHealth> health, int player, int tileX, int chassis, long hitPoints)
    {
        var unit = match.SpawnUnit(player, MapGrid.TileCenter(tileX, 10), Fixed.Zero);
        health.Add(unit, new TestHealth { Chassis = chassis, HitPoints = hitPoints, Maximum = match.Research.Tech(player).Blueprints[chassis].HitPoints });
        return unit;
    }

    private struct TestHealth : IStateValue
    {
        public int Chassis;
        public long HitPoints;
        public long Maximum;

        public void Walk(IStateWalker walker)
        {
            Chassis = walker.Field(Chassis);
            HitPoints = walker.Field(HitPoints);
            Maximum = walker.Field(Maximum);
        }
    }

    /// <summary>What the store of hit points will do when a research finishes.</summary>
    private sealed class HealthRefresh(ComponentStore<Body> bodies, ComponentStore<TestHealth> health) : ITechChangeHandler
    {
        public void TechChanged(World world, int player, PlayerTech before, PlayerTech after)
        {
            for (var i = 0; i < health.Count; i++)
            {
                if (bodies.Get(health.EntityAt(i)).Owner != player)
                {
                    continue;
                }

                ref var unit = ref health.ValueAt(i);
                var maximum = after.Blueprints[unit.Chassis].HitPoints;
                unit.HitPoints = HitPointRules.KeepFraction(unit.HitPoints, unit.Maximum, maximum);
                unit.Maximum = maximum;
            }
        }
    }

    private sealed class Recorder(string name, List<string> calls, Match match) : ITechChangeHandler
    {
        public void TechChanged(World world, int player, PlayerTech before, PlayerTech after)
        {
            Assert.Same(after, match.Research.Tech(player));
            Assert.Same(match.World, world);
            calls.Add($"{name}: player {player}, {string.Join('/', before.Tiers)} -> {string.Join('/', after.Tiers)} in tick {world.Tick}");
        }
    }
}
