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
    ];

    /// <summary>What the golden replay must reach.</summary>
    private static readonly string[] Expected =
    [
        .. EveryKindOfOrder,
        "an order placed",
        "an order refused because the queue is full",
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
        "a worker trained by a player who had spent the Materials of the start",
    ];

    private static readonly CommandType[] ItsCommands = [CommandType.Train, CommandType.CancelTrain, CommandType.SetRallyPoint];

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

    /// <summary>A match without a structure that trains has nothing for the script to order at, and it gives no orders.</summary>
    [Fact]
    public void TheScriptGivesNothingInAMatchWithoutAStructureThatTrains()
    {
        var replay = ReplayRuns.Record("--map", "ridge", "--starts", "-,-", "--ticks", "600", "--orders", "33", "--script", "production");

        Assert.Empty(replay.Commands);
    }

    /// <summary>
    /// What this recorded match is for: it must reach the rules of production that a change could
    /// break. It began with workers alone and trains more, who earn what the next cost.
    /// </summary>
    /// <remarks>
    /// Throughout it no queue holds more than five orders, no player uses more supply than the
    /// cap, and the supply the queues hold is what their orders add up to. What it does not
    /// reach, because the frozen data has no structure but the Capitol and nothing in a scripted
    /// match destroys one: a unit of a chassis, a pair, an order refused as locked or for want of
    /// supply, and the refund of a destroyed structure. The scenario tests of the simulation
    /// cover those.
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
        var trained = 0;
        Assert.Equal(0, match.Combatants.Count);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            seen.UnionWith(KindsOfOrders(replay, match, tick));
            var cancels = replay.Commands.Where(entry => entry.Tick == tick && entry.Command.Type == CommandType.CancelTrain).Select(entry => entry.Command).ToArray();
            var rallies = replay.Commands.Count(entry => entry.Tick == tick && entry.Command.Type == CommandType.SetRallyPoint);
            match.Simulation.Tick();
            seen.UnionWith(WhatProductionDid(match, cancels, rallies));
            trained += match.Simulation.Events.Count(simEvent => simEvent.Kind == SimEventKind.UnitTrained);
            AssertTheQueuesAreSound(match, tick);
        }

        Assert.True(trained >= 10, $"Only {trained} workers were trained.");
        Assert.Equal(workersAtTheStart + trained, match.Workers.Count);
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
    private static HashSet<string> WhatProductionDid(Match match, Command[] cancels, int rallies)
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
                    Assert.Equal((TrainOrder.Worker, 1), (train.Unit, train.Count));
                    seen.Add("an order placed");
                    break;
                case SimEventKind.TrainRefused:
                    seen.Add(train.Reason switch
                    {
                        TrainCheck.QueueFull => "an order refused because the queue is full",
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
                case SimEventKind.UnitTrained:
                    seen.Add("a worker trained");
                    seen.UnionWith(WhatTheNewWorkerDoes(match, train));
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
        var health = match.Health.Get(train.Trained);
        Assert.Equal(health.Maximum, health.HitPoints);
        Assert.True(match.Grid.IsPassable(train.Position), "A worker appeared on ground it cannot stand on.");
        Assert.Equal(train.Player, match.Bodies.Get(train.Trained).Owner);

        if (producer.Count > 0)
        {
            seen.Add("a queue that goes on to its next order");
        }

        // What income has added since the start is what paid for this worker, when the start's 400 Materials buy 8.
        var owned = Enumerable.Range(0, match.Workers.Count).Count(index => match.Bodies.Get(match.Workers.EntityAt(index)).Owner == train.Player);
        if (owned > 6 + 8)
        {
            seen.Add("a worker trained by a player who had spent the Materials of the start");
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
