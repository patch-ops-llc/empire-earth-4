using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Production;
using Sim.Setup;
using Sim.Tech;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Perks;

/// <summary>
/// Reserves (design 2.3): "The refund is 25% of the Materials actually paid for that unit,
/// rounded half up. The unit must carry Reserves at the moment it dies." And the third
/// consequence of a displaced accent: "A unit that loses Reserves no longer refunds on death,
/// and one that gains it refunds from the price stored when it was built."
/// </summary>
/// <remarks>
/// A chassis of epoch 1 is Standardized II, which is Batch and Reserves, for a player with
/// Manufacturing at tier 3. So a unit with Reserves is normally one of a pair, and what was paid
/// for it is half the pair's price.
/// </remarks>
public class ReservesTests
{
    private static readonly int Firepower = DesignData.Track("firepower");
    private static readonly int Protection = DesignData.Track("protection");
    private static readonly int Manufacturing = DesignData.Track("manufacturing");

    private static readonly int Peltast = Chassis("peltast");
    private static readonly int Hoplite = Chassis("hoplite");

    /// <summary>Standardized II alone: a Peltast costs 32 and a Hoplite 57.</summary>
    private static readonly int[] Standardized = DesignData.Mix(1, 1, 1, 3, 1, 1);

    /// <summary>Overgunned II and Standardized II, with Protection one ahead and out of slots: a Peltast costs 49 and a Hoplite 86.</summary>
    private static readonly int[] BeforeReservesGoes = DesignData.Mix(3, 2, 1, 3, 1, 1);

    /// <summary>Overgunned I and Hardened I, with Manufacturing one ahead and out of slots: a Peltast costs 48, one at a time.</summary>
    private static readonly int[] BeforeReservesComes = DesignData.Mix(2, 2, 1, 2, 1, 1);

    /// <summary>A Barracks for player 0, who has these tiers and more than the test spends.</summary>
    private static Match WithABarracks(int[] tiers, out EntityId barracks)
    {
        var match = Create(TestMaps.Open(24, 24));
        match.Research.SetTiers(0, tiers);
        barracks = match.Place(0, "barracks", 10, 10);
        match.MakeRich(0);
        return match;
    }

    /// <summary>The same with a Capitol and a research under way.</summary>
    private static Match Researching(int[] tiers, string track, out EntityId barracks)
    {
        var match = Create(TestMaps.Open(24, 24));
        match.Research.SetTiers(0, tiers);
        match.Place(0, "capitol", 2, 2);
        barracks = match.Place(0, "barracks", 10, 10);
        match.MakeRich(0);
        match.OrderResearch(0, track);
        match.Simulation.Tick();
        Assert.True(match.Research.State.Slot(0, 0).IsBusy);
        return match;
    }

    /// <summary>Orders a chassis at the Barracks and runs until its units appear.</summary>
    private static EntityId[] Train(Match match, EntityId barracks, int chassis)
    {
        match.OrderTrain(0, barracks, chassis);
        match.RunUntilAppeared();
        return match.Appeared();
    }

    private static SimEvent[] Refunds(Match match) =>
        [.. match.Simulation.Events.Where(simEvent => simEvent.Kind == SimEventKind.ReservesRefunded)];

    private static Fraction PaidFor(Match match, EntityId unit) => match.PerkCounts.Get(unit).Paid;

