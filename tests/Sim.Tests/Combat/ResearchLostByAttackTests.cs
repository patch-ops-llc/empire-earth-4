using Sim.Core;
using Sim.Research;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Research;
using static Sim.Tests.Combat.SiegeMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// Design 1.3 and 2.5, where research meets structures in combat: a research whose slot is lost
/// because its Academy, or its player's last Capitol, falls to an attack is cancelled with its
/// whole price back, in the tick the structure falls.
/// </summary>
/// <remarks>
/// <para>
/// <c>FinishedStructureTests</c> has the same for a structure whose hit points a test sets to
/// zero. Here the last hit points are taken by a unit of another player, so the blow, the fall and
/// the refund must come in that order within one tick: the attacks, then the deaths, then the
/// slot closed, by <see cref="StructureSystem"/> for a Capitol and by the research of the tick for
/// an Academy.
/// </para>
/// <para>
/// Player 0 has a Capitol on tiles 4 to 7 by 8 to 11 and an Academy on tiles 18 and 19 by 10 and
/// 11. It researches Firepower, which takes the Capitol's slot, and then Protection, which takes
/// the Academy's. A unit of player 1 has stood in reach or in range of the structure since the
/// start, is ordered to attack it when it has one hit point left, and destroys it with its next
/// blow: a Hoplite's from beside it, or a Peltast's shot from 4.5 tiles off.
/// </para>
/// </remarks>
public class ResearchLostByAttackTests
{
    private const int TicksBeforeTheAttack = 100;

    private sealed record Siege(
        Match Match,
        EntityId Capitol,
        EntityId Academy,
        (long Materials, long Energy) FirepowerPrice,
        (long Materials, long Energy) ProtectionPrice,
        (long Materials, long Energy) Stock,
        int FirepowerOrdered);

    private static (long Materials, long Energy) StockOf(Match match, int player) =>
        (match.Stockpiles.Materials(player), match.Stockpiles.Energy(player));

    /// <summary>The match of the remarks, 100 ticks into both researches.</summary>
    private static Siege BothResearching()
    {
        var match = Field();
        var capitol = match.PlaceStructure(0, Capitol, 4, 8);
        var academy = match.PlaceStructure(0, Academy, 18, 10);
        match.Stockpiles.Add(0, 5_000, 5_000);
        var start = StockOf(match, 0);

        var firepowerOrdered = match.World.Tick;
        match.OrderResearch(0, ResearchMatches.Firepower);
        match.Simulation.Tick();
        var afterFirepower = StockOf(match, 0);
        match.OrderResearch(0, ResearchMatches.Protection);
        match.Simulation.Tick();
        var afterBoth = StockOf(match, 0);

        Assert.Equal((true, ResearchMatches.Firepower, EntityId.None), (match.Slot(0, 0).IsBusy, match.Slot(0, 0).Track, match.Slot(0, 0).Holder));
        Assert.Equal((true, ResearchMatches.Protection, academy), (match.Slot(0, 1).IsBusy, match.Slot(0, 1).Track, match.Slot(0, 1).Holder));
        var firepowerPrice = (start.Materials - afterFirepower.Materials, start.Energy - afterFirepower.Energy);
        var protectionPrice = (afterFirepower.Materials - afterBoth.Materials, afterFirepower.Energy - afterBoth.Energy);
        Assert.True(firepowerPrice.Item1 > 0 && protectionPrice.Item1 > 0, "Both researches cost Materials.");

        match.Simulation.Run(TicksBeforeTheAttack);
        Assert.Equal(afterBoth, StockOf(match, 0));
        return new Siege(match, capitol, academy, firepowerPrice, protectionPrice, afterBoth, firepowerOrdered);
    }

    /// <summary>
    /// Leaves the structure one hit point, orders the attack and runs to the tick in which it
    /// falls. While it stands, nothing of the research changes.
    /// </summary>
    /// <returns>What research reported in the tick of the fall, each with whether the fall had been reported before it.</returns>
    private static List<(ResearchEvent Event, bool AfterTheFall)> RunToTheFall(Siege siege, EntityId structure, EntityId attacker, SimEventKind blow)
    {
        var match = siege.Match;
        var reported = new List<(ResearchEvent Event, bool AfterTheFall)>();
        match.Research.Reported += happened => reported.Add(
            (happened, match.Simulation.Events.Any(simEvent => simEvent.Kind == SimEventKind.Death && simEvent.Entity == structure)));

        Assert.True(match.IsAlive(attacker));
        match.SetHitPoints(structure, 1_000);
        match.OrderAttack(1, structure, attacker);
        var fell = false;
        for (var tick = 0; tick < 60 && !fell; tick++)
        {
            match.Simulation.Tick();
            fell = match.EventsOf(SimEventKind.Death).Length > 0;
            if (!fell)
            {
                Assert.Empty(reported);
                Assert.Equal(siege.Stock, StockOf(match, 0));
                Assert.True(match.Slot(0, 0).IsBusy && match.Slot(0, 1).IsBusy);
            }
        }

        Assert.True(fell, "The structure still stands 60 ticks after the order to attack it.");

        // The blow and the fall, in that order, in the tick whose research events are returned.
        var kinds = match.Simulation.Events
            .Where(simEvent => simEvent.Kind == blow || simEvent.Kind == SimEventKind.Death)
            .Select(simEvent => (simEvent.Kind, simEvent.Entity))
            .ToArray();
        Assert.Equal([(blow, structure), (SimEventKind.Death, structure)], kinds);
        Assert.False(match.IsAlive(structure));
        return reported;
    }

