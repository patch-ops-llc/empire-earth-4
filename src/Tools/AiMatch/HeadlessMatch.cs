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
/// <param name="OutTick">The tick after which it had no Capitol and no Outpost left, or -1 while it is in the match.</param>
/// <param name="FirstAttackOrderTick">The tick in which it first gave an attack-move or an attack order, or -1.</param>
/// <param name="EveryTrackAtCapTick">The tick after which every one of its tracks stood at the epoch cap, or -1.</param>
/// <param name="ResearchMaterials">The Materials it paid for research on each track and did not get back, in the order of the data.</param>
/// <param name="ResearchEnergy">The same for Energy.</param>
/// <param name="TicksAtSupplyCap">The ticks after which it used all its supply cap or more.</param>
/// <param name="TicksAboveSupplyCap">The ticks after which it used more supply than its cap.</param>
/// <param name="MostAboveSupplyCap">The most supply it used above its cap.</param>
/// <param name="Samples">What it had after every ten seconds of game time, in order.</param>
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
    long Energy,
    int OutTick,
    int FirstAttackOrderTick,
    int EveryTrackAtCapTick,
    IReadOnlyList<long> ResearchMaterials,
    IReadOnlyList<long> ResearchEnergy,
    int TicksAtSupplyCap,
    int TicksAboveSupplyCap,
    int MostAboveSupplyCap,
    IReadOnlyList<SeatSample> Samples);

/// <summary>How a headless match ended.</summary>
public enum MatchEnd
{
    /// <summary>The time ran out with two or more players still in the match.</summary>
    TimeLimit,

    /// <summary>By conquest (design 5.1): at most one player still had a Capitol or an Outpost.</summary>
    Conquest,
}

