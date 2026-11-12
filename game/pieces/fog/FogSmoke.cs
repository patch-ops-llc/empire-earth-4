using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Vision;

namespace Game;

/// <summary>
/// The smoke test's fog of war: 25 seconds of play in which the other seat gets one unit on
/// ground the local player does not see, and what is left of the local player's force walks
/// there, finds it and kills it. On the way the part asks the view what it shows, and fails
/// when the view shows the unit while the player does not see it, or hides it once they do.
/// </summary>
/// <remarks>
/// <para>
/// The steps, in ticks of the part:
/// </para>
/// <list type="bullet">
/// <item>At 0 the unit is made, 17 tiles east of where the fight before this part ended.</item>
/// <item>At <see cref="HiddenAt"/> it is in the match and out of sight: the view must not draw
/// it, and must have no attack order for it. The development switch then lifts the fog.</item>
/// <item>One tick on the view must draw it, and still have no attack order for it, since the
/// switch changes the screen and not what the units see. The fog is laid back.</item>
/// <item>One tick on the view must not draw it again. The local force sets out with an attack-move.</item>
/// <item>In the tick the player first sees the unit, the view must draw it and the tile under it
/// must be visible. The force is given an attack order on it, which the simulation takes now.</item>
/// <item>At the end the unit must be dead, and the player must have explored ground that they do not see now.</item>
/// </list>
/// <para>
/// Like the fight, the part needs <c>debug.spawn</c> on in <c>game/data/rules.jsonc</c>, and the
/// units the local player kept from the fight.
/// </para>
/// </remarks>
public sealed class FogSmoke : SmokePart
{
    private const int HiddenAt = 5;
    private const int Length = 500;

    // Where the other seat's unit is made, in sixteenths of a tile. On ridge this is open ground
    // on the south edge, east of the fight and west of the cliffs in the middle.
    private const int X16 = 616;
    private const int Y16 = 1496;

    private readonly HashSet<EntityId> _before = [];
    private EntityId[] _force = [];
    private EntityId _hidden = EntityId.None;
    private FixedVector2 _place;
    private bool _found;

    public override string Name => "Fog of war";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        foreach (var happened in orders.Match.Simulation.Events)
        {
            if (tick > 0 && happened.Kind == SimEventKind.OrderRefused)
            {
                orders.Fail($"the simulation refused a {(CommandType)happened.Amount} order of the fog part for player {happened.Player}.");
                return;
            }
        }

