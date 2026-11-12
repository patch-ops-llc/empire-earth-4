using Sim.Core;
using Sim.Numerics;

namespace Game.Tests.Sound;

/// <summary>
/// Which sound the events of a tick call for, and for whom: the rules of <see cref="EventSounds"/>.
/// The kinds of event that come with later pieces are given made-up numbers here, since the
/// number of a kind is recorded nowhere and this branch may not have the kind at all.
/// </summary>
public class EventSoundsTests
{
    private const int Me = 0;
    private const int Other = 1;

    private const SimEventKind StructureFinished = (SimEventKind)1001;
    private const SimEventKind StructureRemoved = (SimEventKind)1002;
    private const SimEventKind ResearchFinished = (SimEventKind)1003;
    private const SimEventKind EpochChanged = (SimEventKind)1004;
    private const SimEventKind BuildRefused = (SimEventKind)1005;
    private const SimEventKind TrainRefused = (SimEventKind)1006;
    private const SimEventKind Unheard = (SimEventKind)1999;

    private static readonly SoundEventKinds AllKinds = new(name => name switch
    {
        "StructureFinished" => StructureFinished,
        "StructureRemoved" => StructureRemoved,
        "ResearchFinished" => ResearchFinished,
        "EpochChanged" => EpochChanged,
        "BuildRefused" => BuildRefused,
        "TrainRefused" => TrainRefused,
        _ => null,
    });

    private static readonly SoundEventKinds NoLaterKinds = new(_ => null);

    private static SimEvent Event(SimEventKind kind, int player, int entity = 0, int x = 10, int y = 10) =>
        new(kind, Tick: 5, player, new EntityId(entity), EntityId.None, FixedVector2.FromInts(x, y), Amount: 0);

    private static bool All(in SimEvent happened) => true;

    private static bool OwnOnly(in SimEvent happened) => happened.Player == Me;

    private static SoundCue[] Cues(SoundEventKinds kinds, KnowsEvent knows, params SimEvent[] events)
    {
        var requests = new List<SoundRequest>();
        new EventSounds(Me, kinds).Collect(events, knows, requests);
        return [.. requests.Select(request => request.Cue)];
    }

    [Fact]
    public void AMeleeHitAndAShotThatLandsEachHaveTheirOwnSound()
    {
        var cues = Cues(AllKinds, All, Event(SimEventKind.MeleeHit, Other), Event(SimEventKind.ShotHit, Me));

        Assert.Equal([SoundCue.MeleeHit, SoundCue.ShotHit], cues);
    }

    [Fact]
    public void ADeathIsTheSoundOfAUnitDying()
    {
        Assert.Equal([SoundCue.UnitDied], Cues(AllKinds, All, Event(SimEventKind.Death, Other, entity: 7)));
    }

    [Fact]
    public void AFightThePlayerMayNotKnowOfMakesNoSound()
    {
        var cues = Cues(
            AllKinds,
            OwnOnly,
            Event(SimEventKind.MeleeHit, Other),
            Event(SimEventKind.ShotHit, Other),
            Event(SimEventKind.Death, Other, entity: 7),
            Event(SimEventKind.Death, Other, entity: 8),
            Event(StructureRemoved, Other, entity: 8));

        Assert.Empty(cues);
    }

    [Fact]
    public void ThePlayersOwnFightIsHeardUnderAnyFog()
    {
        var cues = Cues(AllKinds, OwnOnly, Event(SimEventKind.MeleeHit, Me), Event(SimEventKind.Death, Me, entity: 3));

        Assert.Equal([SoundCue.MeleeHit, SoundCue.UnitDied], cues);
    }

    [Fact]
    public void AStructureThatFallsToAttacksIsAStructureLostAndNotAUnitDying()
    {
        // The death and the removal of one entity in one tick, in either order.
        var deathFirst = Cues(AllKinds, All, Event(SimEventKind.Death, Me, entity: 9), Event(StructureRemoved, Me, entity: 9));
        var removalFirst = Cues(AllKinds, All, Event(StructureRemoved, Other, entity: 9), Event(SimEventKind.Death, Other, entity: 9));

        Assert.Equal([SoundCue.StructureLost], deathFirst);
        Assert.Equal([SoundCue.StructureLost], removalFirst);
    }

    [Fact]
    public void AUnitThatDiesInTheTickAStructureFallsIsStillHeard()
    {
        var cues = Cues(
            AllKinds,
            All,
            Event(SimEventKind.Death, Me, entity: 4),
            Event(SimEventKind.Death, Other, entity: 9),
            Event(StructureRemoved, Other, entity: 9));

        Assert.Equal([SoundCue.UnitDied, SoundCue.StructureLost], cues);
    }

    [Fact]
    public void AStructureRemovedWithoutADeathMakesNoSound()
    {
        // A foundation its owner cancelled is removed and is not reported as a death.
        Assert.Empty(Cues(AllKinds, All, Event(StructureRemoved, Me, entity: 9)));
    }

    [Fact]
    public void AFinishedStructureAndAFinishedResearchAreHeardByTheirOwnerAlone()
    {
        var own = Cues(AllKinds, All, Event(StructureFinished, Me), Event(ResearchFinished, Me));
        var others = Cues(AllKinds, All, Event(StructureFinished, Other), Event(ResearchFinished, Other));

        Assert.Equal([SoundCue.StructureFinished, SoundCue.ResearchFinished], own);
        Assert.Empty(others);
    }