/// <summary>A headless match between computer players, when it has ended.</summary>
/// <param name="Ticks">The length of the match.</param>
/// <param name="Ended">How it ended.</param>
/// <param name="Winner">The one player still in the match when it ended by conquest. None at the time limit, and none when the last players went out in one tick.</param>
/// <param name="CommandDelay">The ticks between a computer player's thinking and its commands taking effect.</param>
/// <param name="FirstHitTick">The tick of the first melee hit or shot that landed, or -1.</param>
/// <param name="StateHash">The hash of the state at the end: the same for the same match on every machine.</param>
/// <param name="CommandsGiven">The commands the computer players gave.</param>
/// <param name="CommandsDropped">The commands the simulation dropped as not valid. A computer player should give none.</param>
/// <param name="TickSeconds">The time the ticks took on this machine, in seconds, without the players' thinking.</param>
/// <param name="WorstTickSeconds">The longest tick.</param>
/// <param name="ThinkSeconds">The time the computer players took to think, all together.</param>
public sealed record HeadlessResult(
    int Ticks,
    MatchEnd Ended,
    int? Winner,
    int CommandDelay,
    int FirstHitTick,
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
/// commands are scheduled the command delay ahead, <see cref="CommandDelay"/> ticks unless the
/// caller says otherwise. Nothing in a match is random: the simulation rolls nothing (design
/// 5.2), so the seed changes the state hash and nothing else, and the same arguments give the
/// same match on every machine; only the times differ. What does change a match is who sits in
/// which seat, who has which start, the map, and the command delay.
/// </para>
/// <para>
/// <b>When it ends.</b> At the time limit, or by conquest (design 5.1): a player with a start who
/// has no Capitol and no Outpost left on the map, finished or still building itself, is out,
/// and the match ends when at most one player is still in. That player has won. Players who
/// lose their last one in the same tick go out together, and then nobody has won. This is the
/// rule the simulation itself gets with the end of a match (plan M24), counted here from
/// outside so that the tool needs nothing of that piece.
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
    /// <param name="commandDelay">The ticks between a computer player's thinking and its commands taking effect, 1 or more.</param>
    /// <param name="starts">The start of each player, in the order of the seats, each start once. Without it, player i has start i.</param>
    public static HeadlessResult Play(
        GameData data,
        MapDef map,
        IReadOnlyList<AiPersonalityDef> personalities,
        ulong seed,
        int mostTicks,
        int commandDelay = CommandDelay,
        IReadOnlyList<int>? starts = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(personalities);
        ArgumentOutOfRangeException.ThrowIfLessThan(personalities.Count, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(personalities.Count, map.Starts.Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(mostTicks, 1);
        starts ??= [.. Enumerable.Range(0, personalities.Count)];
        if (starts.Count != personalities.Count
            || starts.Distinct().Count() != starts.Count
            || starts.Any(start => start < 0 || start >= map.Starts.Count))
        {
            throw new ArgumentException($"Each of the {personalities.Count} players needs a start of its own among the {map.Starts.Count} of the map.", nameof(starts));
        }

        return Play(
            MatchSetup.Create(data, map, seed, [.. starts.Select(start => new PlayerSetup(start))]),
            personalities,
            mostTicks,
            commandDelay);
    }

    /// <summary>Plays a match that is already set up, from the tick it is in.</summary>
    /// <param name="match">The match, with a seat for each personality.</param>
    /// <param name="personalities">The personality of each player, in the order of the seats.</param>
    /// <param name="mostTicks">The time limit, as a tick of the match.</param>
    /// <param name="commandDelay">The ticks between a computer player's thinking and its commands taking effect, 1 or more.</param>
    public static HeadlessResult Play(Match match, IReadOnlyList<AiPersonalityDef> personalities, int mostTicks, int commandDelay = CommandDelay)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(personalities);
        ArgumentOutOfRangeException.ThrowIfNotEqual(personalities.Count, match.Players.Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(commandDelay, 1);

        var seats = personalities.Count;
        var players = personalities.Select((personality, player) => new AiPlayer(match, player, personality)).ToList();
        var commands = new List<Command>();
        var epochTicks = Enumerable.Range(0, seats).Select(_ => new List<int>()).ToArray();
        var epochs = Enumerable.Range(0, seats).Select(player => match.Research.Tech(player).Epoch).ToArray();
        var unitsLost = new int[seats];
        var structuresLost = new int[seats];
        var fallen = new HashSet<EntityId>();
        var watches = Enumerable.Range(0, seats).Select(player => new SeatWatch(match, player)).ToArray();
        var outTicks = Enumerable.Repeat(-1, seats).ToArray();
        var holds = new bool[seats];
        var (given, tickTime, worstTick, thinkTime, firstHit) = (0, 0L, 0L, 0L, -1);
        var ended = MatchEnd.TimeLimit;

        while (match.World.Tick < mostTicks && ended == MatchEnd.TimeLimit)
        {
            var before = Stopwatch.GetTimestamp();
            match.Simulation.Tick();
            var elapsed = Stopwatch.GetTimestamp() - before;
            tickTime += elapsed;
            worstTick = Math.Max(worstTick, elapsed);

            before = Stopwatch.GetTimestamp();
            for (var player = 0; player < seats; player++)
            {
                if (outTicks[player] >= 0)
                {
                    continue;
                }

                commands.Clear();
                players[player].Think(commands);
                given += commands.Count;
                foreach (var command in commands)
                {
                    watches[player].Given(command, match.World.Tick);
                    match.Simulation.Commands.Enqueue(match.World.Tick + commandDelay - 1, command);
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
                else if (firstHit < 0 && simEvent.Kind is SimEventKind.MeleeHit or SimEventKind.ShotHit)
                {
                    firstHit = match.World.Tick;
                }
            }

            foreach (var watch in watches)
            {
                watch.AfterTick();
            }

            for (var player = 0; player < seats; player++)
            {
                var epoch = match.Research.Tech(player).Epoch;
                for (; epochs[player] < epoch; epochs[player]++)
                {
                    epochTicks[player].Add(match.World.Tick);
                }
            }

            if (StillIn(match, holds, outTicks) <= 1)
            {
                ended = MatchEnd.Conquest;
            }
        }

        int? winner = ended == MatchEnd.Conquest && Array.IndexOf(outTicks, -1) is var last and >= 0 ? last : null;

        var toSeconds = 1.0 / Stopwatch.Frequency;
        return new HeadlessResult(
            match.World.Tick,
            ended,
            winner,
            commandDelay,
            firstHit,
            [.. Enumerable.Range(0, seats).Select(player => Seat(match, player, personalities[player], epochTicks[player], unitsLost[player], structuresLost[player], outTicks[player], watches[player]))],
            match.World.ComputeHash(),
            given,
            match.Simulation.DroppedCommandCount,
            tickTime * toSeconds,
            worstTick * toSeconds,
            thinkTime * toSeconds);
    }

    /// <summary>
    /// Takes out every player with a start who holds no Capitol and no Outpost on the map as the
    /// tick left it, and says how many are still in. A player without a start has none to lose
    /// and stays in.
    /// </summary>
    private static int StillIn(Match match, bool[] holds, int[] outTicks)
    {
        Array.Clear(holds);
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var entity = match.Structures.EntityAt(i);
            if (match.World.Entities.IsAlive(entity)
                && match.Data.StructureOf(match.Structures.ValueAt(i).Type)?.TerritoryRadius is not null
                && match.Bodies.TryGet(entity, out var body)
                && body.Owner >= 0
                && body.Owner < holds.Length)
            {
                holds[body.Owner] = true;
            }
        }

        var stillIn = 0;
        for (var player = 0; player < holds.Length; player++)
        {
            if (outTicks[player] < 0 && match.Players[player].HasStart && !holds[player])
            {
                outTicks[player] = match.World.Tick;
            }

            stillIn += outTicks[player] < 0 ? 1 : 0;
        }

        return stillIn;
    }

    private static SeatResult Seat(
        Match match, int player, AiPersonalityDef personality, List<int> epochTicks, int unitsLost, int structuresLost, int outTick, SeatWatch watch)
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
            match.Stockpiles.Energy(player) / Sim.Economy.Stockpiles.PerResource,
            outTick,
            watch.FirstAttackOrderTick,
            watch.EveryTrackAtCapTick,
            [.. watch.ResearchMaterials],
            [.. watch.ResearchEnergy],
            watch.TicksAtSupplyCap,
            watch.TicksAboveSupplyCap,
            watch.MostAboveSupplyCap,
            [.. watch.Samples]);
    }
}
