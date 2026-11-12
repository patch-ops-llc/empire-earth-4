using Sim.Core;

namespace Ai.Tests;

/// <summary>
/// What it remembers of the enemy (design 5.5, item 4): the signals of design 3.2 that anyone
/// with sight can read, kept from what its player has seen. Each test puts units on Ridge where
/// a unit of its own sees them, or where none does.
/// </summary>
public class AiIntelTests
{
    private const int Firepower = 0;
    private const int Protection = 1;
    private const int Mobility = 2;
    private const int Manufacturing = 3;
    private const int Logistics = 4;

    /// <summary>A tile across the ridge from its start, far from everything of either player.</summary>
    private const int WatchX = 60;
    private const int WatchY = 30;

    [Fact]
    public void ItRemembersTheFightingUnitsItHasSeenByLine()
    {
        var (fixture, intel) = Make();
        fixture.SpawnAt(AiFixture.Player, "peltast", WatchX, WatchY);
        fixture.SpawnRow(1, "hoplite", 3, WatchX, WatchY + 3);
        fixture.SpawnRow(1, "ballista", 2, WatchX, WatchY + 4);
        fixture.SpawnWorkerAt(1, WatchX, WatchY + 5);

        intel.Update();

        // The worker is no part of an army.
        Assert.Equal(5, intel.EnemyUnits);
        Assert.Equal(3, intel.UnitsOfLine(AiFixture.Line("guard")));
        Assert.Equal(2, intel.UnitsOfLine(AiFixture.Line("artillery")));
        Assert.Equal(0, intel.UnitsOfLine(AiFixture.Line("line_infantry")));
        Assert.Equal(AiFixture.Line("guard"), intel.MainLine);
        Assert.Equal(
            (3 * fixture.Sight.ValueOf(AiFixture.Chassis("hoplite"))) + (2 * fixture.Sight.ValueOf(AiFixture.Chassis("ballista"))),
            intel.EnemyValue);

        // Seen again, each is counted once.
        intel.Update();
        Assert.Equal(5, intel.EnemyUnits);
    }

    [Fact]
    public void WhatItDoesNotSeeItDoesNotKnow()
    {
        var (fixture, intel) = Make();
        fixture.SpawnRow(1, "hoplite", 5, WatchX, WatchY);
        fixture.Match.Research.SetTiers(1, [1, 3, 1, 1, 1, 1]);

        intel.Update();

        Assert.Empty(fixture.Sight.Enemies);
        Assert.Equal(0, intel.EnemyUnits);
        Assert.Equal(-1, intel.MainLine);
        Assert.Equal(0, intel.EnemyValue);
        Assert.False(intel.HasAccent(Protection, 1, onMainLine: false));
        Assert.All(Enumerable.Range(0, fixture.Data.Tracks.Count), track => Assert.Equal(0, intel.EnemyTier(track)));
    }

    [Fact]
    public void OfLinesThatAreLevelTheMainLineIsTheFirstOfTheRoster()
    {
        var (fixture, intel) = Make();
        fixture.SpawnAt(AiFixture.Player, "peltast", WatchX, WatchY);
        fixture.SpawnRow(1, "peltast", 2, WatchX, WatchY + 3);
        fixture.SpawnRow(1, "hippeus", 2, WatchX, WatchY + 4);

        intel.Update();

        Assert.True(AiFixture.Line("cavalry_armor") < AiFixture.Line("line_infantry"));
        Assert.Equal(AiFixture.Line("cavalry_armor"), intel.MainLine);

        // One more of the other line, and it is the main line.
        fixture.SpawnAt(1, "peltast", WatchX + 2, WatchY + 3);
        intel.Update();
        Assert.Equal(AiFixture.Line("line_infantry"), intel.MainLine);
    }

