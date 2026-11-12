using Sim.Core;

namespace Tools.Replay.Scripts;

/// <summary>
/// Orders to concede (M24). For the first minute or two only orders in the name of players who
/// are not in the match, which are dropped; after that also orders from the players, whoever
/// the draw names: one who is in the match and goes out with it, one who is out already, and
/// the winner of a match that is decided, whose order is dropped. The orders need no units.
/// </summary>
/// <remarks>
/// The script waits before the first order of a player because a match of two is decided by it,
/// and from then on the simulation drops everything the other scripts order for the player who
/// is out: a recording with every script keeps a match worth the name before that.
/// </remarks>
[OrderScript("concede")]
internal sealed class ConcedeOrders : OrderScript
{
    private const int LongestPause = 400;
    private const int ShortestQuiet = 1200;

    /// <summary>What the seed of the orders to concede differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D24_0000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly int _quietUntil;
    private int _nextTick;

    public ConcedeOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(LongestPause);
        _quietUntil = ShortestQuiet + _random.NextInt(ShortestQuiet);
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick < _nextTick)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var players = Match.Players.Count;
        var player = _random.NextInt(100) < 30 || tick < _quietUntil
            ? players + _random.NextInt(5)
            : _random.NextInt(players);
        commands.Add(new Command(CommandType.Concede, player));
    }
}
