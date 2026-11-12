using Net;
using Sim.Core;
using Sim.Data;
using Sim.Serialization;
using static Tools.DesyncDiff.DesyncDiffTool;

namespace Tools.DesyncDiff;

/// <summary>
/// The part of <c>desync-diff</c> that reads desync bundles: two players' bundles of one match
/// against each other, or one bundle against a run of the match on this machine.
/// </summary>
/// <remarks>
/// <para>
/// A bundle has the hash of every part of the state for each tick of the match's last stretch,
/// so the first tick that differs and the parts that differ in it are read off, with no run at
/// all. The parts are whatever the bundle lists: the world's own fields and every store the
/// match had, by name. Nothing here knows what the stores are.
/// </para>
/// <para>
/// To name entities and fields, the whole state at that tick is needed from both sides. A bundle
/// has the state its match had in each tick the fault may have happened in, unless its machine
/// was too far behind when the match stopped. For a side that lacks it, this machine's run
/// stands in when its hashes for that tick are that side's. When that fails too, the states the
/// match stopped in are compared instead, and the report says how many ticks later they are.
/// </para>
/// </remarks>
internal static class BundleDiff
{
    public static int Run(IReadOnlyList<string> paths, GameData data, int limit, bool fromStart, TextWriter output, TextWriter errors)
    {
        var sides = new List<Side>();
        for (var i = 0; i < paths.Count; i++)
        {
            if (!TryLoad(paths[i], paths.Count == 1 ? "The bundle" : i == 0 ? "A" : "B", data, out var side, out var problem))
            {
                errors.WriteLine($"desync-diff: {paths[i]}: {problem}");
                return ExitCodes.Failed;
            }

            sides.Add(side);
        }

        return sides.Count == 1
            ? CompareWithHere(sides[0], data, limit, fromStart, output)
            : Compare(sides[0], sides[1], data, limit, fromStart, output, errors);
    }

    private static int Compare(Side first, Side second, GameData data, int limit, bool fromStart, TextWriter output, TextWriter errors)
    {
        output.WriteLine(Describe(first));
        output.WriteLine(Describe(second));

        if (!SameMatch(first, second))
        {
            errors.WriteLine("desync-diff: the two are not from the same match: the game version, the data, the map, the seed or the players differ.");
            return ExitCodes.Failed;
        }

        if (first.Bundle.Player == second.Bundle.Player)
        {
            output.WriteLine(Invariant($"Both bundles are from player {first.Bundle.Player + 1}. Two players' bundles are needed to compare their games."));
        }

        WriteMachines(first, second, output);

        if (first.Bundle.AgreedSnapshot is { } agreedA && second.Bundle.AgreedSnapshot is { } agreedB && first.Bundle.AgreedTick == second.Bundle.AgreedTick)
        {
            output.WriteLine(agreedA.AsSpan().SequenceEqual(agreedB)
                ? Invariant($"Both kept the same state after {first.Bundle.AgreedTick} ticks, from before the comparison that failed after {first.Bundle.DesyncTick}.")
                : Invariant($"The states the two kept after {first.Bundle.AgreedTick} ticks, which should be the same, differ."));
        }

        var parted = FirstDifference(first, second, output);
        WriteSecondRun(first, output);
        WriteSecondRun(second, output);

        // This machine's run of each side's commands: a third opinion, and a source of the state at the tick.
        var here = new HereRun?[2];
        Side[] both = [first, second];
        for (var i = 0; i < 2; i++)
        {
            here[i] = RunHere(both[i], data, fromStart, parted?.Tick ?? -1, out var whyNot);
            output.WriteLine(here[i] is { } run ? DescribeHere(both[i], run) : $"{both[i].Label}'s match was not run here: {whyNot}");
        }

        WriteReading(first, second, here, output);

        if (parted is null)
        {
            return CompareFinalStates(first, second, data, limit, "", [], output) ? ExitCodes.Failed : ExitCodes.Ok;
        }

        var tick = parted.Value.Tick;
        var stateA = StateAt(first, here[0], tick, data);
        var stateB = StateAt(second, here[1], tick, data);
        if (stateA is not null && stateB is not null)
        {
            var differences = Differences(stateA, stateB);
            if (differences.Count > 0)
            {
                WriteDifferences(differences, limit, Invariant($"after {tick} ticks, the first tick that differs"), "A | B", output);
                return ExitCodes.Failed;
            }

            output.WriteLine(Invariant($"The states after {tick} ticks have the same value in every field, though their hashes differ."));
        }

        var missing = (stateA is null, stateB is null) switch
        {
            (true, true) => "the state of either side",
            (true, false) => "A's state",
            (false, true) => "B's state",
            _ => "",
        };
        var why = missing.Length == 0
            ? ""
            : Invariant($"The fields after {tick} ticks cannot be compared: {missing} at that tick is not on file, and this machine's run does not give it. ");
        CompareFinalStates(first, second, data, limit, why, parted.Value.Parts, output);
        return ExitCodes.Failed;
    }