    [Fact]
    public void AUnitIsForgottenWhenItIsSeenToDieAndNotBefore()
    {
        var (fixture, intel) = Make();
        var watcher = fixture.SpawnAt(AiFixture.Player, "peltast", WatchX, WatchY);
        var enemies = fixture.SpawnRow(1, "hoplite", 3, WatchX, WatchY + 3);
        var hoplite = fixture.Sight.ValueOf(AiFixture.Chassis("hoplite"));
        intel.Update();

        fixture.Kill(enemies[0]);
        intel.Watch();
        intel.Update();
        Assert.Equal(2, intel.EnemyUnits);
        Assert.Equal(2 * hoplite, intel.EnemyValue);
        Assert.Equal(2, intel.UnitsOfLine(AiFixture.Line("guard")));

        // With its own unit gone it sees none of them, and still takes them to be there.
        fixture.Kill(watcher);
        intel.Watch();
        fixture.Run(Sim.SimInfo.TicksPerSecond);
        Assert.Empty(fixture.Sight.Enemies);
        intel.Update();
        Assert.Equal(2, intel.EnemyUnits);

        // One that dies out of its sight is not known to be dead.
        fixture.Kill(enemies[1]);
        intel.Watch();
        intel.Update();
        Assert.Equal(2, intel.EnemyUnits);
        Assert.Equal(2 * hoplite, intel.EnemyValue);
    }

    /// <summary>Design 2.3: a track one tier above a chassis gives its accent at level I, two above at level II, which includes level I.</summary>
    [Fact]
    public void ItReadsTheAccentsOfTheUnitsItSees()
    {
        var (fixture, intel) = Make();
        fixture.SpawnAt(AiFixture.Player, "peltast", WatchX, WatchY);
        fixture.SpawnRow(1, "peltast", 2, WatchX, WatchY + 3);

        intel.Update();
        Assert.All(Enumerable.Range(0, 5), track => Assert.False(intel.HasAccent(track, 1, onMainLine: false)));

        // Manufacturing one above the Peltast, Logistics two above.
        fixture.Match.Research.SetTiers(1, [1, 1, 1, 2, 3, 1]);
        intel.Update();

        Assert.True(intel.HasAccent(Manufacturing, 1, onMainLine: false));
        Assert.False(intel.HasAccent(Manufacturing, 2, onMainLine: false));
        Assert.True(intel.HasAccent(Logistics, 1, onMainLine: false));
        Assert.True(intel.HasAccent(Logistics, 2, onMainLine: false));
        Assert.False(intel.HasAccent(Firepower, 1, onMainLine: false));
        Assert.False(intel.HasAccent(Protection, 1, onMainLine: false));
        Assert.False(intel.HasAccent(Mobility, 1, onMainLine: false));
    }

    [Fact]
    public void AnAccentOnTheMainLineIsOneAUnitOfThatLineCarries()
    {
        var (fixture, intel) = Make();
        fixture.SpawnAt(AiFixture.Player, "peltast", WatchX, WatchY);

        // Protection 3: the Hoplite of epoch 1 is Hardened II, the Man-at-arms of epoch 3 is not hardened at all.
        fixture.Match.Research.SetTiers(1, [1, 3, 1, 1, 1, 1]);
        fixture.SpawnAt(1, "hoplite", WatchX, WatchY + 3);
        fixture.SpawnRow(1, "peltast", 2, WatchX, WatchY + 4);
        intel.Update();

        // Line infantry is the main line, and a Peltast two tiers under Protection 3 is Hardened II as well.
        Assert.Equal(AiFixture.Line("line_infantry"), intel.MainLine);
        Assert.True(intel.HasAccent(Protection, 2, onMainLine: true));

        var (other, otherIntel) = Make();
        other.SpawnAt(AiFixture.Player, "peltast", WatchX, WatchY);
        other.Match.Research.SetTiers(1, [1, 3, 1, 1, 1, 1]);
        other.SpawnAt(1, "hoplite", WatchX, WatchY + 3);
        other.SpawnRow(1, "man_at_arms", 2, WatchX, WatchY + 4);
        otherIntel.Update();

        // Guard is the only line, and of its units only the one Hoplite carries the accent: that is enough.
        Assert.Equal(AiFixture.Line("guard"), otherIntel.MainLine);
        Assert.True(otherIntel.HasAccent(Protection, 2, onMainLine: true));

        var (third, thirdIntel) = Make();
        third.SpawnAt(AiFixture.Player, "peltast", WatchX, WatchY);
        third.Match.Research.SetTiers(1, [1, 3, 1, 1, 1, 1]);
        third.SpawnAt(1, "hoplite", WatchX, WatchY + 3);
        third.SpawnRow(1, "man_at_arms", 2, WatchX, WatchY + 4);
        third.SpawnRow(1, "knight", 3, WatchX, WatchY - 3);
        thirdIntel.Update();

        // The Knights of epoch 3 are the main line now and carry none; the Hoplite's does not count for them.
        Assert.Equal(AiFixture.Line("cavalry_armor"), thirdIntel.MainLine);
        Assert.True(thirdIntel.HasAccent(Protection, 2, onMainLine: false));
        Assert.False(thirdIntel.HasAccent(Protection, 2, onMainLine: true));
        Assert.False(thirdIntel.HasAccent(Protection, 1, onMainLine: true));
    }

