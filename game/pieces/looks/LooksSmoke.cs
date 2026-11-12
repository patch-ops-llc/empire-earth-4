using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Research;

namespace Game;

/// <summary>
/// The smoke test's structure looks: 32 seconds of play in which the local player has one
/// structure built that shows a track. It checks that every structure the view lists is drawn
/// with the tier and the banner the list gives it and no other structure with any, that the
/// list has each of the local player's structures with that player's tier in the track it
/// shows and the track in research in its slot, that the new structure carries no look while
/// it is unfinished and its tier's look once it is finished, and that the minimap shows the
/// territories the world shows, the local player's among them.
/// </summary>
/// <remarks>
/// <para>
/// It runs after the research part, which has raised one track of the local player, so the
/// tiers are no longer all the same and a look read from the wrong track is found.
/// </para>
/// <para>
/// It builds the first structure of the roster that shows a track, takes no longer to build
/// than <see cref="MostBuildTicks"/> and that the player may order and can pay for now, on the
/// free site nearest to the Capitol. When the parts before it have left the player nothing to
/// build with, it builds nothing, says so, and checks the structures that stand.
/// </para>
/// </remarks>
public sealed class LooksSmoke : SmokePart
{
    // The longest building time of a structure this part builds, and the ticks it allows the
    // worker to reach the site and the view to follow.
    private const int MostBuildTicks = 500;
    private const int WalkTicks = 140;

    // How far from the Capitol's lowest tile a site is looked for, in tiles.
    private const int NearestSite = 4;
    private const int FarthestSite = 12;

    private readonly List<StructureLook> _looks = [];
    private StructureDef? _building;
    private int _siteX;
    private int _siteY;
    private bool _seenUnfinished;

    public override string Name => "Structure looks";

    public override int Ticks => MostBuildTicks + WalkTicks;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        var world = orders.View.World.GetChildren();
        if (world.OfType<TierMarkLayer>().FirstOrDefault() is not { } tiers || world.OfType<BannerLayer>().FirstOrDefault() is not { } banners)
        {
            orders.Fail("the world has no layer for the tier looks, or none for the banners.");
            return;
        }