    [Theory]
    [InlineData(Hoplite, "17.5", "10.5", SimEventKind.MeleeHit)]
    [InlineData(Peltast, "13.5", "10.5", SimEventKind.ShotHit)]
    public void AnAcademyThatFallsToAnAttackGivesBackItsResearchInTheTickItFalls(string chassis, string x, string y, SimEventKind blow)
    {
        var siege = BothResearching();
        var match = siege.Match;
        var attacker = match.Unit(1, chassis, x, y);

        var reported = RunToTheFall(siege, siege.Academy, attacker, blow);
        var fellIn = match.World.Tick - 1;

        // The Academy's research is lost, after the fall, and its whole price is back.
        Assert.Equal(
            [(new ResearchEvent(ResearchEventKind.Lost, fellIn, 0, ResearchMatches.Protection, 2, match.Research.Tech(0).Epoch), true)],
            reported);
        Assert.Equal(
            (siege.Stock.Materials + siege.ProtectionPrice.Materials, siege.Stock.Energy + siege.ProtectionPrice.Energy),
            StockOf(match, 0));
        Assert.Equal((false, false), (match.Slot(0, 1).IsOpen, match.Slot(0, 1).IsBusy));
        Assert.Equal(1, match.Research.State.Tier(0, ResearchMatches.Protection));
        Assert.Equal((0, 0), StockOf(match, 1));

        // The Capitol's research is untouched and finishes when it would have.
        Assert.Equal((true, ResearchMatches.Firepower), (match.Slot(0, 0).IsBusy, match.Slot(0, 0).Track));
        match.Simulation.Run(siege.FirepowerOrdered + ResearchMatches.TicksToTier2 - 1 - match.World.Tick);
        Assert.Equal(1, match.Research.State.Tier(0, ResearchMatches.Firepower));
        match.Simulation.Tick();
        Assert.Equal(2, match.Research.State.Tier(0, ResearchMatches.Firepower));

        // It is given back once.
        Assert.Equal(
            (siege.Stock.Materials + siege.ProtectionPrice.Materials, siege.Stock.Energy + siege.ProtectionPrice.Energy),
            StockOf(match, 0));
        Assert.Single(reported, entry => entry.Event.Kind == ResearchEventKind.Lost);
    }

    [Theory]
    [InlineData(Hoplite, "8.5", "9.5", SimEventKind.MeleeHit)]
    [InlineData(Peltast, "12.5", "9.5", SimEventKind.ShotHit)]
    public void TheLastCapitolThatFallsToAnAttackGivesBackItsResearchInTheTickItFalls(string chassis, string x, string y, SimEventKind blow)
    {
        var siege = BothResearching();
        var match = siege.Match;
        var attacker = match.Unit(1, chassis, x, y);

        var reported = RunToTheFall(siege, siege.Capitol, attacker, blow);
        var fellIn = match.World.Tick - 1;

        // The research in the Capitol's slot is lost, after the fall, and its whole price is back.
        Assert.Equal(
            [(new ResearchEvent(ResearchEventKind.Lost, fellIn, 0, ResearchMatches.Firepower, 2, match.Research.Tech(0).Epoch), true)],
            reported);
        Assert.Equal(
            (siege.Stock.Materials + siege.FirepowerPrice.Materials, siege.Stock.Energy + siege.FirepowerPrice.Energy),
            StockOf(match, 0));
        Assert.Equal((false, false), (match.Slot(0, 0).IsOpen, match.Slot(0, 0).IsBusy));
        Assert.Equal((0, 0), StockOf(match, 1));

        // The Academy stands, and its research goes on to its end. Firepower is not researched.
        Assert.Equal((true, ResearchMatches.Protection), (match.Slot(0, 1).IsBusy, match.Slot(0, 1).Track));
        match.Simulation.Run(ResearchMatches.TicksToTier2);
        Assert.Equal((1, 2), (match.Research.State.Tier(0, ResearchMatches.Firepower), match.Research.State.Tier(0, ResearchMatches.Protection)));
        Assert.Single(reported, entry => entry.Event.Kind == ResearchEventKind.Lost);
    }

    /// <summary>Design 1.3: the slot is the player's for as long as they have a finished Capitol, so one of two may fall.</summary>
    [Fact]
    public void ACapitolThatFallsToAnAttackWhileAnotherStandsLosesNoResearch()
    {
        var siege = BothResearching();
        var match = siege.Match;
        match.PlaceStructure(0, Capitol, 4, 16);
        var attacker = match.Unit(1, Hoplite, "8.5", "9.5");

        var reported = RunToTheFall(siege, siege.Capitol, attacker, SimEventKind.MeleeHit);

        Assert.Empty(reported);
        Assert.Equal(siege.Stock, StockOf(match, 0));
        Assert.True(match.Slot(0, 0).IsBusy && match.Slot(0, 1).IsBusy);
        match.Simulation.Run(ResearchMatches.TicksToTier2);
        Assert.Equal((2, 2), (match.Research.State.Tier(0, ResearchMatches.Firepower), match.Research.State.Tier(0, ResearchMatches.Protection)));
    }
}
