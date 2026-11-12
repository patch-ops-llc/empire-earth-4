using System.Diagnostics;
using Ai;
using Sim;
using Sim.Core;
using Sim.Data;
using Sim.Setup;

namespace Tools.AiMatch;

/// <summary>What one computer player had when a headless match ended.</summary>
/// <param name="Player">Its number in the match.</param>
/// <param name="Start">The start of the map it had.</param>
/// <param name="EpochTicks">The tick in which it reached each epoch above the one the match starts in, in order.</param>
/// <param name="Tiers">Its tier on each track, in the order of the data.</param>
/// <param name="ArmyValue">The price of its living fighting units, Materials and Energy added up, at its blueprints of the end.</param>
/// <param name="UnitsByChassis">Its living fighting units, by the ID of their chassis, in the order of the data.</param>
/// <param name="Structures">Its structures, finished or not, by their ID, in the order of the roster.</param>
/// <param name="UnitsLost">Its workers and fighting units that were destroyed.</param>
/// <param name="StructuresLost">Its structures that were destroyed.</param>
/// <param name="Materials">Its stockpile of Materials at the end, in whole resources.</param>
/// <param name="Energy">Its stockpile of Energy at the end, in whole resources.</param>
public sealed record SeatResult(
    int Player,
    string Personality,
    int Start,
    int Epoch,
    IReadOnlyList<int> EpochTicks,
    IReadOnlyList<int> Tiers,
    int SupplyUsed,
    int SupplyCap,
    int Workers,
    int Units,
    long ArmyValue,
    IReadOnlyList<(string Chassis, int Count)> UnitsByChassis,
    IReadOnlyList<(string Structure, int Count)> Structures,
    int UnitsLost,
    int StructuresLost,
    long Materials,
    long Energy);

/// <summary>A headless match between computer players, when it has ended.</summary>
/// <param name="Ticks">The length of the match.</param>
/// <param name="Winner">The one player that still had something on the map when every other had nothing. None at the time limit.</param>
/// <param name="StateHash">The hash of the state at the end: the same for the same match on every machine.</param>
/// <param name="CommandsGiven">The commands the computer players gave.</param>
/// <param name="CommandsDropped">The commands the simulation dropped as not valid. A computer player should give none.</param>
/// <param name="TickSeconds">The time the ticks took on this machine, in seconds, without the players' thinking.</param>
/// <param name="WorstTickSeconds">The longest tick.</param>
/// <param name="ThinkSeconds">The time the computer players took to think, all together.</param>
public sealed record HeadlessResult(
    int Ticks,
    int? Winner,
    IReadOnlyList<SeatResult> Seats,
    ulong StateHash,
    int CommandsGiven,
    long CommandsDropped,
    double TickSeconds,
    double WorstTickSeconds,
    double ThinkSeconds)
{
    public double GameSeconds => (double)Ticks / SimInfo.TicksPerSecond;
}

/// <summary>
/// Plays a match with a computer player in every seat and nobody watching, as fast as the
/// machine allows (design 5.5, item 7), and says what came of it.
/// </summary>
/// <remarks>
/// <para>
/// The match is run as a host runs one: after every tick each computer player thinks, and its
/// commands are scheduled <see cref="CommandDelay"/> ticks ahead. Nothing in it is random beyond
/// the seed of the match, so the same arguments give the same match on every machine; only the
/// times differ.
/// </para>
/// <para>
/// <b>When it ends.</b> At the time limit, or as soon as only one player has anything left on
/// the map: a worker, a fighting unit or a structure. That player is the winner. This is the
/// tool's own measure, taken from outside; when the rules of a match have an end of their own,
/// it takes their place.
/// </para>
/// </remarks>
public static class HeadlessMatch
{
    /// <summary>The ticks between a computer player's thinking and its commands taking effect, as in a match over the network.</summary>
    public const int CommandDelay = 2;

