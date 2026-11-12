using Sim.Core;
using Sim.Map;

namespace Ai.Tests;

/// <summary>
/// How the computer player orders a structure, and what it does when an order came to nothing.
/// </summary>
public class AiConstructionTests
{
    [Fact]
    public void AnOrderGoesToEveryWorkerAndIsPaidForAndPlanned()
    {
        var fixture = new AiFixture();
        var construction = new AiConstruction(fixture.Sight);
        var barracks = AiFixture.Structure("barracks");
        var (x, y) = SiteFor(fixture, construction, "barracks");
        var budget = fixture.With(barracks.Materials + 10, barracks.Energy + 5);

        Assert.True(construction.CanOrder(barracks, budget));
        Assert.True(construction.Order(barracks, x, y, budget, fixture.Commands));

        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.Build, order.Type);
        Assert.Equal(AiFixture.Player, order.Player);
        Assert.Equal(barracks.Index, order.ContentId);
        Assert.Equal(MapGrid.TileCenter(x, y).X, order.TargetX);
        Assert.Equal(MapGrid.TileCenter(x, y).Y, order.TargetY);
        Assert.Equal(fixture.Sight.Workers.Select(worker => worker.Entity).ToArray(), order.Entities.ToArray());

        Assert.True(budget.Covers(10, 5));
        Assert.False(budget.Covers(11, 0));
        Assert.Equal(1, construction.UnderWayCount(barracks.Index));

