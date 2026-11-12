using Sim.Combat;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Research.ResearchMatches;

namespace Sim.Tests.Research;

/// <summary>
/// Design 2.3: "when a research finishes, every existing unit updates". What a unit keeps in its
/// own state is brought up to date in the tick the research finishes: its hit points, which keep
/// their fraction of the maximum, and its speed.
/// </summary>
/// <remarks>
/// The numbers are the design's. A Hoplite has 130 hit points and walks 2.6 tiles a second; a
/// tier of Protection multiplies hit points by 1.25 and a tier of Mobility multiplies speed by
/// 1.08. A worker has 50 hit points under Protection 1 and walks 3 tiles a second whatever is
/// researched.
/// </remarks>
public class LiveRefreshTests
{
    /// <summary>2.6 tiles a second as 65,536ths of a tile a tick: 8,519.68, rounded half up.</summary>
    private const int HopliteSpeed = 8520;

    /// <summary>2.6 times 1.08: 9,201.25, rounded half up.</summary>
    private const int HopliteSpeedAtMobility2 = 9201;

    /// <summary>3 tiles a second: 9,830.4, rounded half up.</summary>
    private const int WorkerSpeed = 9830;

    private static readonly int Hoplite = CombatMatches.Chassis(CombatMatches.Hoplite);

    /// <summary>The first line of the hand-over's "done when".</summary>
    [Fact]
    public void AUnitAndAWorkerAtHalfTheirHitPointsAreAtHalfAfterAProtectionTier()
    {
        var match = Create();
        var unit = match.Spawn(0, CombatMatches.Hoplite, "2.5", "2.5");
        var worker = match.SpawnWorker(0, "4.5", "2.5");
        match.SetHitPoints(unit, 65_000);
        match.SetHitPoints(worker, 25_000);
        Assert.Equal((130_000, 50_000), (match.Health.Get(unit).Maximum, match.Health.Get(worker).Maximum));

        match.OrderResearch(0, Protection);
        match.Simulation.Run(TicksToTier2 - 1);
        Assert.Equal(Health(65_000, 130_000), match.Health.Get(unit));
        Assert.Equal(Health(25_000, 50_000), match.Health.Get(worker));

        match.Simulation.Tick();

        Assert.Equal(Health(81_250, 162_500), match.Health.Get(unit));
        Assert.Equal(Health(31_250, 62_500), match.Health.Get(worker));
        Assert.Equal(162_500, match.Research.Tech(0).Blueprints[Hoplite].HitPoints);
    }

