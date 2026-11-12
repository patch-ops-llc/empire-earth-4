using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Research;
using Sim.Serialization;
using Sim.Setup;
using Sim.Tech;
using Sim.Tests.Combat;
using Sim.Tests.Data;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Research.ResearchMatches;

namespace Sim.Tests.Research;

/// <summary>
/// The development tier, <see cref="CommandType.DebugSetTier"/>: one track of one player raised to
/// a tier at once and for nothing, by the way a research that finishes takes, and only while the
/// rules switch it on.
/// </summary>
/// <remarks>
/// The numbers are the design's, as in <see cref="LiveRefreshTests"/>. A Hoplite has 130 hit
/// points and walks 2.6 tiles a second; a tier of Protection multiplies hit points by 1.25 and a
/// tier of Mobility multiplies speed by 1.08. A worker has 50 hit points under Protection 1.
/// </remarks>
public class DebugSetTierTests
{
    /// <summary>2.6 tiles a second as 65,536ths of a tile a tick: 8,519.68, rounded half up.</summary>
    private const int HopliteSpeed = 8520;

    /// <summary>2.6 times 1.08: 9,201.25, rounded half up.</summary>
    private const int HopliteSpeedAtMobility2 = 9201;

    /// <summary>2.6 times 1.08 times 1.08: 9,937.35, rounded half up.</summary>
    private const int HopliteSpeedAtMobility3 = 9937;

    private static readonly int Hoplite = CombatMatches.Chassis(CombatMatches.Hoplite);

    private static readonly GameData SwitchedOff =
        DataAssert.Valid(DesignData.Files().Edit(DesignData.RulesPath, "\"spawn\": true", "\"spawn\": false"));

    [Fact]
    public void ItRaisesTheTrackOfThePlayerTheCommandNamesInTheTickItRunsAndForNothing()
    {
        var match = Create();
        match.Simulation.Run(7);
        var events = match.Events();
        var (mine, theirs) = (match.Research.Tech(1), match.Research.Tech(0));
        match.OrderTier(1, Logistics, 2);

        match.Simulation.Tick();

        Assert.Equal([1, 1, 1, 1, 2, 1], match.Research.State.Tiers(1).ToArray());
        Assert.Equal([1, 1, 1, 1, 1, 1], match.Research.State.Tiers(0).ToArray());
        Assert.Equal([new ResearchEvent(SimEventKind.ResearchFinished, 7, 1, Logistics, 2, 1)], events);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);

        // Nothing is paid, and no slot is used: the player's own is as free as it was.
        Assert.Equal((Plenty, Plenty), match.Stock(1));
        Assert.True(match.Slot(1).IsFree);
        Assert.True(match.Slot(1).IsOpen);