        switch (tick)
        {
            case 0:
                MakeHiddenUnit(orders);
                break;
            case HiddenAt:
                FindHiddenUnit(orders);
                if (!orders.Failed)
                {
                    MustBeHidden(orders, "before the player has come near it");
                    orders.View.Sight.FogOff = true;
                }

                break;
            case HiddenAt + 1:
                MustBeShownByTheSwitch(orders);
                orders.View.Sight.FogOff = false;
                break;
            case HiddenAt + 2:
                MustBeHidden(orders, "after the development switch laid the fog back");
                if (!orders.Failed)
                {
                    SetOut(orders);
                }

                break;
            case > HiddenAt + 2 and < Length - 1 when !_found:
                AttackWhenSeen(orders);
                break;
            case Length - 1:
                Judge(orders);
                break;
        }
    }

    private void MakeHiddenUnit(SmokeOrders orders)
    {
        var match = orders.Match;
        if (!match.Data.Rules.Debug.Spawn || match.Players.Count < 2)
        {
            orders.Fail("the fog part needs the development spawn and a second seat to make a unit out of sight.");
            return;
        }

        _place = new FixedVector2(Fixed.FromRaw(X16 * (Fixed.RawOne / 16)), Fixed.FromRaw(Y16 * (Fixed.RawOne / 16)));
        if (!match.Grid.IsPassable(_place))
        {
            orders.Fail($"no unit can stand at {_place}, where the script makes one. If the map changed, the fog part needs another place.");
            return;
        }

        if (orders.View.Sight.Seen.FogAt(_place.X.FloorToInt(), _place.Y.FloorToInt()) == FogState.Visible)
        {
            orders.Fail($"the local player sees {_place}, where the script makes a unit that is to be out of sight.");
            return;
        }

        var chassis = DevelopmentSpawn.FirstChassis(match.Data, chassis => chassis.Attack.Kind == AttackKind.Melee);
        if (chassis is null)
        {
            orders.Fail("the data has no melee chassis.");
            return;
        }

        for (var i = 0; i < match.Combatants.Count; i++)
        {
            _before.Add(match.Combatants.EntityAt(i));
        }

        var other = (orders.Player + 1) % match.Players.Count;
        orders.SubmitFor(new Command(CommandType.DebugSpawn, other, targetX: _place.X, targetY: _place.Y, contentId: chassis.Index));
    }

    private void FindHiddenUnit(SmokeOrders orders)
    {
        var match = orders.Match;
        var force = new List<EntityId>();
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            if (!match.Bodies.TryGet(entity, out var body))
            {
                continue;
            }

            if (body.Owner == orders.Player)
            {
                force.Add(entity);
            }
            else if (!_before.Contains(entity))
            {
                _hidden = entity;
            }
        }

        force.Sort();
        _force = [.. force];
        if (_hidden == EntityId.None || _force.Length == 0)
        {
            orders.Fail($"{HiddenAt} ticks after the unit was ordered, the other seat has {(_hidden == EntityId.None ? "no" : "a")} new unit and the local player has {_force.Length} fighting units.");
        }
    }

    private void MustBeHidden(SmokeOrders orders, string when)
    {
        var view = orders.View;
        if (view.Sight.TrulySees(_hidden))
        {
            orders.Fail($"the local player sees the other seat's unit {when}. The script places it out of sight.");
        }
        else if (view.Sight.ShowsUnit(_hidden) || view.World.TryGetDrawnPosition(_hidden, out _))
        {
            orders.Fail($"the view draws a unit of the other seat that the local player does not see, {when}.");
        }
        else if (CombatOrders.CanAttack(view, _force, _hidden))
        {
            orders.Fail($"the view has an attack order for a unit the local player does not see, {when}.");
        }
        else if (view.World.Fog.ShownAt(_place.X.FloorToInt(), _place.Y.FloorToInt()) <= 0f)
        {
            orders.Fail($"the fog picture has no fog on the tile of a unit the local player does not see, {when}.");
        }
    }

    private void MustBeShownByTheSwitch(SmokeOrders orders)
    {
        var view = orders.View;
        if (!view.World.TryGetDrawnPosition(_hidden, out _))
        {
            orders.Fail("with the development switch the fog is off, and the view still does not draw the other seat's unit.");
        }
        else if (CombatOrders.CanAttack(view, _force, _hidden))
        {
            orders.Fail("with the fog off on the screen, the view has an attack order for a unit the local player does not see. The simulation would refuse it.");
        }
    }

    private void SetOut(SmokeOrders orders)
    {
        _force = Alive(orders, _force);
        orders.Submit(new Command(CommandType.AttackMove, orders.Player, _force, _place.X, _place.Y));
    }

    private void AttackWhenSeen(SmokeOrders orders)
    {
        var view = orders.View;
        var match = orders.Match;
        if (!match.World.Entities.IsAlive(_hidden) || !view.Sight.TrulySees(_hidden))
        {
            return;
        }

        _found = true;
        var at = match.Bodies.Get(_hidden).Position;
        if (!view.World.TryGetDrawnPosition(_hidden, out _))
        {
            orders.Fail("the local player sees the other seat's unit, and the view does not draw it.");
            return;
        }

        if (view.Sight.FogAt(at.X.FloorToInt(), at.Y.FloorToInt()) != FogState.Visible)
        {
            orders.Fail("the local player sees the other seat's unit, and the tile under it is not visible.");
            return;
        }

        _force = Alive(orders, _force);
        if (!CombatOrders.CanAttack(view, _force, _hidden))
        {
            orders.Fail("the local player sees the other seat's unit, and the view has no attack order for it.");
            return;
        }

        orders.Submit(new Command(CommandType.Attack, orders.Player, _force, targetEntity: _hidden));
    }

    private void Judge(SmokeOrders orders)
    {
        var match = orders.Match;
        var grid = match.Vision.Grid;
        var visible = grid.CountVisible(orders.Player);
        var explored = grid.CountExplored(orders.Player);
        GD.Print(
            $"Smoke test, the fog: the local player sees {visible} tiles and has explored {explored} of {grid.Width * grid.Height}. " +
            $"The view draws {orders.View.World.UnitCount} units.");

        if (!_found)
        {
            orders.Fail($"the local player never saw the other seat's unit in {Length - 1} ticks of walking toward it.");
        }
        else if (match.World.Entities.IsAlive(_hidden))
        {
            orders.Fail($"the other seat's unit is alive {Length - 1} ticks after the part began. The parts after this one need it gone.");
        }
        else if (explored <= visible)
        {
            orders.Fail($"the local player has explored {explored} tiles and sees {visible}: after a walk there must be ground that is explored and not seen now.");
        }
    }

    private static EntityId[] Alive(SmokeOrders orders, EntityId[] units) =>
        Array.FindAll(units, orders.Match.World.Entities.IsAlive);
}
