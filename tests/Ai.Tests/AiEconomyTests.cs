using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Production;

namespace Ai.Tests;

/// <summary>
/// The economy (design 5.5, item 1): workers up to the count of the epoch, divided between
/// Materials and Energy by what the next purchases lack, and when to expand.
/// </summary>
public class AiEconomyTests
{
    [Fact]
    public void ItTrainsAWorkerWhileItHasFewerThanItsPersonalityWants()
    {
        var fixture = new AiFixture();
        var workers = fixture.Sight.Workers.Count;
        var capitol = fixture.Sight.Structures.Single().Entity;

        new AiEconomy(fixture.Sight, AiFixture.Personality(workers)).TrainWorkers(fixture.Rich(), fixture.Commands);
        Assert.Empty(fixture.Commands);

        new AiEconomy(fixture.Sight, AiFixture.Personality(workers + 1)).TrainWorkers(fixture.Rich(), fixture.Commands);
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.Train, order.Type);
        Assert.Equal(TrainOrder.Worker, order.ContentId);
        Assert.Equal([capitol], order.Entities.ToArray());
    }

    [Fact]
    public void AWorkerInTrainingCountsAsAWorker()
    {
        var fixture = new AiFixture();
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality(fixture.Sight.Workers.Count + 1));

        economy.TrainWorkers(fixture.Rich(), fixture.Commands);
        fixture.Carry();
        Assert.Equal(1, fixture.Sight.WorkersInTraining);

        economy.TrainWorkers(fixture.Rich(), fixture.Commands);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void ItKeepsTwoWorkersInTheQueueAndNoMore()
    {
        var fixture = new AiFixture();
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality(workers: 30));
        fixture.SetStock(AiFixture.Plenty, 0);

        var ordered = 0;
        for (var round = 0; round < 6; round++)
        {
            economy.TrainWorkers(fixture.Rich(), fixture.Commands);
            ordered += fixture.Commands.Count;
            fixture.Carry();
        }

        Assert.Equal(AiEconomy.QueueLength, ordered);
        Assert.Equal(AiEconomy.QueueLength, fixture.Sight.WorkersInTraining);
    }

    [Fact]
    public void AWorkerItCannotPayForIsWaitedFor()
    {
        var fixture = new AiFixture();
        var price = fixture.Data.Resources.Worker.Materials;
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality(workers: 30));

        var budget = fixture.With(price - 1, 0);
        economy.TrainWorkers(budget, fixture.Commands);

        Assert.Empty(fixture.Commands);
        Assert.False(budget.Covers(1, 0));
        Assert.Equal(1 + price, budget.MissingMaterials);
    }

    [Fact]
    public void ItKeepsSupplyFreeForTheMostWorkersItsPersonalityEverKeeps()
    {
        var fixture = new AiFixture();
        var workers = fixture.Sight.Workers.Count;
        var personality = AiFixture.Personality() with
        {
            WorkerTargets = [new AiWorkerTarget(1, workers), new AiWorkerTarget(2, workers + 9), new AiWorkerTarget(3, workers + 5)],
        };
        var economy = new AiEconomy(fixture.Sight, personality);

        Assert.Equal(9 * fixture.Data.Resources.Worker.Supply, economy.SupplyKeptForWorkers);

        // In epoch 1 it trains none of them yet.
        economy.TrainWorkers(fixture.Rich(), fixture.Commands);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void ItExpandsWhenTheStockpileIsAboveTheMarkOfItsPersonality()
    {
        var fixture = new AiFixture();
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality(expandAt: 600));

        fixture.SetStock(600, 5000);
        Assert.False(economy.WantsToExpand);

        fixture.SetStock(601, 0);
        Assert.True(economy.WantsToExpand);
    }

    /// <summary>
    /// The design's other trigger. One depleted deposit beside one at the whole rate with places
    /// free is no reason yet: the workers move over.
    /// </summary>
    [Fact]
    public void ItExpandsWhenADepositIsDepletedAndNoOtherHasAPlaceFree()
    {
        var fixture = new AiFixture();
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality());
        var held = HeldDeposits(fixture);
        Assert.True(held.Count >= 2);
        Assert.False(economy.WantsToExpand);

        Deplete(fixture, held[0].Entity);
        Assert.False(economy.WantsToExpand);

        foreach (var deposit in held)
        {
            Deplete(fixture, deposit.Entity);
        }

        Assert.True(economy.WantsToExpand);
    }

    [Fact]
    public void WorkersWithoutWorkAreSentToADepositItHolds()
    {
        var fixture = new AiFixture();
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality());
        var workers = fixture.Sight.Workers.Select(worker => worker.Entity).ToList();
        var held = HeldDeposits(fixture).Select(site => site.Entity).ToList();

        economy.AssignWorkers(fixture.With(0, 0), fixture.Commands);

        Assert.Equal(workers.Count, fixture.Commands.Count);
        Assert.All(fixture.Commands, command =>
        {
            Assert.Equal(CommandType.Gather, command.Type);
            Assert.Contains(command.TargetEntity, held);
        });
        Assert.Equal(workers, fixture.Commands.Select(command => Assert.Single(command.Entities.ToArray())).ToList());

        // Every one of them finds its place: none is told to go where another already was sent.
        fixture.Carry();
        Assert.DoesNotContain(fixture.Sight.Workers, worker => worker.State is WorkerState.Idle or WorkerState.Waiting);

        // And a worker that has work is left alone.
        economy.AssignWorkers(fixture.With(0, 0), fixture.Commands);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void AWorkerWithoutWorkGoesToADepositAtTheWholeRateBeforeADepletedOne()
    {
        var fixture = new AiFixture();
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality());

        // Where they would go otherwise: the deposit nearest to them.
        economy.AssignWorkers(fixture.With(0, 0), fixture.Commands);
        var nearest = fixture.Commands[0].TargetEntity;
        fixture.Commands.Clear();
        fixture.Sight.Refresh();

        Deplete(fixture, nearest);
        economy.AssignWorkers(fixture.With(0, 0), fixture.Commands);

        Assert.NotEmpty(fixture.Commands);
        Assert.All(fixture.Commands, command =>
        {
            Assert.NotEqual(nearest, command.TargetEntity);
            Assert.False(fixture.SiteOf(command.TargetEntity).IsDepleted);
        });
    }

    /// <summary>
    /// The division is by the time each resource takes to earn: what is missing of it over what a
    /// worker earns of it. The parts here are seconds of one worker's work.
    /// </summary>
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 6)]
    [InlineData(1, 1, 3)]
    [InlineData(1, 2, 4)]
    [InlineData(2, 1, 2)]
    [InlineData(3, 1, 2)] // One and a half, rounded half up.
    [InlineData(1, 5, 5)]
    public void WorkersAreDividedByWhatEachResourceLacksOverWhatAWorkerEarnsOfIt(int materialsParts, int energyParts, int atGenerators)
    {
        var fixture = new AiFixture();
        Assert.Equal(6, fixture.Sight.Workers.Count);
        var generators = new[] { fixture.Place("generator"), fixture.Place("generator") };
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality());

        var budget = fixture.With(0, 0);
        budget.Want(
            materialsParts * (int)fixture.Sight.PaymentOf(Resource.Materials),
            energyParts * (int)fixture.Sight.PaymentOf(Resource.Energy));
        economy.AssignWorkers(budget, fixture.Commands);

        Assert.Equal(atGenerators, economy.EnergyWorkersWanted);
        Assert.Equal(6, fixture.Commands.Count);
        Assert.Equal(atGenerators, fixture.Commands.Count(command => generators.Contains(command.TargetEntity)));
    }

    [Fact]
    public void WithNothingMissingTheDivisionStaysAsItIs()
    {
        var fixture = new AiFixture();
        fixture.Place("generator");
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality());

        economy.AssignWorkers(fixture.Rich(), fixture.Commands);

        Assert.Equal(0, economy.EnergyWorkersWanted);
    }

    /// <summary>What it wants at Generators is what tells it to build one.</summary>
    [Fact]
    public void WithoutAGeneratorEveryWorkerGoesToADepositAndItStillSaysHowManyItWantsAtGenerators()
    {
        var fixture = new AiFixture();
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality());
        var held = HeldDeposits(fixture).Select(site => site.Entity).ToList();

        var budget = fixture.With(0, 0);
        budget.Want(0, 100);
        economy.AssignWorkers(budget, fixture.Commands);

        Assert.Equal(6, economy.EnergyWorkersWanted);
        Assert.Equal(6, fixture.Commands.Count);
        Assert.All(fixture.Commands, command => Assert.Contains(command.TargetEntity, held));
    }

    [Fact]
    public void AGeneratorTakesNoMoreWorkersThanItHasPlaces()
    {
        var fixture = new AiFixture();
        var generator = fixture.Place("generator");
        var places = fixture.Data.Resources.Energy.WorkersPerGenerator;
        Assert.True(places < 6);
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality());

        var budget = fixture.With(0, 0);
        budget.Want(0, 100);
        economy.AssignWorkers(budget, fixture.Commands);

        Assert.Equal(6, economy.EnergyWorkersWanted);
        Assert.Equal(places, fixture.Commands.Count(command => command.TargetEntity == generator));
        Assert.Equal(6, fixture.Commands.Count);
    }

    /// <summary>Moving costs the walk, so the division follows the purchases one worker at a time.</summary>
    [Fact]
    public void OneWorkerChangesResourceAtATime()
    {
        var fixture = new AiFixture();
        var generators = new[] { fixture.Place("generator"), fixture.Place("generator") };
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality());
        economy.AssignWorkers(fixture.With(0, 0), fixture.Commands);
        Assert.DoesNotContain(fixture.Commands, command => generators.Contains(command.TargetEntity));
        fixture.Carry();

        var moved = new List<EntityId>();
        for (var move = 0; move < 3; move++)
        {
            economy.AssignWorkers(WantingEnergy(fixture), fixture.Commands);
            var order = Assert.Single(fixture.Commands);
            Assert.Equal(CommandType.Gather, order.Type);
            Assert.Contains(order.TargetEntity, generators);
            moved.Add(Assert.Single(order.Entities.ToArray()));
            fixture.Carry();

            // Not again until the interval is over.
            economy.AssignWorkers(WantingEnergy(fixture), fixture.Commands);
            Assert.Empty(fixture.Commands);
            fixture.Run(AiEconomy.RebalanceInterval - 2);
            economy.AssignWorkers(WantingEnergy(fixture), fixture.Commands);
            Assert.Empty(fixture.Commands);
            fixture.Run(1);
        }

        Assert.Equal(3, moved.Distinct().Count());

        static AiBudget WantingEnergy(AiFixture fixture)
        {
            var budget = fixture.With(0, 0);
            budget.Want(0, 100);
            return budget;
        }
    }

    [Fact]
    public void OneWorkerTooManyAtTheGeneratorsIsLeftThere()
    {
        var fixture = new AiFixture();
        var generators = new[] { fixture.Place("generator"), fixture.Place("generator") };
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality());

        // Three at the Generators.
        economy.AssignWorkers(Wanting(fixture, materialsParts: 1, energyParts: 1), fixture.Commands);
        Assert.Equal(3, fixture.Commands.Count(command => generators.Contains(command.TargetEntity)));
        fixture.Carry();
        fixture.Run(AiEconomy.RebalanceInterval);

        // It wants two: one too many, which stays.
        economy.AssignWorkers(Wanting(fixture, materialsParts: 2, energyParts: 1), fixture.Commands);
        Assert.Equal(2, economy.EnergyWorkersWanted);
        Assert.Empty(fixture.Commands);

        // It wants none: one goes back to a deposit.
        economy.AssignWorkers(Wanting(fixture, materialsParts: 1, energyParts: 0), fixture.Commands);
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(WorkSiteKind.Deposit, fixture.SiteOf(order.TargetEntity).Kind);

        static AiBudget Wanting(AiFixture fixture, int materialsParts, int energyParts)
        {
            var budget = fixture.With(0, 0);
            budget.Want(
                materialsParts * (int)fixture.Sight.PaymentOf(Resource.Materials),
                energyParts * (int)fixture.Sight.PaymentOf(Resource.Energy));
            return budget;
        }
    }

    [Fact]
    public void AWorkerOfADepletedDepositMovesToOneAtTheWholeRate()
    {
        var fixture = new AiFixture();
        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality());
        economy.AssignWorkers(fixture.With(0, 0), fixture.Commands);
        fixture.Carry();
        var worked = fixture.Sight.Workers[0].Site;
        Deplete(fixture, worked);
        fixture.Run(AiEconomy.RebalanceInterval);

        economy.AssignWorkers(fixture.With(0, 0), fixture.Commands);

        var order = Assert.Single(fixture.Commands);
        var worker = Assert.Single(order.Entities.ToArray());
        Assert.Equal(worked, fixture.Sight.Workers.Single(candidate => candidate.Entity == worker).Site);
        var target = fixture.SiteOf(order.TargetEntity);
        Assert.Equal(WorkSiteKind.Deposit, target.Kind);
        Assert.True(target.IsHeld);
        Assert.False(target.IsDepleted);
    }

    /// <summary>A pair of units can leave one supply over, which nothing of the army fits into.</summary>
    [Fact]
    public void AWorkerFillsTheSupplyThatNoUnitOfTheArmyFits()
    {
        var fixture = new AiFixture();
        var unit = fixture.Sight.Tech.Blueprints[AiFixture.Chassis("peltast")].Supply;
        var economy = WithOddSupplyFree(fixture, unit, workersStillWanted: 0);

        // Plenty of room: it is the army's.
        economy.FillSupply(unit, fixture.Rich(), fixture.Commands);
        Assert.Empty(fixture.Commands);

        fixture.Spawn("peltast", fixture.Sight.SupplyFree / unit);
        Assert.Equal(1, fixture.Sight.SupplyFree);
        economy.FillSupply(unit, fixture.Rich(), fixture.Commands);

        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.Train, order.Type);
        Assert.Equal(TrainOrder.Worker, order.ContentId);

        // With that worker in training the supply is full.
        fixture.Carry();
        Assert.Equal(0, fixture.Sight.SupplyFree);
        economy.FillSupply(unit, fixture.Rich(), fixture.Commands);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void SupplyKeptForAWorkerOfItsPersonalityIsNotFilledHere()
    {
        var fixture = new AiFixture();
        var unit = fixture.Sight.Tech.Blueprints[AiFixture.Chassis("peltast")].Supply;
        var economy = WithOddSupplyFree(fixture, unit, workersStillWanted: 1);
        fixture.Spawn("peltast", fixture.Sight.SupplyFree / unit);
        Assert.Equal(1, fixture.Sight.SupplyFree);
        Assert.Equal(1, economy.SupplyKeptForWorkers);

        // The one supply is the next worker's, which TrainWorkers orders.
        economy.FillSupply(unit, fixture.Rich(), fixture.Commands);

        Assert.Empty(fixture.Commands);
    }

    /// <summary>
    /// Brings the match to a free supply that pairs of units cannot fill, with a worker in
    /// training if that is what it takes, and returns the economy of a personality that wants so
    /// many workers more than it then has.
    /// </summary>
    private static AiEconomy WithOddSupplyFree(AiFixture fixture, int unit, int workersStillWanted)
    {
        Assert.Equal(2, unit);
        Assert.Equal(1, fixture.Data.Resources.Worker.Supply);
        fixture.SetStock(AiFixture.Plenty, 0);
        var workers = fixture.Sight.Workers.Count + (1 - (fixture.Sight.SupplyFree % unit));
        new AiEconomy(fixture.Sight, AiFixture.Personality(workers)).TrainWorkers(fixture.Rich(), fixture.Commands);
        fixture.Carry();
        Assert.Equal(1, fixture.Sight.SupplyFree % unit);

        var economy = new AiEconomy(fixture.Sight, AiFixture.Personality(workers + workersStillWanted));
        Assert.Equal(workersStillWanted, economy.SupplyKeptForWorkers);
        return economy;
    }

    private static List<KnownSite> HeldDeposits(AiFixture fixture) =>
        [.. fixture.Sight.Sites.Where(site => site.Kind == WorkSiteKind.Deposit && site.IsHeld)];

    /// <summary>Has a deposit paid out everything it pays at the whole rate.</summary>
    private static void Deplete(AiFixture fixture, EntityId deposit)
    {
        fixture.Match.WorkSites.Get(deposit).Delivered = fixture.Data.Resources.Materials.FullRateAmount * Stockpiles.PerResource;
        fixture.Sight.Refresh();
        Assert.True(fixture.SiteOf(deposit).IsDepleted);
    }
}
