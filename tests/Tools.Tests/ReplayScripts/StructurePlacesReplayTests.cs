using Sim.Core;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Tools.Replay;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The places round a structure's footprint (16c) in the golden replays: every rule of them that
/// a change could break is reached by a recorded match, so that the check of the replays on every
/// system covers it.
/// </summary>
/// <remarks>
/// The rules are asked of the golden replays taken together and of no one of them. Which match
/// brings melee units to a structure, and how many, turns on rules that are not about places:
/// whether a player who has lost its Capitol still gives orders, whether an attack order needs
/// sight of its target, whether a wall in the way is broken. A list held against one recorded
/// match would have to be written again with each such rule, by whoever brings it, and each would
/// have to know of places. Here a rule that some match reaches stays reached whichever match
/// that is, and the message of a failure says what each match reaches.
/// <para>
/// <see cref="SurroundScriptTests"/> asks more of the one match that was recorded for places
/// round a footprint: nine units round one structure, all four of its sides, and a unit that
/// waits behind five.
/// </para>
/// </remarks>
public class StructurePlacesReplayTests
{
    private static readonly string[] EveryRule =
    [
        "a melee unit walking to its place by a structure",
        "a melee unit that attacks a structure from its place",
        "a melee unit with a place by a wall",
        "three melee units on their places by one structure",
        "a melee unit that stands out of reach of a structure while another is on its place by it",
    ];

    [Fact]
    public void TheGoldenReplaysTogetherReachEveryRuleOfThePlacesRoundAStructure()
    {
        var reachedBy = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Repository.GoldenReplaysFolder, "*" + ReplayTool.FileEnding))
        {
            var name = Path.GetFileName(file);
            reachedBy[name] = Reached(ReplayRuns.ReadGolden(name));
        }

        var missing = EveryRule.Where(rule => !reachedBy.Values.Any(reached => reached.Contains(rule))).ToList();
        var listing = string.Join(
            Environment.NewLine,
            reachedBy.Select(entry => $"  {entry.Key}: {(entry.Value.Count == 0 ? "none" : string.Join("; ", entry.Value.Order(StringComparer.Ordinal)))}"));
        Assert.True(
            missing.Count == 0,
            $"No golden replay reaches: {string.Join("; ", missing)}.{Environment.NewLine}What each reaches:{Environment.NewLine}{listing}");
    }

    /// <summary>What the match of a replay reaches of the places round structures, in the words of <see cref="EveryRule"/>.</summary>
    private static HashSet<string> Reached(Sim.Serialization.Replay replay)
    {
        var match = ReplayRuns.Start(replay);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var tick = 0; tick < replay.TickCount && seen.Count < EveryRule.Length; tick++)
        {
            match.Simulation.Tick();
            seen.UnionWith(PlacesByStructures(match));
        }

        return seen;
    }

    /// <summary>What the melee units have of the places round structures after the tick that has just run.</summary>
    private static HashSet<string> PlacesByStructures(Match match)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var onPlaces = new Dictionary<EntityId, int>();
        var waiting = new HashSet<EntityId>();
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            var unit = match.Combatants.ValueAt(i);
            var body = match.Bodies.Get(entity);
            if (!match.Combat.ProfileOf(body.Owner, unit.Chassis).Melee)
            {
                continue;
            }

            if (!unit.HasPlace)
            {
                // Stopped behind its own side, with the structure it is after still out of reach.
                if (unit.Engaged
                    && match.Structures.TryGet(unit.Target, out var wanted)
                    && match.Movers.Get(entity).State == MoveState.Idle
                    && wanted.Tiles.DistanceSquaredRawTo(body.Position) > match.Combat.StructureReachSquared)
                {
                    waiting.Add(unit.Target);
                }

                continue;
            }

            // The place is by what the unit fights, which is its target wherever a unit fights nothing else.
            if (!match.Structures.TryGet(unit.PlaceFor, out var structure))
            {
                continue;
            }

            Assert.True(
                structure.Tiles.DistanceSquaredRawTo(unit.Place) <= match.Combat.StructureReachSquared,
                "A place by a structure is out of reach of it.");
            if (match.Data.StructureOf(structure.Type)?.Wall is not null)
            {
                seen.Add("a melee unit with a place by a wall");
            }

            if (body.Position != unit.Place)
            {
                seen.Add("a melee unit walking to its place by a structure");
                continue;
            }

            onPlaces[unit.PlaceFor] = onPlaces.GetValueOrDefault(unit.PlaceFor) + 1;
            if (unit.Wait > Fraction.Zero && unit.Fought == unit.Place)
            {
                seen.Add("a melee unit that attacks a structure from its place");
            }
        }

        if (onPlaces.Values.Any(count => count >= 3))
        {
            seen.Add("three melee units on their places by one structure");
        }

        if (waiting.Any(onPlaces.ContainsKey))
        {
            seen.Add("a melee unit that stands out of reach of a structure while another is on its place by it");
        }

        return seen;
    }
}
