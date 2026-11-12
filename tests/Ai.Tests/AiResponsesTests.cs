using Sim.Data;

namespace Ai.Tests;

/// <summary>
/// The response table (design 5.5, item 4), with the rows of the game's own files: what brings
/// each row into force, what it then asks for, and when it stops asking. Each test puts units
/// on Ridge where a unit of its own sees them and has the table judged.
/// </summary>
public class AiResponsesTests
{
    private const int Firepower = 0;
    private const int Protection = 1;
    private const int Manufacturing = 3;

    /// <summary>The rows, in the order of the design's table.</summary>
    private const int MassWithBadges = 0;
    private const int Overgunned = 1;
    private const int Hardened = 2;
    private const int EpochsAhead = 3;

    private const int WatchX = 60;
    private const int WatchY = 30;

    private static readonly int[] Standardized = [1, 1, 1, 2, 1, 1];
    private static readonly int[] Supplied = [1, 1, 1, 1, 2, 1];
    private static readonly int[] Plain = [1, 1, 1, 1, 1, 1];

    /// <summary>The table is the design's, so both personalities have the same four rows.</summary>
    [Theory]
    [InlineData("swarm")]
    [InlineData("elite")]
    public void TheGamesFilesHoldTheFirstPlayableRowsOfTheDesignsTable(string personality)
    {
        var scouting = GameFiles.Data.PersonalityOf(personality)!.Scouting;

        Assert.Equal(180 * Sim.SimInfo.TicksPerSecond, scouting.EveryTicks);
        Assert.Equal(4, scouting.Responses.Count);

        var mass = scouting.Responses[MassWithBadges];
        var sees = Assert.Single(mass.Sees);
        Assert.Equal([new AiAccent(Manufacturing, 1), new AiAccent(AiFixture.Track("logistics"), 1)], sees.Accents);
        Assert.Equal((15L, 1), (sees.ArmyCountRatio!.Value.Unscaled, sees.ArmyCountRatio!.Value.Scale));
        var artillery = Assert.Single(mass.Does.ArmySupply);
        Assert.Equal((AiFixture.Line("artillery"), 3L, 1), (artillery.Line, artillery.Share.Unscaled, artillery.Share.Scale));
        Assert.Equal(new AiResearchResponse(Firepower, AiResearchUntil.Tiers, 1), mass.Does.Research);
        Assert.Equal(MarkerKind.Chokepoint, mass.Does.Passage);

        var overgunned = scouting.Responses[Overgunned];
        Assert.Equal(2, overgunned.Sees.Count);
        Assert.Equal([new AiAccent(Firepower, 2)], overgunned.Sees[0].Accents);
        Assert.Equal((AiFixture.Line("artillery"), 3), (overgunned.Sees[1].Line, overgunned.Sees[1].Units));
        Assert.Equal(new AiResearchResponse(Protection, AiResearchUntil.AccentLevel, 1), overgunned.Does.Research);
        Assert.Equal(AiFixture.Line("cavalry_armor"), Assert.Single(overgunned.Does.ArmySupply).Line);

        var hardened = Assert.Single(scouting.Responses[Hardened].Sees);
        Assert.Equal([new AiAccent(Protection, 2)], hardened.Accents);
        Assert.True(hardened.OnMainLine);
        Assert.Equal(new AiResearchResponse(Firepower, AiResearchUntil.EnemyTier, Protection), scouting.Responses[Hardened].Does.Research);

        Assert.Equal(2, Assert.Single(scouting.Responses[EpochsAhead].Sees).EpochsAhead);
        Assert.True(scouting.Responses[EpochsAhead].Does.CatchUpFirst);
    }

    [Fact]
    public void WithNothingSeenNoRowIsInForce()
    {
        var (fixture, intel, responses) = Make();
        fixture.SpawnRow(AiFixture.Player, "peltast", 2, WatchX, WatchY);

        Judge(intel, responses);

        Assert.All(Enumerable.Range(0, 4), row => Assert.False(responses.IsInForce(row)));
        Assert.Empty(responses.ArmySupply);
        Assert.Empty(responses.ResearchFirst);
        Assert.Null(responses.Passage);
        Assert.False(responses.CatchUpFirst);
    }

