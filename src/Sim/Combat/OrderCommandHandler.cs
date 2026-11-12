using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;

namespace Sim.Combat;

/// <summary>
/// Executes the four orders a unit takes (design 2.4): <see cref="CommandType.Move"/>,
/// <see cref="CommandType.AttackMove"/>, <see cref="CommandType.Attack"/> and
/// <see cref="CommandType.Stop"/>. A new order replaces a unit's old one at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Move and attack-move</b> are walked by the move order's own handler, which this one wraps: it
/// judges the command and places the walk, and every listed unit that can walk goes. Afterwards
/// each unit of a chassis that went is told which of the two it is on. A move order never fights.
/// For a unit that is not of a chassis, a worker, an attack-move is a move order.
/// </para>
/// <para>
/// <b>Attack</b> names one entity. The command is valid when its player is in the match, the entity
/// is alive, belongs to another player, can be damaged and is seen by the player when the command
/// runs (design 5.3), and at least one listed unit can take
/// the order: alive, owned by the player, of a chassis, and of a line that can hit the target. The
/// others are skipped, workers among them, and keep the order they had.
/// </para>
/// <para>
/// <b>Stop</b> is valid when at least one listed entity is alive, owned by the player and either
/// walks or is of a chassis. Each stands where it is and is idle there.
/// </para>
/// <para>
/// A command that is not valid changes nothing and is reported as a
/// <see cref="SimEventKind.OrderRefused"/>, so that the player who gave it can be told.
/// </para>
/// </remarks>
public sealed class OrderCommandHandler : ICommandHandler
{
    private readonly ICommandHandler _move;
    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly Targeting _targeting;
    private readonly Approach _approach;
    private readonly SimEventList _events;
    private readonly int _playerCount;

    /// <param name="move">The handler that walks a move order. It is given attack-move commands too, as they are.</param>
    public OrderCommandHandler(
        ICommandHandler move,
        MapGrid grid,
        ComponentStore<Body> bodies,
        ComponentStore<Mover> movers,
        ComponentStore<Combatant> combatants,
        Targeting targeting,
        Approach approach,
        SimEventList events,
        int playerCount)
    {
        ArgumentNullException.ThrowIfNull(move);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(targeting);
        ArgumentNullException.ThrowIfNull(approach);
        ArgumentNullException.ThrowIfNull(events);
        _move = move;
        _grid = grid;
        _bodies = bodies;
        _movers = movers;
        _combatants = combatants;
        _targeting = targeting;
        _approach = approach;
        _events = events;
        _playerCount = playerCount;
    }

    public bool Execute(World world, in Command command)
    {
        ArgumentNullException.ThrowIfNull(world);
        var carriedOut = command.Type switch
        {
            CommandType.Move => Walk(world, in command, UnitOrder.Move),
            CommandType.AttackMove => Walk(world, in command, UnitOrder.AttackMove),
            CommandType.Attack => Attack(world, in command),
            CommandType.Stop => Stop(world, in command),
            _ => false,
        };

        if (!carriedOut)
        {
            Refuse(_events, in command);
        }

        return carriedOut;
    }

    /// <summary>Reports a command that was not carried out as a <see cref="SimEventKind.OrderRefused"/>.</summary>
    internal static void Refuse(SimEventList events, in Command command) =>
        events.Add(
            SimEventKind.OrderRefused,
            command.Player,
            command.Entities.IsEmpty ? EntityId.None : command.Entities[0],
            command.TargetEntity,
            new FixedVector2(command.TargetX, command.TargetY),
            (long)command.Type);

    private bool Walk(World world, in Command command, UnitOrder order)
    {
        if (!_move.Execute(world, in command))
        {
            return false;
        }

        foreach (var entity in command.Entities)
        {
            // The units the move order sent: it skips one that stands where no unit can.
            var mover = _movers.IndexOf(entity);
            if (mover < 0 || !IsOwn(world, entity, command.Player, out var body) || _grid.RegionOf(body.Position) == 0)
            {
                continue;
            }

            var index = _combatants.IndexOf(entity);
            if (index < 0)
            {
                continue;
            }

            ref var unit = ref _combatants.ValueAt(index);
            unit.Order = order;
            unit.Target = EntityId.None;
            unit.Engaged = false;
            unit.Anchor = _movers.ValueAt(mover).Destination;
        }

        return true;
    }

    private bool Attack(World world, in Command command)
    {
        if (command.Player < 0
            || command.Player >= _playerCount
            || !_targeting.IsEnemy(world, command.Player, command.TargetEntity, out var target))
        {
            return false;
        }

        var targetLine = _targeting.LineOf(command.TargetEntity);
        var taken = false;
        foreach (var entity in command.Entities)
        {
            var index = _combatants.IndexOf(entity);
            if (index < 0 || !IsOwn(world, entity, command.Player, out var body))
            {
                continue;
            }

            ref var unit = ref _combatants.ValueAt(index);
            var profile = _targeting.Rules.ProfileOf(body.Owner, unit.Chassis);
            if (!_targeting.Rules.CanHit(profile.Line, targetLine))
            {
                continue;
            }

            taken = true;
            unit.Order = UnitOrder.Attack;
            unit.Target = command.TargetEntity;
            unit.Engaged = true;
            unit.AimedAt = target.Position;
            unit.Anchor = target.Position;

            var mover = _movers.IndexOf(entity);
            if (mover < 0)
            {
                continue;
            }

            if (_targeting.Reaches(profile, body.Position, target.Position, targetLine)
                || Targeting.IsTooClose(profile, body.Position, target.Position))
            {
                _movers.ValueAt(mover).Stop();
            }
            else
            {
                _approach.WalkTo(ref _movers.ValueAt(mover), body.Position, target.Position);
            }
        }

        return taken;
    }

    private bool Stop(World world, in Command command)
    {
        if (command.Player < 0 || command.Player >= _playerCount)
        {
            return false;
        }

        var taken = false;
        foreach (var entity in command.Entities)
        {
            if (!IsOwn(world, entity, command.Player, out var body))
            {
                continue;
            }

            var mover = _movers.IndexOf(entity);
            if (mover >= 0)
            {
                _movers.ValueAt(mover).Stop();
                taken = true;
            }

            var index = _combatants.IndexOf(entity);
            if (index >= 0)
            {
                AttackSystem.BecomeIdle(ref _combatants.ValueAt(index), body.Position);
                taken = true;
            }
        }

        return taken;
    }

    private bool IsOwn(World world, EntityId entity, int player, out Body body)
    {
        body = default;
        return world.Entities.IsAlive(entity) && _bodies.TryGet(entity, out body) && body.Owner == player;
    }
}