    /// <summary>
    /// Whole stays whole, alive stays alive, and what is between is rounded once, half up, to a
    /// thousandth of a hit point: 52,001 of 130,000 is 65,001.25 of 162,500.
    /// </summary>
    [Fact]
    public void EveryUnitAndWorkerOfThePlayerKeepsItsFractionAndNobodyElseChanges()
    {
        var match = Create();
        var whole = match.Spawn(0, CombatMatches.Hoplite, "2.5", "2.5");
        var hurt = match.Spawn(0, CombatMatches.Hoplite, "4.5", "2.5");
        var nearlyDead = match.Spawn(0, CombatMatches.Hoplite, "6.5", "2.5");
        var peltast = match.Spawn(0, CombatMatches.Peltast, "8.5", "2.5");
        var wholeWorker = match.SpawnWorker(0, "2.5", "4.5");
        var hurtWorker = match.SpawnWorker(0, "4.5", "4.5");
        var theirs = match.Spawn(1, CombatMatches.Hoplite, "21.5", "21.5");
        var theirWorker = match.SpawnWorker(1, "19.5", "21.5");
        match.SetHitPoints(hurt, 52_001);
        match.SetHitPoints(nearlyDead, 1);
        match.SetHitPoints(peltast, 20_000);
        match.SetHitPoints(hurtWorker, 1);
        match.SetHitPoints(theirs, 52_001);
        match.SetHitPoints(theirWorker, 25_000);

        // Another track changes nobody's hit points.
        match.OrderResearch(0, Firepower);
        match.Simulation.Run(TicksToTier2);
        Assert.Equal(Health(130_000, 130_000), match.Health.Get(whole));
        Assert.Equal(Health(52_001, 130_000), match.Health.Get(hurt));
        Assert.Equal(Health(50_000, 50_000), match.Health.Get(wholeWorker));

        match.OrderResearch(0, Protection);
        match.Simulation.Run(TicksToTier2);

        Assert.Equal(Health(162_500, 162_500), match.Health.Get(whole));
        Assert.Equal(Health(65_001, 162_500), match.Health.Get(hurt));
        Assert.Equal(Health(1, 162_500), match.Health.Get(nearlyDead));
        Assert.Equal(Health(25_000, 75_000), match.Health.Get(peltast));
        Assert.Equal(Health(62_500, 62_500), match.Health.Get(wholeWorker));
        Assert.Equal(Health(1, 62_500), match.Health.Get(hurtWorker));
        Assert.Equal(Health(52_001, 130_000), match.Health.Get(theirs));
        Assert.Equal(Health(25_000, 50_000), match.Health.Get(theirWorker));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    /// <summary>The second line of the hand-over's "done when".</summary>
    [Fact]
    public void AUnitWalksAtItsNewSpeedInTheTickAfterAMobilityTier()
    {
        var match = Create();
        var walker = match.Spawn(0, CombatMatches.Hoplite, "2.5", "2.5");
        var standing = match.Spawn(0, CombatMatches.Hoplite, "2.5", "6.5");
        var worker = match.SpawnWorker(0, "2.5", "10.5");
        var theirs = match.Spawn(1, CombatMatches.Hoplite, "2.5", "21.5");
        Assert.Equal(HopliteSpeed, match.Movers.Get(walker).Speed.Raw);
        Assert.Equal(WorkerSpeed, match.Movers.Get(worker).Speed.Raw);

        // The research finishes in the tick with the number TicksToTier2 - 1. The walk is ordered
        // ten ticks before it and goes on long after.
        match.OrderResearch(0, Mobility);
        match.Simulation.Run(TicksToTier2 - 10);
        match.OrderMove(0, TestMatches.Point("21.5", "2.5"), walker);
        match.OrderMove(0, TestMatches.Point("21.5", "10.5"), worker);
        match.OrderMove(1, TestMatches.Point("21.5", "21.5"), theirs);

        var steps = new List<(int Tick, int Walker, int Worker, int Theirs)>();
        for (var i = 0; i < 20; i++)
        {
            var before = (match.PositionOf(walker).X.Raw, match.PositionOf(worker).X.Raw, match.PositionOf(theirs).X.Raw);
            var tick = match.World.Tick;
            match.Simulation.Tick();
            steps.Add((
                tick,
                match.PositionOf(walker).X.Raw - before.Item1,
                match.PositionOf(worker).X.Raw - before.Item2,
                match.PositionOf(theirs).X.Raw - before.Item3));
        }

        var last = TicksToTier2 - 1;
        Assert.Equal(
            Enumerable.Range(last - 9, 20).Select(tick => (tick, tick <= last ? HopliteSpeed : HopliteSpeedAtMobility2, WorkerSpeed, HopliteSpeed)),
            steps);
        Assert.Equal(HopliteSpeedAtMobility2, match.Movers.Get(standing).Speed.Raw);
        Assert.Equal(match.Research.Tech(0).Blueprints[Hoplite].SpeedPerTick, match.Movers.Get(walker).Speed);
        Assert.Equal(WorkerSpeed, match.Movers.Get(worker).Speed.Raw);
        Assert.Equal(HopliteSpeed, match.Movers.Get(theirs).Speed.Raw);
        Assert.Equal(TestMatches.Point("2.5", "2.5").Y, match.PositionOf(walker).Y);
    }

    /// <summary>
    /// M14's scenario, in one match and on units that stand on the map: two players start with the
    /// same Hoplites, research different tracks, and end with different Hoplites. Player 0 goes
    /// for Protection and then Firepower, player 1 for Mobility. Then one of each fights the
    /// other, and the blows differ as the blueprints say.
    /// </summary>
    [Fact]
    public void TwoPlayersWithDifferentMixesGetDifferentStatsForTheSameChassisInOneMatch()
    {
        var match = Create();
        var elite = match.Spawn(0, CombatMatches.Hoplite, "2.5", "2.5");
        var swift = match.Spawn(1, CombatMatches.Hoplite, "21.5", "21.5");
        Assert.Equal(match.Health.Get(elite), match.Health.Get(swift));
        Assert.Equal(match.Movers.Get(elite).Speed, match.Movers.Get(swift).Speed);
        Assert.Equal(match.Combat.ProfileOf(0, Hoplite).Damage, match.Combat.ProfileOf(1, Hoplite).Damage);

        match.OrderResearch(0, Protection);
        match.OrderResearch(1, Mobility);
        match.Simulation.Run(TicksToTier2);
        match.OrderResearch(0, Firepower);
        match.Simulation.Run(TicksToTier2);

        Assert.Equal([2, 2, 1, 1, 1, 1], match.Research.State.Tiers(0).ToArray());
        Assert.Equal([1, 1, 2, 1, 1, 1], match.Research.State.Tiers(1).ToArray());
        Assert.Equal(Health(162_500, 162_500), match.Health.Get(elite));
        Assert.Equal(Health(130_000, 130_000), match.Health.Get(swift));
        Assert.Equal((HopliteSpeed, HopliteSpeedAtMobility2), (match.Movers.Get(elite).Speed.Raw, match.Movers.Get(swift).Speed.Raw));

        // Each walks to the other and they trade blows: one blow of each is the blueprint's damage
        // per hit, 8 a second at Firepower 1 and 10 at Firepower 2 with one attack a second.
        match.OrderAttack(0, swift, elite);
        match.OrderAttack(1, elite, swift);
        while (match.HitPointsOf(elite) == 162_500 || match.HitPointsOf(swift) == 130_000)
        {
            Assert.True(match.World.Tick < 3 * TicksToTier2, "The two never met.");
            match.Simulation.Tick();
        }

        Assert.Equal(match.Blueprint(0, CombatMatches.Hoplite).DamagePerHit.RoundHalfUp(1000), 130_000 - match.HitPointsOf(swift));
        Assert.Equal(match.Blueprint(1, CombatMatches.Hoplite).DamagePerHit.RoundHalfUp(1000), 162_500 - match.HitPointsOf(elite));
        Assert.Equal((10_000, 8_000), (130_000 - match.HitPointsOf(swift), 162_500 - match.HitPointsOf(elite)));
    }

    /// <summary>
    /// A unit destroyed in the tick the research finishes is still in the stores until the tick
    /// ends, and is left as it fell. So is whatever has hit points and is neither a unit of a
    /// chassis nor a worker: what places such a thing brings its own handler.
    /// </summary>
    [Fact]
    public void TheDeadAndWhatIsNeitherUnitNorWorkerAreLeftAlone()
    {
        var match = Create();
        var fallen = match.Spawn(0, CombatMatches.Hoplite, "2.5", "2.5");
        var other = match.PlaceHolder(0, 6, 6);
        match.Health.Add(other, Health(300, 400));
        match.SetHitPoints(fallen, 40_000);
        var seen = new List<Health>();
        match.Research.AddTechChangeHandler(new Look(match, fallen, seen));

        match.OrderResearch(0, Protection);
        match.Simulation.Run(TicksToTier2 - 1);
        match.World.Entities.Destroy(fallen);
        match.Simulation.Tick();

        Assert.Equal([Health(40_000, 130_000)], seen);
        Assert.False(match.Health.Contains(fallen));
        Assert.Equal(Health(300, 400), match.Health.Get(other));
        Assert.Equal(2, match.Research.State.Tier(0, Protection));
    }

    /// <summary>
    /// The last line of the hand-over's "done when": a snapshot taken on either side of a
    /// finishing research restores and continues. Two researches finish, of Protection in the
    /// tick with the number 899 and of Mobility ten ticks later, while hurt units and workers of
    /// both players stand and walk. Snapshots: long before, right before and right after each,
    /// and at the end.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(TicksToTier2 - 1)]
    [InlineData(TicksToTier2)]
    [InlineData(TicksToTier2 + 1)]
    [InlineData(TicksToTier2 + 9)]
    [InlineData(TicksToTier2 + 10)]
    [InlineData(TicksToTier2 + 60)]
    public void ASnapshotOnEitherSideOfAFinishingResearchRestoresAndContinues(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(BuildForSnapshots, SnapshotScript(), snapshotTick, TicksToTier2 + 60);

    /// <summary>The run of the test above does change hit points and speed, on both sides of the snapshots.</summary>
    [Fact]
    public void TheRunOfTheSnapshotTestRefreshesUnitsTwice()
    {
        var match = BuildForSnapshots();
        var events = match.Events();
        foreach (var (tick, command) in SnapshotScript())
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        match.Simulation.Run(TicksToTier2 + 60);

        Assert.Equal(
            [(SimEventKind.ResearchFinished, TicksToTier2 - 1, 0, Protection), (SimEventKind.ResearchFinished, TicksToTier2 + 9, 1, Mobility)],
            events.Where(reported => reported.Kind == SimEventKind.ResearchFinished).Select(reported => (reported.Kind, reported.Tick, reported.Player, reported.Track)));
        Assert.Equal(Health(81_250, 162_500), match.Health.Get(new EntityId(1)));
        Assert.Equal(Health(37_500, 62_500), match.Health.Get(new EntityId(2)));
        Assert.Equal(Health(65_000, 130_000), match.Health.Get(new EntityId(3)));
        Assert.Equal(
            (HopliteSpeed, WorkerSpeed, HopliteSpeedAtMobility2, WorkerSpeed),
            (Speed(match, 1), Speed(match, 2), Speed(match, 3), Speed(match, 4)));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
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
        (10, new Command(CommandType.StartResearch, 1, contentId: Mobility)),
        (TicksToTier2 - 20, CombatMatches.MoveCommand(0, TestMatches.Point("21.5", "2.5"), new EntityId(1), new EntityId(2))),
        (TicksToTier2 - 20, CombatMatches.MoveCommand(1, TestMatches.Point("21.5", "20.5"), new EntityId(3), new EntityId(4))),
    ];

    private static int Speed(Match match, int entity) => match.Movers.Get(new EntityId(entity)).Speed.Raw;

    private static Health Health(long hitPoints, long maximum) => new() { HitPoints = hitPoints, Maximum = maximum };

    /// <summary>Notes the hit points of one entity when a research finishes, after the refresh has run.</summary>
    private sealed class Look(Match match, EntityId entity, List<Health> seen) : Sim.Research.ITechChangeHandler
    {
        public void TechChanged(World world, int player, Sim.Tech.PlayerTech before, Sim.Tech.PlayerTech after) =>
            seen.Add(match.Health.Get(entity));
    }
}
