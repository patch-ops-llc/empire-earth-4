using Sim.Core;
using Sim.Research;
using Sim.Tests.Tech;
using static Sim.Tests.Research.ResearchMatches;

namespace Sim.Tests.Research;

/// <summary>Design 1.3: cancelling refunds everything, and so does losing the slot.</summary>
public class CancellingAndLosingTests
{
    [Fact]
    public void CancellingGivesBackEverythingPaidAndFreesTheSlot()
    {
        var match = Create();
        match.OrderResearch(0, Firepower);
        match.Simulation.Run(400);
        var events = match.Events();

        match.OrderCancel(0, Firepower);
        match.Simulation.Tick();

        Assert.Equal((Plenty, Plenty), match.Stock(0));
        AssertFree(match.Slot(0));
        Assert.Equal(1, match.Research.State.Tier(0, Firepower));
        Assert.Equal([new ResearchEvent(SimEventKind.ResearchCancelled, 400, 0, Firepower, 2, 1)], events);

        // The time already spent is lost: the research starts again from the beginning.
        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();
        Assert.Equal(TicksToTier2 - 1, match.Slot(0).TicksLeft);
    }

    [Fact]
    public void ACancelledResearchNeverFinishes()
    {
        var match = Create();
        match.OrderResearch(0, Firepower);
        match.Simulation.Run(TicksToTier2 - 1);
        match.OrderCancel(0, Firepower);
        match.Simulation.Run(TicksToTier2);

        Assert.Equal(1, match.Research.State.Tier(0, Firepower));
        Assert.Equal((Plenty, Plenty), match.Stock(0));
    }

    [Fact]
    public void OnlyAResearchInProgressCanBeCancelledAndOnlyByItsPlayer()
    {
        var match = Create();
        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();

        match.OrderCancel(1, Firepower);
        match.OrderCancel(0, Protection);
        match.Simulation.Tick();

        Assert.Equal(2, match.Simulation.DroppedCommandCount);
        Assert.True(match.Slot(0).IsBusy);
        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(0));
        Assert.Equal((Plenty, Plenty), match.Stock(1));