    [Fact]
    public void TheRefundIsAQuarterOfTheMaterialsPaidForThatUnit()
    {
        var match = WithABarracks(Standardized, out var barracks);
        var before = match.Stock(0);
        var pair = Train(match, barracks, Peltast);

        // A pair of Peltasts at 32 each: 64 paid, 32 for each of the two.
        Assert.Equal(2, pair.Length);
        Assert.Equal(before.Materials - 64, match.Stock(0).Materials);
        Assert.All(pair, unit => Assert.Equal(new Fraction(32, 1), PaidFor(match, unit)));
        var position = match.Bodies.Get(pair[0]).Position;

        match.Kill([pair[0]]);
        match.Simulation.Tick();

        // 25% of 32 is 8, in the stockpile in the tick the unit dies.
        Assert.False(match.World.Entities.IsAlive(pair[0]));
        Assert.Equal((before.Materials - 64 + 8, before.Energy), match.Stock(0));
        var refund = Assert.Single(Refunds(match));
        Assert.Equal((0, pair[0], position, 8L), (refund.Player, refund.Entity, refund.Position, refund.Amount));

        // It comes after the unit's death in the events of the tick.
        var kinds = match.Simulation.Events.Select(simEvent => simEvent.Kind).ToList();
        Assert.True(kinds.IndexOf(SimEventKind.Death) < kinds.IndexOf(SimEventKind.ReservesRefunded));

        // Once: the next tick gives nothing more, and the other of the pair gives its own when it dies.
        match.Simulation.Tick();
        Assert.Empty(Refunds(match));
        match.Kill([pair[1]]);
        match.Simulation.Tick();
        Assert.Equal(pair[1], Assert.Single(Refunds(match)).Entity);
        Assert.Equal(before.Materials - 64 + 16, match.Stock(0).Materials);
    }

    /// <summary>
    /// "Rounded half up", on what was actually paid: 25% of a Hoplite at 86 is 21.5, which gives
    /// 22, and of one at 57 it is 14.25, which gives 14.
    /// </summary>
    [Theory]
    [InlineData(true, 86, 22)]
    [InlineData(false, 57, 14)]
    public void TheRefundIsRoundedHalfUp(bool overgunned, int price, int refund)
    {
        var match = WithABarracks(overgunned ? BeforeReservesGoes : Standardized, out var barracks);
        var pair = Train(match, barracks, Hoplite);
        Assert.All(pair, unit => Assert.Equal(new Fraction(price, 1), PaidFor(match, unit)));
        var before = match.Stock(0);

        match.Kill(pair);
        match.Simulation.Tick();

        // Each unit's refund is rounded by itself: two of 22 are 44, not 2 x 21.5 = 43.
        Assert.Equal(before.Materials + (2 * refund), match.Stock(0).Materials);
        Assert.Equal([refund, refund], Refunds(match).Select(simEvent => simEvent.Amount));
        Assert.Equal(pair, Refunds(match).Select(simEvent => simEvent.Entity));
    }