    [Fact]
    public void ANewEpochIsAnnouncedToEveryPlayerWhateverTheFogSays()
    {
        var asked = 0;
        bool Nothing(in SimEvent happened)
        {
            asked++;
            return false;
        }

        var cues = Cues(AllKinds, Nothing, Event(EpochChanged, Other), Event(EpochChanged, Me));

        Assert.Equal([SoundCue.EpochReached, SoundCue.EpochReached], cues);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void ARefusedOrderOfAnyKindIsHeardByThePlayerWhoGaveItAlone()
    {
        var own = Cues(AllKinds, All, Event(SimEventKind.OrderRefused, Me), Event(BuildRefused, Me), Event(TrainRefused, Me));
        var others = Cues(AllKinds, All, Event(SimEventKind.OrderRefused, Other), Event(BuildRefused, Other), Event(TrainRefused, Other));

        Assert.Equal([SoundCue.OrderRefused, SoundCue.OrderRefused, SoundCue.OrderRefused], own);
        Assert.Empty(others);
    }

    [Fact]
    public void KindsThatHaveNoSoundArePassedOver()
    {
        var cues = Cues(AllKinds, All, Event(SimEventKind.ShotFired, Me), Event(SimEventKind.ShotLost, Me), Event(Unheard, Me));

        Assert.Empty(cues);
    }

    [Fact]
    public void EveryEventThatCallsForASoundAddsOneRequestAtItsPlaceInTheOrderOfTheEvents()
    {
        var requests = new List<SoundRequest>();
        new EventSounds(Me, AllKinds).Collect(
            [
                Event(SimEventKind.MeleeHit, Me, x: 1, y: 2),
                Event(SimEventKind.ShotFired, Me),
                Event(SimEventKind.MeleeHit, Me, x: 3, y: 4),
                Event(SimEventKind.Death, Other, entity: 5, x: 5, y: 6),
            ],
            All,
            requests);

        Assert.Equal(
            [
                new SoundRequest(SoundCue.MeleeHit, FixedVector2.FromInts(1, 2)),
                new SoundRequest(SoundCue.MeleeHit, FixedVector2.FromInts(3, 4)),
                new SoundRequest(SoundCue.UnitDied, FixedVector2.FromInts(5, 6)),
            ],
            requests);
    }

    [Fact]
    public void CollectingAddsToTheListAndForgetsTheTickBefore()
    {
        var sounds = new EventSounds(Me, AllKinds);
        var requests = new List<SoundRequest>();

        // The structure of the first tick has the number of the unit that dies in the second.
        sounds.Collect([Event(SimEventKind.Death, Me, entity: 9), Event(StructureRemoved, Me, entity: 9)], All, requests);
        sounds.Collect([Event(SimEventKind.Death, Me, entity: 9)], All, requests);

        Assert.Equal([SoundCue.StructureLost, SoundCue.UnitDied], requests.Select(request => request.Cue));
    }

    [Fact]
    public void ABuildWithoutTheLaterKindsPlaysCombatAndRefusedOrdersAndWaitsForTheRest()
    {
        var cues = Cues(
            NoLaterKinds,
            All,
            Event(SimEventKind.MeleeHit, Me),
            Event(SimEventKind.Death, Me, entity: 2),
            Event(SimEventKind.OrderRefused, Me),
            Event(StructureFinished, Me),
            Event(StructureRemoved, Me, entity: 2),
            Event(ResearchFinished, Me),
            Event(EpochChanged, Me),
            Event(BuildRefused, Me),
            Event(TrainRefused, Me));

        Assert.Equal([SoundCue.MeleeHit, SoundCue.UnitDied, SoundCue.OrderRefused], cues);
        Assert.Equal(SoundEventKinds.LaterNames, NoLaterKinds.Missing);
        Assert.True(NoLaterKinds.IsWaiting(SoundCue.StructureFinished));
        Assert.True(NoLaterKinds.IsWaiting(SoundCue.StructureLost));
        Assert.True(NoLaterKinds.IsWaiting(SoundCue.ResearchFinished));
        Assert.True(NoLaterKinds.IsWaiting(SoundCue.EpochReached));
        Assert.False(NoLaterKinds.IsWaiting(SoundCue.MeleeHit));
        Assert.False(NoLaterKinds.IsWaiting(SoundCue.OrderRefused));
    }

    [Fact]
    public void ABuildWithEveryLaterKindWaitsForNothing()
    {
        Assert.Empty(AllKinds.Missing);
        Assert.All(SoundCues.All, cue => Assert.False(AllKinds.IsWaiting(cue)));
    }

    [Fact]
    public void TheKindsOfThisBuildAreFoundByTheNamesTheSimulationGivesThem()
    {
        // Holds on a branch that has none of the later kinds and on one that has them all: a
        // name is missing exactly when the simulation's list of kinds does not have it.
        var kinds = SoundEventKinds.OfThisBuild();

        foreach (var name in SoundEventKinds.LaterNames)
        {
            Assert.Equal(!Enum.IsDefined(typeof(SimEventKind), name), kinds.Missing.Contains(name));
        }

        SimEventKind[] found = [kinds.StructureFinished, kinds.StructureRemoved, kinds.ResearchFinished, kinds.EpochChanged, kinds.BuildRefused, kinds.TrainRefused];
        var present = found.Where(kind => kind != SimEventKind.None).ToArray();
        Assert.Equal(SoundEventKinds.LaterNames.Count - kinds.Missing.Count, present.Length);
        Assert.Equal(present.Length, present.Distinct().Count());
        Assert.All(present, kind => Assert.True(Enum.IsDefined(kind)));
    }
}