    private static int CompareWithHere(Side side, GameData data, int limit, bool fromStart, TextWriter output)
    {
        output.WriteLine(Describe(side));
        output.WriteLine("The machine it is from:");
        foreach (var (key, value) in side.Bundle.Machine)
        {
            output.WriteLine($"  {key}: {value}");
        }

        WriteSecondRun(side, output);

        var run = RunHere(side, data, fromStart, -1, out var whyNot);
        if (run is null)
        {
            output.WriteLine($"The match was not run here: {whyNot}");
            return ExitCodes.Failed;
        }

        output.WriteLine(DescribeHere(side, run));
        if (run.PartedAt < 0)
        {
            output.WriteLine("This machine repeats what that machine did, so it does not show the fault alone. The other player's bundle is needed: give both to compare them.");
            return ExitCodes.Ok;
        }

        // The bundle's state at the tick, if it has it, against this machine's. Else the final states.
        var theirs = side.Bundle.StateAt(run.PartedAt) is { } state ? Fields(side, state.Snapshot, data) : null;
        var differences = theirs is null ? [] : Differences(theirs, run.FieldsAtParted!, "the bundle", "here");
        if (differences.Count > 0)
        {
            WriteDifferences(differences, limit, Invariant($"after {run.PartedAt} ticks, the first tick that differs"), "the bundle | here", output);
            return ExitCodes.Failed;
        }

        var final = Fields(side, side.Bundle.FinalSnapshot, data);
        if (final is null || run.FinalFields is null)
        {
            output.WriteLine("The fields cannot be compared: the bundle's final state does not fit a match built here.");
            return ExitCodes.Failed;
        }

        differences = FirstPartsFirst(Differences(final, run.FinalFields, "the bundle", "here"), run.PartedIn);
        var later = side.Bundle.FinalTick - run.PartedAt;
        output.WriteLine(Invariant($"The bundle has no state for the tick that differs, so the states the match stopped in are compared, after {side.Bundle.FinalTick} ticks: {later} {(later == 1 ? "tick" : "ticks")} later, so some of what follows came of the first difference."));
        if (differences.Count == 0)
        {
            output.WriteLine("They are the same in every field.");
        }
        else
        {
            WriteDifferences(differences, limit, "between the final states", "the bundle | here", output);
        }

        return ExitCodes.Failed;
    }

    /// <summary>Compares the states the match stopped in. Returns true if they differ.</summary>
    private static bool CompareFinalStates(Side first, Side second, GameData data, int limit, string why, List<string> firstParts, TextWriter output)
    {
        var finalA = Fields(first, first.Bundle.FinalSnapshot, data);
        var finalB = Fields(second, second.Bundle.FinalSnapshot, data);
        if (finalA is null || finalB is null)
        {
            output.WriteLine($"{why}The final states cannot be compared either: {(finalA is null ? "A" : "B")}'s does not fit a match built here.");
            return true;
        }

        var (tickA, tickB) = (first.Bundle.FinalTick, second.Bundle.FinalTick);
        output.WriteLine(tickA == tickB
            ? Invariant($"{why}The states the match stopped in are compared, after {tickA} ticks{(firstParts.Count > 0 ? ": later than the first difference, so some of what follows came of it" : "")}.")
            : Invariant($"{why}The states the match stopped in are compared. They are from different ticks, {tickA} and {tickB}, so most of what follows is the time between them."));

        var differences = FirstPartsFirst(Differences(finalA, finalB), firstParts);
        if (differences.Count == 0)
        {
            output.WriteLine("The two final states are the same in every field.");
            return false;
        }

        WriteDifferences(differences, limit, "between the final states", "A | B", output);
        return true;
    }

