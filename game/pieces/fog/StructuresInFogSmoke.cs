using Godot;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Vision;

namespace Game;

/// <summary>
/// The smoke test's structures in the fog of war: up to twenty-five seconds of play in which the
/// other seat lays a structure on ground the local player does not see, and one of the local
/// player's units walks there, sees it, and walks back. On the way the part asks the view what
/// it shows of the structure, and fails when the view shows it before the player has seen it,
/// does not show it while they see it, or shows it afterwards as anything but a memory of what
/// they last saw.
/// </summary>
/// <remarks>
/// <para>
/// The steps:
/// </para>
/// <list type="bullet">
/// <item>At tick 0 the other seat orders the structure, inside its own territory and further
/// from every unit of the local player than any unit sees.</item>
/// <item>In the tick after it is laid, the local player has never seen it: it must not be drawn
/// in the world, nor listed for the minimap, nor found under the pointer. The development switch
/// then lifts the fog.</item>
/// <item>One tick on it must be drawn, as it stands and not as a memory. The fog is laid back.</item>
/// <item>One tick on it must be gone again. The nearest unit of the local player is sent to it.</item>
/// <item>In the tick the player first sees it, it must be drawn as it stands and be found under
/// the pointer as another player's structure. The unit is sent back.</item>
/// <item>In the tick the player first no longer sees it, it must be drawn as a memory and still
/// be found under the pointer. The development switch must show it as it stands, and laying the
/// fog back must make it a memory again, of how far it had built itself when last seen and not
/// of how far it has by then.</item>
/// <item>All the while the territory of the other seat's Capitol must be drawn exactly when the
/// local player knows of that Capitol.</item>
/// </list>
/// <para>
/// The part holds whatever ran before it. It needs a worker of the other seat's, a structure
/// that seat may order, and room for it in that seat's territory out of the local player's sight.
/// </para>
/// </remarks>
public sealed class StructuresInFogSmoke : SmokePart
{
    private const int Length = 500;

    // No unit sees further than this many tiles, with room to spare.
    private const int OutOfSight = 12;

    private readonly List<SeenStructure> _listed = [];
    private StructureDef? _structure;
    private SeenStructure _capitol;
    private SeenStructure _built;
    private SeenStructure _lastSeen;
    private EntityId _scout = EntityId.None;
    private FixedVector2 _home;
    private int _other;
    private int _siteX;
    private int _siteY;
    private int _laidAt = -1;
    private int _seenAt = -1;
    private int _rememberedAt = -1;
    private bool _tested;

    public override string Name => "Structures in the fog";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        foreach (var happened in orders.Match.Simulation.Events)
        {
            if (tick > 0 && happened.Kind is SimEventKind.OrderRefused or SimEventKind.BuildRefused or SimEventKind.BuildOrderEnded)
            {
                orders.Fail($"the simulation refused or ended an order of the part for player {happened.Player}: {happened.Kind}, reason {happened.Amount}.");
                return;
            }
        }

        if (tick == 0)
        {
            OrderStructure(orders);
        }
        else if (tick == Length - 1)
        {
            Judge(orders);
        }
        else if (_laidAt < 0)
        {
            WaitForStructure(orders, tick);
        }
        else
        {
            Follow(orders, tick);
        }

