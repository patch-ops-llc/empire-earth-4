using Godot;
using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Structures;
using Sim.Vision;

namespace Game;

/// <summary>
/// The smoke test's structures in combat: up to forty seconds of play in an empty corner
/// of the map, in two scenes. In the first the local player's artillery destroys a wall of the
/// other seat's that the player remembers and does not see. In the second the local player's
/// units destroy a Tower that shoots back. On the way the part asks the view what it shows, and
/// fails when that is not what the player may know.
/// </summary>
/// <remarks>
/// <para>
/// The wall, which the player remembers:
/// </para>
/// <list type="bullet">
/// <item>At tick 0 the other seat lays a wall with the development placement, and the local
/// player gets a unit that fires shells and a scout, both too far from the wall to see it. The
/// wall must not be drawn.</item>
/// <item>The scout walks up until the player sees the wall, which must then be drawn as it
/// stands, and walks back until the player no longer sees it, which must make it a memory.</item>
/// <item>The order button on the remembered wall, with the unit that fires shells selected,
/// must give an attack order on it, and the wall must show the mark of the order.</item>
/// <item>The unit shells the wall from further than it sees. While it does, the wall must stay
/// drawn as the memory it is: with every hit point it had when last seen, so without a bar.</item>
/// <item>When the wall is destroyed, the player does not see it: it must stay drawn, and
/// nothing must show it falling.</item>
/// <item>The unit walks on to where the wall stood. When the player sees its tiles, the wall
/// must be gone from the screen, still without having been shown to fall.</item>
/// </list>
/// <para>
/// The Tower, which the player sees:
/// </para>
/// <list type="bullet">
/// <item>The other seat lays a Tower in sight of the local player's units, and the local player
/// gets four units that fight hand to hand. The order button on the Tower must give them and
/// the unit that fires shells an attack order on it.</item>
/// <item>A shot of the Tower's must be drawn starting from its top.</item>
/// <item>Once the Tower has lost hit points, it must have a bar and have been shown hit; with
/// few enough left it must smoke, and with fewer burn.</item>
/// <item>In the tick the Tower is destroyed it must be taken off the screen and be shown to fall.</item>
/// </list>
/// <para>
/// The part holds whatever ran before it, as long as nobody stands near its corner of the map:
/// it fails at once, and says so, when a unit does. It needs <c>debug.spawn</c> on in
/// <c>game/data/rules.jsonc</c>, like the fight.
/// </para>
/// </remarks>
public sealed class SiegeSmoke : SmokePart
{
    private const int Length = 800;

    // The ground the part uses, on ridge the open north west corner: a row of tiles from the
    // local player's two units in the west to the wall in the east, and the Tower south east
    // of the wall. Nothing that ran before may stand within this many tiles of it.
    private const int Row = 10;
    private const int ShellsX = 6;
    private const int ScoutX = 9;
    private const int LookFromX = 17;
    private const int WallX = 24;
    private const int TowerX = 27;
    private const int TowerY = 12;
    private const int Clear = 12;

    // Where the four that fight hand to hand are made: west of the Tower, in its range.
    private static readonly (int X, int Y)[] Assault = [(20, 12), (20, 13), (21, 12), (21, 13)];

    private readonly HashSet<EntityId> _before = [];
    private Step _step;
    private StructureDef? _wallDef;
    private StructureDef? _towerDef;
    private ChassisDef? _melee;
    private ChassisDef? _shellsChassis;
    private EntityId _wall;
    private EntityId _tower;
    private EntityId _shells;
    private EntityId _scout;
    private EntityId[] _assault = [];
    private int _other;
    private int _stepAt;
    private int _hitsOutOfSight;
    private int _towerShots;
    private int _highShots;
    private int _fallsBefore;
    private bool _sawBar;
    private bool _sawSmoke;
    private bool _sawFire;

    public override string Name => "Structures in combat";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (tick > 0)
        {
            foreach (var happened in orders.Match.Simulation.Events)
            {
                if (happened.Kind == SimEventKind.OrderRefused)
                {
                    orders.Fail($"the simulation refused a {(CommandType)happened.Amount} order of the part for player {happened.Player}.");
                    return;
                }
            }
        }

        if (tick == 0)
        {
            Begin(orders);
            return;
        }