    /// <summary>"Standardized or Supplied badges and an army more than 1.5x its own count."</summary>
    [Theory]
    [InlineData("standardized", 4, true)]
    [InlineData("supplied", 4, true)]
    [InlineData("standardized", 3, false)]
    [InlineData("plain", 4, false)]
    [InlineData("plain", 9, false)]
    public void TheFirstRowNeedsTheBadgesAndTheNumbers(string badges, int enemies, bool inForce)
    {
        var (fixture, intel, responses) = Make();
        fixture.SpawnRow(AiFixture.Player, "peltast", 2, WatchX, WatchY);
        fixture.Match.Research.SetTiers(1, badges == "standardized" ? Standardized : badges == "supplied" ? Supplied : Plain);
        fixture.SpawnRow(1, "peltast", enemies, WatchX - 4, WatchY + 3);

        Judge(intel, responses);

        Assert.Equal(inForce, responses.IsInForce(MassWithBadges));
        Assert.All([Overgunned, Hardened, EpochsAhead], row => Assert.False(responses.IsInForce(row)));
        if (inForce)
        {
            // Artillery to 30% of its army's supply, Firepower next, and it fights at chokepoints.
            var share = Assert.Single(responses.ArmySupply);
            Assert.Equal(AiFixture.Line("artillery"), share.Line);
            Assert.Equal([Firepower], responses.ResearchFirst);
            Assert.Equal(MarkerKind.Chokepoint, responses.Passage);
            Assert.False(responses.CatchUpFirst);
        }
        else
        {
            Assert.Empty(responses.ArmySupply);
            Assert.Empty(responses.ResearchFirst);
            Assert.Null(responses.Passage);
        }
    }

    /// <summary>"Researches Firepower next": one tier above the one it had when the row came into force.</summary>
    [Fact]
    public void FirepowerNextIsOneTierAndNoMore()
    {
        var (fixture, intel, responses) = Make();
        fixture.SpawnRow(AiFixture.Player, "peltast", 2, WatchX, WatchY);
        fixture.Match.Research.SetTiers(AiFixture.Player, [2, 1, 1, 1, 1, 1]);
        fixture.Match.Research.SetTiers(1, Standardized);
        fixture.SpawnRow(1, "peltast", 4, WatchX - 4, WatchY + 3);

        Judge(intel, responses);
        Assert.Equal([Firepower], responses.ResearchFirst);

        fixture.Match.Research.SetTiers(AiFixture.Player, [3, 1, 1, 1, 1, 1]);
        Judge(intel, responses);

        // The row stays in force with the rest of what it does.
        Assert.True(responses.IsInForce(MassWithBadges));
        Assert.Empty(responses.ResearchFirst);
        Assert.Single(responses.ArmySupply);
        Assert.Equal(MarkerKind.Chokepoint, responses.Passage);
    }

    /// <summary>"Overgunned II or three or more Artillery."</summary>
    [Theory]
    [InlineData("ballista", 3, 1, true)]
    [InlineData("ballista", 2, 1, false)]
    [InlineData("peltast", 1, 3, true)]
    [InlineData("peltast", 5, 2, false)]
    public void TheSecondRowNeedsOvergunnedTwoOrThreeArtillery(string chassis, int enemies, int enemyFirepower, bool inForce)
    {
        var (fixture, intel, responses) = Make();
        fixture.SpawnRow(AiFixture.Player, "peltast", 6, WatchX - 2, WatchY);
        fixture.Match.Research.SetTiers(1, [enemyFirepower, 1, 1, 1, 1, 1]);
        fixture.SpawnRow(1, chassis, enemies, WatchX - 2, WatchY + 3);

        Judge(intel, responses);

        Assert.Equal(inForce, responses.IsInForce(Overgunned));
        Assert.All([MassWithBadges, Hardened, EpochsAhead], row => Assert.False(responses.IsInForce(row)));
        if (inForce)
        {
            // Protection until its main line is Hardened I, and toward Cavalry/Armor.
            Assert.Equal([Protection], responses.ResearchFirst);
            Assert.Equal(AiFixture.Line("cavalry_armor"), Assert.Single(responses.ArmySupply).Line);
            Assert.Null(responses.Passage);
        }
    }