    /// <summary>Puts the lines of the parts that differed first in front: they are where the cause is.</summary>
    private static List<Difference> FirstPartsFirst(List<Difference> differences, List<string> firstParts) =>
        [.. differences.OrderBy(difference => firstParts.Contains(difference.Section) ? 0 : 1)];

    private static string Describe(Side side)
    {
        var bundle = side.Bundle;
        var agreed = bundle.AgreedTick < 0 ? "none" : Invariant($"{bundle.AgreedTick}");
        return Invariant(
            $"{side.Label}: {side.Name}  player {bundle.Player + 1} of {bundle.PlayerCount}  map={bundle.MapId} seed={bundle.Seed} game={bundle.GameVersion} failed_comparison_tick={bundle.DesyncTick} final_tick={bundle.FinalTick} agreed_state_tick={agreed}");
    }

    private static bool SameMatch(Side first, Side second)
    {
        if (first.DescriptionBytes is not null && second.DescriptionBytes is not null)
        {
            return first.DescriptionBytes.AsSpan().SequenceEqual(second.DescriptionBytes);
        }

        var (a, b) = (first.Bundle, second.Bundle);
        return a.GameVersion == b.GameVersion && a.ContentHash == b.ContentHash && a.MapId == b.MapId && a.Seed == b.Seed && a.PlayerCount == b.PlayerCount;
    }

    private static void WriteMachines(Side first, Side second, TextWriter output)
    {
        var keys = first.Bundle.Machine.Select(fact => fact.Key).Concat(second.Bundle.Machine.Select(fact => fact.Key)).Distinct().ToList();
        if (keys.Count == 0)
        {
            return;
        }

        output.WriteLine("The machines, A | B:");
        var width = keys.Max(key => key.Length);
        foreach (var key in keys)
        {
            var a = first.Bundle.MachineValue(key) ?? "?";
            var b = second.Bundle.MachineValue(key) ?? "?";

            // When each file was written always differs and says nothing about the machines.
            var note = a == b || key == DesyncBundle.WrittenAtKey ? "" : "   <- differs";
            output.WriteLine($"  {key.PadRight(width)}  {a} | {b}{note}");
        }
    }