    /// <param name="data">The game data.</param>
    /// <param name="map">One of the data's maps.</param>
    /// <param name="personalities">The personality of each player, in the order of the seats. Player i has start i of the map.</param>
    /// <param name="seed">The seed of the match.</param>
    /// <param name="mostTicks">The time limit.</param>
    public static HeadlessResult Play(GameData data, MapDef map, IReadOnlyList<AiPersonalityDef> personalities, ulong seed, int mostTicks)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(personalities);
        ArgumentOutOfRangeException.ThrowIfLessThan(personalities.Count, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(personalities.Count, map.Starts.Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(mostTicks, 1);

        return Play(
            MatchSetup.Create(data, map, seed, [.. Enumerable.Range(0, personalities.Count).Select(start => new PlayerSetup(start))]),
            personalities,
            mostTicks);
    }

    /// <summary>Plays a match that is already set up, from the tick it is in.</summary>
    /// <param name="match">The match, with a seat for each personality.</param>
    /// <param name="personalities">The personality of each player, in the order of the seats.</param>
    /// <param name="mostTicks">The time limit, as a tick of the match.</param>
    public static HeadlessResult Play(Match match, IReadOnlyList<AiPersonalityDef> personalities, int mostTicks)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(personalities);
        ArgumentOutOfRangeException.ThrowIfNotEqual(personalities.Count, match.Players.Count);

        var seats = personalities.Count;
        var players = personalities.Select((personality, player) => new AiPlayer(match, player, personality)).ToList();
        var commands = new List<Command>();
        var epochTicks = Enumerable.Range(0, seats).Select(_ => new List<int>()).ToArray();
        var epochs = Enumerable.Range(0, seats).Select(player => match.Research.Tech(player).Epoch).ToArray();
        var unitsLost = new int[seats];
        var structuresLost = new int[seats];
        var fallen = new HashSet<EntityId>();
        var (given, tickTime, worstTick, thinkTime) = (0, 0L, 0L, 0L);
        int? winner = null;

        while (match.World.Tick < mostTicks && winner is null)
        {
            var before = Stopwatch.GetTimestamp();
            match.Simulation.Tick();
            var elapsed = Stopwatch.GetTimestamp() - before;
            tickTime += elapsed;
            worstTick = Math.Max(worstTick, elapsed);

            before = Stopwatch.GetTimestamp();
            foreach (var player in players)
            {
                commands.Clear();
                player.Think(commands);
                given += commands.Count;
                foreach (var command in commands)
                {
                    match.Simulation.Commands.Enqueue(match.World.Tick + CommandDelay - 1, command);
                }
            }

            thinkTime += Stopwatch.GetTimestamp() - before;

            // A structure that fell is a death first and a removal after it; one that was cancelled is a removal alone.
            fallen.Clear();
            foreach (var simEvent in match.Simulation.Events)
            {
                if (simEvent.Kind == SimEventKind.Death && simEvent.Player >= 0)
                {
                    fallen.Add(simEvent.Entity);
                    unitsLost[simEvent.Player]++;
                }
                else if (simEvent.Kind == SimEventKind.StructureRemoved && fallen.Contains(simEvent.Entity))
                {
                    unitsLost[simEvent.Player]--;
                    structuresLost[simEvent.Player]++;
                }
            }

            for (var player = 0; player < seats; player++)
            {
                var epoch = match.Research.Tech(player).Epoch;
                for (; epochs[player] < epoch; epochs[player]++)
                {
                    epochTicks[player].Add(match.World.Tick);
                }
            }

            if (fallen.Count > 0)
            {
                winner = LastOneStanding(match, seats);
            }
        }

        var toSeconds = 1.0 / Stopwatch.Frequency;
        return new HeadlessResult(
            match.World.Tick,
            winner,
            [.. Enumerable.Range(0, seats).Select(player => Seat(match, player, personalities[player], epochTicks[player], unitsLost[player], structuresLost[player]))],
            match.World.ComputeHash(),
            given,
            match.Simulation.DroppedCommandCount,
            tickTime * toSeconds,
            worstTick * toSeconds,
            thinkTime * toSeconds);
    }

    /// <summary>The one player with something left on the map, or none while two or more have, or nobody has.</summary>
    private static int? LastOneStanding(Match match, int seats)
    {
        var has = new bool[seats];
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            var entity = match.Bodies.EntityAt(i);
            if (match.Workers.Contains(entity) || match.Combatants.Contains(entity) || match.Structures.Contains(entity))
            {
                has[match.Bodies.ValueAt(i).Owner] = true;
            }
        }

        return has.Count(one => one) == 1 ? Array.IndexOf(has, true) : null;
    }

    private static SeatResult Seat(Match match, int player, AiPersonalityDef personality, List<int> epochTicks, int unitsLost, int structuresLost)
    {
        var data = match.Data;
        var tech = match.Research.Tech(player);
        var byChassis = new int[data.Chassis.Count];
        var units = 0;
        long armyValue = 0;
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            if (match.Bodies.Get(match.Combatants.EntityAt(i)).Owner == player)
            {
                var chassis = match.Combatants.ValueAt(i).Chassis;
                byChassis[chassis]++;
                units++;
                armyValue += tech.Blueprints[chassis].Materials + tech.Blueprints[chassis].Energy;
            }
        }

        var workers = 0;
        for (var i = 0; i < match.Workers.Count; i++)
        {
            workers += match.Bodies.Get(match.Workers.EntityAt(i)).Owner == player ? 1 : 0;
        }

        var byStructure = new SortedDictionary<int, int>();
        for (var i = 0; i < match.Structures.Count; i++)
        {
            if (match.Bodies.Get(match.Structures.EntityAt(i)).Owner == player)
            {
                var type = match.Structures.ValueAt(i).Type;
                byStructure[type] = byStructure.GetValueOrDefault(type) + 1;
            }
        }

        return new SeatResult(
            player,
            personality.Id,
            match.Players[player].Start,
            tech.Epoch,
            epochTicks,
            tech.Tiers,
            match.Supply.Used(player),
            match.Supply.Cap(player),
            workers,
            units,
            armyValue,
            [.. Enumerable.Range(0, byChassis.Length).Where(chassis => byChassis[chassis] > 0).Select(chassis => (data.Chassis[chassis].Id, byChassis[chassis]))],
            [.. byStructure.Select(entry => (data.StructureOf(entry.Key)?.Id ?? $"structure {entry.Key}", entry.Value))],
            unitsLost,
            structuresLost,
            match.Stockpiles.Materials(player) / Sim.Economy.Stockpiles.PerResource,
            match.Stockpiles.Energy(player) / Sim.Economy.Stockpiles.PerResource);
    }
}