    /// <summary>The rule on any price: the share of what is stored, rounded half up to whole Materials, and no event for nothing.</summary>
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 0)]
    [InlineData("1.99", 0)]
    [InlineData("2", 1)]
    [InlineData("5.99", 1)]
    [InlineData("6", 2)]
    [InlineData("37.5", 9)]
    [InlineData("49", 12)]
    [InlineData("50", 13)]
    [InlineData("51", 13)]
    public void TheRefundFollowsFromThePriceStored(string paid, int refund)
    {
        var match = WithABarracks(Standardized, out _);
        var unit = match.SpawnMany(0, Peltast, 1)[0];
        match.PerkCounts.Get(unit).Paid = Fraction.Parse(paid);
        var before = match.Stock(0);

        match.Kill([unit]);
        match.Simulation.Tick();

        Assert.Equal(before.Materials + refund, match.Stock(0).Materials);
        Assert.Equal(refund > 0 ? [refund] : [], Refunds(match).Select(simEvent => simEvent.Amount));
    }

    /// <summary>A unit nobody paid for, such as one from the development spawn, gives nothing back.</summary>
    [Fact]
    public void AUnitNobodyPaidForRefundsNothing()
    {
        var match = WithABarracks(Standardized, out _);
        var unit = match.SpawnMany(0, Peltast, 1)[0];
        Assert.Equal(Fraction.Zero, PaidFor(match, unit));
        var before = match.Stock(0);

        match.Kill([unit]);
        match.Simulation.Tick();

        Assert.False(match.World.Entities.IsAlive(unit));
        Assert.Equal(before, match.Stock(0));
        Assert.Empty(Refunds(match));
    }

    /// <summary>
    /// Every unit of a chassis keeps what was paid for it, with the perk or without: a single
    /// unit its whole price. Without Reserves its death gives nothing. A worker keeps nothing.
    /// </summary>
    [Fact]
    public void AUnitWithoutReservesKeepsItsPriceAndRefundsNothing()
    {
        var match = WithABarracks(DesignData.Mix(1, 1, 1, 1, 1, 1), out var barracks);
        var capitol = match.Place(0, "capitol", 2, 2);
        var unit = Assert.Single(Train(match, barracks, Peltast));
        Assert.Equal(new Fraction(40, 1), PaidFor(match, unit));
        match.OrderTrain(0, capitol, AWorker);
        match.RunUntilAppeared();
        Assert.False(match.PerkCounts.Contains(Assert.Single(match.Appeared())));
        var before = match.Stock(0);

        match.Kill([unit]);
        match.Simulation.Tick();

        Assert.Equal(before, match.Stock(0));
        Assert.Empty(Refunds(match));
    }

    /// <summary>The refund goes to the unit's owner, when an enemy kills it in a fight.</summary>
    [Fact]
    public void AUnitKilledByAnEnemyRefundsItsOwner()
    {
        var match = WithABarracks(Standardized, out var barracks);
        var pair = Train(match, barracks, Peltast);
        var before = (match.Stock(0), match.Stock(1));
        var position = match.Bodies.Get(pair[0]).Position;
        FixedVector2[] beside = [new(position.X, position.Y + Fixed.One), new(position.X, position.Y - Fixed.One)];
        var enemy = match.SpawnChassis(1, Hoplite, beside.First(match.Grid.IsPassable));
        match.Vision.Refresh();
        match.Health.Get(pair[0]).HitPoints = 1;
        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.Attack, 1, [enemy], targetEntity: pair[0]));

        match.Simulation.Tick();

        Assert.False(match.World.Entities.IsAlive(pair[0]));
        Assert.Equal(((before.Item1.Materials + 8, before.Item1.Energy), before.Item2), (match.Stock(0), match.Stock(1)));
        Assert.Equal(0, Assert.Single(Refunds(match)).Player);
    }

    /// <summary>
    /// "A unit that loses Reserves no longer refunds on death." Protection 3 takes the slot of
    /// Standardized on a Peltast that is Overgunned II and Standardized II: of a pair built
    /// before, the one that dies before the research finishes refunds, the other does not.
    /// </summary>
    [Fact]
    public void AUnitThatLosesReservesNoLongerRefundsOnDeath()
    {
        var match = Researching(BeforeReservesGoes, "protection", out var barracks);
        var preview = TechResolver.Preview(match.Data, BeforeReservesGoes, Protection)!;
        Assert.Equal([new Accent(Firepower, 2), new Accent(Manufacturing, 2)], match.BlueprintOf(0, Peltast).Accents);
        Assert.Contains(new AccentChange(Peltast, Manufacturing, 2, 0), preview.AccentChanges);
        Assert.Contains(PerkKind.DeathRefund, match.BlueprintOf(0, Peltast).Perks.Kinds);
        var pair = Train(match, barracks, Peltast);
        Assert.All(pair, unit => Assert.Equal(new Fraction(49, 1), PaidFor(match, unit)));

        // The research finishes in the next tick but one. A death in the tick before it still refunds: 25% of 49 is 12.25.
        match.RunUntilResearchHas(0, ticksLeft: 2);
        var before = match.Stock(0);
        match.Kill([pair[0]]);
        match.Simulation.Tick();
        Assert.Equal(12, Assert.Single(Refunds(match)).Amount);
        Assert.Equal(before.Materials + 12, match.Stock(0).Materials);

        // A death in the tick the research finishes in does too: the new tier is in effect from the next tick.
        var third = match.SpawnMany(0, Peltast, 1)[0];
        match.PerkCounts.Get(third).Paid = new Fraction(49, 1);
        match.Kill([third]);
        match.RunUntilResearchHas(0, ticksLeft: 0);
        Assert.Equal(12, Assert.Single(Refunds(match)).Amount);

        // Reserves is gone, and the price stored with the unit that still stands is not.
        Assert.Equal([new Accent(Firepower, 2), new Accent(Protection, 2)], match.BlueprintOf(0, Peltast).Accents);
        Assert.DoesNotContain(PerkKind.DeathRefund, match.BlueprintOf(0, Peltast).Perks.Kinds);
        Assert.Equal(new Fraction(49, 1), PaidFor(match, pair[1]));
        before = match.Stock(0);

        match.Kill([pair[1]]);
        match.Simulation.Tick();

        Assert.False(match.World.Entities.IsAlive(pair[1]));
        Assert.Equal(before, match.Stock(0));
        Assert.Empty(Refunds(match));
    }

    /// <summary>
    /// "One that gains it refunds from the price stored when it was built." Manufacturing 3
    /// takes the slot of Hardened on a Peltast that is Overgunned I and Hardened I. One built
    /// before at 48 refunds 12 when it dies after; one built after, as one of a pair at 43
    /// each, refunds 25% of 43, which is 10.75 and gives 11.
    /// </summary>
    [Fact]
    public void AUnitThatGainsReservesRefundsFromThePriceStoredWhenItWasBuilt()
    {
        var match = Researching(BeforeReservesComes, "manufacturing", out var barracks);
        var preview = TechResolver.Preview(match.Data, BeforeReservesComes, Manufacturing)!;
        Assert.Equal([new Accent(Firepower, 1), new Accent(Protection, 1)], match.BlueprintOf(0, Peltast).Accents);
        Assert.Contains(new AccentChange(Peltast, Protection, 1, 0), preview.AccentChanges);
        Assert.Contains(new AccentChange(Peltast, Manufacturing, 0, 2), preview.AccentChanges);

        var early = Train(match, barracks, Peltast);
        var doomed = Train(match, barracks, Peltast);
        var old = Assert.Single(early);
        Assert.Equal(new Fraction(48, 1), PaidFor(match, old));

        // Without Reserves a death gives nothing.
        var before = match.Stock(0);
        match.Kill(doomed);
        match.Simulation.Tick();
        Assert.Equal(before, match.Stock(0));
        Assert.Empty(Refunds(match));

        match.RunUntilResearchHas(0, ticksLeft: 0);

        Assert.Equal([new Accent(Manufacturing, 2), new Accent(Firepower, 1)], match.BlueprintOf(0, Peltast).Accents);
        Assert.Equal((43, 2), (match.BlueprintOf(0, Peltast).Materials, match.BlueprintOf(0, Peltast).BatchSize));
        var pair = Train(match, barracks, Peltast);
        Assert.Equal(2, pair.Length);
        Assert.All(pair, unit => Assert.Equal(new Fraction(43, 1), PaidFor(match, unit)));
        Assert.Equal(new Fraction(48, 1), PaidFor(match, old));
        before = match.Stock(0);

        match.Kill([old, pair[0]]);
        match.Simulation.Tick();

        Assert.Equal([(old, 12L), (pair[0], 11L)], Refunds(match).Select(simEvent => (simEvent.Entity, simEvent.Amount)));
        Assert.Equal(before.Materials + 23, match.Stock(0).Materials);
    }

    /// <summary>
    /// The refund is for a unit that dies. One that leaves the match in another way, with hit
    /// points left, gives nothing; no rule does that to a unit today, and this keeps it so for
    /// the one that will.
    /// </summary>
    [Fact]
    public void AUnitThatIsTakenAwayWithHitPointsLeftRefundsNothing()
    {
        var match = WithABarracks(Standardized, out var barracks);
        var pair = Train(match, barracks, Peltast);
        var before = match.Stock(0);

        match.World.Entities.Destroy(pair[0]);
        match.Simulation.Tick();

        Assert.False(match.PerkCounts.Contains(pair[0]));
        Assert.Equal(before, match.Stock(0));
        Assert.Empty(Refunds(match));

        // Its partner dies as units do, and refunds.
        match.Kill([pair[1]]);
        match.Simulation.Tick();
        Assert.Equal(8L, Assert.Single(Refunds(match)).Amount);
    }
}
