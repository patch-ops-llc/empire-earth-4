using Sim;
using Sim.Data;
using Tools.AiMatch;

namespace Tools.AiSeries;

/// <summary>One match of a series: what set it apart from the others, and what came of it.</summary>
/// <param name="Map">The map.</param>
/// <param name="CommandDelay">The ticks between a computer player's thinking and its commands taking effect.</param>
/// <param name="Result">The match, with the seats in the order of the players.</param>
public sealed record SeriesMatch(MapDef Map, int CommandDelay, HeadlessResult Result)
{
    /// <summary>The seats that won and lost, when the match has one winner and two seats.</summary>
    public (SeatResult Winner, SeatResult Loser)? Decided =>
        Result.Winner is { } winner && Result.Seats.Count == 2 ? (Result.Seats[winner], Result.Seats[1 - winner]) : null;

    /// <summary>
    /// The last moment, of those sampled, at which the player who lost in the end still had at
    /// least half the army value of the winner, as a tick. From then on it was behind for good.
    /// None for a match without a winner.
    /// </summary>
    public int? LoserBehindFromTick
    {
        get
        {
            if (Decided is not var (winner, loser))
            {
                return null;
            }

            var last = 0;
            for (var i = 0; i < Math.Min(winner.Samples.Count, loser.Samples.Count); i++)
            {
                if (2 * loser.Samples[i].ArmyValue >= winner.Samples[i].ArmyValue)
                {
                    last = loser.Samples[i].Tick;
                }
            }

            return last;
        }
    }
}

/// <summary>
/// A series of headless matches between two computer players (design 5.5, item 7): every map,
/// every pairing of personalities in both orders of the seats, with the starts both ways round,
/// at every command delay asked for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not seeds.</b> The simulation rolls nothing (design 5.2), and a computer player rolls
/// nothing either, so two matches that differ in their seed alone are the same match. What makes
/// one match differ from another is the map, who sits in which seat (the order the players are
/// served in), who has which start (the side of the map), and the command delay, which moves
/// every order of both players by a twentieth of a second per tick. A series varies those.
/// The matches of a series are therefore not independent draws; they are the same match
/// disturbed a little, and a share of wins from them says how stable a result is, not how
/// likely.
/// </para>
/// <para>
/// The matches are played side by side on as many threads as asked for. What each comes to is
/// the same however many there are; only the time it takes differs.
/// </para>
/// </remarks>
public static class Series
{
    /// <summary>Plays the series and returns its matches in a fixed order: by map, pairing, starts and delay.</summary>
    /// <param name="progress">Called once after each match, from the thread that played it.</param>
    public static IReadOnlyList<SeriesMatch> Play(
        GameData data,
        IReadOnlyList<MapDef> maps,
        IReadOnlyList<AiPersonalityDef> personalities,
        IReadOnlyList<int> delays,
        int mostTicks,
        int jobs,
        Action? progress = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(personalities);
        ArgumentNullException.ThrowIfNull(delays);
        ArgumentOutOfRangeException.ThrowIfLessThan(mostTicks, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(jobs, 1);

        var planned = new List<(MapDef Map, AiPersonalityDef First, AiPersonalityDef Second, int[] Starts, int Delay)>();
        foreach (var map in maps)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(map.Starts.Count, 2);
            foreach (var first in personalities)
            {
                foreach (var second in personalities)
                {
                    foreach (var starts in (int[][])[[0, 1], [1, 0]])
                    {
                        foreach (var delay in delays)
                        {
                            planned.Add((map, first, second, starts, delay));
                        }
                    }
                }
            }
        }

        var played = new SeriesMatch[planned.Count];
        Parallel.For(0, planned.Count, new ParallelOptions { MaxDegreeOfParallelism = jobs }, index =>
        {
            var (map, first, second, starts, delay) = planned[index];
            played[index] = new SeriesMatch(map, delay, HeadlessMatch.Play(data, map, [first, second], 1, mostTicks, delay, starts));
            progress?.Invoke();
        });
        return played;
    }

    /// <summary>The middle of the values, the lower of the two middle ones when there is an even number, and the least and the most. None for no values.</summary>
    public static (int Median, int Least, int Most)? Spread(IEnumerable<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? null : (sorted[(sorted.Count - 1) / 2], sorted[0], sorted[^1]);
    }

    /// <summary>The tick in which a seat reached an epoch, or -1 when it never did.</summary>
    public static int EpochTick(SeatResult seat, int epoch)
    {
        ArgumentNullException.ThrowIfNull(seat);
        var index = epoch - (seat.Epoch - seat.EpochTicks.Count) - 1;
        return index >= 0 && index < seat.EpochTicks.Count ? seat.EpochTicks[index] : -1;
    }

    /// <summary>A tick as minutes and seconds of game time.</summary>
    public static string Clock(int ticks)
    {
        var seconds = ticks / SimInfo.TicksPerSecond;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }
}
