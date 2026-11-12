using Sim.Combat;
using Sim.Core;
using Sim.Economy;
using Sim.Numerics;
using Sim.Research;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>fight</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach.
/// </summary>
public class FightScriptTests
{
    private const string GoldenReplay = "ridge-fight.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "a unit made",
        "a unit made for every player on one point in one tick",
        "a unit of a chassis the data does not have",
        "a unit on blocked ground",
        "a unit off the map",
        "a unit for nobody in the match",
        "an attack-move",
        "an attack-move for workers and fighting units together",
        "an attack-move from nobody in the match",
        "an attack-move without units",
        "an attack on a fighting unit",
        "an attack on a worker",
        "an attack that lists workers beside fighting units",
        "an attack by workers alone",
        "an attack by units that are not the player's",
        "an attack on one of the player's own",
        "an attack on an entity that never existed",
        "an attack from nobody in the match",
        "an attack without units",
        "a stop",
        "a stop for units that are not the player's",
        "a stop from nobody in the match",
        "a stop without units",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// The rarest kinds come about once in 700 ticks, so a shorter match can miss one.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "12000", "--orders", "33", "--script", "fight");

        Assert.All(replay.Commands, entry => Assert.True(IsOfThisScript(entry.Command), $"The script gave a command of type {entry.Command.Type}."));
        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            seen.UnionWith(KindsOfOrders(match, [.. due[tick]]));
            match.Simulation.Tick();
        }

        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>
    /// What this recorded match is for: it must reach the rules of combat that a change could
    /// break, where combat meets the workers, their work and research, and each order refused.
    /// The match has the orders of every script of its time, so that fighting units are also sent
    /// about by move orders, workers are at deposits when they are attacked, and tiers rise
    /// under units that are there already.
    /// </summary>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        match.Research.Reported += reported =>
        {
            if (reported.Kind == ResearchEventKind.Finished && HasFightingUnits(match, reported.Player))
            {
                seen.Add("a tier raised for a player with fighting units");
            }
        };
        var workers = new Dictionary<EntityId, WorkerState>();
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            seen.UnionWith(KindsOfOrders(match, [.. due[tick].Where(IsOfThisScript)]));
            workers.Clear();
            for (var i = 0; i < match.Workers.Count; i++)
            {
                workers[match.Workers.EntityAt(i)] = match.Workers.ValueAt(i).State;
            }

            match.Simulation.Tick();
            seen.UnionWith(WhatTheTickDid(match, workers));
        }

        string[] expected =
        [
            .. EveryKindOfOrder,
            "a melee hit",
            "a shot fired",
            "a shot that lands",
            "a shot that is lost",
            "a fighting unit killed",
            "a worker killed",
            "a worker killed that had a place at a deposit",
            "a spawn refused",
            "an attack-move refused",
            "an attack refused",
            "a stop refused",
            "a unit fighting an enemy it picked while idle",
            "a unit taken off its attack-move by an enemy",
            "a unit on an attack order",
            "a fighting unit on a move order",
            "a tier raised for a player with fighting units",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    private static bool IsOfThisScript(Command command) =>
        command.Type is CommandType.DebugSpawn or CommandType.AttackMove or CommandType.Attack or CommandType.Stop;

    private static bool HasFightingUnits(Match match, int player) =>
        Enumerable.Range(0, match.Combatants.Count).Any(i => match.Bodies.Get(match.Combatants.EntityAt(i)).Owner == player);

    /// <summary>
    /// What the commands of this script that are due in the next tick are, in the words of
    /// <see cref="EveryKindOfOrder"/>. The match is as it is before that tick, which is what the
    /// script saw when it gave them.
    /// </summary>
    private static HashSet<string> KindsOfOrders(Match match, IReadOnlyList<Command> commands)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var players = match.Players.Count;
        foreach (var command in commands)
        {
            var own = command.Entities.ToArray().Where(entity => match.Bodies.TryGet(entity, out var body) && body.Owner == command.Player).ToList();
            var fighting = own.Count(match.Combatants.Contains);
            switch (command.Type)
            {
                case CommandType.DebugSpawn:
                    seen.Add(KindOfSpawn(match, command));
                    var madeOnThePoint = commands.Where(other => SamePoint(other, command) && KindOfSpawn(match, other) == "a unit made");
                    if (madeOnThePoint.Select(other => other.Player).Distinct().Count() == players)
                    {
                        seen.Add("a unit made for every player on one point in one tick");
                    }

                    break;
                case CommandType.AttackMove when command.Player >= players:
                    seen.Add("an attack-move from nobody in the match");
                    break;
                case CommandType.AttackMove when command.Entities.IsEmpty:
                    seen.Add("an attack-move without units");
                    break;
                case CommandType.AttackMove:
                    seen.Add("an attack-move");
                    if (fighting > 0 && fighting < own.Count)
                    {
                        seen.Add("an attack-move for workers and fighting units together");
                    }

                    break;
                case CommandType.Attack when command.Player >= players:
                    seen.Add("an attack from nobody in the match");
                    break;
                case CommandType.Attack when command.Entities.IsEmpty:
                    seen.Add("an attack without units");
                    break;
                case CommandType.Attack when !match.Bodies.TryGet(command.TargetEntity, out _):
                    seen.Add("an attack on an entity that never existed");
                    break;
                case CommandType.Attack when match.Bodies.Get(command.TargetEntity).Owner == command.Player:
                    seen.Add("an attack on one of the player's own");
                    break;
                case CommandType.Attack when own.Count == 0:
                    seen.Add("an attack by units that are not the player's");
                    break;
                case CommandType.Attack when fighting == 0:
                    seen.Add("an attack by workers alone");
                    break;
                case CommandType.Attack:
                    seen.Add(match.Combatants.Contains(command.TargetEntity) ? "an attack on a fighting unit" : "an attack on a worker");
                    if (fighting < own.Count)
                    {
                        seen.Add("an attack that lists workers beside fighting units");
                    }

                    break;
                case CommandType.Stop when command.Player >= players:
                    seen.Add("a stop from nobody in the match");
                    break;
                case CommandType.Stop when command.Entities.IsEmpty:
                    seen.Add("a stop without units");
                    break;
                case CommandType.Stop:
                    seen.Add(own.Count == 0 ? "a stop for units that are not the player's" : "a stop");
                    break;
                default:
                    break;
            }
        }

        return seen;
    }

    private static string KindOfSpawn(Match match, Command command)
    {
        var point = new FixedVector2(command.TargetX, command.TargetY);
        return command switch
        {
            _ when command.Type != CommandType.DebugSpawn => "",
            _ when command.Player >= match.Players.Count => "a unit for nobody in the match",
            _ when command.ContentId < 0 || command.ContentId >= match.Data.Chassis.Count => "a unit of a chassis the data does not have",
            _ when !match.Grid.Contains(point) => "a unit off the map",
            _ when !match.Grid.IsPassable(point) => "a unit on blocked ground",
            _ => "a unit made",
        };
    }

    private static bool SamePoint(Command a, Command b) => a.TargetX == b.TargetX && a.TargetY == b.TargetY;

    /// <summary>
    /// What the tick that has just run did, from its events and from the state after it, in the
    /// words of the list of <see cref="ItsGoldenReplayReachesTheRulesItGuards"/>.
    /// </summary>
    /// <param name="workersBefore">The workers there were before the tick, each with what it was doing.</param>
    private static HashSet<string> WhatTheTickDid(Match match, Dictionary<EntityId, WorkerState> workersBefore)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reported in match.Simulation.Events)
        {
            switch (reported.Kind)
            {
                case SimEventKind.MeleeHit:
                    seen.Add("a melee hit");
                    break;
                case SimEventKind.ShotFired:
                    seen.Add("a shot fired");
                    break;
                case SimEventKind.ShotHit:
                    seen.Add("a shot that lands");
                    break;
                case SimEventKind.ShotLost:
                    seen.Add("a shot that is lost");
                    break;
                case SimEventKind.Death when workersBefore.TryGetValue(reported.Entity, out var state):
                    seen.Add("a worker killed");
                    if (state != WorkerState.Idle)
                    {
                        seen.Add("a worker killed that had a place at a deposit");
                    }

                    break;
                case SimEventKind.Death:
                    seen.Add("a fighting unit killed");
                    break;
                case SimEventKind.OrderRefused when (CommandType)reported.Amount == CommandType.DebugSpawn:
                    seen.Add("a spawn refused");
                    break;
                case SimEventKind.OrderRefused when (CommandType)reported.Amount == CommandType.AttackMove:
                    seen.Add("an attack-move refused");
                    break;
                case SimEventKind.OrderRefused when (CommandType)reported.Amount == CommandType.Attack:
                    seen.Add("an attack refused");
                    break;
                case SimEventKind.OrderRefused when (CommandType)reported.Amount == CommandType.Stop:
                    seen.Add("a stop refused");
                    break;
                default:
                    break;
            }
        }

        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var unit = match.Combatants.ValueAt(i);
            switch (unit.Order)
            {
                case UnitOrder.Idle when unit.Target != EntityId.None:
                    seen.Add("a unit fighting an enemy it picked while idle");
                    break;
                case UnitOrder.AttackMove when unit.Engaged:
                    seen.Add("a unit taken off its attack-move by an enemy");
                    break;
                case UnitOrder.Attack:
                    seen.Add("a unit on an attack order");
                    break;
                case UnitOrder.Move:
                    seen.Add("a fighting unit on a move order");
                    break;
                default:
                    break;
            }
        }

        return seen;
    }
}