        // The player's table is replaced, with what follows from the tiers, and nobody else's is.
        Assert.NotSame(mine, match.Research.Tech(1));
        Assert.Same(theirs, match.Research.Tech(0));
        TechAssert.Same(TechResolver.Resolve(DesignData.Data, DesignData.Mix(1, 1, 1, 1, 2, 1)), match.Research.Tech(1));
        Assert.Equal((55, 70), (mine.SupplyCap, match.Research.Tech(1).SupplyCap));
        Assert.Equal(3, match.Research.Tech(1).Research[Logistics].Tier);
    }

    /// <summary>
    /// The state it leaves is the state a research that finishes leaves. One player researches
    /// Protection, which is done in the tick with the number 899. The other match gets the command
    /// for that tick. Both players were given what the research costs, so nothing but the way to
    /// the tier differs, and the two matches have the same state hash and report the same.
    /// </summary>
    [Fact]
    public void ItLeavesTheStateAResearchThatFinishesLeaves()
    {
        var researched = MatchWithUnits();
        var commanded = MatchWithUnits();
        researched.Stockpiles.Add(0, 100, 100);
        var (researchedEvents, commandedEvents) = (researched.Events(), commanded.Events());

        researched.OrderResearch(0, Protection);
        researched.Simulation.Run(TicksToTier2 - 1);
        commanded.Simulation.Run(TicksToTier2 - 1);
        Assert.NotEqual(researched.World.ComputeHash(), commanded.World.ComputeHash());
        commanded.OrderTier(0, Protection, 2);
        researched.Simulation.Tick();
        commanded.Simulation.Tick();

        Assert.Equal(researched.World.ComputeHash(), commanded.World.ComputeHash());
        Assert.Equal([new ResearchEvent(SimEventKind.ResearchFinished, TicksToTier2 - 1, 0, Protection, 2, 1)], commandedEvents);
        Assert.Equal(researchedEvents[^1], commandedEvents[^1]);
        TechAssert.Same(researched.Research.Tech(0), commanded.Research.Tech(0));

        // And it is the refreshed state: the hurt Hoplite and the hurt worker kept their fraction.
        Assert.Equal(Health(81_250, 162_500), commanded.Health.Get(new EntityId(1)));
        Assert.Equal(Health(31_250, 62_500), commanded.Health.Get(new EntityId(2)));

        static Match MatchWithUnits()
        {
            var match = Create();
            var unit = match.Spawn(0, CombatMatches.Hoplite, "2.5", "2.5");
            var worker = match.SpawnWorker(0, "4.5", "2.5");
            match.Spawn(1, CombatMatches.Hoplite, "21.5", "21.5");
            Assert.Equal((new EntityId(1), new EntityId(2)), (unit, worker));
            match.SetHitPoints(unit, 65_000);
            match.SetHitPoints(worker, 25_000);
            match.RefreshDerivedState();
            return match;
        }
    }

    /// <summary>
    /// A command for a tier several above the track's finishes every tier on the way, lowest
    /// first, as so many researches would one after another: each is reported, and each handler
    /// is called once for each with one track one tier higher.
    /// </summary>
    [Fact]
    public void EveryTierOnTheWayIsFinishedInTurn()
    {
        var match = Create(data: CappedData);
        var calls = new List<(int Player, int[] Before, int[] After)>();
        match.Research.AddTechChangeHandler(new Listener(calls));
        var events = match.Events();
        match.OrderTier(0, Mobility, 4);

        match.Simulation.Tick();

        Assert.Equal(4, match.Research.State.Tier(0, Mobility));
        Assert.Equal(
            [
                new ResearchEvent(SimEventKind.ResearchFinished, 0, 0, Mobility, 2, 1),
                new ResearchEvent(SimEventKind.ResearchFinished, 0, 0, Mobility, 3, 1),
                new ResearchEvent(SimEventKind.ResearchFinished, 0, 0, Mobility, 4, 1),
            ],
            events);
        Assert.Equal(3, calls.Count);
        Assert.Equal((0, DesignData.Mix(1, 1, 1, 1, 1, 1), DesignData.Mix(1, 1, 2, 1, 1, 1)), calls[0], TiersComparer.Instance);
        Assert.Equal((0, DesignData.Mix(1, 1, 2, 1, 1, 1), DesignData.Mix(1, 1, 3, 1, 1, 1)), calls[1], TiersComparer.Instance);
        Assert.Equal((0, DesignData.Mix(1, 1, 3, 1, 1, 1), DesignData.Mix(1, 1, 4, 1, 1, 1)), calls[2], TiersComparer.Instance);
        TechAssert.Same(TechResolver.Resolve(CappedData, DesignData.Mix(1, 1, 4, 1, 1, 1)), match.Research.Tech(0));
    }

    /// <summary>
    /// Units that stand on the map are refreshed once for each tier, so hit points are rounded as
    /// two researches round them. 52,001 of 130,000 is 65,001.25 of 162,500 after the first tier,
    /// kept as 65,001, and that is 81,251.25 of 203,125 after the second, kept as 81,251. Scaled
    /// once from 130,000 to 203,125 it would be 81,251.5625, kept as 81,252.
    /// </summary>
    [Fact]
    public void UnitsOnTheMapAreRefreshedOnceForEachTierAndNobodyElsesAre()
    {
        var match = Create();
        var whole = match.Spawn(0, CombatMatches.Hoplite, "2.5", "2.5");
        var hurt = match.Spawn(0, CombatMatches.Hoplite, "4.5", "2.5");
        var worker = match.SpawnWorker(0, "2.5", "4.5");
        var theirs = match.Spawn(1, CombatMatches.Hoplite, "21.5", "21.5");
        var theirWorker = match.SpawnWorker(1, "19.5", "21.5");
        match.SetHitPoints(hurt, 52_001);
        match.SetHitPoints(worker, 25_000);
        match.SetHitPoints(theirs, 52_001);

        match.OrderTier(0, Protection, 3);
        match.OrderTier(0, Mobility, 3);
        match.Simulation.Tick();

        Assert.Equal(Health(203_125, 203_125), match.Health.Get(whole));
        Assert.Equal(Health(81_251, 203_125), match.Health.Get(hurt));
        Assert.Equal(Health(39_063, 78_125), match.Health.Get(worker));
        Assert.Equal(203_125, match.Research.Tech(0).Blueprints[Hoplite].HitPoints);
        Assert.Equal(HopliteSpeedAtMobility3, match.Movers.Get(whole).Speed.Raw);
        Assert.Equal(match.Research.Tech(0).Blueprints[Hoplite].SpeedPerTick, match.Movers.Get(hurt).Speed);
        Assert.Equal(match.Movers.Get(theirWorker).Speed, match.Movers.Get(worker).Speed);

        Assert.Equal(Health(52_001, 130_000), match.Health.Get(theirs));
        Assert.Equal(Health(50_000, 50_000), match.Health.Get(theirWorker));
        Assert.Equal(HopliteSpeed, match.Movers.Get(theirs).Speed.Raw);
    }

    /// <summary>
    /// A command runs before the systems of its tick, so a unit takes its step of that tick at the
    /// new speed. A research that finishes does so in the last system, and its first step at the
    /// new speed is in the next tick (<see cref="LiveRefreshTests"/>).
    /// </summary>
    [Fact]
    public void AUnitWalksAtItsNewSpeedInTheTickTheCommandRuns()
    {
        var match = Create();
        var walker = match.Spawn(0, CombatMatches.Hoplite, "2.5", "2.5");
        match.OrderMove(0, TestMatches.Point("21.5", "2.5"), walker);
        match.Simulation.Run(5);

        var steps = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            if (i == 2)
            {
                match.OrderTier(0, Mobility, 2);
            }

            var before = match.PositionOf(walker).X.Raw;
            match.Simulation.Tick();
            steps.Add(match.PositionOf(walker).X.Raw - before);
        }

        Assert.Equal([HopliteSpeed, HopliteSpeed, HopliteSpeedAtMobility2, HopliteSpeedAtMobility2], steps);
    }

    /// <summary>
    /// Design 1.4: the epoch is the highest tier three tracks have reached. It is worked out again
    /// after each tier, and each rise is announced after the tier that brought it.
    /// </summary>
    [Fact]
    public void TheEpochIsDerivedAgainAfterEachTierAndEachRiseIsAnnounced()
    {
        var match = Create();
        var events = match.Events();
        match.OrderTier(0, Firepower, 3);
        match.OrderTier(0, Extraction, 3);
        match.OrderTier(1, Mobility, 2);
        match.OrderTier(0, Mobility, 3);

        match.Simulation.Tick();

        Assert.Equal(
            [
                new ResearchEvent(SimEventKind.ResearchFinished, 0, 0, Firepower, 2, 1),
                new ResearchEvent(SimEventKind.ResearchFinished, 0, 0, Firepower, 3, 1),
                new ResearchEvent(SimEventKind.ResearchFinished, 0, 0, Extraction, 2, 1),
                new ResearchEvent(SimEventKind.ResearchFinished, 0, 0, Extraction, 3, 1),
                new ResearchEvent(SimEventKind.ResearchFinished, 0, 1, Mobility, 2, 1),
                new ResearchEvent(SimEventKind.ResearchFinished, 0, 0, Mobility, 2, 2),
                new ResearchEvent(SimEventKind.EpochChanged, 0, 0, Mobility, 2, 2),
                new ResearchEvent(SimEventKind.ResearchFinished, 0, 0, Mobility, 3, 3),
                new ResearchEvent(SimEventKind.EpochChanged, 0, 0, Mobility, 3, 3),
            ],
            events);
        Assert.Equal((3, 1), (match.Research.Tech(0).Epoch, match.Research.Tech(1).Epoch));

        // What the epoch brings is there: a research of a track left behind is at the catch-up price.
        Assert.Equal(ResearchBand.CatchUp, match.Research.Tech(0).Research[Logistics].Price!.Value.Band);
    }

    /// <summary>
    /// What the command is for. Blueprints are resolved again, so an accent of level II, which
    /// needs its track two tiers ahead, is on the Hoplite after one command where it takes
    /// researches that the reach of the epoch spreads over other tracks first. A chassis of the
    /// next epoch is unlocked by three commands.
    /// </summary>
    [Fact]
    public void BlueprintsAreResolvedAgainSoAccentsAndChassisAreReachedByCommandsAlone()
    {
        var match = Create(data: CappedData);
        var legionary = CombatMatches.Chassis("legionary");
        Assert.Empty(match.Research.Tech(0).Blueprints[Hoplite].Accents);
        Assert.False(match.Research.Tech(0).Blueprints[legionary].Unlocked);

        match.OrderTier(0, Firepower, 3);
        match.Simulation.Tick();

        Assert.Equal([new Accent(Firepower, 2)], match.Research.Tech(0).Blueprints[Hoplite].Accents);
        Assert.False(match.Research.Tech(0).Blueprints[legionary].Unlocked);
        Assert.Empty(match.Research.Tech(1).Blueprints[Hoplite].Accents);

        match.OrderTier(0, Protection, 2);
        match.OrderTier(0, Mobility, 2);
        match.Simulation.Tick();

        Assert.True(match.Research.Tech(0).Blueprints[legionary].Unlocked);
        TechAssert.Same(TechResolver.Resolve(CappedData, DesignData.Mix(3, 2, 2, 1, 1, 1)), match.Research.Tech(0));
    }

    /// <summary>
    /// The reach of the epoch, a free slot and the price are rules of starting a research. The
    /// command starts none: a player with no slot and an empty stockpile, at epoch 1, has a track
    /// raised to the epoch cap, three tiers above the epoch, where a research may go two.
    /// </summary>
    [Fact]
    public void NeitherTheReachOfTheEpochNorASlotNorThePriceHoldsItBack()
    {
        var match = MatchSetup.Create(CappedData, TestMaps.Open(24, 24), 1, [PlayerSetup.WithoutStart, PlayerSetup.WithoutStart]);
        Assert.False(match.Slot(0).IsOpen);
        Assert.Equal((0, 0), match.Stock(0));
        Assert.Equal(ResearchStartCheck.NoFreeSlot, match.Research.CheckStart(0, Firepower));

        match.OrderTier(0, Firepower, 4);
        match.Simulation.Tick();

        Assert.Equal([4, 1, 1, 1, 1, 1], match.Research.State.Tiers(0).ToArray());
        Assert.Equal(1, match.Research.Tech(0).Epoch);
        Assert.Equal(ResearchStatus.AboveEpochCap, match.Research.Tech(0).Research[Firepower].Status);
        Assert.Equal((0, 0), match.Stock(0));
        Assert.False(match.Slot(0).IsOpen);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    /// <summary>
    /// A research under way on the track is cancelled first and its price given back, as an order
    /// to cancel does it. Left in its slot it would finish later and put the track at the tier it
    /// was started for.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void AResearchUnderWayOnTheTrackIsCancelledAndItsPriceGivenBack(int tier)
    {
        var match = Create();
        var holder = match.PlaceHolder(0);
        match.Research.OpenSlot(0, holder);
        var events = match.Events();
        match.OrderResearch(0, Mobility);
        match.OrderResearch(0, Firepower);
        match.Simulation.Run(10);
        Assert.Equal((Plenty - 200, Plenty - 200), match.Stock(0));
        Assert.Equal((Firepower, 2, TicksToTier2 - 10), (match.Slot(0, 1).Track, match.Slot(0, 1).Tier, match.Slot(0, 1).TicksLeft));

        match.OrderTier(0, Firepower, tier);
        match.Simulation.Tick();

        Assert.Equal(tier, match.Research.State.Tier(0, Firepower));
        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(0));
        Assert.True(match.Slot(0, 1).IsFree);
        Assert.True(match.Slot(0, 1).IsOpen);
        Assert.Equal(holder, match.Slot(0, 1).Holder);
        Assert.Equal(
            [
                new ResearchEvent(SimEventKind.ResearchStarted, 0, 0, Mobility, 2, 1),
                new ResearchEvent(SimEventKind.ResearchStarted, 0, 0, Firepower, 2, 1, holder),
                new ResearchEvent(SimEventKind.ResearchCancelled, 10, 0, Firepower, 2, 1, holder),
                .. Enumerable.Range(2, tier - 1).Select(reached => new ResearchEvent(SimEventKind.ResearchFinished, 10, 0, Firepower, reached, 1)),
            ],
            events);

        // The research of the other track is left as it is and finishes at its time. Nothing
        // more comes of the one that was cancelled.
        Assert.Equal((Mobility, TicksToTier2 - 11), (match.Slot(0).Track, match.Slot(0).TicksLeft));
        events.Clear();
        match.Simulation.Run(TicksToTier2);
        Assert.Equal([new ResearchEvent(SimEventKind.ResearchFinished, TicksToTier2 - 1, 0, Mobility, 2, 1)], events);
        Assert.Equal(DesignData.Mix(tier, 1, 2, 1, 1, 1), match.Research.State.Tiers(0).ToArray());
        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(0));
    }

    /// <summary>A track is only ever raised: its own tier and every tier below it are refused.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    [InlineData(1)]
    public void ATierThatIsNotAboveTheTracksIsRefused(int tier)
    {
        var match = Create();
        match.OrderTier(0, Extraction, 3);
        match.Simulation.Tick();

        match.OrderTier(0, Extraction, tier);
        match.Simulation.Tick();

        Assert.Equal(3, match.Research.State.Tier(0, Extraction));
        Assert.Equal([Refused(1, 0, Extraction, Fixed.FromInt(tier))], match.Simulation.Events);
        Assert.Equal(1, match.Simulation.DroppedCommandCount);

        // Nothing but the tick has changed.
        var untouched = Create();
        untouched.OrderTier(0, Extraction, 3);
        untouched.Simulation.Run(2);
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
    }

    /// <summary>
    /// No track may pass the match's epoch cap, by research or by this command: with the first
    /// playable's cap of 4, tier 4 is given and tier 5 refused, though the data has nine tiers.
    /// </summary>
    [Fact]
    public void ATierAboveTheEpochCapIsRefused()
    {
        var match = Create(data: CappedData);
        Assert.Equal((4, 9), (match.Research.EpochCap, CappedData.TierCount));
        match.OrderTier(0, Firepower, 5);
        match.OrderTier(1, Firepower, 4);
        match.Simulation.Tick();

        Assert.Equal((1, 4), (match.Research.State.Tier(0, Firepower), match.Research.State.Tier(1, Firepower)));
        Assert.Equal([Refused(0, 0, Firepower, Fixed.FromInt(5))], match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal(1, match.Simulation.DroppedCommandCount);

        // Nothing is raised part of the way.
        TechAssert.Same(TechResolver.Resolve(CappedData, DesignData.Mix(1, 1, 1, 1, 1, 1)), match.Research.Tech(0));
    }

    public static TheoryData<int, int, string> CommandsThatAreNotValid() => new()
    {
        { -1, 0, "2" },
        { 2, 0, "2" },
        { 0, -1, "2" },
        { 0, DesignData.Data.Tracks.Count, "2" },
        { 0, 0, "1" },
        { 0, 0, "0" },
        { 0, 0, "-3" },
        { 0, 0, "10" },
        { 0, 0, "2.5" },
        { 0, 0, "1.99998474121" },
    };

    /// <summary>
    /// A player not in the match, a track the data does not have, the track's own tier, tiers
    /// below it, a tier above the top one, and tiers that are no whole number.
    /// </summary>
    [Theory]
    [MemberData(nameof(CommandsThatAreNotValid))]
    public void ACommandThatIsNotValidChangesNothingAndIsReported(int player, int track, string tier)
    {
        var match = Create();
        match.OrderResearch(0, Firepower);
        match.Simulation.Run(3);
        var command = new Command(CommandType.DebugSetTier, player, targetX: Fraction.Parse(tier).ToFixed(), contentId: track);
        match.Simulation.Commands.Enqueue(match.World.Tick, command);

        match.Simulation.Tick();

        Assert.Equal([Refused(3, player, track, command.TargetX)], match.Simulation.Events);
        Assert.Equal(1, match.Simulation.DroppedCommandCount);

        // Nothing but the tick and the research under way, which was not cancelled, has moved on.
        var untouched = Create();
        untouched.OrderResearch(0, Firepower);
        untouched.Simulation.Run(4);
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
        Assert.Equal((Firepower, 2, TicksToTier2 - 4), (match.Slot(0).Track, match.Slot(0).Tier, match.Slot(0).TicksLeft));
        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(0));
    }

    [Fact]
    public void WithTheSwitchOffTheCommandIsDroppedLikeAnyCommandWithoutAHandler()
    {
        Assert.False(SwitchedOff.Rules.Debug.Spawn);
        var match = Create(data: SwitchedOff);
        match.OrderTier(0, Firepower, 2);
        match.Simulation.Tick();

        Assert.Equal([1, 1, 1, 1, 1, 1], match.Research.State.Tiers(0).ToArray());
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Empty(match.Simulation.Events);

        var untouched = Create(data: SwitchedOff);
        untouched.Simulation.Tick();
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
    }

    [Fact]
    public void WithTheSwitchOffResearchStillRuns()
    {
        var match = Create(data: SwitchedOff);
        match.OrderResearch(0, Firepower);
        match.Simulation.Run(TicksToTier2);

        Assert.Equal(2, match.Research.State.Tier(0, Firepower));
    }

    /// <summary>
    /// A match with the command is saved and restored like any other: before it, in the tick it
    /// runs, right after, and later. The commands cancel a research under way, raise tracks by
    /// one tier and by several, raise the epoch, and refresh hurt units that walk.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(40)]
    [InlineData(41)]
    [InlineData(90)]
    public void AMatchWithTheCommandRestoresAndContinuesFromASnapshot(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(BuildForSnapshots, SnapshotScript(), snapshotTick, lastTick: 120);

    /// <summary>The run of the test above does what its summary says, on both sides of the snapshots.</summary>
    [Fact]
    public void TheRunOfTheSnapshotTestRaisesTiersAndRefreshesUnits()
    {
        var match = BuildForSnapshots();
        var events = match.Events();
        foreach (var (tick, command) in SnapshotScript())
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        match.Simulation.Run(120);

        Assert.Equal(
            [
                (SimEventKind.ResearchStarted, 0, 0, Protection, 2),
                (SimEventKind.ResearchCancelled, 20, 0, Protection, 2),
                (SimEventKind.ResearchFinished, 20, 0, Protection, 2),
                (SimEventKind.ResearchFinished, 20, 0, Protection, 3),
                (SimEventKind.ResearchFinished, 21, 1, Mobility, 2),
                (SimEventKind.ResearchFinished, 40, 0, Firepower, 2),
                (SimEventKind.ResearchFinished, 40, 0, Firepower, 3),
                (SimEventKind.ResearchFinished, 40, 0, Mobility, 2),
                (SimEventKind.EpochChanged, 40, 0, Mobility, 2),
            ],
            events.Select(reported => (reported.Kind, reported.Tick, reported.Player, reported.Track, reported.Tier)));
        Assert.Equal(Health(101_563, 203_125), match.Health.Get(new EntityId(1)));
        Assert.Equal(Health(46_875, 78_125), match.Health.Get(new EntityId(2)));
        Assert.Equal(Health(65_000, 130_000), match.Health.Get(new EntityId(3)));
        Assert.Equal(
            (HopliteSpeedAtMobility2, HopliteSpeedAtMobility2),
            (match.Movers.Get(new EntityId(1)).Speed.Raw, match.Movers.Get(new EntityId(3)).Speed.Raw));
        Assert.Equal((Plenty, Plenty), match.Stock(0));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    /// <summary>
    /// A replay holds the command: a match with it is recorded as a session records one, the
    /// replay is read back from its bytes, and running it gives every recorded hash again and
    /// ends with the tiers the commands gave.
    /// </summary>
    [Fact]
    public void AReplayThatCarriesTheCommandReproducesTheMatch()
    {
        var data = DesignData.Data;
        var description = new MatchDescription("test build", data.ContentHash, data.Maps[0].Id, 7, [new PlayerSetup(0), new PlayerSetup(1)]);
        (int Tick, Command Command)[] script =
        [
            (0, new Command(CommandType.StartResearch, 0, contentId: Protection)),
            (3, CombatMatches.SpawnCommand(1, Hoplite, Start(1))),
            (20, DebugSetTierHandler.CommandFor(0, Protection, 3)),
            (21, DebugSetTierHandler.CommandFor(1, Mobility, 2)),
            (21, DebugSetTierHandler.CommandFor(1, Mobility, 2)),
            (40, DebugSetTierHandler.CommandFor(1, Protection, 4)),
            (41, DebugSetTierHandler.CommandFor(0, Protection, 2)),
        ];

        var match = description.CreateMatch(data);
        var writer = new ByteWriter();
        var recorder = new ReplayRecorder(writer, description, match.World);
        for (var tick = 0; tick < 100; tick++)
        {
            var due = script.Where(entry => entry.Tick == tick).Select(entry => entry.Command).ToArray();
            recorder.RecordCommands(tick, due);
            foreach (var command in due)
            {
                match.Simulation.Commands.Enqueue(tick, command);
            }

            match.Simulation.Tick();
            recorder.AfterTick();
        }

        recorder.Finish();
        Assert.Equal(DesignData.Mix(1, 3, 1, 1, 1, 1), match.Research.State.Tiers(0).ToArray());
        Assert.Equal(DesignData.Mix(1, 4, 2, 1, 1, 1), match.Research.State.Tiers(1).ToArray());
        Assert.Equal(2, match.Simulation.DroppedCommandCount);

        var replay = Replay.Read(new ByteReader(writer.ToArray()));
        Assert.True(replay.IsComplete);
        Assert.Equal(script.Select(entry => (entry.Tick, Bytes(entry.Command))), replay.Commands.Select(entry => (entry.Tick, Bytes(entry.Command))));

        var again = description.CreateMatch(data);
        var result = replay.Run(again);
        Assert.True(result.Reproduced);
        Assert.Equal((100, match.World.ComputeHash()), (result.TicksRun, result.FinalHash));
        Assert.Equal(DesignData.Mix(1, 3, 1, 1, 1, 1), again.Research.State.Tiers(0).ToArray());
        Assert.Equal(DesignData.Mix(1, 4, 2, 1, 1, 1), again.Research.State.Tiers(1).ToArray());

        // The same match without the commands is another match: the replay holds them, and they count.
        var without = description.CreateMatch(data);
        foreach (var (tick, command) in script.Where(entry => entry.Command.Type != CommandType.DebugSetTier))
        {
            without.Simulation.Commands.Enqueue(tick, command);
        }

        without.Simulation.Run(100);
        Assert.NotEqual(match.World.ComputeHash(), without.World.ComputeHash());

        FixedVector2 Start(int player)
        {
            var probe = description.CreateMatch(data);
            for (var i = 0; i < probe.Workers.Count; i++)
            {
                var worker = probe.Workers.EntityAt(i);
                if (probe.Bodies.Get(worker).Owner == player)
                {
                    return probe.Bodies.Get(worker).Position;
                }
            }

            throw new InvalidOperationException($"Player {player} has no worker.");
        }

        static string Bytes(Command command)
        {
            var bytes = new ByteWriter();
            CommandEncoding.Write(bytes, in command);
            return Convert.ToHexString(bytes.ToArray());
        }
    }

    /// <summary>The command survives the bytes a replay and the network give it.</summary>
    [Fact]
    public void TheCommandKeepsItsPlayerItsTrackAndItsTierThroughItsEncoding()
    {
        var command = DebugSetTierHandler.CommandFor(1, Manufacturing, 4);
        Assert.Equal(
            (CommandType.DebugSetTier, 1, Manufacturing, Fixed.FromInt(4), Fixed.Zero, EntityId.None, 0),
            (command.Type, command.Player, command.ContentId, command.TargetX, command.TargetY, command.TargetEntity, command.Entities.Length));

        var writer = new ByteWriter();
        CommandEncoding.Write(writer, in command);
        var read = CommandEncoding.Read(new ByteReader(writer.ToArray()));

        Assert.Equal(
            (CommandType.DebugSetTier, 1, Manufacturing, Fixed.FromInt(4)),
            (read.Type, read.Player, read.ContentId, read.TargetX));
        Assert.Equal(17, (int)CommandType.DebugSetTier);
    }

    private static Match BuildForSnapshots()
    {
        var match = Create();
        var unit = match.Spawn(0, CombatMatches.Hoplite, "2.5", "2.5");
        var worker = match.SpawnWorker(0, "2.5", "4.5");
        var theirs = match.Spawn(1, CombatMatches.Hoplite, "2.5", "20.5");
        match.SpawnWorker(1, "2.5", "22.5");
        Assert.Equal((new EntityId(1), new EntityId(2), new EntityId(3)), (unit, worker, theirs));
        match.SetHitPoints(unit, 65_000);
        match.SetHitPoints(worker, 30_000);
        match.SetHitPoints(theirs, 65_000);
        match.RefreshDerivedState();
        return match;
    }

    private static List<(int Tick, Command Command)> SnapshotScript() =>
    [
        (0, new Command(CommandType.StartResearch, 0, contentId: Protection)),
        (5, CombatMatches.MoveCommand(0, TestMatches.Point("21.5", "2.5"), new EntityId(1), new EntityId(2))),
        (5, CombatMatches.MoveCommand(1, TestMatches.Point("21.5", "20.5"), new EntityId(3), new EntityId(4))),
        (20, DebugSetTierHandler.CommandFor(0, Protection, 3)),
        (21, DebugSetTierHandler.CommandFor(1, Mobility, 2)),
        (40, DebugSetTierHandler.CommandFor(0, Firepower, 3)),
        (40, DebugSetTierHandler.CommandFor(0, Mobility, 2)),
    ];

    private static SimEvent Refused(int tick, int player, int track, Fixed tier) =>
        new(SimEventKind.OrderRefused, tick, player, EntityId.None, EntityId.None, new FixedVector2(tier, Fixed.Zero), (long)CommandType.DebugSetTier);

    private static Health Health(long hitPoints, long maximum) => new() { HitPoints = hitPoints, Maximum = maximum };

    /// <summary>Notes each call a research that finishes makes to the handlers, with the tiers before and after.</summary>
    private sealed class Listener(List<(int Player, int[] Before, int[] After)> calls) : ITechChangeHandler
    {
        public void TechChanged(World world, int player, PlayerTech before, PlayerTech after) =>
            calls.Add((player, [.. before.Tiers], [.. after.Tiers]));
    }

    private sealed class TiersComparer : IEqualityComparer<(int Player, int[] Before, int[] After)>
    {
        public static TiersComparer Instance { get; } = new();

        public bool Equals((int Player, int[] Before, int[] After) x, (int Player, int[] Before, int[] After) y) =>
            x.Player == y.Player && x.Before.SequenceEqual(y.Before) && x.After.SequenceEqual(y.After);

        public int GetHashCode((int Player, int[] Before, int[] After) obj) => obj.Player;
    }
}

/// <summary>The development tier as an order of a test.</summary>
internal static class DebugSetTierOrders
{
    /// <summary>Queues the command that raises a track of a player to a tier, for the next tick to run.</summary>
    public static void OrderTier(this Match match, int player, int track, int tier) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, DebugSetTierHandler.CommandFor(player, track, tier));
}
