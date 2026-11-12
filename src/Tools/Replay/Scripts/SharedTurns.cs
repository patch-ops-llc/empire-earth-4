using Sim.Core;

namespace Tools.Replay.Scripts;

/// <summary>
/// The clock and the random generator that movement and research draw from together. Every few
/// ticks it is somebody's turn: a player and a kind of order are drawn, and the kind says which
/// of the two scripts gives the order.
/// </summary>
/// <remarks>
/// Research (M14) was added by widening the draw of movement's kinds, before a kind of order
/// came on a clock of its own, and replays have been recorded with what that gives. So these two
/// stay on one draw, and nothing else joins it: a later script brings its own clock and
/// generator, as <see cref="GatherOrders"/> does.
/// <para>
/// Movement's kinds are always in the draw. Research's are in it when research is used. So a
/// recording with movement and without research draws what recordings drew before M14.
/// </para>
/// </remarks>
internal sealed class SharedTurns
{
    /// <summary>The kinds of order from M2 to M5 are drawn below this number, and those of research from it.</summary>
    public const int MovementKinds = 100;

    private const int LongestPause = 120;

    private readonly ScriptSetup _setup;
    private int _kinds = MovementKinds;
    private int _nextTick;
    private int _drawnFor = -1;
    private bool _isDue;
    private int _player;
    private int _kind;

    private SharedTurns(ScriptSetup setup)
    {
        _setup = setup;
        Random = setup.Generator(0);
        _nextTick = 1 + Random.NextInt(10);
    }

    /// <summary>The generator of the turns. The script whose turn it is draws the rest of its order from it.</summary>
    public DeterministicRandom Random { get; }

    /// <summary>The turns of a recording: the same for every script that asks.</summary>
    public static SharedTurns Of(ScriptSetup setup) => setup.Shared(static recording => new SharedTurns(recording));

    /// <summary>
    /// Adds kinds to the draw after movement's and returns the number of the first of them. For
    /// <see cref="ResearchOrders"/>, when it starts and before the first tick.
    /// </summary>
    public int Widen(int kinds)
    {
        var first = _kinds;
        _kinds += kinds;
        return first;
    }

    /// <summary>
    /// Whether a tick, which is the next to run, is a turn, and whose and of which kind. The
    /// first script to ask about a tick makes the draw, and a script that asks later gets the
    /// same answer.
    /// </summary>
    public bool IsTurn(int tick, out int player, out int kind)
    {
        if (tick != _drawnFor)
        {
            _drawnFor = tick;
            _isDue = tick >= _nextTick;
            if (_isDue)
            {
                _nextTick = tick + 1 + Random.NextInt(LongestPause);
                _player = Random.NextInt(_setup.Match.Players.Count);
                _kind = Random.NextInt(_kinds);
            }
        }

        player = _player;
        kind = _kind;
        return _isDue;
    }
}