    /// <summary>Finds the first tick for which both have hashes and any part differs, and writes what it finds.</summary>
    private static (int Tick, List<string> Parts)? FirstDifference(Side first, Side second, TextWriter output)
    {
        // First the replays, which have one hash of the whole state for each tick of the whole match.
        var replayTick = -1;
        if (first.Replay is not null && second.Replay is not null)
        {
            var others = second.Replay.Hashes.ToDictionary(hash => hash.Tick, hash => hash.Hash);
            var compared = 0;
            var lastSame = -1;
            foreach (var hash in first.Replay.Hashes)
            {
                if (!others.TryGetValue(hash.Tick, out var other))
                {
                    continue;
                }

                compared++;
                if (hash.Hash != other)
                {
                    replayTick = hash.Tick;
                    output.WriteLine(lastSame < 0
                        ? Invariant($"The recorded state hashes differ from the first one compared, after {hash.Tick} ticks: {hash.Hash:x16} | {other:x16}.")
                        : Invariant($"The recorded state hashes are the same after {lastSame} ticks and differ after {hash.Tick}: {hash.Hash:x16} | {other:x16}."));
                    break;
                }

                lastSame = hash.Tick;
            }

            if (replayTick < 0)
            {
                output.WriteLine(compared == 0
                    ? "The two replays recorded no state hash for the same tick."
                    : Invariant($"The recorded state hashes of the replays are the same, all {compared} that both have, up to {lastSame} ticks."));
            }
        }

        // Then the hashes of the parts, which say where in the state.
        var names = first.Bundle.Parts.Intersect(second.Bundle.Parts, StringComparer.Ordinal).ToList();
        var onlyOne = first.Bundle.Parts.Except(second.Bundle.Parts, StringComparer.Ordinal).Concat(second.Bundle.Parts.Except(first.Bundle.Parts, StringComparer.Ordinal)).ToList();
        if (onlyOne.Count > 0)
        {
            output.WriteLine($"The two games do not have the same stores, so they are not the same build. Only one has: {string.Join(", ", onlyOne)}.");
        }

        var indexA = names.Select(name => first.Bundle.Parts.IndexOf(name)).ToArray();
        var indexB = names.Select(name => second.Bundle.Parts.IndexOf(name)).ToArray();
        var comparedTicks = 0;
        foreach (var hashes in first.Bundle.Hashes)
        {
            if (second.Bundle.HashesAt(hashes.Tick) is not { } other)
            {
                continue;
            }

            comparedTicks++;
            var differing = new List<string>();
            for (var i = 0; i < names.Count; i++)
            {
                if (hashes.Hashes[indexA[i]] != other.Hashes[indexB[i]])
                {
                    differing.Add(names[i]);
                }
            }

            if (differing.Count > 0)
            {
                if (replayTick >= 0 && replayTick < hashes.Tick)
                {
                    output.WriteLine(Invariant($"The bundles have hashes of the parts of the state only from {hashes.Tick} ticks on, later than the first difference, so some of what follows came of it."));
                }

                output.WriteLine(Invariant($"First tick that differs: {hashes.Tick}. The states are the same after {hashes.Tick - 1} ticks and differ after {hashes.Tick}."));
                var stores = differing.Where(name => name != StoreHasher.WorldPart).ToList();
                var firstStore = stores.Count == 0 ? "First store that differs: none" : $"First store that differs: {stores[0]}";
                var world = differing.Count == stores.Count ? "" : " The world's own fields differ too: the tick, the random generator or the list of entities.";
                output.WriteLine(Invariant($"{firstStore}. In that tick {differing.Count} of the {names.Count} parts of the state differ: {string.Join(", ", differing)}.{world}"));
                return (hashes.Tick, differing);
            }
        }

        output.WriteLine(comparedTicks == 0
            ? "The bundles have no hashes of the parts of the state for the same tick, so they do not say which store differed first."
            : Invariant($"The hashes of the parts of the state are the same in all {comparedTicks} ticks that both bundles have."));
        return replayTick >= 0 ? (replayTick, []) : null;
    }

    private static void WriteSecondRun(Side side, TextWriter output)
    {
        var run = side.Bundle.SecondRun;
        var label = side.Label;
        output.WriteLine(run.Outcome switch
        {
            SecondRunOutcome.Repeated => Invariant($"{label}'s machine ran ticks {run.From} to {run.To} a second time from the agreed state and got the same states: it repeats itself."),
            SecondRunOutcome.Parted => Invariant($"{label}'s machine ran the match a second time from the agreed state after {run.From} ticks and got another state than the first time, first after {run.PartedAt} ticks, in: {run.Detail}. It does not repeat itself."),
            _ => $"{label}'s machine did not run the match a second time: {run.Detail}.",
        });
    }

    private static string DescribeHere(Side side, HereRun run)
    {
        var from = run.From == 0 ? "from the start" : Invariant($"from the agreed state after {run.From} ticks");
        var whose = side.Label == "The bundle" ? "the bundle's" : $"{side.Label}'s";
        if (run.PartedAt < 0)
        {
            return Invariant($"Run here {from}, {whose} commands give the states that machine had, in all {run.Compared} ticks compared, up to {run.To}.");
        }

        var where = run.PartedIn.Count == 0 ? "" : $", in: {string.Join(", ", run.PartedIn)}";
        return Invariant($"Run here {from}, {whose} commands give another state than that machine had, first after {run.PartedAt} ticks{where}.");
    }