    /// <summary>
    /// "Until its main line is Hardened I": the newest chassis of the line with the largest
    /// share carries the accent. A unit carries two accents at most (design 2.3), so one tier of
    /// Protection is not always enough.
    /// </summary>
    [Theory]
    [InlineData(new[] { 1, 1, 1, 1, 1, 1 }, false)]
    [InlineData(new[] { 1, 2, 1, 1, 1, 1 }, true)]
    [InlineData(new[] { 1, 2, 1, 3, 3, 1 }, false)]
    [InlineData(new[] { 1, 3, 1, 3, 3, 1 }, true)]
    public void ProtectionIsResearchedUntilItsMainLineIsHardened(int[] ownTiers, bool hardened)
    {
        var (fixture, intel, responses) = Make(lines: [("cavalry_armor", 1), ("line_infantry", 3), ("guard", 3)]);
        Assert.Equal(AiFixture.Line("line_infantry"), AiResponses.MainLineOf(AiFixture.Personality(lines: [("cavalry_armor", 1), ("line_infantry", 3), ("guard", 3)])));
        fixture.SpawnRow(AiFixture.Player, "peltast", 6, WatchX - 2, WatchY);
        fixture.SpawnRow(1, "ballista", 3, WatchX - 2, WatchY + 3);
        fixture.Match.Research.SetTiers(AiFixture.Player, ownTiers);

        Judge(intel, responses);

        var newest = AiArmy.NewestUnlocked(fixture.Sight.Tech, AiFixture.Line("line_infantry"))!;
        Assert.Equal(hardened, newest.AccentLevel(Protection) >= 1);
        Assert.True(responses.IsInForce(Overgunned));
        Assert.Equal(hardened ? [] : [Protection], responses.ResearchFirst);
        Assert.Single(responses.ArmySupply);
    }

    /// <summary>"Hardened II on the enemy's main line: researches Firepower up to the enemy's Protection tier."</summary>
    [Fact]
    public void TheThirdRowResearchesFirepowerUpToTheProtectionItHasSeen()
    {
        var (fixture, intel, responses) = Make();
        fixture.SpawnRow(AiFixture.Player, "peltast", 6, WatchX - 2, WatchY);
        fixture.Match.Research.SetTiers(1, [1, 3, 1, 1, 1, 1]);
        fixture.SpawnRow(1, "hoplite", 2, WatchX - 2, WatchY + 3);

        Judge(intel, responses);

        Assert.True(responses.IsInForce(Hardened));
        Assert.Equal(3, intel.EnemyTier(Protection));
        Assert.Equal([Firepower], responses.ResearchFirst);
        Assert.Empty(responses.ArmySupply);

        fixture.Match.Research.SetTiers(AiFixture.Player, [2, 1, 1, 1, 1, 1]);
        Judge(intel, responses);
        Assert.Equal([Firepower], responses.ResearchFirst);

        fixture.Match.Research.SetTiers(AiFixture.Player, [3, 1, 1, 1, 1, 1]);
        Judge(intel, responses);
        Assert.True(responses.IsInForce(Hardened));
        Assert.Empty(responses.ResearchFirst);
    }

