using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Data;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Research;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// Where combat meets research and the economy in one match. Each of these is a case that, built
/// wrong, compiles and passes every test of the three parts taken alone: combat that fights with
/// tech of its own, workers nothing can attack, an order that walks a worker off without freeing
/// its place, and systems in an order nobody chose.
/// </summary>
public class CombatInAMatchTests
{
    private const long WorkerHitPoints = 50_000;
    private const long HopliteDamage = 8_000;

    private static int HopliteInterval => CombatMatches.Data.Chassis[Chassis(Hoplite)].Attack.IntervalTicks;

    /// <summary>A match in which player 0 can research: a slot and a full stockpile.</summary>
    private static Match Researching()
    {
        var match = Create(TestMaps.Open(24, 12));
        match.Stockpiles.Add(0, ResearchMatches.Plenty, ResearchMatches.Plenty);
        return match;
    }

    /// <summary>Runs one tick with the target at full hit points, and returns the damage it took in that tick.</summary>
    private static long DamageInOneTick(Match match, EntityId target)
    {
        var full = match.Health.Get(target).Maximum;
        match.SetHitPoints(target, full);
        match.Run(1);
        return full - match.HitPointsOf(target);
    }

    /// <summary>Runs ticks until the target is hit, and returns the number of the tick it was hit in.</summary>
    private static int RunUntilHit(Match match, EntityId target)
    {
        for (var i = 0; i < 200; i++)
        {
            if (DamageInOneTick(match, target) > 0)
            {
                return match.World.Tick - 1;
            }
        }

        throw new InvalidOperationException("The target was never hit.");
    }

    /// <summary>
    /// Starts player 0's research of Firepower 2 in such a tick that it would finish in a tick in
    /// which the attacker, who hits every <see cref="HopliteInterval"/> ticks, hits again. Returns
    /// that tick, with the match run up to it.
    /// </summary>
    private static int ResearchToFinishInATickWithAHit(Match match, EntityId target)
    {
        var hit = RunUntilHit(match, target);
        var start = match.World.Tick;
        while ((start + ResearchMatches.TicksToTier2 - 1 - hit) % HopliteInterval != 0)
        {
            start++;
        }

        while (match.World.Tick < start)
        {
            DamageInOneTick(match, target);
        }

        match.OrderResearch(0, ResearchMatches.Firepower);
        var finish = start + ResearchMatches.TicksToTier2 - 1;
        while (match.World.Tick < finish)
        {
            DamageInOneTick(match, target);
        }

        Assert.True(match.Slot(0).IsBusy || match.Slot(0, 1).IsBusy, "The research is still under way.");
        Assert.Equal(1, match.Research.Tech(0).Tiers[ResearchMatches.Firepower]);
        return finish;
    }

    /// <summary>
    /// Combat reads the tech research holds. With a table of its own it would go on hitting for
    /// what it hit for at the start of the match, whatever was researched.
    /// </summary>
    [Fact]
    public void AFirepowerResearchRaisesTheDamageOfTheNextHit()
    {
        var match = Researching();
        match.Research.OpenPlayerSlot(0);
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        var guard = CombatMatches.Data.Chassis[Chassis(Hoplite)].Line;

        var finish = ResearchToFinishInATickWithAHit(match, worker);

        // The hit of the tick in which the research finishes is still the old one: research runs last.
        Assert.Equal(HopliteDamage, DamageInOneTick(match, worker));
        Assert.Equal(finish + 1, match.World.Tick);
        Assert.Equal(2, match.Research.Tech(0).Tiers[ResearchMatches.Firepower]);

        var raised = match.Combat.DamageOf(match.Blueprint(0, Hoplite).DamagePerHit, guard, CombatRules.NoLine);
        Assert.True(raised > HopliteDamage, "Firepower 2 raises the Hoplite's damage.");
        Assert.Equal(raised, match.Combat.ProfileOf(0, Chassis(Hoplite)).Damage.RoundHalfUp(1000));

        long next;
        do
        {
            next = DamageInOneTick(match, worker);
        }
        while (next == 0 && match.World.Tick < finish + 100);

        Assert.Equal(raised, next);
        Assert.True(match.IsAlive(hoplite));
    }

    /// <summary>
    /// Research runs after the deaths of the tick. Run before them, a research would finish in the
    /// tick in which the building that holds it falls.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AResearchIsLostWithItsRefundWhenItsHolderDiesInTheTickInWhichItWouldHaveFinished(bool holderDies)
    {
        var match = Researching();
        var holder = match.SpawnWorker(0, "6.5", "5.5");
        Assert.True(match.Research.OpenSlot(0, holder));
        match.Spawn(1, Hoplite, "5.5", "5.5");

        var finish = ResearchToFinishInATickWithAHit(match, holder);
        Assert.True(match.Stock(0).Materials < ResearchMatches.Plenty, "The research was paid for.");
        match.SetHitPoints(holder, holderDies ? 1 : WorkerHitPoints);
        match.Run(1);

        Assert.Equal(finish + 1, match.World.Tick);
        Assert.Equal(!holderDies, match.IsAlive(holder));
        if (holderDies)
        {
            Assert.Equal(1, match.Research.Tech(0).Tiers[ResearchMatches.Firepower]);
            Assert.Equal((ResearchMatches.Plenty, ResearchMatches.Plenty), match.Stock(0));
            Assert.False(match.Slot(0, 1).IsOpen);
        }
        else
        {
            Assert.Equal(WorkerHitPoints - HopliteDamage, match.HitPointsOf(holder));
            Assert.Equal(2, match.Research.Tech(0).Tiers[ResearchMatches.Firepower]);
            Assert.True(match.Stock(0).Materials < ResearchMatches.Plenty);
        }
    }

