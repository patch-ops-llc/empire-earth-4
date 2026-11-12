using Sim.Core;
using Sim.Numerics;

namespace Tools.Replay.Scripts;

/// <summary>
/// Move orders (M2 to M5): every few ticks one of the players sends some of its units somewhere.
/// </summary>
/// <remarks>
/// The mix is chosen to reach the rules a recorded match should pin: groups that share routes,
/// many separate orders in one tick, which spend the route budget and make units wait, short hops
/// that need no route, targets on blocked ground and outside the map, and commands the simulation
/// must drop: for units of another player, for entities that never existed, from a player who is
/// not in the match and of a kind without a handler.
/// <para>
/// Its clock and generator are <see cref="SharedTurns"/>, which research draws from as well.
/// </para>
/// </remarks>
[OrderScript("movement")]
internal sealed class MoveOrders : OrderScript
{
    private readonly SharedTurns _turns;
    private readonly DeterministicRandom _random;

    public MoveOrders(ScriptSetup setup)
        : base(setup)
    {
        _turns = SharedTurns.Of(setup);
        _random = _turns.Random;
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (!_turns.IsTurn(tick, out var player, out var kind) || kind >= SharedTurns.MovementKinds)
        {
            return;
        }

        CollectUnitsOf(player);
        if (Own.Count == 0)
        {
            return;
        }

        if (kind < 45)
        {
            PickSome(_random);
            commands.Add(Move(player, Picked, AnyPoint(_random)));
        }
        else if (kind < 60)
        {
            commands.Add(Move(player, Own, AnyPoint(_random)));
        }
        else if (kind < 78)
        {
            // One order each, and half the time from every player in the same tick: every unit
            // needs a route of its own, which is more than one tick's budget finds.
            var everyone = _random.NextInt(2) == 0;
            for (var other = 0; other < Match.Players.Count; other++)
            {
                if (other != player && !everyone)
                {
                    continue;
                }

                CollectUnitsOf(other);
                foreach (var unit in Own)
                {
                    var point = AnyPoint(_random);
                    commands.Add(new Command(CommandType.Move, other, [unit], point.X, point.Y));
                }
            }
        }
        else if (kind < 88)
        {
            PickSome(_random);
            commands.Add(Move(player, Picked, PointNear(Picked[0])));
        }
        else if (kind < 93)
        {
            // Units of every player, one twice and one that never existed. Only the player's own move.
            Picked.Clear();
            for (var i = 0; i < Match.World.Entities.Count; i++)
            {
                Picked.Add(Match.World.Entities[i]);
            }

            Picked.Add(Own[0]);
            Picked.Add(new EntityId(1_000_000 + _random.NextInt(1000)));
            commands.Add(Move(player, Picked, AnyPoint(_random)));
        }
        else if (kind < 96)
        {
            // Off the map: a little way, or as far as a number goes.
            var outside = _random.NextInt(2) == 0
                ? new FixedVector2(Fixed.FromInt(Match.Grid.Width + 1 + _random.NextInt(50)), Fixed.FromInt(-1 - _random.NextInt(50)))
                : new FixedVector2(Fixed.MaxValue, Fixed.MinValue);
            commands.Add(Move(player, Own, outside));
        }
        else if (kind < 98)
        {
            commands.Add(Move(Match.Players.Count + _random.NextInt(5), Own, AnyPoint(_random)));
        }
        else
        {
            var type = (CommandType)(60_000 + _random.NextInt(1000));
            var point = AnyPoint(_random);
            commands.Add(new Command(type, player, [.. Own], point.X, point.Y, Own[0], _random.NextInt(100)));
        }
    }

    private static Command Move(int player, List<EntityId> units, FixedVector2 target) =>
        new(CommandType.Move, player, [.. units], target.X, target.Y);

    /// <summary>A point within three tiles of a unit, which may lie off the map.</summary>
    private FixedVector2 PointNear(EntityId unit)
    {
        var position = Match.Bodies.Get(unit).Position;
        var reach = 3 * Fixed.RawOne;
        var x = Fixed.FromRaw(position.X.Raw + _random.NextInt(-reach, reach + 1));
        var y = Fixed.FromRaw(position.Y.Raw + _random.NextInt(-reach, reach + 1));
        return new FixedVector2(x, y);
    }
}
