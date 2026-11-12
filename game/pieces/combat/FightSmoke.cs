using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;

namespace Game;

/// <summary>
/// The smoke test's fight: 25 seconds of play in which two small forces are made with the
/// development spawn in an empty corner of the map and the local player's is sent at the other
/// with the three fighting orders. The part fails when the fight lands no hit, kills nobody or
/// has no blast: one unit of the local force fires shells.
/// </summary>
/// <remarks>
/// <para>
/// The local player's force is the larger, and the part fails unless it destroys the other. So
/// the parts after this one find no enemy that would attack the local player's workers; what is
/// left of the local force stands idle in its corner, away from every worker of the other seat.
/// </para>
/// <para>
/// The other seat's units are made by the host, which gives the commands of a seat nobody sits
/// at (<see cref="SmokeOrders.SubmitFor"/>). The part needs <c>debug.spawn</c> on in
/// <c>game/data/rules.jsonc</c>. The piece that turns it off, once units can be trained, gives
/// this part its two forces another way.
/// </para>
/// </remarks>
public sealed class FightSmoke : SmokePart
{
    // When the forces made at tick 0 are counted and ordered, when the survivors are told to
    // stop, and how long the part runs. The fight itself is over well before the stop.
    private const int OrdersAt = 5;
    private const int StopAt = 460;
    private const int Length = 500;

    // The local force: melee units in front, ranged units a tile behind them, and one unit that
    // fires shells behind those. The other force stands twelve tiles to the east, out of sight,
    // so that nothing happens before the orders. Points are in sixteenths of a tile. On ridge
    // this is the open ground along the south edge.
    private static readonly Spawn[] Spawns =
    [
        new(Local: true, Arm.Melee, X16: 136, Y16: 1480),
        new(Local: true, Arm.Melee, X16: 136, Y16: 1496),
        new(Local: true, Arm.Melee, X16: 136, Y16: 1512),
        new(Local: true, Arm.Ranged, X16: 112, Y16: 1480),
        new(Local: true, Arm.Ranged, X16: 112, Y16: 1496),
        new(Local: true, Arm.Ranged, X16: 112, Y16: 1512),
        new(Local: true, Arm.Shells, X16: 88, Y16: 1496),
        new(Local: false, Arm.Melee, X16: 328, Y16: 1488),
        new(Local: false, Arm.Melee, X16: 328, Y16: 1504),
        new(Local: false, Arm.Ranged, X16: 344, Y16: 1488),
        new(Local: false, Arm.Ranged, X16: 344, Y16: 1504),
    ];

    private readonly HashSet<EntityId> _before = [];
    private EntityId[] _melee = [];
    private EntityId[] _ranged = [];
    private EntityId[] _others = [];
    private int _hits;
    private int _shots;
    private int _lost;
    private int _blasts;
    private int _deaths;
    private int _localDeaths;
    private int _lastDeathAt;

    public override string Name => "Fight";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        // The events of the tick before this one. They are gone once the next tick starts.
        if (tick > 0)
        {
            Count(orders, tick - 1);
        }

