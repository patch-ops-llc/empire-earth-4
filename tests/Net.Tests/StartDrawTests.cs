using Sim.Core;
using Sim.Setup;

namespace Net.Tests;

public class StartDrawTests
{
    [Fact]
    public void EveryPlayerGetsADifferentStart()
    {
        var random = new DeterministicRandom(11);

        for (var round = 0; round < 200; round++)
        {
            var players = StartDraw.Draw(5, 8, random.NextInt);

            Assert.Equal(5, players.Length);
            Assert.Equal(5, players.Select(player => player.Start).Distinct().Count());
            Assert.All(players, player => Assert.InRange(player.Start, 0, 7));
        }
    }

    [Fact]
    public void EachOfTwoPlayersGetsEachStartAboutHalfTheTime()
    {
        var random = new DeterministicRandom(3);
        var firstGotStartZero = 0;

        for (var round = 0; round < 2000; round++)
        {
            var players = StartDraw.Draw(2, 2, random.NextInt);
            firstGotStartZero += players[0].Start == 0 ? 1 : 0;
            Assert.Equal(1, players[0].Start + players[1].Start);
        }

        Assert.InRange(firstGotStartZero, 900, 1100);
    }

    [Fact]
    public void TheDrawFollowsTheSourceOfChance()
    {
        Assert.Equal([new PlayerSetup(0), new PlayerSetup(1), new PlayerSetup(2)], StartDraw.Draw(3, 3, _ => 0));
        Assert.Equal([new PlayerSetup(2), new PlayerSetup(0), new PlayerSetup(1)], StartDraw.Draw(3, 3, left => left - 1));
    }

    /// <summary>Design 5.2: which start each player gets is a setting of the match, and the draw is its default.</summary>
    [Fact]
    public void AStartTheMatchSetsIsKeptAndTheOthersAreDrawnFromWhatIsLeft()
    {
        var random = new DeterministicRandom(17);
        var seen = new HashSet<(int, int)>();

        for (var round = 0; round < 400; round++)
        {
            var players = StartDraw.Assign([null, 4, null, 0], 6, random.NextInt);

            Assert.Equal((4, 0), (players[1].Start, players[3].Start));
            Assert.Equal(4, players.Select(player => player.Start).Distinct().Count());
            Assert.All(players, player => Assert.InRange(player.Start, 0, 5));
            seen.Add((players[0].Start, players[2].Start));
        }

        // Starts 1, 2, 3 and 5 are left for two players: twelve ways, all of which come up.
        Assert.Equal(12, seen.Count);
    }

    [Fact]
    public void WhenEveryStartIsSetNothingIsDrawn()
    {
        var players = StartDraw.Assign([1, 0], 2, _ => throw new InvalidOperationException("Nothing is left to draw."));

        Assert.Equal([new PlayerSetup(1), new PlayerSetup(0)], players);
    }

    [Fact]
    public void WithNoStartSetTheStartsAreTheOnesOfTheDraw()
    {
        var (drawn, assigned) = (new DeterministicRandom(5), new DeterministicRandom(5));

        for (var round = 0; round < 50; round++)
        {
            Assert.Equal(StartDraw.Draw(3, 5, drawn.NextInt), StartDraw.Assign([null, null, null], 5, assigned.NextInt));
        }
    }

    [Fact]
    public void TheSourceOfChanceIsAskedOnlyForTheStartsThatAreLeft()
    {
        var asked = new List<int>();

        var players = StartDraw.Assign([null, 2, null], 4, left =>
        {
            asked.Add(left);
            return left - 1;
        });

        Assert.Equal([3, 2], asked);
        Assert.Equal([new PlayerSetup(3), new PlayerSetup(2), new PlayerSetup(0)], players);
    }

    [Fact]
    public void StartsThatCannotBeSetAreRefused()
    {
        Assert.Throws<ArgumentException>(() => StartDraw.Assign([1, 1], 2, _ => 0));
        Assert.Throws<ArgumentException>(() => StartDraw.Assign([2, null], 2, _ => 0));
        Assert.Throws<ArgumentException>(() => StartDraw.Assign([-1, null], 2, _ => 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StartDraw.Assign([null, null, 0], 2, _ => 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StartDraw.Assign([], 2, _ => 0));
        Assert.Throws<ArgumentException>(() => StartDraw.Assign([0, null], 2, left => left));
    }

    [Fact]
    public void ADrawThatCannotBeMadeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StartDraw.Draw(3, 2, _ => 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StartDraw.Draw(0, 2, _ => 0));
        Assert.Throws<ArgumentException>(() => StartDraw.Draw(2, 2, left => left));
        Assert.Throws<ArgumentException>(() => StartDraw.Draw(2, 2, _ => -1));
    }
}