        if (tick == 0)
        {
            OrderBuilding(orders);
        }
        else if (tick < Ticks - 1)
        {
            WatchBuilding(orders, tiers, banners);
        }
        else
        {
            CheckAll(orders, tiers, banners);
        }
    }

    // Orders the structure, with every worker of the player listed: the simulation sends the nearest.
    private void OrderBuilding(SmokeOrders orders)
    {
        var match = orders.Match;
        var workers = Array.FindAll(orders.OwnUnits(), match.Workers.Contains);
        foreach (var def in match.Data.Structures)
        {
            if (def.Track is null
                || def.BuildTicks > MostBuildTicks
                || workers.Length == 0
                || match.Construction.CheckStructure(orders.Player, def.Index) != Sim.Construction.BuildProblem.None
                || !TryFindSite(orders, def))
            {
                continue;
            }

            _building = def;
            var point = MapGrid.TileCenter(_siteX, _siteY);
            orders.Submit(new Command(CommandType.Build, orders.Player, workers, point.X, point.Y, contentId: def.Index));
            return;
        }

        Godot.GD.Print("Structure looks: the player can build nothing that shows a track now, so the unfinished look is not checked.");
    }

    // The free site nearest to the lowest tile of the player's first structure, ring by ring.
    private bool TryFindSite(SmokeOrders orders, StructureDef def)
    {
        var match = orders.Match;
        for (var i = 0; i < match.Structures.Count; i++)
        {
            if (!match.Bodies.TryGet(match.Structures.EntityAt(i), out var body) || body.Owner != orders.Player)
            {
                continue;
            }

            var home = match.Structures.ValueAt(i);
            for (var ring = NearestSite; ring <= FarthestSite; ring++)
            {
                for (var dy = -ring; dy <= ring; dy++)
                {
                    for (var dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == ring
                            && match.Construction.CheckArrival(orders.Player, def, home.X + dx, home.Y + dy) == Sim.Construction.BuildProblem.None)
                        {
                            _siteX = home.X + dx;
                            _siteY = home.Y + dy;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        return false;
    }

    // While the structure is building itself, it carries no tier's look and no banner.
    private void WatchBuilding(SmokeOrders orders, TierMarkLayer tiers, BannerLayer banners)
    {
        if (_building is null || Built(orders) is not { } structure || !orders.Match.Foundations.Contains(structure))
        {
            return;
        }

        _seenUnfinished = true;
        if (tiers.TierShown(structure) != 0 || banners.TrackShown(structure) != StructureLook.NoTrack)
        {
            orders.Fail($"the unfinished {_building.Id} {structure.Value} is drawn at tier {tiers.TierShown(structure)} with the banner of track {banners.TrackShown(structure)}, and should carry neither.");
        }
    }

    // The structure this part ordered, once its foundation is laid.
    private EntityId? Built(SmokeOrders orders)
    {
        var match = orders.Match;
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var placed = match.Structures.ValueAt(i);
            if (placed.Type == _building!.Index && placed.X == _siteX && placed.Y == _siteY)
            {
                return match.Structures.EntityAt(i);
            }
        }

        return null;
    }

    private void CheckAll(SmokeOrders orders, TierMarkLayer tiers, BannerLayer banners)
    {
        if (_building is not null)
        {
            if (Built(orders) is not { } structure || !_seenUnfinished || orders.Match.Foundations.Contains(structure))
            {
                orders.Fail(
                    $"the {_building.Id} ordered at tile ({_siteX}, {_siteY}) was not seen unfinished and then finished within {Ticks} ticks. " +
                    "If a worker walks or a structure builds more slowly in the data now, this part needs more ticks.");
                return;
            }

            if (tiers.TierShown(structure) < 1)
            {
                orders.Fail($"the finished {_building.Id} {structure.Value} carries no tier's look.");
                return;
            }
        }

        // Every structure the view lists is drawn as the list has it. The local player's own are
        // never under the fog of war, so the list must have each of them, and as the match does.
        var match = orders.Match;
        var research = match.Research.State;
        var withTier = 0;
        var poles = 0;
        var flown = 0;
        var own = 0;
        StructureLooks.Collect(orders.View, _looks);
        foreach (var look in _looks)
        {
            var tier = look.IsFinished ? look.LookTier : 0;
            if (tiers.TierShown(look.Entity) != tier)
            {
                orders.Fail($"the {look.Def.Id} {look.Entity.Value} is drawn at tier {tiers.TierShown(look.Entity)}, and the tier its look shows is {tier}.");
                return;
            }

            var banner = look.FliesBanner ? look.Banner : StructureLook.NoTrack;
            if (banners.TrackShown(look.Entity) != banner)
            {
                orders.Fail($"the {look.Def.Id} {look.Entity.Value} flies the banner of track {banners.TrackShown(look.Entity)}, and its banner is that of track {banner}.");
                return;
            }

            withTier += tier > 0 ? 1 : 0;
            poles += look.FliesBanner ? 1 : 0;
            flown += banner != StructureLook.NoTrack ? 1 : 0;
            if (look.Owner != orders.Player)
            {
                continue;
            }

            own++;
            var ownTier = look.Def.Track is { } track ? research.Tier(look.Owner, track) : 0;
            if (look.LookTier != ownTier || look.Banner != InResearch(research, look))
            {
                orders.Fail(
                    $"the {look.Def.Id} {look.Entity.Value} of the local player shows tier {look.LookTier} and the banner of track {look.Banner}. " +
                    $"The player's tier in the track it shows is {ownTier}, and the track in research in its slot is {InResearch(research, look)}.");
                return;
            }
        }

        if (own != OwnStructures(orders) || tiers.Count != withTier || banners.Count != poles || banners.Flown != flown)
        {
            orders.Fail(
                $"of the local player's {OwnStructures(orders)} structures the view lists {own}; it draws {tiers.Count} tier looks for {withTier}, " +
                $"{banners.Count} banner poles for {poles} and {banners.Flown} banners for {flown}.");
            return;
        }

        CheckMinimap(orders);
    }

    // The minimap shows the territories the world shows. Checked once the minimap has drawn
    // itself: it does so also without a window on the machines tried, and a build that draws
    // nothing has nothing here to be wrong.
    private static void CheckMinimap(SmokeOrders orders)
    {
        var minimap = orders.View.Interface.Parts.OfType<MinimapPart>().FirstOrDefault()?.Territory;
        var world = orders.View.World.GetChildren().OfType<TerritoryLayer>().FirstOrDefault()?.Tiles;
        if (minimap is null || world is null || minimap.Pictures == 0)
        {
            return;
        }

        if (minimap.Holders != world.Players || (minimap.Holders & (1 << orders.Player)) == 0)
        {
            orders.Fail($"the minimap shows the territories of players {minimap.Holders:b}, the world those of {world.Players:b}, and the local player is {orders.Player}.");
        }
    }

    private static int OwnStructures(SmokeOrders orders)
    {
        var match = orders.Match;
        var own = 0;
        for (var i = 0; i < match.Structures.Count; i++)
        {
            own += match.Bodies.TryGet(match.Structures.EntityAt(i), out var body) && body.Owner == orders.Player ? 1 : 0;
        }

        return own;
    }

    // The track in research in the slot a structure holds, read from the match and not from the seam.
    private static int InResearch(ResearchState research, in StructureLook look)
    {
        for (var i = 0; i < ResearchState.SlotsPerPlayer; i++)
        {
            ref readonly var slot = ref research.Slot(look.Owner, i);
            if (slot.IsOpen && slot.IsBusy && slot.Holder == look.Entity)
            {
                return slot.Track;
            }
        }

        return StructureLook.NoTrack;
    }
}