        // The round's next site keeps clear of this one.
        Assert.False(construction.Sites.IsGood(barracks, x, y));
    }

    [Fact]
    public void TheMatchCarriesTheOrderOutWhereItWasPlaced()
    {
        var fixture = new AiFixture();
        var construction = new AiConstruction(fixture.Sight);
        var barracks = AiFixture.Structure("barracks");
        var (x, y) = SiteFor(fixture, construction, "barracks");
        construction.Order(barracks, x, y, fixture.Rich(), fixture.Commands);

        // A worker is on its way.
        fixture.Carry();
        construction.BeginRound();
        Assert.Equal(new OwnBuildOrder(barracks.Index, x, y, barracks.Footprint), Assert.Single(fixture.Sight.BuildOrders));
        Assert.Equal(1, fixture.Sight.Workers.Count(worker => worker.IsBuilding));
        Assert.Equal(1, construction.UnderWayCount(barracks.Index));
        Assert.Equal(0, construction.FinishedCount(barracks.Index));

        // The foundation is laid, and builds itself.
        RunUntil(fixture, () => fixture.Sight.Structures.Any(structure => structure.Type == barracks.Index));
        construction.BeginRound();
        var laid = fixture.Sight.Structures.Single(structure => structure.Type == barracks.Index);
        Assert.Equal((x, y), (laid.X, laid.Y));
        Assert.False(laid.IsFinished);
        Assert.Empty(fixture.Sight.BuildOrders);
        Assert.Equal(1, construction.UnderWayCount(barracks.Index));

        RunUntil(fixture, () => fixture.Sight.Structures.Single(structure => structure.Type == barracks.Index).IsFinished);
        construction.BeginRound();
        Assert.Equal(0, construction.UnderWayCount(barracks.Index));
        Assert.Equal(1, construction.FinishedCount(barracks.Index));

        // Nothing went wrong, so nothing is held against the site.
        Assert.False(construction.Sites.WasRefusedNear(barracks.Index, x, y, within: 0));
    }

    [Fact]
    public void ASecondOrderOfOneRoundGoesToAnotherWorker()
    {
        var fixture = new AiFixture();
        var construction = new AiConstruction(fixture.Sight);
        var barracks = AiFixture.Structure("barracks");
        var generator = AiFixture.Structure("generator");
        fixture.SetStock(AiFixture.Plenty, AiFixture.Plenty);

        var first = SiteFor(fixture, construction, "barracks");
        construction.Order(barracks, first.X, first.Y, fixture.Rich(), fixture.Commands);
        var second = SiteFor(fixture, construction, "generator");
        construction.Order(generator, second.X, second.Y, fixture.Budget, fixture.Commands);
        fixture.Carry();

        Assert.Equal(2, fixture.Sight.BuildOrders.Count);
        Assert.Equal(2, fixture.Sight.Workers.Count(worker => worker.IsBuilding));

        // A worker that is building is given no further order: it would end the one it has.
        construction.BeginRound();
        var third = SiteFor(fixture, construction, "academy");
        construction.Order(AiFixture.Structure("academy"), third.X, third.Y, fixture.Rich(), fixture.Commands);
        var builders = fixture.Sight.Workers.Where(worker => worker.IsBuilding).Select(worker => worker.Entity).ToList();
        Assert.Equal(fixture.Sight.Workers.Count - 2, fixture.Commands[0].Entities.Length);
        Assert.DoesNotContain(fixture.Commands[0].Entities.ToArray(), builders.Contains);
    }

    /// <summary>
    /// An order the match did not take, for whatever reason, is neither on its way nor a
    /// structure in the next round. Its site is left alone for three minutes, then tried again.
    /// </summary>
    [Fact]
    public void AnOrderThatCameToNothingLeavesItsSiteAloneForAWhile()
    {
        var fixture = new AiFixture();
        var construction = new AiConstruction(fixture.Sight);
        var barracks = AiFixture.Structure("barracks");
        var (x, y) = SiteFor(fixture, construction, "barracks");
        construction.Order(barracks, x, y, fixture.Rich(), fixture.Commands);

        // The match never gets it.
        fixture.Commands.Clear();
        fixture.Run(1);
        construction.BeginRound();

        Assert.True(construction.Sites.WasRefusedNear(barracks.Index, x, y, within: 0));
        Assert.NotEqual((x, y), SiteFor(fixture, construction, "barracks"));
        Assert.Equal(0, construction.UnderWayCount(barracks.Index));

        fixture.Run(AiConstruction.RefusedTicks - 1);
        construction.BeginRound();
        Assert.True(construction.Sites.WasRefusedNear(barracks.Index, x, y, within: 0));

        fixture.Run(1);
        construction.BeginRound();
        Assert.False(construction.Sites.WasRefusedNear(barracks.Index, x, y, within: 0));
        Assert.Equal((x, y), SiteFor(fixture, construction, "barracks"));
    }

    [Fact]
    public void WithoutAWorkerNothingIsOrdered()
    {
        var fixture = new AiFixture();
        var construction = new AiConstruction(fixture.Sight);
        var barracks = AiFixture.Structure("barracks");
        var (x, y) = SiteFor(fixture, construction, "barracks");
        fixture.Sight.Workers.Clear();
        var budget = fixture.With(barracks.Materials, barracks.Energy);

        Assert.False(construction.Order(barracks, x, y, budget, fixture.Commands));

        Assert.Empty(fixture.Commands);
        Assert.True(budget.Covers(barracks.Materials, barracks.Energy));
        Assert.Equal(0, construction.UnderWayCount(barracks.Index));
    }

    /// <summary>The first structure of a round that it cannot pay for is waited for, and the later ones of the round are not.</summary>
    [Fact]
    public void TheFirstStructureItCannotPayForIsHeldFor()
    {
        var fixture = new AiFixture();
        var construction = new AiConstruction(fixture.Sight);
        var barracks = AiFixture.Structure("barracks");
        var works = AiFixture.Structure("works");
        Assert.True(works.Materials > barracks.Materials);
        var budget = fixture.With(barracks.Materials - 1, AiFixture.Plenty);
        construction.BeginRound();

        Assert.False(construction.CanOrder(barracks, budget));
        Assert.False(budget.Covers(1, 0));

        // The Works is counted among what the workers earn for, and holds nothing more back.
        Assert.False(construction.CanOrder(works, budget));
        Assert.Equal(barracks.Materials + works.Materials - (barracks.Materials - 1), budget.MissingMaterials);
        Assert.True(budget.Covers(0, AiFixture.Plenty - barracks.Energy));

        // The next round holds again.
        budget = fixture.With(works.Materials - 1, AiFixture.Plenty);
        construction.BeginRound();
        Assert.False(construction.CanOrder(works, budget));
        Assert.False(budget.Covers(1, 0));
    }

    [Fact]
    public void AStructureItMayNotHaveMoreOfIsNotCounted()
    {
        var fixture = new AiFixture();
        var construction = new AiConstruction(fixture.Sight);
        var academy = AiFixture.Structure("academy");
        Assert.NotNull(academy.MaxPerPlayer);
        Assert.True(construction.CanOrder(academy, fixture.Rich()));

        for (var count = 0; count < academy.MaxPerPlayer; count++)
        {
            fixture.Place("academy");
        }

        var budget = fixture.With(0, 0);
        Assert.False(construction.CanOrder(academy, budget));
        Assert.Equal(0, budget.MissingMaterials);
        Assert.True(budget.Covers(0, 0));
    }

    private static (int X, int Y) SiteFor(AiFixture fixture, AiConstruction construction, string structure)
    {
        var (homeX, homeY, radius) = fixture.Home;
        Assert.True(construction.Sites.TryFind(AiFixture.Structure(structure), homeX, homeY, radius, homeX, homeY, alsoNeeds: null, out var x, out var y));
        return (x, y);
    }

    private static void RunUntil(AiFixture fixture, Func<bool> condition)
    {
        for (var tick = 0; tick < 3000 && !condition(); tick++)
        {
            fixture.Run(1);
        }

        Assert.True(condition());
    }
}