    /// <summary>What the runs say together, for whoever reads the report first.</summary>
    private static void WriteReading(Side first, Side second, HereRun?[] here, TextWriter output)
    {
        var (a, b) = (first.Bundle.SecondRun.Outcome, second.Bundle.SecondRun.Outcome);
        if (a == SecondRunOutcome.Repeated && b == SecondRunOutcome.Repeated)
        {
            output.WriteLine("Reading: each machine repeats itself, so the same code gives different results on the two. Look at what differs between the machines and at the fields below.");
        }
        else if (a == SecondRunOutcome.Parted || b == SecondRunOutcome.Parted)
        {
            var who = (a == SecondRunOutcome.Parted, b == SecondRunOutcome.Parted) switch
            {
                (true, true) => "Neither machine repeats",
                (true, false) => "A's machine does not repeat",
                _ => "B's machine does not repeat",
            };
            output.WriteLine($"Reading: {who} its own run. Something there changed the state that its commands and its last agreed state do not account for: state that the walk misses, or something outside the simulation.");
        }

        var agreesA = here[0] is { PartedAt: < 0 };
        var agreesB = here[1] is { PartedAt: < 0 };
        if (agreesA != agreesB && here[0] is not null && here[1] is not null)
        {
            output.WriteLine($"This machine agrees with {(agreesA ? "A" : "B")}.");
        }
    }

    /// <summary>
    /// The fields of a side's state after a number of ticks: from its bundle if the bundle has
    /// that state, else from this machine's run if the run had that side's hashes at that tick.
    /// </summary>
    private static List<StateField>? StateAt(Side side, HereRun? here, int tick, GameData data)
    {
        if (side.Bundle.StateAt(tick) is { } state && Fields(side, state.Snapshot, data) is { } fields)
        {
            return fields;
        }

        return here is { AskedMatches: true } ? here.FieldsAtAsked : null;
    }

