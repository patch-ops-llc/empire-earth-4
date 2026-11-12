using Sim.Research;

namespace Game;

/// <summary>
/// The smoke test's structure looks: one second of play in which nothing is ordered. It checks
/// that every structure the view lists is drawn with the tier and the banner the list gives it
/// and no other structure with any, that the list has each of the local player's structures
/// with that player's tier in the track it shows and the track in research in its slot, and
/// that the minimap shows the territories the world shows, the local player's among them.
/// </summary>
/// <remarks>
/// It runs after the research part, which has raised one track of the local player, so the
/// tiers are no longer all the same and a look read from the wrong track is found.
/// </remarks>
public sealed class LooksSmoke : SmokePart
{
    private readonly List<StructureLook> _looks = [];

    public override string Name => "Structure looks";

    public override int Ticks => 20;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (tick != Ticks - 1)
        {
            return;
        }

        var world = orders.View.World.GetChildren();
        if (world.OfType<TierMarkLayer>().FirstOrDefault() is not { } tiers || world.OfType<BannerLayer>().FirstOrDefault() is not { } banners)
        {
            orders.Fail("the world has no layer for the tier looks, or none for the banners.");
            return;
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
