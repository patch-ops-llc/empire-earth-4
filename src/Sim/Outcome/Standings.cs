using Sim.Core;

namespace Sim.Outcome;

/// <summary>
/// Where every player stands in the match, and with that how the match ended (design 5.1): a
/// player is out when it has no Capitol and no Outpost, or when it concedes, and the last player
/// standing wins.
/// </summary>
/// <remarks>
/// <para>
/// A player who is out stays out, and gives no more orders: as the gate of the commands
/// (<see cref="ICommandGate"/>), this store turns away every command in such a player's name
/// before a handler sees it. What the player had stays on the map as it is.
/// </para>
/// <para>
/// The match is decided once at most one player is still in it. From then on nothing changes
/// here: nobody is taken out of a match that is decided, so the winner stays the winner whatever
/// happens to it afterwards. A match of one player is decided when that player is out, and has
/// no winner.
/// </para>
/// </remarks>
public sealed class Standings : IStateStore, ICommandGate
{
    public const string StoreName = "standings";

    /// <summary>The value of <see cref="Winner"/> and of <see cref="OutTick"/> when there is none.</summary>
    public const int None = -1;

    private readonly int[] _standing;
    private readonly int[] _outTick;

    public Standings(int players)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(players);
        _standing = new int[players];
        _outTick = new int[players];
        Array.Fill(_outTick, None);
    }

    public string Name => StoreName;

    public int PlayerCount => _standing.Length;

    /// <summary>The number of players still in the match.</summary>
    public int PlayingCount
    {
        get
        {
            var playing = 0;
            foreach (var standing in _standing)
            {
                if (standing == (int)PlayerStanding.Playing)
                {
                    playing++;
                }
            }

            return playing;
        }
    }

    /// <summary>
    /// True once the match has its result: at most one player is still in it, or, in a match of
    /// one player, none.
    /// </summary>
    public bool IsDecided => PlayingCount <= (PlayerCount > 1 ? 1 : 0);

    /// <summary>
    /// The player who won: the one still in a decided match of several players.
    /// <see cref="None"/> while the match is not decided, and when it ended with nobody left, as
    /// it does when the last players lose their last Capitols in the same tick.
    /// </summary>
    public int Winner
    {
        get
        {
            if (PlayerCount == 1 || !IsDecided)
            {
                return None;
            }

            return Array.IndexOf(_standing, (int)PlayerStanding.Playing);
        }
    }

    /// <summary>The tick in which the match was decided, or <see cref="None"/> while it is not.</summary>
    public int DecidedTick => IsDecided ? _outTick.Max() : None;

    /// <summary>Where a player stands.</summary>
    public PlayerStanding Of(int player) => (PlayerStanding)_standing[player];

    /// <summary>True for a player of the match who is still in it. False for a number that is no player.</summary>
    public bool IsPlaying(int player) =>
        player >= 0 && player < _standing.Length && _standing[player] == (int)PlayerStanding.Playing;

    /// <summary>The tick in which the player went out, or <see cref="None"/> while it is in the match.</summary>
    public int OutTick(int player) => _outTick[player];

    /// <summary>
    /// Takes a player out of the match. The caller has checked that the match is not decided:
    /// players who go out in the same tick for the same cause all go out, so this does not ask.
    /// </summary>
    /// <param name="player">A player who is still in the match.</param>
    /// <param name="how">Why: anything but <see cref="PlayerStanding.Playing"/>.</param>
    /// <param name="tick">The tick this happens in.</param>
    public void TakeOut(int player, PlayerStanding how, int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        if (how is not (PlayerStanding.Eliminated or PlayerStanding.Conceded))
        {
            throw new ArgumentException($"{how} does not take a player out of the match.", nameof(how));
        }

        if (!IsPlaying(player))
        {
            throw new ArgumentException($"Player {player} is not in the match.", nameof(player));
        }

        _standing[player] = (int)how;
        _outTick[player] = tick;
    }

    /// <summary>
    /// A command in the name of a player who is out is not run. One in the name of a number that
    /// is no player is let through, for its handler to refuse as it always has.
    /// </summary>
    public bool Admits(World world, in Command command) =>
        command.Player < 0 || command.Player >= _standing.Length || _standing[command.Player] == (int)PlayerStanding.Playing;

    public void RemoveEntities(ReadOnlySpan<EntityId> destroyed)
    {
    }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        for (var player = 0; player < _standing.Length; player++)
        {
            _standing[player] = walker.Field(_standing[player]);
            _outTick[player] = walker.Field(_outTick[player]);
            var playing = _standing[player] == (int)PlayerStanding.Playing;
            if (_standing[player] is < (int)PlayerStanding.Playing or > (int)PlayerStanding.Conceded
                || (playing ? _outTick[player] != None : _outTick[player] < 0))
            {
                throw new InvalidDataException($"The standing of player {player} is not one a match can reach.");
            }
        }
    }
}