    /// <summary>
    /// The match setup places workers through the one method that makes a worker, so each is both
    /// something that works and something that can be damaged.
    /// </summary>
    [Fact]
    public void AStartingWorkerHasBothAWorkerAndAHealth()
    {
        var result = DataLoader.Load(GameDataOnDisk.Files());
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        var data = result.Data;
        var match = MatchSetup.Create(data, data.Maps[0], 1, [new(0), new(1)]);

        Assert.Equal(2 * data.Rules.Start.Workers, match.Workers.Count);
        for (var i = 0; i < match.Workers.Count; i++)
        {
            var worker = match.Workers.EntityAt(i);
            Assert.True(match.Health.TryGet(worker, out var health), $"Worker {worker} has no hit points.");
            Assert.Equal(Health.Full(data.Resources.Worker.HitPoints.ToFraction().RoundHalfUp(1000)), health);
            Assert.False(match.Combatants.Contains(worker));
        }
    }

    [Fact]
    public void AStartingWorkerCanBeAttackedAndKilled()
    {
        var map = TestMaps.Open(40, 24) with { Starts = [new MapStart(4, 4), new MapStart(30, 4)] };
        var match = MatchSetup.Create(CombatMatches.Data, map, 1, [new(0), new(1)]);
        var worker = match.Workers.EntityAt(0);
        var owner = match.Bodies.Get(worker).Owner;
        var position = match.PositionOf(worker);
        var hoplite = match.SpawnChassis(1 - owner, Chassis(Hoplite), new(position.X, position.Y - Fixed.One));
        match.RefreshDerivedState();
        match.Vision.Refresh();

        match.OrderAttack(1 - owner, worker, hoplite);
        match.Run(1);
        Assert.Equal(WorkerHitPoints - HopliteDamage, match.HitPointsOf(worker));

        match.Run(7 * HopliteInterval);
        Assert.False(match.IsAlive(worker));
        Assert.False(match.Workers.Contains(worker));
    }

    /// <summary>An attack-move is a move order to a worker, so it takes the worker off its work as a move does.</summary>
    [Fact]
    public void AWorkerAtWorkWhoIsGivenAnAttackMoveGivesUpItsPlace()
    {
        var match = EconomyMatches.WithOneDeposit(out var deposit, CombatMatches.Data);
        var worker = match.WorkerAt(0, 4, 10);
        match.OrderGather(0, deposit, worker);
        match.RunUntilWorking(worker);
        Assert.Equal(1, match.Work.HoldersOf(match.World, deposit));

        match.OrderAttackMove(0, TestMatches.Point("3.5", "3.5"), worker);
        match.Run(1);

        Assert.Equal(WorkerState.Idle, match.StateOf(worker));
        Assert.Equal(EntityId.None, match.WorkerOf(worker).Site);
        Assert.Equal(0, match.Work.HoldersOf(match.World, deposit));
        Assert.NotEqual(MoveState.Idle, match.Movers.Get(worker).State);

        var paid = match.Stockpiles.Materials(0);
        match.Run(200);
        Assert.Equal(TestMatches.Point("3.5", "3.5"), match.PositionOf(worker));
        Assert.Equal(paid, match.Stockpiles.Materials(0));
    }

    /// <summary>The orders that do not make a worker walk leave it at its work: it takes no attack order, and it stands already.</summary>
    [Fact]
    public void AWorkerAtWorkKeepsItsPlaceThroughAnAttackOrderAndAStopOrder()
    {
        var match = EconomyMatches.WithOneDeposit(out var deposit, CombatMatches.Data);
        var worker = match.WorkerAt(0, 4, 10);
        var hoplite = match.Spawn(0, Hoplite, "4.5", "4.5");

        // In sight of the worker at the deposit, so that the order can name it, and out of the Guard's.
        var enemy = match.SpawnWorker(1, "12.5", "14.5");
        match.RefreshDerivedState();
        match.OrderGather(0, deposit, worker);
        match.RunUntilWorking(worker);

        match.OrderAttack(0, enemy, hoplite, worker);
        match.OrderStop(0, worker);
        match.Run(1);

        Assert.Equal(UnitOrder.Attack, match.UnitOf(hoplite).Order);
        Assert.Equal(WorkerState.Working, match.StateOf(worker));
        Assert.Equal(1, match.Work.HoldersOf(match.World, deposit));

        var paid = match.Stockpiles.Materials(0);
        match.Run(20);
        Assert.True(match.Stockpiles.Materials(0) > paid, "The worker is still paid.");
    }

    /// <summary>
    /// Income runs after the deaths of the tick: a worker killed in the tick in which its payment
    /// is due earns nothing in it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWorkerKilledInTheTickItsPaymentIsDueIsNotPaid(bool killed)
    {
        var match = EconomyMatches.WithOneDeposit(out var deposit, CombatMatches.Data);
        var worker = match.WorkerAt(0, 4, 10);
        match.OrderGather(0, deposit, worker);
        match.RunUntilWorking(worker);

        var before = match.Stockpiles.Materials(0);
        var ticks = 0;
        while (match.Stockpiles.Materials(0) == before)
        {
            match.Run(1);
            Assert.True(++ticks <= SimInfo.TicksPerSecond, "The worker was paid within a second of arriving.");
        }

        // The next payment is due one second after the first.
        var paid = match.Stockpiles.Materials(0);
        match.Run(SimInfo.TicksPerSecond - 1);
        Assert.Equal(paid, match.Stockpiles.Materials(0));

        match.SetHitPoints(worker, killed ? 0 : 1);
        match.Run(1);

        Assert.Equal(!killed, match.IsAlive(worker));
        Assert.Equal(killed, match.Stockpiles.Materials(0) == paid);
        Assert.Equal(killed ? 0 : 1, match.Work.HoldersOf(match.World, deposit));
    }
}
