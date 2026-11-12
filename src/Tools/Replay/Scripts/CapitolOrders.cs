using Sim.Core;
using Sim.Map;
using Sim.Numerics;
using Sim.Structures;

namespace Tools.Replay.Scripts;

/// <summary>
/// Orders at the starting Capitols (15b): units sent onto a Capitol, whose tiles they cannot
/// stand on, to a tile right beside one, and past one to its far side, so that their way leads
/// round it; and orders to attack a Capitol, which the simulation refuses until structures can be
/// attacked.
/// </summary>
/// <remarks>
/// A match has no structures but the Capitols of its start until workers build, so these are the
/// only structures the script finds. In a match without one it gives no orders.
/// </remarks>
[OrderScript("capitol")]
internal sealed class CapitolOrders : OrderScript
{
    private const int LongestPause = 120;

    /// <summary>What the seed of these orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D15_B000_0000_0000;

    private readonly DeterministicRandom _random;
    private int _nextTick;

    public CapitolOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick < _nextTick)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var player = _random.NextInt(Match.Players.Count);
        var kind = _random.NextInt(100);
        var structures = Match.Structures;
        CollectUnitsOf(player);
        if (Own.Count == 0 || structures.Count == 0)
        {
            return;
        }

        var index = _random.NextInt(structures.Count);
        var capitol = structures.EntityAt(index);
        var tiles = structures.ValueAt(index).Tiles;
        var center = StructureSetup.CenterOf(tiles);
        if (kind < 30)
        {
            // Onto the Capitol: no unit can stand there, so each goes to the nearest tile it can reach.
            PickSome(_random);
            var point = new FixedVector2(
                Fixed.FromRaw((tiles.X * Fixed.RawOne) + _random.NextInt(tiles.Size * Fixed.RawOne)),
                Fixed.FromRaw((tiles.Y * Fixed.RawOne) + _random.NextInt(tiles.Size * Fixed.RawOne)));
            commands.Add(Move(CommandType.Move, player, Picked, _random.NextInt(4) == 0 ? center : point));
        }
        else if (kind < 50)
        {
            // To a tile that shares a side with the Capitol, anywhere on it: also right at the wall.
            PickSome(_random);
            var along = _random.NextInt(tiles.Size);
            var (x, y) = _random.NextInt(4) switch
            {
                0 => (tiles.X + along, tiles.Y - 1),
                1 => (tiles.X + along, tiles.Y + tiles.Size),
                2 => (tiles.X - 1, tiles.Y + along),
                _ => (tiles.X + tiles.Size, tiles.Y + along),
            };
            var point = new FixedVector2(
                Fixed.FromRaw((x * Fixed.RawOne) + _random.NextInt(Fixed.RawOne)),
                Fixed.FromRaw((y * Fixed.RawOne) + _random.NextInt(Fixed.RawOne)));
            commands.Add(Move(CommandType.Move, player, Picked, point));
        }
        else if (kind < 75)
        {
            // To the far side: as far beyond the Capitol's center as the first unit is before it,
            // so the straight line there leads through the Capitol.
            PickSome(_random);
            var from = Match.Bodies.Get(Picked[0]).Position;
            var beyond = new FixedVector2(
                Clamp((2L * center.X.Raw) - from.X.Raw, Match.Grid.Width),
                Clamp((2L * center.Y.Raw) - from.Y.Raw, Match.Grid.Height));
            commands.Add(Move(CommandType.Move, player, _random.NextInt(2) == 0 ? Picked : Own, beyond));
        }
        else if (kind < 90)
        {
            // An order to attack a Capitol, the player's own or another's. Refused either way.
            PickSome(_random);
            commands.Add(new Command(CommandType.Attack, player, [.. Picked], targetEntity: capitol));
        }
        else
        {
            commands.Add(Move(CommandType.AttackMove, player, Own, center));
        }
    }

    private static Command Move(CommandType type, int player, List<EntityId> units, FixedVector2 target) =>
        new(type, player, [.. units], target.X, target.Y);

    /// <summary>A coordinate kept on the map, which is <paramref name="tiles"/> tiles long.</summary>
    private static Fixed Clamp(long raw, int tiles) =>
        Fixed.FromRaw((int)Math.Clamp(raw, 0, ((long)tiles * Fixed.RawOne) - 1));
}