    /// <summary>Design 3.2: inspecting a unit shows its effective tiers, and the chassis a line fields shows its primary track.</summary>
    [Fact]
    public void ItLearnsTheEnemysTiersFromTheUnitsItSees()
    {
        var (fixture, intel) = Make();
        fixture.SpawnAt(AiFixture.Player, "peltast", WatchX, WatchY);
        fixture.Match.Research.SetTiers(1, [2, 4, 3, 2, 1, 1]);
        fixture.SpawnAt(1, "auxiliary", WatchX, WatchY + 3);

        intel.Update();

        // An Auxiliary is of epoch 2: it uses Protection up to 4, and its line is unlocked by Manufacturing.
        Assert.Equal(2, intel.EnemyTier(Firepower));
        Assert.Equal(4, intel.EnemyTier(Protection));
        Assert.Equal(3, intel.EnemyTier(Mobility));
        Assert.Equal(2, intel.EnemyTier(Manufacturing));
        Assert.Equal(0, intel.EnemyTier(Logistics));

        // A Peltast of epoch 1 shows no more than tier 3 of anything, and what was learned stays.
        var (other, otherIntel) = Make();
        other.SpawnAt(AiFixture.Player, "peltast", WatchX, WatchY);
        other.Match.Research.SetTiers(1, [2, 4, 3, 2, 1, 1]);
        var peltast = other.SpawnAt(1, "peltast", WatchX, WatchY + 3);
        otherIntel.Update();
        Assert.Equal(3, otherIntel.EnemyTier(Protection));
        Assert.Equal(1, otherIntel.EnemyTier(Manufacturing));

        other.Kill(peltast);
        otherIntel.Watch();
        otherIntel.Update();
        Assert.Equal(0, otherIntel.EnemyUnits);
        Assert.Equal(3, otherIntel.EnemyTier(Protection));
    }

    /// <summary>Design 3.2: a building shows the tier of its track, and inspecting it shows its owner's Protection, and Firepower when it attacks.</summary>
    [Fact]
    public void ItLearnsTheEnemysTiersFromTheStructuresItSees()
    {
        var (fixture, intel) = Make();
        fixture.SpawnAt(AiFixture.Player, "peltast", WatchX, WatchY);
        var barracks = AiFixture.Structure("barracks");
        var shown = barracks.Track!.Value;
        var tiers = new[] { 2, 3, 1, 1, 1, 1 };
        tiers[shown] = 4;
        fixture.Match.Research.SetTiers(1, tiers);
        fixture.PlaceAt(1, "barracks", WatchX - 2, WatchY - 6);

        intel.Update();

        Assert.Equal(4, intel.EnemyTier(shown));
        if (shown != Protection)
        {
            Assert.Equal(3, intel.EnemyTier(Protection));
        }

        // A Barracks does not attack, so it shows nothing of Firepower. A Tower does.
        if (shown != Firepower)
        {
            Assert.Equal(0, intel.EnemyTier(Firepower));
            fixture.PlaceAt(1, "tower", WatchX + 2, WatchY - 5);
            intel.Update();
            Assert.Equal(2, intel.EnemyTier(Firepower));
        }

        Assert.Equal(0, intel.EnemyUnits);
    }

    /// <summary>Design 3.2: an epoch is announced to everyone, so it is known with nothing in sight.</summary>
    [Fact]
    public void TheEnemysEpochIsKnownWithoutSeeingAnything()
    {
        var (fixture, intel) = Make();
        Assert.Equal(1, intel.EnemyEpoch);

        fixture.Match.Research.SetTiers(1, [3, 3, 3, 1, 1, 1]);

        Assert.Empty(fixture.Sight.Enemies);
        Assert.Equal(3, intel.EnemyEpoch);

        // Its own epoch is not the enemy's.
        fixture.Match.Research.SetTiers(AiFixture.Player, [4, 4, 4, 1, 1, 1]);
        Assert.Equal(3, intel.EnemyEpoch);
    }

    private static (AiFixture Fixture, AiIntel Intel) Make()
    {
        var fixture = new AiFixture(players: 2);
        return (fixture, new AiIntel(fixture.Sight));
    }
}