    [Fact]
    public void HardenedTwoOnALineThatIsNotTheEnemysMainLineIsNotTheThirdRow()
    {
        var (fixture, intel, responses) = Make();
        fixture.SpawnRow(AiFixture.Player, "peltast", 8, WatchX - 4, WatchY);
        fixture.Match.Research.SetTiers(1, [1, 3, 1, 1, 1, 1]);
        fixture.SpawnAt(1, "hoplite", WatchX - 2, WatchY + 3);
        fixture.SpawnRow(1, "knight", 2, WatchX - 2, WatchY + 4);

        Judge(intel, responses);

        Assert.True(intel.HasAccent(Protection, 2, onMainLine: false));
        Assert.False(responses.IsInForce(Hardened));
        Assert.Empty(responses.ResearchFirst);
    }

    /// <summary>"Enemy epoch two or more above its own: buys catch-up tiers before anything else."</summary>
    [Theory]
    [InlineData(new[] { 3, 3, 3, 1, 1, 1 }, new[] { 1, 1, 1, 1, 1, 1 }, true)]
    [InlineData(new[] { 4, 4, 4, 1, 1, 1 }, new[] { 2, 2, 2, 1, 1, 1 }, true)]
    [InlineData(new[] { 2, 2, 2, 1, 1, 1 }, new[] { 1, 1, 1, 1, 1, 1 }, false)]
    [InlineData(new[] { 3, 3, 3, 1, 1, 1 }, new[] { 2, 2, 2, 1, 1, 1 }, false)]
    [InlineData(new[] { 3, 3, 1, 1, 1, 1 }, new[] { 1, 1, 1, 1, 1, 1 }, false)]
    public void TheLastRowGoesByTheEpochsWhichAreAnnounced(int[] enemyTiers, int[] ownTiers, bool inForce)
    {
        var (fixture, intel, responses) = Make();
        fixture.Match.Research.SetTiers(1, enemyTiers);
        fixture.Match.Research.SetTiers(AiFixture.Player, ownTiers);

        Judge(intel, responses);

        // Nothing of the enemy is in sight.
        Assert.Empty(fixture.Sight.Enemies);
        Assert.Equal(inForce, responses.IsInForce(EpochsAhead));
        Assert.Equal(inForce, responses.CatchUpFirst);
        Assert.Empty(responses.ResearchFirst);
        Assert.Empty(responses.ArmySupply);
    }

    [Fact]
    public void RowsInForceTogetherAreActedOnInTheOrderOfTheFile()
    {
        var (fixture, intel, responses) = Make();
        fixture.SpawnRow(AiFixture.Player, "peltast", 2, WatchX, WatchY);

        // Epoch 3, and a Ballista of epoch 1 is Standardized II and Overgunned II: its two accents.
        fixture.Match.Research.SetTiers(1, [3, 3, 3, 4, 1, 1]);
        fixture.SpawnRow(1, "ballista", 4, WatchX - 4, WatchY + 3);

        Judge(intel, responses);

        Assert.True(responses.IsInForce(MassWithBadges));
        Assert.True(responses.IsInForce(Overgunned));
        Assert.False(responses.IsInForce(Hardened));
        Assert.True(responses.IsInForce(EpochsAhead));
        Assert.Equal([AiFixture.Line("artillery"), AiFixture.Line("cavalry_armor")], responses.ArmySupply.Select(share => share.Line));

        Assert.Equal([Firepower, Protection], responses.ResearchFirst);
        Assert.Equal(MarkerKind.Chokepoint, responses.Passage);
        Assert.True(responses.CatchUpFirst);
    }