    /// <summary>The fields of a snapshot of a side's match, or null if it does not fit a match built here.</summary>
    private static List<StateField>? Fields(Side side, byte[] snapshot, GameData data)
    {
        if (side.Replay is null)
        {
            return null;
        }

        try
        {
            var match = side.Replay.Description.CreateMatch(data);
            var reader = new ByteReader(snapshot);
            Snapshot.Restore(match, reader);
            reader.ExpectEnd();
            return StateListing.Of(match.World);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs a side's match on this machine, from the agreed state or from the start, with the
    /// commands in its replay, and compares every tick with what the side has for it: the hashes
    /// of the parts where the bundle has them, else the hash in the replay.
    /// </summary>
    /// <param name="side">The side whose match to run.</param>
    /// <param name="data">The game data.</param>
    /// <param name="fromStart">True to run from the first tick even when there is an agreed state.</param>
    /// <param name="askedTick">A tick whose state to keep, or -1.</param>
    /// <param name="whyNot">Why there was no run, when null is returned.</param>
    private static HereRun? RunHere(Side side, GameData data, bool fromStart, int askedTick, out string whyNot)
    {
        whyNot = "";
        var bundle = side.Bundle;
        if (side.Replay is null)
        {
            whyNot = "the bundle has no replay that can be read, so the match and its commands are not known.";
            return null;
        }

        var match = side.Replay.Description.CreateMatch(data);
        if (!fromStart && bundle.AgreedSnapshot is not null)
        {
            try
            {
                var reader = new ByteReader(bundle.AgreedSnapshot);
                Snapshot.Restore(match, reader);
                reader.ExpectEnd();
            }
            catch (InvalidDataException refused)
            {
                whyNot = $"the agreed state in the bundle does not fit a match built here: {refused.Message}";
                return null;
            }
        }

        var parts = StoreHasher.PartNames(match.World);
        var sameParts = parts.AsSpan().SequenceEqual(bundle.Parts.AsSpan());
        var from = match.World.Tick;
        foreach (var (tick, command) in side.Replay.Commands)
        {
            if (tick >= from)
            {
                match.Simulation.Commands.Enqueue(tick, command);
            }
        }

        var tables = sameParts ? bundle.Hashes.ToDictionary(hashes => hashes.Tick) : [];
        var recorded = side.Replay.Hashes.ToDictionary(hash => hash.Tick, hash => hash.Hash);
        var hasher = new StoreHasher();
        var hashes = new ulong[parts.Length];
        var run = new HereRun { From = from, To = bundle.FinalTick };
        while (true)
        {
            var tick = match.World.Tick;
            var same = true;
            var known = false;
            if (tables.TryGetValue(tick, out var theirs))
            {
                known = true;
                hasher.Compute(match.World, hashes);
                same = hashes.AsSpan().SequenceEqual(theirs.Hashes.AsSpan());
                if (!same && run.PartedAt < 0)
                {
                    run.PartedIn = [.. parts.Where((_, part) => hashes[part] != theirs.Hashes[part])];
                }
            }
            else if (recorded.TryGetValue(tick, out var whole))
            {
                known = true;
                same = match.World.ComputeHash() == whole;
            }

            if (known)
            {
                run.Compared++;
                if (!same && run.PartedAt < 0)
                {
                    run.PartedAt = tick;
                    run.FieldsAtParted = StateListing.Of(match.World);
                }
            }

            if (tick == askedTick)
            {
                run.AskedMatches = known && same;
                run.FieldsAtAsked = StateListing.Of(match.World);
            }

            if (tick >= bundle.FinalTick)
            {
                break;
            }

            match.Simulation.Tick();
        }

        run.FinalFields = StateListing.Of(match.World);
        return run;
    }

    private static bool TryLoad(string path, string label, GameData data, out Side side, out string problem)
    {
        side = default!;
        problem = "";

        DesyncBundle bundle;
        try
        {
            using var file = File.OpenRead(path);
            bundle = DesyncBundle.Read(file);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            problem = refused.Message;
            return false;
        }

        Sim.Serialization.Replay? replay = null;
        byte[]? descriptionBytes = null;
        if (bundle.Replay.Length > 0)
        {
            try
            {
                replay = Sim.Serialization.Replay.Read(new ByteReader(bundle.Replay), keepWhatIsWhole: true);
            }
            catch (InvalidDataException refused)
            {
                problem = $"the replay in the bundle cannot be read: {refused.Message}";
                return false;
            }

            if (replay.Description.FindProblem(data) is { } unfit)
            {
                problem = unfit;
                return false;
            }

            var writer = new ByteWriter();
            replay.Description.Write(writer);
            descriptionBytes = writer.ToArray();
        }

        side = new Side(label, Path.GetFileName(path), bundle, replay, descriptionBytes);
        return true;
    }

    private sealed record Side(string Label, string Name, DesyncBundle Bundle, Sim.Serialization.Replay? Replay, byte[]? DescriptionBytes);

    /// <summary>What a run on this machine gave.</summary>
    private sealed class HereRun
    {
        /// <summary>The tick the run started from: 0, or the tick of the agreed state.</summary>
        public int From { get; init; }

        /// <summary>The last tick of the run.</summary>
        public int To { get; init; }

        /// <summary>The number of ticks for which the side had a hash to compare with.</summary>
        public int Compared { get; set; }

        /// <summary>The first tick whose state here was not the side's, or -1.</summary>
        public int PartedAt { get; set; } = -1;

        /// <summary>The parts that differed in that tick, when the bundle has their hashes.</summary>
        public List<string> PartedIn { get; set; } = [];

        public List<StateField>? FieldsAtParted { get; set; }

        /// <summary>True when the state here at the tick asked for had the side's hashes, so it is the side's state.</summary>
        public bool AskedMatches { get; set; }

        public List<StateField>? FieldsAtAsked { get; set; }

        public List<StateField>? FinalFields { get; set; }
    }
}