        if (Layers.Of(orders) is not { } layers)
        {
            orders.Fail("the world has no layer for structures, for their bars, for what a fight does to them, or for shots.");
            return;
        }

        switch (_step)
        {
            case Step.Laid:
                WallLaid(tick, orders, layers);
                break;
            case Step.ScoutOut:
                ScoutOut(tick, orders, layers);
                break;
            case Step.ScoutBack:
                ScoutBack(tick, orders, layers);
                break;
            case Step.Shelling:
                Shelling(tick, orders, layers);
                break;
            case Step.GoneUnseen:
                GoneUnseen(tick, orders, layers);
                break;
            case Step.TowerLaid:
                TowerLaid(tick, orders, layers);
                break;
            case Step.Assault:
                AssaultOnTower(tick, orders, layers);
                break;
            default:
                break;
        }

        if (!orders.Failed && tick == Length - 1 && _step != Step.Done)
        {
            orders.Fail($"the part ended at its step '{_step}', which began at its tick {_stepAt}. If a fight takes longer now, the part needs more ticks.");
        }
    }

    private void Begin(SmokeOrders orders)
    {
        var match = orders.Match;
        var data = match.Data;
        if (!data.Rules.Debug.Spawn || match.Players.Count < 2)
        {
            orders.Fail("the development spawn is off in the rules, or the match has one seat. The part needs both to make a wall, a Tower and their attackers.");
            return;
        }

        _wallDef = data.Structures.FirstOrDefault(structure => structure.Wall is not null);
        _towerDef = data.Structures.FirstOrDefault(structure => structure.Attack is not null);
        _melee = DevelopmentSpawn.FirstChassis(data, chassis => chassis.Attack.Kind == AttackKind.Melee);
        _shellsChassis = DevelopmentSpawn.FirstChassis(data, chassis => chassis.Attack.Kind == AttackKind.Ranged && chassis.Attack.BlastRadius is not null);
        if (_wallDef is null || _towerDef is null || _melee is null || _shellsChassis is null)
        {
            orders.Fail("the data has no wall, no structure that attacks, no melee chassis or no chassis that fires shells.");
            return;
        }

        // Nobody near: the wall must be out of everyone's sight, and nobody may join the fight.
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            var position = WorldView.ToWorld(match.Bodies.ValueAt(i).Position);
            if (position.X < TowerX + _towerDef.Footprint + Clear && position.Z < TowerY + _towerDef.Footprint + Clear)
            {
                orders.Fail($"{match.Bodies.EntityAt(i)} stands at {position}, within {Clear} tiles of the corner the part uses. The part needs other ground, or the part before it must leave this corner alone.");
                return;
            }
        }

        if (!match.HasRoomFor(_wallDef, WallX, Row) || !match.HasRoomFor(_towerDef, TowerX, TowerY) || orders.View.Sight.FogAt(WallX, Row) == FogState.Visible)
        {
            orders.Fail("the wall or the Tower has no room where the part lays it, or the local player sees the ground there. If the map changed, the part needs another place.");
            return;
        }

        for (var i = 0; i < match.Combatants.Count; i++)
        {
            _before.Add(match.Combatants.EntityAt(i));
        }

        _other = (orders.Player + 1) % match.Players.Count;
        orders.SubmitFor(new Command(CommandType.DebugPlace, _other, targetX: Fixed.FromInt(WallX), targetY: Fixed.FromInt(Row), contentId: _wallDef.Index));
        Spawn(orders, _shellsChassis, ShellsX, Row);
        Spawn(orders, _melee, ScoutX, Row);
        To(Step.Laid, 0);
    }

    private void WallLaid(int tick, SmokeOrders orders, Layers layers)
    {
        if (tick < _stepAt + 5)
        {
            return;
        }

        var match = orders.Match;
        _wall = StructureAt(orders, WallX, Row);
        foreach (var entity in NewUnits(orders))
        {
            if (match.Combatants.Get(entity).Chassis == _shellsChassis!.Index)
            {
                _shells = entity;
            }
            else
            {
                _scout = entity;
            }
        }

        if (_wall == EntityId.None || _shells == EntityId.None || _scout == EntityId.None)
        {
            orders.Fail("the wall, the unit that fires shells or the scout is not in the match five ticks after it was ordered.");
            return;
        }

        if (orders.View.Sight.Seen.KnowsStructure(_wall) || layers.Structures.Draws(_wall))
        {
            orders.Fail("the wall is known to the local player or drawn before any unit of theirs has seen it.");
            return;
        }

        _fallsBefore = layers.Effects.FallsShown;
        orders.Submit(new Command(CommandType.Move, orders.Player, [_scout], MiddleOf(LookFromX), MiddleOf(Row)));
        To(Step.ScoutOut, tick);
    }

    private void ScoutOut(int tick, SmokeOrders orders, Layers layers)
    {
        if (!orders.View.Sight.Seen.TryGetStructure(_wall, out var seen) || seen.IsRemembered)
        {
            return;
        }

        if (!layers.Structures.Draws(_wall) || layers.Structures.DrawsAsMemory(_wall) || layers.Bars.Shows(_wall))
        {
            orders.Fail("the wall is not drawn as it stands, whole and without a bar, in the tick the local player first sees it.");
            return;
        }

        orders.Submit(new Command(CommandType.Move, orders.Player, [_scout], MiddleOf(ScoutX), MiddleOf(Row)));
        To(Step.ScoutBack, tick);
    }

    private void ScoutBack(int tick, SmokeOrders orders, Layers layers)
    {
        var view = orders.View;
        if (!view.Sight.Seen.TryGetStructure(_wall, out var seen))
        {
            orders.Fail("the local player no longer knows of the wall after seeing it.");
            return;
        }

        if (!seen.IsRemembered)
        {
            return;
        }

        if (!layers.Structures.DrawsAsMemory(_wall))
        {
            orders.Fail("the wall is not drawn as a memory in the tick the local player first no longer sees it.");
            return;
        }

        // The order button on the remembered wall, with the unit that fires shells selected.
        view.Selection.Set([_shells]);
        if (!ClickOn(view, new Vector2(WallX + 0.5f, Row + 0.5f), _wall, out var found))
        {
            orders.Fail($"the order button on the remembered wall gave '{found}' and not an attack order on it.");
            return;
        }

        if (layers.Effects.Marked != _wall)
        {
            orders.Fail("the remembered wall does not show the mark of the attack order on it.");
            return;
        }

        To(Step.Shelling, tick);
    }

    private void Shelling(int tick, SmokeOrders orders, Layers layers)
    {
        var match = orders.Match;
        var view = orders.View;
        foreach (var happened in match.Simulation.Events)
        {
            _hitsOutOfSight += happened.Kind == SimEventKind.ShotHit && happened.Entity == _wall ? 1 : 0;
        }

        // The wall is a memory from the order to the end, whatever the shells do to it.
        if (view.Sight.TrulySees(_wall)
            || !layers.Structures.DrawsAsMemory(_wall)
            || layers.Bars.Shows(_wall)
            || layers.Effects.Smokes(_wall)
            || layers.Effects.FallsShown != _fallsBefore)
        {
            orders.Fail(
                "while the local player's unit shells the wall from out of sight, the player sees the wall, or it is not drawn as the memory it is: " +
                "whole as when last seen, without a bar, smoke or fall.");
            return;
        }

        if (tick == _stepAt + 1)
        {
            if (!match.Combatants.TryGet(_shells, out var unit) || unit.Order != UnitOrder.Attack || unit.Target != _wall)
            {
                orders.Fail("the unit that fires shells has no attack order on the remembered wall one tick after the order button gave it.");
            }

            return;
        }

        if (match.World.Entities.IsAlive(_wall))
        {
            return;
        }

        if (_hitsOutOfSight == 0)
        {
            orders.Fail("the wall is gone without a shell having hit it.");
            return;
        }

        To(Step.GoneUnseen, tick);
    }

    // The wall is destroyed and the player does not know: it stays on the screen as it was
    // last seen until the player sees its tiles, and then goes without any effect.
    private void GoneUnseen(int tick, SmokeOrders orders, Layers layers)
    {
        var view = orders.View;
        if (layers.Effects.FallsShown != _fallsBefore)
        {
            orders.Fail("the wall was shown to fall, and the local player did not see it destroyed.");
            return;
        }

        if (view.Sight.Seen.TryGetStructure(_wall, out _))
        {
            if (!layers.Structures.DrawsAsMemory(_wall))
            {
                orders.Fail("the destroyed wall is not drawn as a memory while the local player still remembers it.");
            }

            return;
        }

        if (view.Sight.FogAt(WallX, Row) != FogState.Visible || layers.Structures.Draws(_wall))
        {
            orders.Fail("the memory of the wall ended without the local player seeing its tile, or the wall is still drawn after it.");
            return;
        }

        // The Tower, and those who are to destroy it.
        orders.SubmitFor(new Command(CommandType.DebugPlace, _other, targetX: Fixed.FromInt(TowerX), targetY: Fixed.FromInt(TowerY), contentId: _towerDef!.Index));
        foreach (var (x, y) in Assault)
        {
            Spawn(orders, _melee!, x, y);
        }

        To(Step.TowerLaid, tick);
    }

    private void TowerLaid(int tick, SmokeOrders orders, Layers layers)
    {
        var match = orders.Match;
        var view = orders.View;
        if (tick < _stepAt + 5)
        {
            return;
        }

        if (_tower == EntityId.None)
        {
            _tower = StructureAt(orders, TowerX, TowerY);
            _assault = [.. NewUnits(orders).Where(entity => entity != _shells && entity != _scout)];
            if (_tower == EntityId.None || _assault.Length != Assault.Length)
            {
                orders.Fail($"the Tower is not in the match five ticks after it was ordered, or {_assault.Length} of its {Assault.Length} attackers are.");
                return;
            }
        }

        // The Tower stands in sight of the units made beside it, from the next time sight is worked out.
        if (!view.Sight.TrulySees(_tower))
        {
            if (tick > _stepAt + 5 + match.Vision.RefreshTicks)
            {
                orders.Fail("the local player does not see the Tower laid beside their units.");
            }

            return;
        }

        if (!layers.Structures.Draws(_tower) || layers.Structures.DrawsAsMemory(_tower))
        {
            orders.Fail("the Tower is not drawn as it stands while the local player sees it.");
            return;
        }

        EntityId[] attackers = [.. _assault.Append(_shells).Where(match.World.Entities.IsAlive).Order()];
        view.Selection.Set(attackers);
        var middle = new Vector2(TowerX + (_towerDef!.Footprint / 2f), TowerY + (_towerDef.Footprint / 2f));
        if (!ClickOn(view, middle, _tower, out var found))
        {
            orders.Fail($"the order button on the Tower gave '{found}' and not an attack order on it.");
            return;
        }

        view.Selection.Clear();
        To(Step.Assault, tick);
    }

    private void AssaultOnTower(int tick, SmokeOrders orders, Layers layers)
    {
        var match = orders.Match;
        var fired = false;
        foreach (var happened in match.Simulation.Events)
        {
            fired |= happened.Kind == SimEventKind.ShotFired && happened.Other == _tower;
        }

        // A shot is first drawn in the tick it is fired, where it was fired from: the Tower's top.
        if (fired)
        {
            _towerShots++;
            _highShots += layers.Shots.HighShotCount > 0 ? 1 : 0;
        }

        if (!match.World.Entities.IsAlive(_tower))
        {
            Destroyed(tick, orders, layers);
            return;
        }

        if (!match.Health.TryGet(_tower, out var health) || health.HitPoints >= health.Maximum)
        {
            return;
        }

        // The local player sees the Tower all the while, so the view shows it as it is.
        var left = health.HitPoints / (float)health.Maximum;
        var smokes = left < StructureEffects.SmokesBelow;
        var burns = left < StructureEffects.BurnsBelow;
        if (!layers.Bars.Shows(_tower) || layers.Effects.HitsShown == 0 || layers.Effects.Smokes(_tower) != smokes || layers.Effects.Burns(_tower) != burns)
        {
            orders.Fail(
                $"the Tower has {left:P0} of its hit points, and the view shows a bar: {layers.Bars.Shows(_tower)}, hits: {layers.Effects.HitsShown}, " +
                $"smoke: {layers.Effects.Smokes(_tower)}, fire: {layers.Effects.Burns(_tower)}.");
            return;
        }

        _sawBar = true;
        _sawSmoke |= smokes;
        _sawFire |= burns;
    }

    private void Destroyed(int tick, SmokeOrders orders, Layers layers)
    {
        GD.Print(
            $"Smoke test, structures in combat: {_hitsOutOfSight} shells on the remembered wall, the Tower fired {_towerShots} shots, " +
            $"{_highShots} of them drawn from its top, and fell at tick {tick} of the part.");
        if (layers.Structures.Draws(_tower) || layers.Effects.FallsShown != _fallsBefore + 1)
        {
            orders.Fail(
                $"the Tower was destroyed in the local player's sight, and it is still drawn: {layers.Structures.Draws(_tower)}, " +
                $"or it was not shown to fall: {layers.Effects.FallsShown - _fallsBefore} falls shown.");
            return;
        }

        if (_towerShots == 0 || _highShots != _towerShots)
        {
            orders.Fail($"the Tower fired {_towerShots} shots, and {_highShots} of them were drawn starting from its top. It must fire, and every shot must.");
            return;
        }

        if (!_sawBar || !_sawSmoke || !_sawFire)
        {
            orders.Fail($"before it fell the Tower was shown with a bar: {_sawBar}, smoking: {_sawSmoke}, burning: {_sawFire}. It must have been shown all three ways.");
            return;
        }

        To(Step.Done, tick);
    }

    // Looks at a structure and gives the order a click with the order button on the middle of it
    // gives, as the unit control does. The view is put back where it was. True when the order
    // is this piece's attack order on that structure.
    private static bool ClickOn(MatchView view, Vector2 middle, EntityId structure, out string found)
    {
        found = "nothing";
        var camera = view.World.GetViewport().GetCamera3D();
        if (camera is null)
        {
            return false;
        }

        var looked = view.Camera.Focus;
        view.Camera.CenterOn(middle);
        var screen = camera.UnprojectPosition(new Vector3(middle.X, 0.3f, middle.Y));
        var target = view.UnderPointer.At(screen, view.Orders.Wanted);
        view.Camera.CenterOn(looked);
        if (!target.Is(PointerTargets.EnemyStructure) || target.Entity != structure)
        {
            found = $"a click on {target.Kind} {target.Entity}";
            return false;
        }

        found = view.Orders.Give(view, target)?.Name ?? "no order";
        return found == StructureAttack.Name;
    }

    private void To(Step step, int tick)
    {
        _step = step;
        _stepAt = tick;
    }

    private static Fixed MiddleOf(int tile) => Fixed.FromRaw((tile * Fixed.RawOne) + (Fixed.RawOne / 2));

    private static void Spawn(SmokeOrders orders, ChassisDef chassis, int x, int y)
    {
        var point = new FixedVector2(MiddleOf(x), MiddleOf(y));
        if (!orders.Match.Grid.IsPassable(point))
        {
            orders.Fail($"no unit can stand at {point}, where the part makes one. If the map changed, the part needs another place.");
            return;
        }

        orders.Submit(new Command(CommandType.DebugSpawn, orders.Player, targetX: point.X, targetY: point.Y, contentId: chassis.Index));
    }

    // The fighting units that were not in the match when the part began, in ID order.
    private List<EntityId> NewUnits(SmokeOrders orders)
    {
        var match = orders.Match;
        var units = new List<EntityId>();
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            if (!_before.Contains(entity) && match.World.Entities.IsAlive(entity))
            {
                units.Add(entity);
            }
        }

        units.Sort();
        return units;
    }

    private static EntityId StructureAt(SmokeOrders orders, int x, int y)
    {
        var structures = orders.Match.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            var tiles = structures.ValueAt(i).Tiles;
            if (tiles.X == x && tiles.Y == y && orders.Match.World.Entities.IsAlive(structures.EntityAt(i)))
            {
                return structures.EntityAt(i);
            }
        }

        return EntityId.None;
    }

    private enum Step
    {
        Laid,
        ScoutOut,
        ScoutBack,
        Shelling,
        GoneUnseen,
        TowerLaid,
        Assault,
        Done,
    }

    private sealed record Layers(StructureLayer Structures, StructureBars Bars, StructureEffects Effects, ShotLayer Shots)
    {
        public static Layers? Of(SmokeOrders orders)
        {
            var world = orders.View.World;
            return world.GetNodeOrNull<StructureLayer>(nameof(StructureLayer)) is { } structures
                && world.GetNodeOrNull<StructureBars>(nameof(StructureBars)) is { } bars
                && world.GetNodeOrNull<StructureEffects>(nameof(StructureEffects)) is { } effects
                && world.GetNodeOrNull<ShotLayer>(nameof(ShotLayer)) is { } shots
                ? new Layers(structures, bars, effects, shots)
                : null;
        }
    }
}
