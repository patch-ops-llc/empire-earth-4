using System.Globalization;
using Sim;
using Sim.Data;
using Tools.AiMatch;

namespace Tools.AiSeries;

/// <summary>
/// What a series of headless matches comes to, as text for a reader and as tables for a
/// spreadsheet. Every time is game time.
/// </summary>
public static class SeriesReport
{
    private static readonly string[] PairingHeader = ["map", "player 0", "player 1", "matches", "player 0 won", "player 1 won", "no winner", "length by conquest"];

    /// <summary>Writes the summary: how the matches ended, who won from where, and what each personality did when.</summary>
    public static void Write(TextWriter output, GameData data, IReadOnlyList<SeriesMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(matches);

        var conquests = matches.Where(match => match.Result.Ended == MatchEnd.Conquest).ToList();
        output.WriteLine("How the matches ended");
        output.WriteLine(Invariant($"  by conquest, with a winner     {conquests.Count(match => match.Result.Winner is not null),4}"));
        output.WriteLine(Invariant($"  by conquest, with nobody left  {conquests.Count(match => match.Result.Winner is null),4}"));
        output.WriteLine(Invariant($"  at the time limit              {matches.Count - conquests.Count,4}"));
        output.WriteLine($"  length of those that ended by conquest: {Spread(conquests.Select(match => match.Result.Ticks))}");
        output.WriteLine($"  first hit of a match: {Spread(matches.Select(match => match.Result.FirstHitTick).Where(tick => tick >= 0))}");

        output.WriteLine();
        output.WriteLine("Wins by pairing, over both ways round of the starts and every command delay");
        var pairings = new List<string[]> { PairingHeader };
        foreach (var group in matches.GroupBy(match => (match.Map.Id, First: match.Result.Seats[0].Personality, Second: match.Result.Seats[1].Personality)))
        {
            pairings.Add(
            [
                group.Key.Id,
                group.Key.First,
                group.Key.Second,
                Number(group.Count()),
                Number(group.Count(match => match.Result.Winner == 0)),
                Number(group.Count(match => match.Result.Winner == 1)),
                Number(group.Count(match => match.Result.Winner is null)),
                Spread(group.Where(match => match.Result.Ended == MatchEnd.Conquest).Select(match => match.Result.Ticks)),
            ]);
        }

        Table(output, pairings);

        var personalities = matches.SelectMany(match => match.Result.Seats).Select(seat => seat.Personality).Distinct().Order(StringComparer.Ordinal).ToList();
        var mixed = matches.Where(match => match.Result.Seats[0].Personality != match.Result.Seats[1].Personality).ToList();
        if (mixed.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Wins by personality, in the matches between two different ones");
            var rows = new List<string[]>();
            foreach (var personality in personalities)
            {
                var played = mixed.Where(match => match.Result.Seats.Any(seat => seat.Personality == personality)).ToList();
                rows.Add([personality, Share(played.Count(match => match.Decided?.Winner.Personality == personality), played.Count)]);
            }

            rows.Add(["no winner", Share(mixed.Count(match => match.Result.Winner is null), mixed.Count)]);
            Table(output, rows);
        }

        output.WriteLine();
        output.WriteLine("Wins by seat and by start, in all matches");
        var sides = new List<string[]>();
        foreach (var map in matches.Select(match => match.Map.Id).Distinct())
        {
            var on = matches.Where(match => match.Map.Id == map).ToList();
            sides.Add(
            [
                map,
                $"player 0 {Share(on.Count(match => match.Result.Winner == 0), on.Count)}",
                $"player 1 {Share(on.Count(match => match.Result.Winner == 1), on.Count)}",
                $"start 0 {Share(on.Count(match => match.Decided?.Winner.Start == 0), on.Count)}",
                $"start 1 {Share(on.Count(match => match.Decided?.Winner.Start == 1), on.Count)}",
            ]);
        }

        Table(output, sides);

        var (startEpoch, epochCap) = (data.Rules.Match.StartEpoch, data.Rules.Match.EpochCap);
        output.WriteLine();
        output.WriteLine("When each personality got where, over every seat it had: the middle seat (the earliest to the latest), and how many got there");
        var whens = new List<string[]>();
        whens.Add(["personality", "seats", .. Enumerable.Range(startEpoch + 1, Math.Max(0, epochCap - startEpoch)).Select(epoch => $"epoch {epoch}"), $"every track at tier {epochCap}", "first attack order"]);
        foreach (var personality in personalities)
        {
            var seats = matches.SelectMany(match => match.Result.Seats).Where(seat => seat.Personality == personality).ToList();
            whens.Add(
            [
                personality,
                Number(seats.Count),
                .. Enumerable.Range(startEpoch + 1, Math.Max(0, epochCap - startEpoch)).Select(epoch => Reached(seats.Select(seat => Series.EpochTick(seat, epoch)))),
                Reached(seats.Select(seat => seat.EveryTrackAtCapTick)),
                Reached(seats.Select(seat => seat.FirstAttackOrderTick)),
            ]);
        }

        Table(output, whens);

        output.WriteLine();
        output.WriteLine("Research paid for and not given back, per seat on average: Materials and Energy added up, and the share of all the personality's research");
        var paid = new List<string[]> { (string[])["track", .. personalities] };
        var totals = personalities.ToDictionary(
            personality => personality,
            personality =>
            {
                var seats = matches.SelectMany(match => match.Result.Seats).Where(seat => seat.Personality == personality).ToList();
                return (Seats: seats, All: seats.Sum(seat => seat.ResearchMaterials.Sum() + seat.ResearchEnergy.Sum()));
            });
        for (var track = 0; track < data.Tracks.Count; track++)
        {
            var index = track;
            paid.Add(
            [
                data.Tracks[track].Id,
                .. personalities.Select(personality =>
                {
                    var (seats, all) = totals[personality];
                    var onTrack = seats.Sum(seat => seat.ResearchMaterials[index] + seat.ResearchEnergy[index]);
                    return Invariant($"{(double)onTrack / Math.Max(1, seats.Count):N0} ({(all == 0 ? 0 : 100.0 * onTrack / all):0}%)");
                }),
            ]);
        }

        paid.Add(["all", .. personalities.Select(personality => Invariant($"{(double)totals[personality].All / Math.Max(1, totals[personality].Seats.Count):N0}"))]);
        Table(output, paid);

        output.WriteLine();
        output.WriteLine("Supply at the cap, where nothing can be trained, and above it");
        var above = new List<string[]>();
        foreach (var personality in personalities)
        {
            var seats = totals[personality].Seats;
            var at = seats.Where(seat => seat.TicksAtSupplyCap > 0).ToList();
            var over = seats.Where(seat => seat.TicksAboveSupplyCap > 0).ToList();
            above.Add(
            [
                personality,
                $"at the cap in {Share(at.Count, seats.Count)} seats" + (at.Count == 0 ? "" : $", for {Spread(at.Select(seat => seat.TicksAtSupplyCap))}"),
                $"above it in {Share(over.Count, seats.Count)} seats"
                + (over.Count == 0 ? "" : Invariant($", by at most {over.Max(seat => seat.MostAboveSupplyCap)}, for {Spread(over.Select(seat => seat.TicksAboveSupplyCap))}")),
            ]);
        }

        Table(output, above);

        var decided = matches.Where(match => match.Decided is not null).ToList();
        output.WriteLine();
        output.WriteLine("After the fight was lost, in the matches with a winner");
        output.WriteLine($"  from the last moment the loser had half the winner's army value to the end: {Spread(decided.Select(match => match.Result.Ticks - match.LoserBehindFromTick!.Value))}");
        output.WriteLine($"  workers the loser still had at the end: {Counts(decided.Select(match => match.Decided!.Value.Loser.Workers))}");
        output.WriteLine($"  fighting units the loser still had at the end: {Counts(decided.Select(match => match.Decided!.Value.Loser.Units))}");
        output.WriteLine($"  fighting units the winner had at the end: {Counts(decided.Select(match => match.Decided!.Value.Winner.Units))}");
    }