        if (!orders.Failed && tick > 0)
        {
            TerritoryMustFollowTheCapitol(orders);
        }
    }

    private void OrderStructure(SmokeOrders orders)
    {
        var match = orders.Match;
        _other = (orders.Player + 1) % match.Players.Count;
        var theirs = new PlayerView(match, _other);

        PlayerSight.CollectStanding(match, _listed);
        _capitol = _listed.Find(structure => structure.Owner == _other && match.Data.StructureOf(structure.Type)?.TerritoryRadius is not null);
        var workers = new List<EntityId>();
        for (var i = 0; i < match.Workers.Count; i++)
        {
            var worker = match.Workers.EntityAt(i);
            if (match.Bodies.TryGet(worker, out var body) && body.Owner == _other)
            {
                workers.Add(worker);
            }
        }

        if (match.Players.Count < 2 || _capitol.Entity == EntityId.None || workers.Count == 0)
        {
            orders.Fail("the part needs another seat with a Capitol and a worker to lay a structure out of the local player's sight.");
            return;
        }

        // The first structure of the roster that the other seat may order now and that holds no
        // territory of its own, so that the territory drawn stays that of the Capitol.
        foreach (var candidate in match.Data.Structures)
        {
            if (candidate.TerritoryRadius is null
                && match.Construction.Buildable(candidate.Index) is not null
                && theirs.CheckStructure(candidate.Index) == BuildProblem.None)
            {
                _structure = candidate;
                break;
            }
        }

        if (_structure is null)
        {
            orders.Fail("the other seat can order no structure at the start of the part.");
            return;
        }

        if (!TryFindSite(orders, theirs, _structure, match.Bodies.Get(workers[0]).Position))
        {
            orders.Fail($"the other seat has no site for a {_structure.Id} in its territory that is {OutOfSight} tiles from every unit of the local player.");
            return;
        }

        var site = MapGrid.TileCenter(_siteX, _siteY);
        orders.SubmitFor(new Command(CommandType.Build, _other, [.. workers], site.X, site.Y, contentId: _structure.Index));
    }

    // The site the other seat accepts that is nearest to its worker, among those out of the sight
    // of every unit of the local player. The first found wins a tie, so the choice is the same on
    // every machine.
    private bool TryFindSite(SmokeOrders orders, PlayerView theirs, StructureDef structure, FixedVector2 worker)
    {
        var match = orders.Match;
        var own = orders.OwnUnits();
        var reach = (match.Data.StructureOf(_capitol.Type)?.TerritoryRadius ?? 0) + _capitol.Size;
        var best = long.MaxValue;
        for (var y = _capitol.Y - reach; y <= _capitol.Y + reach; y++)
        {
            for (var x = _capitol.X - reach; x <= _capitol.X + reach; x++)
            {
                if (theirs.CheckSite(structure, x, y) != BuildProblem.None || IsNearAny(match, own, x, y, structure.Footprint))
                {
                    continue;
                }

                var walk = new TileSquare(x, y, structure.Footprint).DistanceSquaredRawTo(worker);
                if (walk < best)
                {
                    best = walk;
                    _siteX = x;
                    _siteY = y;
                }
            }
        }

        return best != long.MaxValue;
    }

    private static bool IsNearAny(Sim.Setup.Match match, EntityId[] units, int x, int y, int size)
    {
        var tiles = new TileSquare(x - OutOfSight, y - OutOfSight, size + (2 * OutOfSight));
        foreach (var unit in units)
        {
            if (tiles.Covers(match.Bodies.Get(unit).Position))
            {
                return true;
            }
        }

        return false;
    }

    private void WaitForStructure(SmokeOrders orders, int tick)
    {
        PlayerSight.CollectStanding(orders.Match, _listed);
        _built = _listed.Find(structure => structure.Owner == _other && structure.X == _siteX && structure.Y == _siteY);
        if (_built.Entity == EntityId.None)
        {
            return;
        }

        _laidAt = tick;
        MustBeUnknown(orders, "when it has just been laid out of the player's sight");
        orders.View.Sight.FogOff = true;
    }

    private void Follow(SmokeOrders orders, int tick)
    {
        var view = orders.View;
        var match = orders.Match;
        if (tick == _laidAt + 1)
        {
            MustStand(orders, "with the fog lifted by the development switch", underPointer: false);
            view.Sight.FogOff = false;
            return;
        }

        if (tick == _laidAt + 2)
        {
            MustBeUnknown(orders, "after the development switch laid the fog back");
            if (!orders.Failed)
            {
                SendScout(orders);
            }

            return;
        }

        var known = view.Sight.Seen.TryGetStructure(_built.Entity, out var seen);
        if (_seenAt < 0 && known && !seen.IsRemembered)
        {
            _seenAt = tick;
            MustStand(orders, "in the tick the player first sees it", underPointer: true);
            orders.Submit(new Command(CommandType.Move, orders.Player, [_scout], _home.X, _home.Y));
        }
        else if (_seenAt >= 0 && _rememberedAt < 0 && known && seen.IsRemembered)
        {
            _rememberedAt = tick;
            _lastSeen = seen;
            MustBeMemory(orders, "in the tick the player first no longer sees it");
            view.Sight.FogOff = true;
        }
        else if (_rememberedAt >= 0 && tick == _rememberedAt + 1)
        {
            MustStand(orders, "remembered, with the fog lifted by the development switch", underPointer: false);
            view.Sight.FogOff = false;
        }
        else if (_rememberedAt >= 0 && tick == _rememberedAt + 2)
        {
            MustBeMemory(orders, "after the development switch laid the fog back over it");
            if (!orders.Failed && match.Vision.TryDescribeStructure(_built.Entity, out var truth) && truth.Built <= _lastSeen.Built)
            {
                orders.Fail($"the structure has built itself for {truth.Built} ticks and the player last saw it at {_lastSeen.Built}: the part meant to show a memory that is out of date.");
            }

            _tested = !orders.Failed;
        }
    }

    // The nearest unit of the local player walks to the structure. Nobody can stand on it, so
    // the unit ends beside it.
    private void SendScout(SmokeOrders orders)
    {
        var match = orders.Match;
        var middle = _built.Center;
        var nearest = long.MaxValue;
        foreach (var unit in orders.OwnUnits())
        {
            var at = match.Bodies.Get(unit).Position;
            long x = at.X.Raw - middle.X.Raw;
            long y = at.Y.Raw - middle.Y.Raw;
            if ((x * x) + (y * y) < nearest)
            {
                nearest = (x * x) + (y * y);
                _scout = unit;
                _home = at;
            }
        }

        if (_scout == EntityId.None)
        {
            orders.Fail("the local player has no unit to send to the structure.");
            return;
        }

        orders.Submit(new Command(CommandType.Move, orders.Player, [_scout], middle.X, middle.Y));
    }

    private void MustBeUnknown(SmokeOrders orders, string when)
    {
        var view = orders.View;
        if (!TryGetLayers(orders, out var structures, out _))
        {
            return;
        }

        if (view.Sight.Seen.KnowsStructure(_built.Entity))
        {
            orders.Fail($"the local player knows of the other seat's {_structure!.Id} {when}. The part lays it out of sight.");
        }
        else if (structures.Draws(_built.Entity))
        {
            orders.Fail($"the view draws a structure of the other seat's that the local player has never seen, {when}.");
        }
        else if (IsOnMinimap(view, out _))
        {
            orders.Fail($"the minimap is given a structure of the other seat's that the local player has never seen, {when}.");
        }
        else if (IsUnderPointer(view))
        {
            orders.Fail($"the pointer finds a structure of the other seat's that the local player has never seen, {when}.");
        }
    }

    private void MustStand(SmokeOrders orders, string when, bool underPointer)
    {
        var view = orders.View;
        if (!TryGetLayers(orders, out var structures, out _))
        {
            return;
        }

        if (!structures.Draws(_built.Entity) || structures.DrawsAsMemory(_built.Entity))
        {
            orders.Fail($"the view does not draw the other seat's structure as it stands, {when}.");
        }
        else if (!IsOnMinimap(view, out var onMinimap) || onMinimap.IsRemembered)
        {
            orders.Fail($"the minimap is not given the other seat's structure as it stands, {when}.");
        }
        else if (underPointer && !IsUnderPointer(view))
        {
            orders.Fail($"the pointer does not find the other seat's structure, {when}.");
        }
    }

    private void MustBeMemory(SmokeOrders orders, string when)
    {
        var view = orders.View;
        if (!TryGetLayers(orders, out var structures, out _))
        {
            return;
        }

        if (!structures.DrawsAsMemory(_built.Entity))
        {
            orders.Fail($"the view does not draw the other seat's structure as a memory, {when}.");
        }
        else if (!IsOnMinimap(view, out var onMinimap) || !onMinimap.IsRemembered || onMinimap.Built != _lastSeen.Built)
        {
            orders.Fail($"the minimap is not given the other seat's structure as the player last saw it, {when}.");
        }
        else if (!IsUnderPointer(view))
        {
            orders.Fail($"the pointer does not find the memory of the other seat's structure, {when}.");
        }
    }

    private void TerritoryMustFollowTheCapitol(SmokeOrders orders)
    {
        if (!TryGetLayers(orders, out _, out var territory))
        {
            return;
        }

        // What the layers read after the last tick, which is what the screen shows now.
        var known = orders.View.Sight.TryGetStructure(_capitol.Entity, out _);
        var drawn = territory.Tiles.Holds(_other, _capitol.X, _capitol.Y);
        if (known != drawn)
        {
            orders.Fail(known
                ? "the local player knows of the other seat's Capitol, and the view does not draw its territory."
                : "the view draws the territory of a Capitol the local player does not know of.");
        }
    }

    private void Judge(SmokeOrders orders)
    {
        GD.Print(
            $"Smoke test, structures in the fog: the other seat's {_structure?.Id} was laid at tick {_laidAt} of the part, " +
            $"the local player saw it at tick {_seenAt} and remembered it from tick {_rememberedAt}, built for {_lastSeen.Built} ticks.");

        if (_laidAt < 0)
        {
            orders.Fail($"the other seat's {_structure?.Id} was not laid in {Length - 1} ticks.");
        }
        else if (_seenAt < 0)
        {
            orders.Fail("the local player never saw the other seat's structure, with a unit sent to it.");
        }
        else if (_rememberedAt < 0)
        {
            orders.Fail($"the local player still sees the other seat's structure {Length - 1 - _seenAt} ticks after the unit was sent back from it.");
        }
        else if (!_tested)
        {
            orders.Fail("the part ended before the memory of the structure was tested.");
        }
        else
        {
            MustBeMemory(orders, "at the end of the part");
        }
    }

    private static bool TryGetLayers(SmokeOrders orders, out StructureLayer structures, out TerritoryLayer territory)
    {
        var world = orders.View.World;
        structures = world.GetNodeOrNull<StructureLayer>(nameof(StructureLayer));
        territory = world.GetNodeOrNull<TerritoryLayer>(nameof(TerritoryLayer));
        if (structures is null || territory is null)
        {
            orders.Fail("the world has no layer for structures or none for territory.");
            return false;
        }

        return true;
    }

    // What the minimap's part asks: the same answers the picture draws from.
    private bool IsOnMinimap(MatchView view, out SeenStructure structure)
    {
        new FogOnMinimap(view).CollectStructures(view, _listed);
        structure = _listed.Find(listed => listed.Entity == _built.Entity);
        return structure.Entity != EntityId.None;
    }

    // Looks at the structure and asks what is at the middle of it on the screen, as a click there
    // does. The view is put back where it was.
    private bool IsUnderPointer(MatchView view)
    {
        var camera = view.World.GetViewport().GetCamera3D();
        if (camera is null)
        {
            return false;
        }

        var looked = view.Camera.Focus;
        var middle = new Vector2(_built.X + (_built.Size / 2f), _built.Y + (_built.Size / 2f));
        view.Camera.CenterOn(middle);
        var screen = camera.UnprojectPosition(new Vector3(middle.X, 0.3f, middle.Y));
        var target = view.UnderPointer.At(screen, PointerTargets.EnemyStructure);
        view.Camera.CenterOn(looked);
        return target.Is(PointerTargets.EnemyStructure) && target.Entity == _built.Entity;
    }
}
