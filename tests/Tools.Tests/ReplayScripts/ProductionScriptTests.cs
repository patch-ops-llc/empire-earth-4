using Sim.Construction;
using Sim.Core;
using Sim.Economy;
using Sim.Movement;
using Sim.Numerics;
using Sim.Production;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>production</c> of <c>replay record</c> and the golden replay recorded for it:
/// what the script must give, and what that replay must reach.
/// </summary>
public class ProductionScriptTests
{
    private const string GoldenReplay = "switchback-training.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "an order to train",
        "more orders to train in one tick than the queue has room for",
        "an order to train what the structure does not train",
        "an order to train at what is no structure of the player's",
        "an order to train from nobody in the match",
        "an order to cancel",
        "an order to cancel at a position with no order",
        "a rally point on a deposit",
        "a rally point on other ground",
        "a rally point off the map",
        "a rally order that lists what is not the player's",
        "an order to build a structure that trains",
    ];

    /// <summary>What the golden replay must reach.</summary>
    private static readonly string[] Expected =
    [
        .. EveryKindOfOrder,
        "an order placed",
        "an order placed at a structure that a worker built",
        "an order refused because the queue is full",
        "an order refused because the unit is locked",
        "an order refused because the stockpile does not cover it",
        "an order refused because the structure does not train it",
        "an order refused at what is no structure of the player's",
        "an order in production cancelled",
        "an order that waits cancelled",
        "a cancel refused",
        "a rally point set",
        "a rally order refused",
        "a queue of five orders",
        "a queue that goes on to its next order",
        "a worker trained",
        "a worker that appears and stays",
        "a worker that appears and is put to work at a deposit",
        "a worker that appears and walks to the rally point",
        "a unit of a chassis trained",
        "a unit trained at the structure 'barracks'",
        "a unit trained at the structure 'works'",
        "a unit that appears and walks to the rally point",
        "a unit trained by a player who had spent more than the Materials of the start",
        "a death at the hands of a unit that was trained",
    ];

    private static readonly string[] StructuresThatTrainUnits = ["barracks", "works"];

    private static readonly CommandType[] ItsCommands = [CommandType.Build, CommandType.Train, CommandType.CancelTrain, CommandType.SetRallyPoint];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// The rarest kinds come about once in 1,500 ticks.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "9000", "--orders", "33", "--script", "production");

        Assert.All(replay.Commands, entry => Assert.Contains(entry.Command.Type, ItsCommands));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var match = ReplayRuns.Start(replay);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            seen.UnionWith(KindsOfOrders(replay, match, tick));
            match.Simulation.Tick();
        }

        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>A match without a structure that trains and without workers has nothing for the script to order, and it gives no orders.</summary>
    [Fact]
    public void TheScriptGivesNothingInAMatchWithoutAStructureThatTrains()
    {
        var replay = ReplayRuns.Record("--map", "ridge", "--starts", "-,-", "--ticks", "600", "--orders", "33", "--script", "production");

        Assert.Empty(replay.Commands);
    }

    /// <summary>
    /// What this recorded match is for: it must reach the rules of production that a change could
    /// break. It began with workers alone: they build a Barracks and a Works, and the match
    /// trains workers and units, paid for by what the workers earn.
    /// </summary>
    /// <remarks>
    /// Throughout it no queue holds more than five orders, no player uses more supply than the
    /// cap, and the supply the queues hold is what their orders add up to. What it does not
    /// reach, because nothing in it researches and nothing destroys a structure: a pair, an order
    /// refused for want of supply, and the refund of a destroyed structure. The scenario tests of
    /// the simulation cover those.
    /// </remarks>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var seen = WhatItReaches(ReplayRuns.ReadGolden(GoldenReplay));

        Assert.Equal(Expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    /// <summary>Runs a replay and says what it reached, in the words of the two lists, checking the queues after every tick.</summary>
    private static HashSet<string> WhatItReaches(Sim.Serialization.Replay replay)
    {
        var match = ReplayRuns.Start(replay);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var workersAtTheStart = match.Workers.Count;
        var structuresAtTheStart = Enumerable.Range(0, match.Structures.Count).Select(match.Structures.EntityAt).ToHashSet();
        var workers = 0;
        var units = 0;
        var deadWorkers = 0;
        var deadUnits = 0;
        Assert.Equal(0, match.Combatants.Count);
        Assert.Equal(match.Players.Count, structuresAtTheStart.Count);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            seen.UnionWith(KindsOfOrders(replay, match, tick));
            var cancels = replay.Commands.Where(entry => entry.Tick == tick && entry.Command.Type == CommandType.CancelTrain).Select(entry => entry.Command).ToArray();
            var rallies = replay.Commands.Count(entry => entry.Tick == tick && entry.Command.Type == CommandType.SetRallyPoint);
            var workersBefore = Enumerable.Range(0, match.Workers.Count).Select(match.Workers.EntityAt).ToHashSet();
            match.Simulation.Tick();
            Assert.DoesNotContain(match.Simulation.Events, simEvent => simEvent.Kind == SimEventKind.BuildRefused);
            seen.UnionWith(WhatProductionDid(match, cancels, rallies, structuresAtTheStart));
            foreach (var simEvent in match.Simulation.Events)
            {
                if (TrainEvent.TryRead(simEvent, out var train) && train.Kind == SimEventKind.UnitTrained)
                {
                    workers += train.Unit == TrainOrder.Worker ? 1 : 0;
                    units += train.Unit == TrainOrder.Worker ? 0 : 1;
                }
                else if (simEvent.Kind == SimEventKind.Death)
                {
                    // The units the match trained fight like any others: they are all it has.
                    deadWorkers += workersBefore.Contains(simEvent.Entity) ? 1 : 0;
                    deadUnits += workersBefore.Contains(simEvent.Entity) ? 0 : 1;
                    seen.Add("a death at the hands of a unit that was trained");
                }
            }

            AssertTheQueuesAreSound(match, tick);
        }

        Assert.True(workers >= 8, $"Only {workers} workers were trained.");
        Assert.True(units >= 3, $"Only {units} units were trained.");
        Assert.Equal(workersAtTheStart + workers - deadWorkers, match.Workers.Count);
        Assert.Equal(units - deadUnits, match.Combatants.Count);
        return seen;
    }

    /// <summary>
    /// What the commands of this script for a tick are, in the words of <see cref="EveryKindOfOrder"/>.
    /// The match is as it is before that tick runs.
    /// </summary>
    private static HashSet<string> KindsOfOrders(Sim.Serialization.Replay replay, Match match, int tick)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var producers = match.Production.Producers;
        var commands = replay.Commands.Where(entry => entry.Tick == tick && ItsCommands.Contains(entry.Command.Type)).Select(entry => entry.Command).ToArray();
        foreach (var command in commands)
        {
            var isOwn = command.Entities.Length > 0
                && command.Player < match.Players.Count
                && producers.Contains(command.Entities[0])
                && match.Bodies.Get(command.Entities[0]).Owner == command.Player;
            switch (command.Type)
            {
                case CommandType.Build:
                    // The script orders a structure only where the order is accepted.
                    var def = match.Data.StructureOf(command.ContentId);
                    Assert.NotNull(def);
                    Assert.Contains(def.Id, StructuresThatTrainUnits);
                    Assert.Equal(0, match.Construction.CountOf(command.Player, def.Index));
                    Assert.Equal(BuildProblem.None, match.Construction.CheckStructure(command.Player, def.Index));
                    Assert.Equal(BuildProblem.None, match.Construction.CheckSite(command.Player, def, command.TargetX.FloorToInt(), command.TargetY.FloorToInt()));
                    var builder = Assert.Single(command.Entities.ToArray());
                    Assert.True(match.Workers.Contains(builder) && match.Bodies.Get(builder).Owner == command.Player);
                    seen.Add("an order to build a structure that trains");
                    break;
                case CommandType.Train when command.Player >= match.Players.Count:
                    seen.Add("an order to train from nobody in the match");
                    break;
                case CommandType.Train when !isOwn:
                    seen.Add("an order to train at what is no structure of the player's");
                    break;
                case CommandType.Train when !match.Production.TrainedAt(match.Structures.Get(command.Entities[0]).Type).Contains(command.ContentId):
                    seen.Add("an order to train what the structure does not train");
                    break;
                case CommandType.Train:
                    seen.Add("an order to train");
                    var together = commands.Count(other => other.Type == CommandType.Train && other.Entities[0] == command.Entities[0]);
                    if (producers.Get(command.Entities[0]).Count + together > Producer.MaxOrders)
                    {
                        seen.Add("more orders to train in one tick than the queue has room for");
                    }

                    break;
                case CommandType.CancelTrain:
                    Assert.True(isOwn, "The script cancelled at a structure that is not the player's.");
                    seen.Add(command.ContentId < producers.Get(command.Entities[0]).Count
                        ? "an order to cancel"
                        : "an order to cancel at a position with no order");
                    break;
                case CommandType.SetRallyPoint:
                    var point = new FixedVector2(command.TargetX, command.TargetY);
                    if (!match.Grid.Contains(point))
                    {
                        seen.Add("a rally point off the map");
                    }
                    else if (IsOnADeposit(match, point))
                    {
                        seen.Add("a rally point on a deposit");
                    }
                    else
                    {
                        seen.Add("a rally point on other ground");
                    }

                    if (command.Entities.ToArray().Any(entity => !match.Bodies.TryGet(entity, out var body) || body.Owner != command.Player))
                    {
                        seen.Add("a rally order that lists what is not the player's");
                    }

                    break;
                default:
                    Assert.Fail($"The script gave a command of type {command.Type}.");
                    break;
            }
        }

        return seen;
    }

    /// <summary>
    /// What the tick that has just run did in production, in the words of the list of
    /// <see cref="ItsGoldenReplayReachesTheRulesItGuards"/>, from its events and the state after it.
    /// </summary>
    private static HashSet<string> WhatProductionDid(Match match, Command[] cancels, int rallies, HashSet<EntityId> structuresAtTheStart)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var producers = match.Production.Producers;
        foreach (var simEvent in match.Simulation.Events)
        {
            if (simEvent.Kind == SimEventKind.OrderRefused && producers.Contains(simEvent.Entity))
            {
                // No other script lists a structure first in an order.
                Assert.Equal(1, cancels.Length + rallies);
                seen.Add(cancels.Length == 1 ? "a cancel refused" : "a rally order refused");
            }

            if (!TrainEvent.TryRead(simEvent, out var train))
            {
                continue;
            }

            switch (train.Kind)
            {
                case SimEventKind.TrainOrdered:
                    // Nothing in the match researches, so no chassis is trained in pairs.
                    Assert.Equal(1, train.Count);
                    Assert.Contains(train.Unit, match.Production.TrainedAt(match.Structures.Get(train.Structure).Type));
                    seen.Add("an order placed");
                    if (!structuresAtTheStart.Contains(train.Structure))
                    {
                        seen.Add("an order placed at a structure that a worker built");
                    }

                    break;
                case SimEventKind.TrainRefused:
                    seen.Add(train.Reason switch
                    {
                        TrainCheck.QueueFull => "an order refused because the queue is full",
                        TrainCheck.Locked => "an order refused because the unit is locked",
                        TrainCheck.TooExpensive => "an order refused because the stockpile does not cover it",
                        TrainCheck.NotTrainedHere => "an order refused because the structure does not train it",
                        TrainCheck.NoProducer => "an order refused at what is no structure of the player's",
                        _ => $"an order refused as {train.Reason}",
                    });
                    break;
                case SimEventKind.TrainCancelled:
                    seen.Add(Assert.Single(cancels).ContentId == 0 ? "an order in production cancelled" : "an order that waits cancelled");
                    break;
                case SimEventKind.RallyPointSet:
                    seen.Add("a rally point set");
                    break;
                case SimEventKind.UnitTrained when train.Unit == TrainOrder.Worker:
                    seen.Add("a worker trained");
                    seen.UnionWith(WhatTheNewWorkerDoes(match, train));
                    break;
                case SimEventKind.UnitTrained:
                    seen.Add("a unit of a chassis trained");
                    seen.UnionWith(WhatTheNewUnitDoes(match, train, structuresAtTheStart));
                    break;
                default:
                    seen.Add($"an event of kind {train.Kind}");
                    break;
            }
        }

        for (var i = 0; i < producers.Count; i++)
        {
            if (producers.ValueAt(i).Count == Producer.MaxOrders)
            {
                seen.Add("a queue of five orders");
            }
        }

        return seen;
    }

    private static HashSet<string> WhatTheNewWorkerDoes(Match match, TrainEvent train)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var worker = match.Workers.Get(train.Trained);
        var producer = match.Production.Producers.Get(train.Structure);
        AssertItAppearedWhole(match, train);

        if (producer.Count > 0)
        {
            seen.Add("a queue that goes on to its next order");
        }

        if (worker.State != WorkerState.Idle)
        {
            Assert.True(producer.RallySet);
            Assert.Equal(WorkSiteKind.Deposit, match.WorkSites.Get(worker.Site).Kind);
            Assert.True(IsOnADeposit(match, producer.RallyPoint));
            seen.Add("a worker that appears and is put to work at a deposit");
        }
        else if (match.Movers.Get(train.Trained).State != MoveState.Idle)
        {
            Assert.True(producer.RallySet);
            seen.Add("a worker that appears and walks to the rally point");
        }
        else
        {
            seen.Add("a worker that appears and stays");
        }

        return seen;
    }

    private static HashSet<string> WhatTheNewUnitDoes(Match match, TrainEvent train, HashSet<EntityId> structuresAtTheStart)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var producer = match.Production.Producers.Get(train.Structure);
        AssertItAppearedWhole(match, train);
        Assert.Equal(train.Unit, match.Combatants.Get(train.Trained).Chassis);
        Assert.False(match.Workers.Contains(train.Trained));
        Assert.DoesNotContain(train.Structure, structuresAtTheStart);
        seen.Add($"a unit trained at the structure '{match.Data.StructureOf(match.Structures.Get(train.Structure).Type)!.Id}'");

        if (match.Movers.Get(train.Trained).State != MoveState.Idle)
        {
            Assert.True(producer.RallySet);
            seen.Add("a unit that appears and walks to the rally point");
        }
        else
        {
            seen.Add("a unit that appears and stays");
        }

        // What the player has trained and built is worth more than the 400 Materials of the start: income paid for the rest.
        var player = train.Player;
        var spent = 0;
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            var entity = match.Bodies.EntityAt(i);
            if (match.Bodies.ValueAt(i).Owner != player || !match.World.Entities.IsAlive(entity))
            {
                continue;
            }

            spent += match.Workers.Contains(entity) ? match.Production.PriceOf(player, TrainOrder.Worker).Materials
                : match.Combatants.TryGet(entity, out var combatant) ? match.Production.PriceOf(player, combatant.Chassis).Materials
                : match.Structures.TryGet(entity, out var structure) && !structuresAtTheStart.Contains(entity) ? match.Data.StructureOf(structure.Type)!.Materials
                : 0;
        }

        var start = match.Data.Rules.Start;
        if (spent - (start.Workers * match.Production.PriceOf(player, TrainOrder.Worker).Materials) > start.Materials)
        {
            seen.Add("a unit trained by a player who had spent more than the Materials of the start");
        }

        return seen;
    }

    private static void AssertItAppearedWhole(Match match, TrainEvent train)
    {
        var health = match.Health.Get(train.Trained);
        Assert.Equal(health.Maximum, health.HitPoints);
        Assert.True(match.Grid.IsPassable(train.Position), "A unit appeared on ground it cannot stand on.");
        Assert.Equal(train.Player, match.Bodies.Get(train.Trained).Owner);
    }

    private static void AssertTheQueuesAreSound(Match match, int tick)
    {
        var producers = match.Production.Producers;
        var held = new int[match.Players.Count];
        for (var i = 0; i < producers.Count; i++)
        {
            var producer = producers.ValueAt(i);
            Assert.InRange(producer.Count, 0, Producer.MaxOrders);
            Assert.InRange(producer.Progress, 0, producer.BuildTicks);
            Assert.True(producer.Count > 0 || producer.BuildTicks == 0, $"An empty queue is in production in tick {tick}.");
            held[match.Bodies.Get(producers.EntityAt(i)).Owner] += producer.ReservedSupply;
        }

        for (var player = 0; player < match.Players.Count; player++)
        {
            Assert.Equal(held[player], match.Production.SupplyUsedBy(player));
            Assert.True(match.Supply.Used(player) <= match.Supply.Cap(player), $"Player {player} uses more supply than the cap in tick {tick}.");
        }
    }

    private static bool IsOnADeposit(Match match, FixedVector2 point)
    {
        var (x, y) = (point.X.FloorToInt(), point.Y.FloorToInt());
        return Enumerable.Range(0, match.WorkSites.Count).Any(index => match.WorkSites.ValueAt(index).Covers(x, y));
    }
}