    /// <summary>Writes one line per seat of every match, with a header line, as comma-separated values. Times are in seconds, empty when the thing never happened.</summary>
    public static void WriteCsv(TextWriter output, GameData data, IReadOnlyList<SeriesMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(matches);

        var (startEpoch, epochCap) = (data.Rules.Match.StartEpoch, data.Rules.Match.EpochCap);
        var epochs = Enumerable.Range(startEpoch + 1, Math.Max(0, epochCap - startEpoch)).ToList();
        output.WriteLine(string.Join(
            ',',
            [
                "match", "map", "command_delay", "ended", "length_s", "winner", "first_hit_s", "loser_behind_from_s", "commands_given", "commands_dropped", "state_hash",
                "player", "start", "personality", "opponent", "result", "out_s",
                .. epochs.Select(epoch => $"epoch_{epoch}_s"), "every_track_at_cap_s", "first_attack_order_s",
                .. data.Tracks.Select(track => $"tier_{track.Id}"),
                .. data.Tracks.Select(track => $"research_materials_{track.Id}"),
                .. data.Tracks.Select(track => $"research_energy_{track.Id}"),
                "supply_used", "supply_cap", "at_cap_s", "above_cap_s", "most_above_cap", "workers", "units", "army_value", "units_lost", "structures_lost", "materials", "energy",
            ]));
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var result = match.Result;
            foreach (var seat in result.Seats)
            {
                output.WriteLine(string.Join(
                    ',',
                    [
                        Number(index + 1), match.Map.Id, Number(match.CommandDelay), result.Ended == MatchEnd.Conquest ? "conquest" : "time_limit", Seconds(result.Ticks),
                        result.Winner is { } winner ? result.Seats[winner].Personality : "", Seconds(result.FirstHitTick), match.LoserBehindFromTick is { } behind ? Seconds(behind) : "",
                        Number(result.CommandsGiven), Invariant($"{result.CommandsDropped}"), Invariant($"{result.StateHash:x16}"),
                        Number(seat.Player), Number(seat.Start), seat.Personality, result.Seats[^(seat.Player + 1)].Personality,
                        result.Winner is null ? "none" : result.Winner == seat.Player ? "won" : "lost", Seconds(seat.OutTick),
                        .. epochs.Select(epoch => Seconds(Series.EpochTick(seat, epoch))), Seconds(seat.EveryTrackAtCapTick), Seconds(seat.FirstAttackOrderTick),
                        .. seat.Tiers.Select(Number),
                        .. seat.ResearchMaterials.Select(amount => Invariant($"{amount}")),
                        .. seat.ResearchEnergy.Select(amount => Invariant($"{amount}")),
                        Number(seat.SupplyUsed), Number(seat.SupplyCap), Seconds(seat.TicksAtSupplyCap), Seconds(seat.TicksAboveSupplyCap), Number(seat.MostAboveSupplyCap), Number(seat.Workers), Number(seat.Units),
                        Invariant($"{seat.ArmyValue}"), Number(seat.UnitsLost), Number(seat.StructuresLost), Invariant($"{seat.Materials}"), Invariant($"{seat.Energy}"),
                    ]));
            }
        }
    }

    /// <summary>Writes what each seat of every match had every ten seconds, one line per seat and moment, with a header line, as comma-separated values.</summary>
    public static void WriteSamples(TextWriter output, IReadOnlyList<SeriesMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(matches);

        output.WriteLine("match,map,command_delay,player,start,personality,time_s,workers,units,army_value,supply_used,supply_cap,materials,energy");
        for (var index = 0; index < matches.Count; index++)
        {
            foreach (var seat in matches[index].Result.Seats)
            {
                foreach (var sample in seat.Samples)
                {
                    output.WriteLine(Invariant(
                        $"{index + 1},{matches[index].Map.Id},{matches[index].CommandDelay},{seat.Player},{seat.Start},{seat.Personality},{sample.Tick / SimInfo.TicksPerSecond},{sample.Workers},{sample.Units},{sample.ArmyValue},{sample.SupplyUsed},{sample.SupplyCap},{sample.Materials},{sample.Energy}"));
                }
            }
        }
    }

    private static string Spread(IEnumerable<int> ticks) =>
        Series.Spread(ticks) is var (median, least, most) ? $"{Series.Clock(median)} ({Series.Clock(least)} to {Series.Clock(most)})" : "none";

    private static string Counts(IEnumerable<int> counts) =>
        Series.Spread(counts) is var (median, least, most) ? Invariant($"{median} ({least} to {most})") : "none";

    /// <summary>The spread of the ticks at which something happened, and in how many of the cases it happened at all; -1 is a case in which it did not.</summary>
    private static string Reached(IEnumerable<int> ticks)
    {
        var all = ticks.ToList();
        var reached = all.Where(tick => tick >= 0).ToList();
        return reached.Count == 0 ? "never" : Invariant($"{Spread(reached)}, {reached.Count} of {all.Count}");
    }

    private static string Share(int count, int of) => Invariant($"{count} of {of} ({(of == 0 ? 0 : 100.0 * count / of):0}%)");

    private static string Number(int number) => number.ToString(CultureInfo.InvariantCulture);

    private static string Seconds(int ticks) => ticks < 0 ? "" : Invariant($"{(double)ticks / SimInfo.TicksPerSecond:0.##}");

    private static void Table(TextWriter output, List<string[]> rows)
    {
        var widths = new int[rows.Max(row => row.Length)];
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
            {
                widths[column] = Math.Max(widths[column], row[column].Length);
            }
        }

        foreach (var row in rows)
        {
            output.WriteLine(("  " + string.Join("  ", row.Select((cell, column) => cell.PadRight(widths[column])))).TrimEnd());
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