        // Twice in one tick: the second finds nothing to cancel and refunds nothing.
        match.OrderCancel(0, Firepower);
        match.OrderCancel(0, Firepower);
        match.Simulation.Tick();
        Assert.Equal(3, match.Simulation.DroppedCommandCount);
        Assert.Equal((Plenty, Plenty), match.Stock(0));
    }

    /// <summary>
    /// "If an Academy is destroyed, the research in its slot is cancelled with a full refund."
    /// The slot goes with it, and the player's other slots carry on.
    /// </summary>
    [Fact]
    public void LosingTheHolderOfASlotCancelsItsResearchWithAFullRefund()
    {
        var match = Create();
        var academy = match.PlaceHolder(0);
        Assert.True(match.Research.OpenSlot(0, academy));
        match.OrderResearch(0, Firepower);
        match.OrderResearch(0, Protection);
        match.Simulation.Run(300);
        Assert.Equal((academy, Protection), (match.Slot(0, 1).Holder, match.Slot(0, 1).Track));
        var events = match.Events();

        match.World.Entities.Destroy(academy);
        match.Simulation.Tick();

        Assert.Equal(default, match.Slot(0, 1));
        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(0));
        Assert.Equal([new ResearchEvent(SimEventKind.ResearchLost, 300, 0, Protection, 2, 1, academy)], events);
        Assert.False(match.Research.State.IsHolder(academy));

        match.Simulation.Run(TicksToTier2 - 301);
        Assert.Equal((2, 1), (match.Research.State.Tier(0, Firepower), match.Research.State.Tier(0, Protection)));
    }

    [Fact]
    public void AFreeSlotGoesWithItsHolderToo()
    {
        var match = Create();
        var academy = match.PlaceHolder(0);
        match.Research.OpenSlot(0, academy);
        var events = match.Events();

        match.World.Entities.Destroy(academy);
        match.Simulation.Tick();

        Assert.False(match.Slot(0, 1).IsOpen);
        Assert.Empty(events);
        Assert.Equal((Plenty, Plenty), match.Stock(0));
    }

    /// <summary>The research system runs after everything that can destroy a holder, so the loss comes first.</summary>
    [Fact]
    public void AResearchDoesNotFinishInTheTickItsHolderFalls()
    {
        var match = Create();
        var academy = match.PlaceHolder(0);
        match.Research.OpenSlot(0, academy);
        match.OrderResearch(0, Firepower);
        match.OrderResearch(0, Protection);
        match.Simulation.Run(TicksToTier2 - 1);
        Assert.Equal(1, match.Slot(0, 1).TicksLeft);

        match.World.Entities.Destroy(academy);
        match.Simulation.Tick();

        Assert.Equal((2, 1), (match.Research.State.Tier(0, Firepower), match.Research.State.Tier(0, Protection)));
        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(0));
    }

    /// <summary>"The same happens to the first slot when the player's last Capitol falls."</summary>
    [Fact]
    public void ClosingThePlayersOwnSlotCancelsItsResearchWithAFullRefund()
    {
        var match = Create();
        match.OrderResearch(0, Firepower);
        match.Simulation.Run(10);

        match.Research.ClosePlayerSlot(0);

        Assert.Equal(default, match.Slot(0));
        Assert.Equal((Plenty, Plenty), match.Stock(0));
        Assert.Equal([new ResearchEvent(SimEventKind.ResearchLost, 10, 0, Firepower, 2, 1)], match.EventsOnTheList());
        Assert.Equal(ResearchStartCheck.NoFreeSlot, match.Research.CheckStart(0, Firepower));

        // Closing a closed slot and opening an open one change nothing.
        match.Research.ClosePlayerSlot(0);
        match.Research.OpenPlayerSlot(0);
        match.Research.OpenPlayerSlot(0);
        AssertFree(match.Slot(0));
        Assert.Single(match.EventsOnTheList());
        Assert.Equal(ResearchStartCheck.Allowed, match.Research.CheckStart(0, Firepower));
    }

    /// <summary>Design 1.3: "to a maximum of two Academies and three slots".</summary>
    [Fact]
    public void APlayerHasAtMostTwoSlotsThatBelongToBuildings()
    {
        var match = Create();
        var (first, second, third) = (match.PlaceHolder(0, 3, 3), match.PlaceHolder(0, 5, 3), match.PlaceHolder(0, 7, 3));

        Assert.True(match.Research.OpenSlot(0, first));
        Assert.True(match.Research.OpenSlot(0, second));
        Assert.False(match.Research.OpenSlot(0, third));
        Assert.Equal(3, ResearchState.SlotsPerPlayer);
        Assert.Equal([EntityId.None, first, second], Enumerable.Range(0, 3).Select(slot => match.Slot(0, slot).Holder));

        // When one is lost, the next building takes the place it left.
        match.World.Entities.Destroy(first);
        match.Simulation.Tick();
        Assert.True(match.Research.OpenSlot(0, third));
        Assert.Equal([EntityId.None, third, second], Enumerable.Range(0, 3).Select(slot => match.Slot(0, slot).Holder));
    }

    [Fact]
    public void ASlotBelongsToOneLivingEntity()
    {
        var match = Create();
        var academy = match.PlaceHolder(0);
        var fallen = match.PlaceHolder(0, 5, 5);
        match.World.Entities.Destroy(fallen);
        match.Research.OpenSlot(0, academy);

        Assert.Throws<ArgumentException>(() => match.Research.OpenSlot(0, academy));
        Assert.Throws<ArgumentException>(() => match.Research.OpenSlot(1, academy));
        Assert.Throws<ArgumentException>(() => match.Research.OpenSlot(0, fallen));
        Assert.Throws<ArgumentException>(() => match.Research.OpenSlot(0, EntityId.None));
        Assert.Throws<ArgumentException>(() => match.Research.OpenSlot(0, new EntityId(500)));
        Assert.Throws<ArgumentOutOfRangeException>(() => match.Research.OpenSlot(2, match.PlaceHolder(1, 9, 9)));
    }

    /// <summary>Open, with no holder, and with nothing left of the research that was in it.</summary>
    private static void AssertFree(ResearchSlot slot)
    {
        Assert.True(slot.IsOpen && slot.IsFree && !slot.IsBusy);
        Assert.Equal(EntityId.None, slot.Holder);
        Assert.Equal((0, 0, 0, 0, 0, 0), (slot.Track, slot.Tier, slot.TicksLeft, slot.Ticks, slot.Materials, slot.Energy));
    }

    /// <summary>A research in progress keeps the price and the time it started with (design 1.3, "Checked once").</summary>
    [Fact]
    public void AResearchInProgressIsNeitherStoppedNorRepricedWhenTheEpochChanges()
    {
        var match = Create();
        match.Research.SetTiers(0, DesignData.Mix(3, 3, 2, 1, 1, 1));
        match.Research.OpenSlot(0, match.PlaceHolder(0));

        // Firepower 4 is two above epoch 2: 338 and 525, 75 seconds. Mobility 3 is the frontier.
        match.OrderResearch(0, Firepower);
        match.OrderResearch(0, Mobility);
        match.Simulation.Run(1200);

        // Mobility 3 has raised the epoch to 3, where Firepower 4 would cost 225 and 350.
        Assert.Equal(3, match.Research.Tech(0).Epoch);
        var slot = match.Slot(0);
        Assert.Equal((Firepower, 4, 338, 525, 1500, 300), (slot.Track, slot.Tier, slot.Materials, slot.Energy, slot.Ticks, slot.TicksLeft));

        match.OrderCancel(0, Firepower);
        match.Simulation.Tick();
        Assert.Equal((Plenty - 150, Plenty - 200), match.Stock(0));

        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();
        Assert.Equal((225, 350), (match.Slot(0).Materials, match.Slot(0).Energy));
    }
}
