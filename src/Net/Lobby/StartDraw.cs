using Sim.Setup;

namespace Net;

/// <summary>
/// The draw of the starts (design 5.2): which of the map's start positions each player gets. The
/// host draws once, outside the simulation, and the result travels in the description of the
/// match, so the chance involved never has to be the same on two machines.
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
        ArgumentOutOfRangeException.ThrowIfGreaterThan(playerCount, startCount);
        ArgumentNullException.ThrowIfNull(pick);

        var starts = new int[startCount];
        for (var i = 0; i < startCount; i++)
        {
            starts[i] = i;
        }

        var players = new PlayerSetup[playerCount];
        for (var player = 0; player < playerCount; player++)
        {
            var left = startCount - player;
            var drawn = pick(left);
            if (drawn < 0 || drawn >= left)
            {
                throw new ArgumentException($"The source of chance returned {drawn} when asked for a number below {left}.", nameof(pick));
            }

            players[player] = new PlayerSetup(starts[player + drawn]);
            starts[player + drawn] = starts[player];
        }

        return players;
    }
}
