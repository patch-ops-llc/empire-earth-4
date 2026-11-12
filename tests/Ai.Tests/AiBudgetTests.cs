namespace Ai.Tests;

/// <summary>The stockpile of one round of thinking: nothing is spent twice, and what is waited for is kept.</summary>
public class AiBudgetTests
{
    [Fact]
    public void WhatIsPaidIsGoneForTheRestOfTheRound()
    {
        var budget = new AiBudget();
        budget.Reset(materials: 100, energy: 50);

        Assert.True(budget.Covers(100, 50));
        Assert.False(budget.Covers(101, 0));
        Assert.False(budget.Covers(0, 51));

        budget.Pay(60, 50);

        Assert.True(budget.Covers(40, 0));
        Assert.False(budget.Covers(41, 0));
        Assert.False(budget.Covers(0, 1));
        Assert.False(budget.StockCovers(41, 0));
    }

    [Fact]
    public void WhatIsHeldIsKeptFromEverythingAfterItAndIsStillInTheStockpile()
    {
        var budget = new AiBudget();
        budget.Reset(materials: 300, energy: 80);

        budget.Hold(100, 100);

        Assert.True(budget.Covers(200, 0));
        Assert.False(budget.Covers(201, 0));
        Assert.False(budget.Covers(0, 1));

        // The simulation knows nothing of what is held: it makes an order a pair by the stockpile.
        Assert.True(budget.StockCovers(300, 80));
    }

    /// <summary>
    /// A research that waits for Energy holds its price back. A worker and a Generator cost
    /// Materials only, and they are what earn the Energy, so the Energy that is held does not
    /// stop them.
    /// </summary>
    [Fact]
    public void EnergyThatIsHeldDoesNotKeepMaterialsFromAPurchaseThatCostsNoEnergy()
    {
        var budget = new AiBudget();
        budget.Reset(materials: 300, energy: 0);

        budget.Hold(100, 100);

        Assert.True(budget.Covers(100, 0));
        Assert.True(budget.Covers(200, 0));
        Assert.False(budget.Covers(201, 0));
        Assert.False(budget.Covers(100, 1));
    }

    [Fact]
    public void MoreCanBeHeldThanTheStockpileHas()
    {
        var budget = new AiBudget();
        budget.Reset(materials: 50, energy: 500);

        budget.Hold(100, 100);

        Assert.False(budget.Covers(1, 0));
        Assert.True(budget.Covers(0, 400));
        Assert.False(budget.Covers(0, 401));
    }

    [Fact]
    public void WhatIsMissingIsWhatTheRoundWantsAboveTheStockpileAtItsStart()
    {
        var budget = new AiBudget();
        budget.Reset(materials: 100, energy: 500);

        Assert.Equal(0, budget.MissingMaterials);
        Assert.Equal(0, budget.MissingEnergy);

        budget.Want(80, 200);
        Assert.Equal(0, budget.MissingMaterials);

        budget.Want(70, 200);
        Assert.Equal(50, budget.MissingMaterials);
        Assert.Equal(0, budget.MissingEnergy);

        // Paying for a part of it changes nothing: it was in the stockpile the workers had earned.
        budget.Pay(80, 200);
        Assert.Equal(50, budget.MissingMaterials);

        budget.Want(0, 150);
        Assert.Equal(50, budget.MissingEnergy);
    }

    [Fact]
    public void ANewRoundStartsWithTheStockpileAndNothingElse()
    {
        var budget = new AiBudget();
        budget.Reset(materials: 100, energy: 100);
        budget.Pay(100, 100);
        budget.Hold(500, 500);
        budget.Want(1000, 1000);

        budget.Reset(materials: 30, energy: 20);

        Assert.True(budget.Covers(30, 20));
        Assert.False(budget.Covers(31, 0));
        Assert.Equal(0, budget.MissingMaterials);
        Assert.Equal(0, budget.MissingEnergy);
    }
}