    /// <summary>
    /// What it knows is as old as its last look, so a row is not dropped in the round a count
    /// dips: it stays in force for the time between two scouts after it last held.
    /// </summary>
    [Fact]
    public void ARowStaysInForceForTheTimeBetweenTwoScoutsAfterItLastHeld()
    {
        var (fixture, intel, responses) = Make();
        var every = GameFiles.Data.PersonalityOf("swarm")!.Scouting.EveryTicks;
        fixture.SpawnRow(AiFixture.Player, "peltast", 6, WatchX - 2, WatchY);
        var enemies = fixture.SpawnRow(1, "ballista", 3, WatchX - 2, WatchY + 3);
        Judge(intel, responses);
        Assert.True(responses.IsInForce(Overgunned));
        var lastHeld = fixture.Match.World.Tick;

        // One Ballista is seen to die: two are left, and the row no longer holds.
        fixture.Kill(enemies[0]);
        Judge(intel, responses);
        Assert.Equal(2, intel.UnitsOfLine(AiFixture.Line("artillery")));
        Assert.True(responses.IsInForce(Overgunned));
        Assert.Single(responses.ArmySupply);

        fixture.Kill(enemies[1], enemies[2]);
        intel.Watch();
        fixture.Run(lastHeld + every - 1 - fixture.Match.World.Tick);
        Judge(intel, responses);
        Assert.True(responses.IsInForce(Overgunned));

        fixture.Run(1);
        Judge(intel, responses);
        Assert.False(responses.IsInForce(Overgunned));
        Assert.Empty(responses.ArmySupply);
        Assert.Empty(responses.ResearchFirst);

        // Seen again, it is in force again at once.
        fixture.SpawnRow(1, "ballista", 3, WatchX - 2, WatchY + 4);
        Judge(intel, responses);
        Assert.True(responses.IsInForce(Overgunned));
    }

    /// <summary>The reaction delay of the difficulty (design 5.5) is waited before a row comes into force.</summary>
    [Fact]
    public void ARowComesIntoForceAfterTheReactionDelay()
    {
        var delay = Sim.SimInfo.TicksPerSecond;
        var (fixture, intel, responses) = Make(AiFixture.Fight(reactionTicks: delay));
        fixture.SpawnRow(AiFixture.Player, "peltast", 6, WatchX - 2, WatchY);
        fixture.SpawnRow(1, "ballista", 3, WatchX - 2, WatchY + 3);

        Judge(intel, responses);
        Assert.False(responses.IsInForce(Overgunned));

        fixture.Run(delay - 1);
        Judge(intel, responses);
        Assert.False(responses.IsInForce(Overgunned));
        Assert.Empty(responses.ArmySupply);

        fixture.Run(1);
        Judge(intel, responses);
        Assert.True(responses.IsInForce(Overgunned));
        Assert.Single(responses.ArmySupply);
    }

    [Fact]
    public void APersonalityWithoutATableRespondsToNothing()
    {
        var fixture = new AiFixture(players: 2);
        var intel = new AiIntel(fixture.Sight);
        var responses = new AiResponses(fixture.Sight, intel, AiFixture.Personality(lines: [("line_infantry", 1)]));
        fixture.SpawnRow(AiFixture.Player, "peltast", 2, WatchX, WatchY);
        fixture.Match.Research.SetTiers(1, [3, 3, 3, 2, 1, 1]);
        fixture.SpawnRow(1, "ballista", 4, WatchX - 4, WatchY + 3);

        Judge(intel, responses);

        Assert.Empty(responses.ArmySupply);
        Assert.Empty(responses.ResearchFirst);
        Assert.Null(responses.Passage);
        Assert.False(responses.CatchUpFirst);
    }

    private static (AiFixture Fixture, AiIntel Intel, AiResponses Responses) Make(AiFightDef? fight = null, (string Line, int Share)[]? lines = null)
    {
        var fixture = new AiFixture(players: 2);
        var intel = new AiIntel(fixture.Sight);
        var personality = AiFixture.Personality(lines: lines ?? [("line_infantry", 3), ("cavalry_armor", 1)]) with
        {
            Fight = fight ?? AiFixture.Fight(),
            Scouting = GameFiles.Data.PersonalityOf("swarm")!.Scouting,
        };
        return (fixture, intel, new AiResponses(fixture.Sight, intel, personality));
    }

    /// <summary>One round: what happened is read, the memory is brought up to date and the table is judged.</summary>
    private static void Judge(AiIntel intel, AiResponses responses)
    {
        intel.Watch();
        intel.Update();
        responses.Update();
    }
}
