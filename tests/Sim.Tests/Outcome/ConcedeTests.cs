using Sim.Core;
using Sim.Outcome;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Tests.Structures;
using static Sim.Outcome.PlayerStanding;

namespace Sim.Tests.Outcome;

/// <summary>Design 5.1: "Concede at any time."</summary>
public class ConcedeTests
{
    [Fact]
    public void APlayerWhoConcedesIsOutInTheTickOfTheOrderAndTheOtherWins()
    {
        var match = OutcomeMatches.WithStarts();
        match.Simulation.Run(7);

        match.OrderConcede(0);
        match.Simulation.Tick();

        Assert.Equal([Conceded, Playing], match.Standings.All());
        Assert.Equal((1, 7, 7), (match.Standings.Winner, match.Standings.DecidedTick, match.Standings.OutTick(0)));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    [Fact]
    public void ConcedingTakesNothingOffTheMap()
    {
        var match = OutcomeMatches.WithStarts();
        var (structures, workers) = (match.Structures.Count, match.Workers.Count);

        match.OrderConcede(0);
        match.Simulation.Run(5);

        Assert.Equal((structures, workers), (match.Structures.Count, match.Workers.Count));
    }

    [Fact]
    public void APlayerConcedesInTheFirstTickOfAMatch()
    {
        var match = OutcomeMatches.WithStarts();
        match.OrderConcede(1);
        match.Simulation.Tick();

        Assert.Equal((0, 0), (match.Standings.Winner, match.Standings.DecidedTick));
    }

    [Fact]
    public void WhenBothConcedeInOneTickTheFirstOrderDecidesAndTheSecondIsDropped()
    {
        var match = OutcomeMatches.WithStarts();
        match.OrderConcede(1);
        match.OrderConcede(0);
        match.Simulation.Tick();

        Assert.Equal([Playing, Conceded], match.Standings.All());
        Assert.Equal(0, match.Standings.Winner);
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
    }

    [Fact]
    public void TheWinnerCannotConcedeAMatchThatIsDecided()
    {
        var match = OutcomeMatches.WithStarts();
        match.Raze(match.CapitolOf(1));
        match.Simulation.Tick();

        match.OrderConcede(0);
        match.Simulation.Tick();

        Assert.Equal([Playing, Eliminated], match.Standings.All());
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
    }

    [Fact]
    public void APlayerWhoConcedesIsNotEliminatedLaterWhenItsCapitolFalls()
    {
        var match = OutcomeMatches.WithStarts(players: 3);
        match.OrderConcede(2);
        match.Simulation.Tick();

        match.Raze(match.CapitolOf(2));
        match.Simulation.Run(3);

        Assert.Equal([Playing, Playing, Conceded], match.Standings.All());
        Assert.Equal(0, match.Standings.OutTick(2));
        Assert.False(match.Standings.IsDecided);
    }

    [Fact]
    public void WithThreePlayersTheLastOneLeftWinsHoweverTheOthersWentOut()
    {
        var match = OutcomeMatches.WithStarts(players: 3);
        match.OrderConcede(0);
        match.Simulation.Run(4);
        Assert.False(match.Standings.IsDecided);

        match.Raze(match.CapitolOf(1));
        match.Simulation.Tick();

        Assert.Equal([Conceded, Eliminated, Playing], match.Standings.All());
        Assert.Equal((2, 4), (match.Standings.Winner, match.Standings.DecidedTick));
    }

    /// <summary>The order comes first in a tick and conquest last: the player who concedes is out by its order.</summary>
    [Fact]
    public void ConcedingInTheTickInWhichTheLastCapitolFallsCountsAsConceding()
    {
        var match = OutcomeMatches.WithStarts();
        match.Raze(match.CapitolOf(1));
        match.OrderConcede(1);
        match.Simulation.Tick();

        Assert.Equal([Playing, Conceded], match.Standings.All());
    }

    [Fact]
    public void APlayerWithoutAStartCanConcede()
    {
        var match = TestMatches.Create(TestMaps.Open(24, 24));
        match.OrderConcede(1);
        match.Simulation.Tick();

        Assert.Equal([Playing, Conceded], match.Standings.All());
        Assert.Equal(0, match.Standings.Winner);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void AnOrderToConcedeInTheNameOfNoPlayerIsDropped(int player)
    {
        var match = OutcomeMatches.WithStarts();
        match.Simulation.Commands.Enqueue(0, new Command(CommandType.Concede, player));
        match.Simulation.Tick();

        Assert.Equal([Playing, Playing], match.Standings.All());
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
    }

    [Fact]
    public void TheNumberOfTheOrderIsFixed() => Assert.Equal(15, (int)CommandType.Concede);
}
