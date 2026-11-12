using Godot;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Research;
using Sim.Vision;

namespace Game;

/// <summary>
/// The smoke test's structure looks in the fog of war: up to two minutes and forty seconds of
/// play in which the other seat researches out of the local player's sight, and one of the local
/// player's units walks to that seat's Academy three times and to its Capitol twice. The part
/// asks the two layers of the looks what they draw, and fails when a structure the player has
/// never seen carries a look, when one they see does not carry the look it has, or when one they
/// remember carries anything but the look it had when they last saw it.
/// </summary>
/// <remarks>
/// <para>
/// It follows <see cref="StructuresInFogSmoke"/>, which leaves an Academy of the other seat's that
/// the local player remembers unfinished. The steps:
/// </para>
/// <list type="bullet">
/// <item>The Academy finishes itself out of sight. The memory of it is still of a foundation, so
/// it must carry no pole. The development switch must show the pole, and laying the fog back
/// must take it away again.</item>
/// <item>The unit walks to the Academy and then to the Capitol, and stops when it sees neither.
/// In sight, the Academy must carry a pole without a banner and the Capitol the turret of its
/// owner's tier. Remembered, both must keep those, in the shade of a memory.</item>
/// <item>The other seat, which has built a Generator and worked it meanwhile, starts a research
/// of the Capitol's track in its own slot and a second in the Academy's. Once the first has
/// finished, the match has the Capitol a tier higher and a banner on the Academy, and the view
/// must show the old tier and no banner. The development switch must show both as they are,
/// and laying the fog back must bring the memories back.</item>
/// <item>The unit walks to both again. In sight they must carry the banner and the new tier, and
/// remembered they must keep them.</item>
/// <item>The other seat cancels the research in the Academy. The remembered Academy must go on
/// flying the banner, until the unit walks to it once more: in sight it must fly none.</item>
/// <item>In every tick, every structure of the other seat's must carry exactly the look the
/// local player's sight gives it, and none when the sight does not know of it; and what is
/// drawn on a structure must be in the shade the structure is drawn in.</item>
/// </list>
/// </remarks>
public sealed class LooksInFogSmoke : SmokePart
{
    private const int Length = 3200;

    // How many ticks before the first research ends the second is to be paid for.
    private const int Overlap = 100;

    // The earnings of this many ticks are measured before the first research is timed by them.
    private const int MeasuredTicks = 200;

    private const int NearestSite = 3;
    private const int FarthestSite = 14;

    private const float ColorSlack = 0.01f;

    private readonly List<SeenStructure> _standing = [];
    private readonly List<StructureLook> _looks = [];
    private Step _step;
    private int _other;
    private SeenStructure _academy;
    private SeenStructure _capitol;
    private int _tierTrack;
    private int _bannerTrack = StructureLook.NoTrack;
    private EntityId[] _generatorWorkers = [];
    private int _siteX;
    private int _siteY;
    private int _workedFrom = -1;
    private long _energyAtWork;
    private bool _visited;
    private bool _firstStarted;
    private bool _secondStarted;
    private EntityId _scout = EntityId.None;
    private FixedVector2 _home;
    private int _tierSeen;
    private int _bannerSeen = StructureLook.NoTrack;
    private int _doneAt = -1;
    private int _dimChecks;
    private int _switched;

    private enum Step
    {
        AcademyFinishes,
        PoleBySwitch,
        PoleGoneAgain,
        ToAcademy,
        ToCapitol,
        OutOfSight,
        ResearchChangesBoth,
        BothBySwitch,
        BothAsRememberedAgain,
        ToAcademyAgain,
        ToCapitolAgain,
        OutOfSightAgain,
        ResearchCancelled,
        ToAcademyOnceMore,
        Done,
    }

    public override string Name => "Structure looks in the fog";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (!TryGetLayers(orders, out var layers))
        {
            return;
        }

        if (tick == 0)
        {
            Begin(orders);
            return;
        }