        switch (tick)
        {
            case 0:
                MakeForces(orders);
                break;

            case OrdersAt:
                SendLocalForce(orders);
                break;

            case StopAt:
                StopSurvivors(orders);
                break;

            case Length - 1:
                Judge(orders);
                break;
        }
    }

    private void Count(SmokeOrders orders, int tick)
    {
        foreach (var happened in orders.Match.Simulation.Events)
        {
            switch (happened.Kind)
            {
                case SimEventKind.MeleeHit:
                case SimEventKind.ShotHit:
                    _hits++;
                    break;
                case SimEventKind.ShotFired:
                    _shots++;
                    break;
                case SimEventKind.ShotLost:
                    _lost++;
                    break;
                case SimEventKind.Blast:
                    _blasts++;
                    break;
                case SimEventKind.Death:
                    _deaths++;
                    _localDeaths += happened.Player == orders.Player ? 1 : 0;
                    _lastDeathAt = tick;
                    break;
                case SimEventKind.OrderRefused:
                    orders.Fail($"the simulation refused a {(CommandType)happened.Amount} order of the fight for player {happened.Player}.");
                    break;
                default:
                    break;
            }
        }
    }

    private void MakeForces(SmokeOrders orders)
    {
        var match = orders.Match;
        if (!match.Data.Rules.Debug.Spawn)
        {
            orders.Fail("the development spawn is off in the rules, and the fight has no other way to make its two forces.");
            return;
        }

        if (match.Players.Count < 2)
        {
            orders.Fail("the match has one seat, and a fight needs two.");
            return;
        }

        // The cheapest fighters of each kind: the first chassis of the first epoch that fights
        // hand to hand, the first that shoots at any distance, and the first whose shots are shells.
        var melee = DevelopmentSpawn.FirstChassis(match.Data, chassis => chassis.Attack.Kind == AttackKind.Melee);
        var ranged = DevelopmentSpawn.FirstChassis(match.Data, chassis => chassis.Attack.Kind == AttackKind.Ranged && chassis.Attack.MinRange is null);
        var shells = DevelopmentSpawn.FirstChassis(match.Data, chassis => chassis.Attack.Kind == AttackKind.Ranged && chassis.Attack.BlastRadius is not null);
        if (melee is null || ranged is null || shells is null)
        {
            orders.Fail("the data has no melee chassis, no ranged chassis without a minimum range, or no chassis that fires shells.");
            return;
        }

        for (var i = 0; i < match.Combatants.Count; i++)
        {
            _before.Add(match.Combatants.EntityAt(i));
        }

        var other = (orders.Player + 1) % match.Players.Count;
        foreach (var spawn in Spawns)
        {
            var point = new FixedVector2(Fixed.FromRaw(spawn.X16 * (Fixed.RawOne / 16)), Fixed.FromRaw(spawn.Y16 * (Fixed.RawOne / 16)));
            if (!match.Grid.IsPassable(point))
            {
                orders.Fail($"no unit can stand at {point}, where the script makes one. If the map changed, the fight needs another place.");
                return;
            }

            var command = new Command(
                CommandType.DebugSpawn,
                spawn.Local ? orders.Player : other,
                targetX: point.X,
                targetY: point.Y,
                contentId: spawn.Arm switch
                {
                    Arm.Melee => melee.Index,
                    Arm.Shells => shells.Index,
                    _ => ranged.Index,
                });
            orders.SubmitFor(command);
        }
    }

    private void SendLocalForce(SmokeOrders orders)
    {
        var match = orders.Match;
        var melee = new List<EntityId>();
        var ranged = new List<EntityId>();
        var others = new List<EntityId>();
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            if (_before.Contains(entity) || !match.Bodies.TryGet(entity, out var body))
            {
                continue;
            }

            if (body.Owner != orders.Player)
            {
                others.Add(entity);
            }
            else if (match.Combat.ProfileOf(body.Owner, match.Combatants.ValueAt(i).Chassis).Melee)
            {
                melee.Add(entity);
            }
            else
            {
                ranged.Add(entity);
            }
        }

        melee.Sort();
        ranged.Sort();
        others.Sort();
        _melee = [.. melee];
        _ranged = [.. ranged];
        _others = [.. others];

        var local = Spawns.Count(spawn => spawn.Local);
        if (_melee.Length + _ranged.Length != local || _others.Length != Spawns.Length - local || _melee.Length == 0 || _ranged.Length == 0)
        {
            orders.Fail(
                $"the development spawn made {_melee.Length} melee and {_ranged.Length} ranged units for the local player and {_others.Length} for the other seat, " +
                $"{OrdersAt} ticks after {local} and {Spawns.Length - local} were ordered.");
            return;
        }

        // The melee units walk into the other force and fight what they meet. The ranged units,
        // the one with shells among them, are told to kill one named unit, and pick for
        // themselves once it is dead.
        var goal = match.Bodies.Get(_others[0]).Position;
        orders.Submit(new Command(CommandType.AttackMove, orders.Player, _melee, goal.X, goal.Y));
        orders.Submit(new Command(CommandType.Attack, orders.Player, _ranged, targetEntity: _others[0]));
    }

    private void StopSurvivors(SmokeOrders orders)
    {
        var alive = Alive(orders, [.. _melee, .. _ranged]);
        if (alive.Length == 0)
        {
            orders.Fail("none of the local player's force is alive to take the stop order. The fight is set up for it to win.");
            return;
        }

        orders.Submit(new Command(CommandType.Stop, orders.Player, alive));
    }

    private void Judge(SmokeOrders orders)
    {
        GD.Print(
            $"Smoke test, the fight: {_hits} hits, {_shots} shots fired and {_lost} lost, {_blasts} blasts, {_deaths} deaths, {_localDeaths} of them the local player's, " +
            $"the last at tick {_lastDeathAt} of the part.");
        if (_hits == 0 || _deaths == 0)
        {
            orders.Fail($"the fight produced {_hits} hits, {_shots} shots and {_deaths} deaths in {Length - 1} ticks. It needs at least one hit and one death.");
            return;
        }

        if (_blasts == 0)
        {
            orders.Fail($"no shell of the local force landed in {Length - 1} ticks, with {_shots} shots fired. The fight needs at least one blast.");
            return;
        }

        var left = Alive(orders, _others).Length;
        if (left > 0)
        {
            orders.Fail(
                $"{left} of the other seat's {_others.Length} units are alive {Length - 1} ticks after the fight began, with {_hits} hits and {_deaths} deaths. " +
                "The parts after this one need them gone. If a fight takes longer now, this part needs more ticks or a larger local force.");
        }
    }

    private static EntityId[] Alive(SmokeOrders orders, EntityId[] units) =>
        Array.FindAll(units, orders.Match.World.Entities.IsAlive);

    private enum Arm
    {
        Melee,
        Ranged,
        Shells,
    }

    private readonly record struct Spawn(bool Local, Arm Arm, int X16, int Y16);
}
