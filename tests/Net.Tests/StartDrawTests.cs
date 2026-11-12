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

    [Fact]
    public void ADrawThatCannotBeMadeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StartDraw.Draw(3, 2, _ => 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StartDraw.Draw(0, 2, _ => 0));
        Assert.Throws<ArgumentException>(() => StartDraw.Draw(2, 2, left => left));
        Assert.Throws<ArgumentException>(() => StartDraw.Draw(2, 2, _ => -1));
    }
}
