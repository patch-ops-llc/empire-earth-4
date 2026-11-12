using Sim.Core;
using Sim.Setup;

namespace Net.Tests;

/// <summary>The settings of a match as the host chooses them, and the match they give (design 5.2).</summary>
public class MatchSettingsTests
{
    [Fact]
    public void ByDefaultEverySeatIsAPersonsAndEveryStartIsDrawn()
    {
        var settings = MatchSettings.Default("ridge", 2);

        Assert.Equal("ridge", settings.MapId);
        Assert.Equal([new SeatSetting(SeatKind.Person, null), new SeatSetting(SeatKind.Person, null)], settings.Seats);
        Assert.Equal([SeatKind.Person, SeatKind.Person], settings.Kinds);
        Assert.Null(settings.FindProblem(TestData.Data));
    }

    [Fact]
    public void TheDefaultDrawGivesEachPlayerEachSideAboutHalfTheTime()
    {
        var settings = MatchSettings.Default("ridge", 2);
        var random = new DeterministicRandom(9);
        var hostAtStartZero = 0;

        for (var round = 0; round < 1000; round++)
        {
            var match = settings.Describe(TestData.Data, TestData.GameVersion, 1, random.NextInt);
            hostAtStartZero += match.Players[0].Start == 0 ? 1 : 0;
            Assert.Equal(1, match.Players[0].Start + match.Players[1].Start);
        }

        Assert.InRange(hostAtStartZero, 440, 560);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void AStartTheHostSetsIsTheStartThePlayerGets(int hostStart, int guestStart)
    {
        var onlyTheHosts = MatchSettings.Default("ridge", 2).WithSeat(0, new SeatSetting(Start: hostStart));
        var both = onlyTheHosts.WithSeat(1, new SeatSetting(Start: guestStart));
        var random = new DeterministicRandom(4);

        for (var round = 0; round < 20; round++)
        {
            Assert.Equal([new PlayerSetup(hostStart), new PlayerSetup(guestStart)], onlyTheHosts.Describe(TestData.Data, TestData.GameVersion, 1, random.NextInt).Players);
        }

        Assert.Equal(
            [new PlayerSetup(hostStart), new PlayerSetup(guestStart)],
            both.Describe(TestData.Data, TestData.GameVersion, 1, _ => throw new InvalidOperationException("Nothing is left to draw.")).Players);
    }

    [Fact]
    public void TheMatchCarriesTheHostsBuildDataMapAndSeed()
    {
        var match = MatchSettings.Default("ridge", 2).Describe(TestData.Data, TestData.GameVersion, 0xFEED, _ => 0);

        Assert.Equal((TestData.GameVersion, TestData.Data.ContentHash, "ridge", 0xFEEDUL), (match.GameVersion, match.ContentHash, match.MapId, match.Seed));
        Assert.Null(match.FindProblem(TestData.Data));
    }

    [Fact]
    public void ChangingASeatLeavesTheSettingsItWasMadeFromAsTheyWere()
    {
        var settings = MatchSettings.Default("ridge", 2);

        var changed = settings.WithSeat(1, new SeatSetting(SeatKind.Nobody, 0));

        Assert.Equal(new SeatSetting(), settings.Seats[1]);
        Assert.Equal(new SeatSetting(SeatKind.Nobody, 0), changed.Seats[1]);
        Assert.Equal([SeatKind.Person, SeatKind.Nobody], changed.Kinds);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.WithSeat(2, new SeatSetting()));
    }

    public static TheoryData<string, string> Problems() => new()
    {
        { "no such map", "The data has no map 'atoll'." },
        { "too many seats for the map", "The map 'ridge' has 2 starts, which is too few for 3 players." },
        { "a start the map does not have", "Seat 2 is set to start 3, and the map 'ridge' has 2 starts." },
        { "a start below the first", "Seat 1 is set to start 0, and the map 'ridge' has 2 starts." },
        { "two seats on one start", "Seats 1 and 2 are both set to start 2. Each player needs a start of its own." },
        { "a kind that does not exist", "Seat 2 is set to something this build does not know." },
        { "more seats than a match has", "A match has 1 to 8 players, and these settings have 9 seats." },
    };

    [Theory]
    [MemberData(nameof(Problems))]
    public void SettingsThatGiveNoMatchSayWhy(string what, string expected)
    {
        var two = MatchSettings.Default("ridge", 2);
        var settings = what switch
        {
            "no such map" => MatchSettings.Default("atoll", 2),
            "too many seats for the map" => MatchSettings.Default("ridge", 3),
            "a start the map does not have" => two.WithSeat(1, new SeatSetting(Start: 2)),
            "a start below the first" => two.WithSeat(0, new SeatSetting(Start: -1)),
            "two seats on one start" => two.WithSeat(0, new SeatSetting(Start: 1)).WithSeat(1, new SeatSetting(Start: 1)),
            "a kind that does not exist" => two.WithSeat(1, new SeatSetting((SeatKind)7)),
            "more seats than a match has" => MatchSettings.Default("ridge", 9),
            _ => throw new ArgumentOutOfRangeException(nameof(what)),
        };

        Assert.Equal(expected, settings.FindProblem(TestData.Data));
        Assert.Equal(expected, Assert.Throws<ArgumentException>(() => settings.Describe(TestData.Data, TestData.GameVersion, 1, _ => 0)).Message);
    }
}
