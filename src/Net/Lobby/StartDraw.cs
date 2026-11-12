using Sim.Setup;

namespace Net;

/// <summary>
/// The draw of the starts (design 5.2): which of the map's start positions each player gets. It
/// is a setting of the match (<see cref="MatchSettings"/>): the host draws every start unless the
/// match sets it. The host draws once, outside the simulation, and the result travels in the
/// description of the match, so the chance involved never has to be the same on two machines.
/// </summary>
public static class StartDraw
{
    /// <summary>
    /// Gives each player a different start, drawn evenly from the map's.
    /// </summary>
    /// <param name="playerCount">The number of players, at most <paramref name="startCount"/>.</param>
    /// <param name="startCount">The number of starts the map has.</param>
    /// <param name="pick">
    /// The source of chance: given n, it returns a whole number from 0 to n - 1, each equally
    /// likely. The caller owns it, because this library reads no random source of its own.
    /// </param>
    public static PlayerSetup[] Draw(int playerCount, int startCount, Func<int, int> pick)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(playerCount, 1);
        return Assign(new int?[playerCount], startCount, pick);
    }

    /// <summary>
    /// Gives each player a different start when the match sets some of them: a player whose start
    /// is set gets it, and the others are drawn evenly from the starts that are left. With none
    /// set this is <see cref="Draw"/>, and with all set nothing is drawn.
    /// </summary>
    /// <param name="set">For each player, the index of the start the match sets, or null to draw it.</param>
    /// <param name="startCount">The number of starts the map has, at least the number of players.</param>
    /// <param name="pick">The source of chance: see <see cref="Draw"/>. It is asked once for every start that is drawn.</param>
    /// <exception cref="ArgumentException">A start that is set is not one of the map's, or two players are set to the same one.</exception>
    public static PlayerSetup[] Assign(IReadOnlyList<int?> set, int startCount, Func<int, int> pick)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(pick);
        ArgumentOutOfRangeException.ThrowIfLessThan(set.Count, 1, nameof(set));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(set.Count, startCount, nameof(set));

        var taken = new bool[startCount];
        foreach (var start in set)
        {
            if (start is not { } index)
            {
                continue;
            }

            if (index < 0 || index >= startCount)
            {
                throw new ArgumentException($"Start {index} is set, and the map has the starts 0 to {startCount - 1}.", nameof(set));
            }

            if (taken[index])
            {
                throw new ArgumentException($"Two players are set to start {index}.", nameof(set));
            }

            taken[index] = true;
        }

        // The starts nobody is set to, lowest first.
        var free = new List<int>(startCount);
        for (var i = 0; i < startCount; i++)
        {
            if (!taken[i])
            {
                free.Add(i);
            }
        }

        var players = new PlayerSetup[set.Count];
        var used = 0;
        for (var player = 0; player < players.Length; player++)
        {
            if (set[player] is { } start)
            {
                players[player] = new PlayerSetup(start);
                continue;
            }

            var left = free.Count - used;
            var drawn = pick(left);
            if (drawn < 0 || drawn >= left)
            {
                throw new ArgumentException($"The source of chance returned {drawn} when asked for a number below {left}.", nameof(pick));
            }

            players[player] = new PlayerSetup(free[used + drawn]);
            free[used + drawn] = free[used];
            used++;
        }

        return players;
    }
}