        foreach (var happened in orders.Match.Simulation.Events)
        {
            if (happened.Kind is SimEventKind.OrderRefused or SimEventKind.BuildRefused or SimEventKind.BuildOrderEnded)
            {
                orders.Fail($"the simulation refused or ended an order of the part for player {happened.Player}: {happened.Kind}, reason {happened.Amount}.");
                return;
            }
        }

        // Before the step, which may flip the development switch: the layers have read the
        // sight as it was after the last tick, and that is what the screen shows now.
        LooksMustFollowTheSight(orders, layers);
        if (orders.Failed)
        {
            return;
        }

        if (tick == Length - 1)
        {
            Judge(orders);
            return;
        }

        RunTheOtherSeat(orders, tick);
        if (!orders.Failed)
        {
            Follow(orders, layers, tick);
        }
    }

    private void Begin(SmokeOrders orders)
    {
        var match = orders.Match;
        var data = match.Data;
        _other = (orders.Player + 1) % match.Players.Count;
        PlayerSight.CollectStanding(match, _standing);
        _academy = _standing.Find(structure => structure.Owner == _other && data.StructureOf(structure.Type) is { ResearchSlots: > 0, Track: null });
        _capitol = _standing.Find(structure => structure.Owner == _other && structure.IsFinished && data.StructureOf(structure.Type) is { Track: not null });
        if (match.Players.Count < 2 || _academy.Entity == EntityId.None || _capitol.Entity == EntityId.None)
        {
            orders.Fail("the part needs another seat with a finished structure that shows a track and with an Academy, which the part before it has that seat lay.");
            return;
        }

        if (!orders.View.Sight.Seen.TryGetStructure(_academy.Entity, out var known) || !known.IsRemembered || known.IsFinished)
        {
            orders.Fail("the part needs the local player to remember the other seat's Academy as unfinished, as the part before it leaves it.");
            return;
        }

        _tierTrack = data.StructureOf(_capitol.Type)!.Track!.Value;
        TurretMustDimWithItsShade(orders, data.StructureOf(_capitol.Type)!);
        if (!orders.Failed)
        {
            StartTheOtherSeatsEconomy(orders);
        }
    }

    // The other seat has what its Academy left it, which pays for no research: every research
    // costs Energy. So some of its workers go to a deposit and the others build a Generator.
    private void StartTheOtherSeatsEconomy(SmokeOrders orders)
    {
        var match = orders.Match;
        var theirs = new PlayerView(match, _other);
        var workers = new List<EntityId>();
        for (var i = 0; i < match.Workers.Count; i++)
        {
            var worker = match.Workers.EntityAt(i);
            if (match.Bodies.TryGet(worker, out var body) && body.Owner == _other)
            {
                workers.Add(worker);
            }
        }

        workers.Sort();
        var generator = match.Data.Structures.FirstOrDefault(
            candidate => candidate.IsEnergyWorkSite && theirs.CheckStructure(candidate.Index) == BuildProblem.None);
        if (workers.Count < 2 || generator is null || !TryFindSite(theirs, generator) || !TryFindDeposit(match, out var deposit))
        {
            orders.Fail(
                $"the other seat has {workers.Count} workers, and needs two, a structure to earn Energy at that it may order, " +
                "a site for it beside its Capitol and a deposit in its territory.");
            return;
        }

        var miners = workers.Count >= 4 ? 2 : 1;
        _generatorWorkers = [.. workers.Skip(miners)];
        var site = MapGrid.TileCenter(_siteX, _siteY);
        orders.SubmitFor(new Command(CommandType.Gather, _other, [.. workers.Take(miners)], targetEntity: deposit));
        orders.SubmitFor(new Command(CommandType.Build, _other, _generatorWorkers, site.X, site.Y, contentId: generator.Index));
    }

    // The free site nearest to the lowest tile of the other seat's Capitol, ring by ring.
    private bool TryFindSite(PlayerView theirs, StructureDef generator)
    {
        for (var ring = NearestSite; ring <= FarthestSite; ring++)
        {
            for (var dy = -ring; dy <= ring; dy++)
            {
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == ring
                        && theirs.CheckSite(generator, _capitol.X + dx, _capitol.Y + dy) == BuildProblem.None)
                    {
                        _siteX = _capitol.X + dx;
                        _siteY = _capitol.Y + dy;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // The first deposit, in ID order, inside the other seat's territory.
    private bool TryFindDeposit(Sim.Setup.Match match, out EntityId deposit)
    {
        deposit = EntityId.None;
        var sites = match.WorkSites;
        for (var i = 0; i < sites.Count; i++)
        {
            var entity = sites.EntityAt(i);
            var site = sites.ValueAt(i);
            if (site.Kind == WorkSiteKind.Deposit
                && match.World.Entities.IsAlive(entity)
                && Territory.Holds(match.Territories, match.World.Entities, _other, site.X, site.Y, site.Size)
                && (deposit == EntityId.None || entity.CompareTo(deposit) < 0))
            {
                deposit = entity;
            }
        }

        return deposit != EntityId.None;
    }

    // The other seat's orders, each given when the match allows it.
    private void RunTheOtherSeat(SmokeOrders orders, int tick)
    {
        var match = orders.Match;
        var research = match.Research;
        var energy = match.Stockpiles.Energy(_other);
        if (_workedFrom < 0)
        {
            SendToTheGenerator(orders, tick, energy);
            return;
        }

        if (!_visited || _secondStarted || tick - _workedFrom < MeasuredTicks)
        {
            return;
        }

        if (_firstStarted)
        {
            // The second research takes the Academy's slot only while the first holds the player's own.
            if (research.CheckStart(_other, _bannerTrack) != ResearchStartCheck.Allowed)
            {
                return;
            }

            if (research.State.SlotResearching(_other, _tierTrack) != ResearchState.PlayerSlot)
            {
                orders.Fail(
                    "the other seat's first research was not in its own slot when the second could be paid for, so the second would not take the Academy's. " +
                    "If a research is shorter or Energy slower in the data now, this part has to time the two anew.");
                return;
            }

            _secondStarted = true;
            orders.SubmitFor(new Command(CommandType.StartResearch, _other, contentId: _bannerTrack));
            return;
        }

        // The first is started as soon as the second can be paid for while the first still runs,
        // by what the Generator has been earning in a tick. The second is of the last track that
        // is offered, Extraction in today's data, whose yellow is easy to tell from the other
        // seat's red when the part is watched.
        var offers = research.Tech(_other).Research;
        _bannerTrack = Enumerable.Range(0, research.State.TrackCount).LastOrDefault(track => track != _tierTrack && offers[track].IsAvailable, StructureLook.NoTrack);
        if (_bannerTrack == StructureLook.NoTrack || offers[_tierTrack].Price is not { } first || offers[_bannerTrack].Price is not { } second)
        {
            orders.Fail($"the other seat is not offered the next tier of track {_tierTrack} and that of another track.");
            return;
        }

        var earnedInATick = (energy - _energyAtWork) / (tick - _workedFrom);
        var energyNeeded = (Stockpiles.PerResource * (first.Energy + second.Energy)) - (earnedInATick * Math.Max(0, first.Ticks - Overlap));
        if (energy >= energyNeeded
            && match.Stockpiles.Covers(_other, first.Materials + second.Materials, first.Energy)
            && research.CheckStart(_other, _tierTrack) == ResearchStartCheck.Allowed)
        {
            _firstStarted = true;
            orders.SubmitFor(new Command(CommandType.StartResearch, _other, contentId: _tierTrack));
        }
    }

    // Once the Generator is finished, the workers that built it work at it.
    private void SendToTheGenerator(SmokeOrders orders, int tick, long energy)
    {
        var match = orders.Match;
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var entity = match.Structures.EntityAt(i);
            var placed = match.Structures.ValueAt(i);
            if (placed.X == _siteX && placed.Y == _siteY && !match.Foundations.Contains(entity) && match.WorkSites.Contains(entity))
            {
                _workedFrom = tick;
                _energyAtWork = energy;
                orders.SubmitFor(new Command(CommandType.Gather, _other, _generatorWorkers, targetEntity: entity));
                return;
            }
        }
    }

    private void Follow(SmokeOrders orders, Layers layers, int tick)
    {
        var view = orders.View;
        var match = orders.Match;
        var sight = view.Sight.Seen;
        var academyKnown = sight.TryGetStructure(_academy.Entity, out var academy);
        var capitolKnown = sight.TryGetStructure(_capitol.Entity, out var capitol);
        var tier = match.Research.State.Tier(_other, _tierTrack);
        var banner = InResearchAt(match.Research.State, _academy.Entity);
        switch (_step)
        {
            case Step.AcademyFinishes:
                if (match.Foundations.Contains(_academy.Entity))
                {
                    break;
                }

                if (!academyKnown || !academy.IsRemembered || academy.IsFinished)
                {
                    orders.Fail("the local player no longer remembers the other seat's Academy as unfinished when it finishes: the part meant to show a memory that is out of date.");
                    break;
                }

                MustFly(orders, layers, pole: false, StructureLook.NoTrack, "remembered as a foundation, when it has finished itself out of sight");
                Switch(view, fogOff: true);
                break;

            case Step.PoleBySwitch:
                MustFly(orders, layers, pole: true, StructureLook.NoTrack, "with the fog lifted by the development switch");
                Switch(view, fogOff: false);
                break;

            case Step.PoleGoneAgain:
                MustFly(orders, layers, pole: false, StructureLook.NoTrack, "after the development switch laid the fog back over the memory of its foundation");
                SendScout(orders, _academy);
                _step++;
                break;

            case Step.ToAcademy:
                if (academyKnown && !academy.IsRemembered)
                {
                    MustFly(orders, layers, pole: true, StructureLook.NoTrack, "in the tick the player first sees it finished");
                    SendScout(orders, _capitol);
                    _step++;
                }

                break;

            case Step.ToCapitol:
                if (capitolKnown && !capitol.IsRemembered)
                {
                    _tierSeen = tier;
                    MustShowTier(orders, layers, tier, "in the tick the player first sees it");
                    orders.Submit(new Command(CommandType.Move, orders.Player, [_scout], _home.X, _home.Y));
                    _step++;
                }

                break;

            case Step.OutOfSight:
                if (academyKnown && academy.IsRemembered && capitolKnown && capitol.IsRemembered)
                {
                    // It waits here, out of sight of both, for the other seat's research.
                    orders.Submit(new Command(CommandType.Stop, orders.Player, [_scout]));
                    _visited = true;
                    _step++;
                }

                break;

            case Step.ResearchChangesBoth:
                if (tier == _tierSeen || banner == StructureLook.NoTrack)
                {
                    break;
                }

                if (!academyKnown || !academy.IsRemembered || !capitolKnown || !capitol.IsRemembered)
                {
                    orders.Fail("the local player sees the other seat's Academy or Capitol while that seat researches: the part meant to show memories that are out of date.");
                    break;
                }

                MustShowTier(orders, layers, _tierSeen, $"remembered, when its owner has reached tier {tier} out of sight");
                MustFly(orders, layers, pole: true, StructureLook.NoTrack, $"remembered without one, when a research of track {banner} runs in its slot out of sight");
                Switch(view, fogOff: true);
                break;

            case Step.BothBySwitch:
                MustShowTier(orders, layers, tier, "with the fog lifted by the development switch");
                MustFly(orders, layers, pole: true, banner, "with the fog lifted by the development switch");
                Switch(view, fogOff: false);
                break;

            case Step.BothAsRememberedAgain:
                MustShowTier(orders, layers, _tierSeen, "after the development switch laid the fog back over it");
                MustFly(orders, layers, pole: true, StructureLook.NoTrack, "after the development switch laid the fog back over it");
                SendScout(orders, _academy);
                _step++;
                break;

            case Step.ToAcademyAgain:
                if (academyKnown && !academy.IsRemembered)
                {
                    if (banner == StructureLook.NoTrack)
                    {
                        orders.Fail("the research in the other seat's Academy ended before the local player's unit saw its banner. If a unit walks more slowly in the data now, this part has to time the visit anew.");
                        break;
                    }

                    _bannerSeen = banner;
                    MustFly(orders, layers, pole: true, banner, "in the tick the player sees it again");
                    SendScout(orders, _capitol);
                    _step++;
                }

                break;

            case Step.ToCapitolAgain:
                if (capitolKnown && !capitol.IsRemembered)
                {
                    MustShowTier(orders, layers, tier, "in the tick the player sees it again");
                    orders.Submit(new Command(CommandType.Move, orders.Player, [_scout], _home.X, _home.Y));
                    _step++;
                }

                break;

            case Step.OutOfSightAgain:
                if (academyKnown && academy.IsRemembered && capitolKnown && capitol.IsRemembered)
                {
                    MustShowTier(orders, layers, tier, "in the tick the player first no longer sees it");
                    MustFly(orders, layers, pole: true, _bannerSeen, "in the tick the player first no longer sees it");
                    if (banner != StructureLook.NoTrack)
                    {
                        orders.SubmitFor(new Command(CommandType.CancelResearch, _other, contentId: banner));
                    }

                    _step++;
                }

                break;

            case Step.ResearchCancelled:
                if (banner == StructureLook.NoTrack)
                {
                    MustFly(orders, layers, pole: true, _bannerSeen, "remembered with it, when no research runs in its slot any more");
                    SendScout(orders, _academy);
                    _step++;
                }

                break;

            case Step.ToAcademyOnceMore:
                if (academyKnown && !academy.IsRemembered)
                {
                    MustFly(orders, layers, pole: true, StructureLook.NoTrack, "in the tick the player sees it without its banner");
                    orders.Submit(new Command(CommandType.Move, orders.Player, [_scout], _home.X, _home.Y));
                    _doneAt = tick;
                    _step++;
                }

                break;
        }
    }

    private void Switch(MatchView view, bool fogOff)
    {
        view.Sight.FogOff = fogOff;
        _switched++;
        _step++;
    }

    // The nearest of the local player's workers walks to the structure, or the nearest unit when
    // the player has no worker. A worker attacks nobody, so it can wait near the other seat's
    // base between its visits and leave that seat's workers to earn what the researches cost.
    // Nobody can stand on a structure, so it ends beside it.
    private void SendScout(SmokeOrders orders, in SeenStructure structure)
    {
        var match = orders.Match;
        var middle = structure.Center;
        if (_scout == EntityId.None)
        {
            var units = orders.OwnUnits();
            var workers = Array.FindAll(units, match.Workers.Contains);
            var nearest = long.MaxValue;
            foreach (var unit in workers.Length > 0 ? workers : units)
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
        }

        if (_scout == EntityId.None || !match.World.Entities.IsAlive(_scout))
        {
            orders.Fail("the local player has no unit to send to the other seat's structures, or the one sent has died.");
            return;
        }

        orders.Submit(new Command(CommandType.Move, orders.Player, [_scout], middle.X, middle.Y));
    }

    private void MustShowTier(SmokeOrders orders, Layers layers, int tier, string when)
    {
        if (!orders.Failed && layers.Tiers.TierShown(_capitol.Entity) != tier)
        {
            orders.Fail($"the view draws the other seat's Capitol at tier {layers.Tiers.TierShown(_capitol.Entity)} and should draw it at tier {tier}, {when}.");
        }
    }

    private void MustFly(SmokeOrders orders, Layers layers, bool pole, int banner, string when)
    {
        if (orders.Failed)
        {
            return;
        }

        var hasPole = layers.Banners.HasPole(_academy.Entity);
        var shown = layers.Banners.TrackShown(_academy.Entity);
        if (hasPole != pole || shown != banner)
        {
            orders.Fail($"the view draws the other seat's Academy {Flying(hasPole, shown)} and should draw it {Flying(pole, banner)}, {when}.");
        }
    }

    private static string Flying(bool pole, int banner) =>
        !pole ? "without a pole" : banner == StructureLook.NoTrack ? "with a pole and no banner" : $"with the banner of track {banner}";

    // Every structure of another player's carries the look the local player's sight gives it,
    // and none when the sight does not know of it, in the shade the structure is drawn in.
    private void LooksMustFollowTheSight(SmokeOrders orders, Layers layers)
    {
        var view = orders.View;
        var data = orders.Match.Data;
        PlayerSight.CollectStanding(orders.Match, _standing);
        StructureLooks.Collect(view, _looks);
        foreach (var standing in _standing)
        {
            if (standing.Owner == orders.Player || data.StructureOf(standing.Type) is not { } def)
            {
                continue;
            }

            var entity = standing.Entity;
            var known = view.Sight.TryGetStructure(entity, out var seen);
            var listed = false;
            foreach (var look in _looks)
            {
                listed |= look.Entity == entity;
            }

            if (listed != known)
            {
                orders.Fail(known
                    ? $"the looks leave out the {def.Id} {entity.Value} of another player's, which the local player knows of."
                    : $"the looks list the {def.Id} {entity.Value} of another player's, which the local player has never seen.");
                return;
            }

            var finished = known && seen.IsFinished;
            var tier = finished && def.Track is not null ? seen.LookTier : 0;
            var pole = finished && def.ResearchSlots > 0 && def.Track is null;
            var banner = pole ? seen.Banner : StructureLook.NoTrack;
            var how = !known ? "has never seen it" : seen.IsRemembered ? "remembers it" : "sees it";
            if (layers.Tiers.TierShown(entity) != tier)
            {
                orders.Fail(
                    $"the view draws the {def.Id} {entity.Value} of another player's at tier {layers.Tiers.TierShown(entity)}, " +
                    $"and the local player {how}{(known ? $" at tier {tier}" : string.Empty)}.");
                return;
            }

            if (layers.Banners.HasPole(entity) != pole || layers.Banners.TrackShown(entity) != banner)
            {
                orders.Fail(
                    $"the view draws the {def.Id} {entity.Value} of another player's {Flying(layers.Banners.HasPole(entity), layers.Banners.TrackShown(entity))}, " +
                    $"and the local player {how}{(known ? $" {Flying(pole, banner)}" : string.Empty)}.");
                return;
            }

            if (banner == StructureLook.NoTrack && layers.Banners.ClothShown(entity) is not null)
            {
                orders.Fail($"a cloth hangs on the {def.Id} {entity.Value} of another player's, and the local player {how}{(known ? " with no banner" : string.Empty)}.");
                return;
            }

            if (tier == 0 && !pole)
            {
                continue;
            }

            if (layers.Structures.ShadeOf(entity) is not { } shade)
            {
                orders.Fail($"the view draws a look on the {def.Id} {entity.Value} of another player's and does not draw the structure.");
                return;
            }

            var paints = layers.Banners;
            if ((tier > 0 && layers.Tiers.ShadeShown(entity) != shade.Stepped())
                || (pole && !Same(paints.PoleShown(entity), shade.Of(BannerLayer.PoleColor)))
                || (banner != StructureLook.NoTrack && !Same(paints.ClothShown(entity), shade.Of(TrackColors.Of(data, banner)))))
            {
                orders.Fail(
                    $"the look of the {def.Id} {entity.Value} of another player's, which the local player {(seen.IsRemembered ? "remembers" : "sees")}, " +
                    $"is not drawn in the shade its structure is drawn in, dimmed by {shade.Dimmed}.");
                return;
            }

            _dimChecks += seen.IsRemembered && shade.Stepped().Dimmed > 0f ? 1 : 0;
        }
    }

    private static bool Same(Color? drawn, Color wanted) =>
        drawn is { } color
        && Mathf.Abs(color.R - wanted.R) <= ColorSlack
        && Mathf.Abs(color.G - wanted.G) <= ColorSlack
        && Mathf.Abs(color.B - wanted.B) <= ColorSlack;

    // The turret of a memory is the turret of the structure in sight with every color dimmed.
    // A build that keeps no meshes, as one without a screen may, has nothing here to be wrong.
    private void TurretMustDimWithItsShade(SmokeOrders orders, StructureDef def)
    {
        var track = TrackColors.Of(orders.Match.Data, _tierTrack);
        var memory = new StructureShade(1f);
        var inSight = ColorsOf(TierShapes.Turret(def, 2, track, new StructureShade(0f)));
        var remembered = ColorsOf(TierShapes.Turret(def, 2, track, memory));
        if (inSight.Length == 0)
        {
            return;
        }

        var changed = false;
        for (var i = 0; i < inSight.Length && remembered.Length == inSight.Length; i++)
        {
            if (!Same(remembered[i], memory.Of(inSight[i])))
            {
                orders.Fail($"corner {i} of the turret of a remembered {def.Id} is not the corner of the turret in sight, dimmed as a memory is.");
                return;
            }

            changed |= !Same(remembered[i], inSight[i]);
        }

        if (!changed || remembered.Length != inSight.Length)
        {
            orders.Fail($"the turret of a remembered {def.Id} is the turret of one in sight: nothing of it is dimmed.");
        }

        static Color[] ColorsOf(ArrayMesh mesh) =>
            mesh.GetSurfaceCount() == 0 ? [] : mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Color].AsColorArray();
    }

    // The track in research in the slot a structure gives its owner, read from the match.
    private int InResearchAt(ResearchState research, EntityId holder)
    {
        for (var i = 0; i < ResearchState.SlotsPerPlayer; i++)
        {
            ref readonly var slot = ref research.Slot(_other, i);
            if (slot.IsOpen && slot.IsBusy && slot.Holder == holder)
            {
                return slot.Track;
            }
        }

        return StructureLook.NoTrack;
    }

    private void Judge(SmokeOrders orders)
    {
        GD.Print(
            $"Smoke test, structure looks in the fog: the other seat's Capitol was remembered at tier {_tierSeen} while it stood at tier {_tierSeen + 1}, " +
            $"its Academy as a foundation while it stood finished, without a banner while it flew that of track {_bannerSeen}, and with it once it flew none, until it was seen again. " +
            $"The development switch was flipped {_switched} times, the looks of memories were {(_dimChecks > 0 ? "found" : "never found")} dimmed with their structures, " +
            $"and the last step was done at tick {_doneAt} of the part.");

        if (_step != Step.Done)
        {
            orders.Fail(
                $"the part reached only its step '{_step}' in {Length - 1} ticks; the other seat's Generator was worked from tick {_workedFrom}, " +
                $"its first research {(_firstStarted ? "was" : "was not")} started and its second {(_secondStarted ? "was" : "was not")}. " +
                "If a unit walks, a structure builds or a research runs more slowly in the data now, this part needs more ticks.");
        }
        else if (_dimChecks == 0)
        {
            orders.Fail("no look of a remembered structure was ever found dimmed, in all the frames the part ran for.");
        }
    }

    private static bool TryGetLayers(SmokeOrders orders, out Layers layers)
    {
        var world = orders.View.World.GetChildren();
        if (world.OfType<StructureLayer>().FirstOrDefault() is { } structures
            && world.OfType<TierMarkLayer>().FirstOrDefault() is { } tiers
            && world.OfType<BannerLayer>().FirstOrDefault() is { } banners)
        {
            layers = new Layers(structures, tiers, banners);
            return true;
        }

        layers = default;
        orders.Fail("the world has no layer for structures, for the tier looks or for the banners.");
        return false;
    }

    private readonly record struct Layers(StructureLayer Structures, TierMarkLayer Tiers, BannerLayer Banners);
}
