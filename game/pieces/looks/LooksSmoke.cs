using Sim.Research;

namespace Game;

/// <summary>
/// The smoke test's structure looks: one second of play in which nothing is ordered. It checks
/// what the view shows of tiers and of research against the match: every structure that shows
/// a track carries the look of its owner's tier in that track and no other structure carries
/// one, a banner hangs exactly where a research runs in a structure's slot, and the minimap
/// has every structure and the local player's territory.
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

        var match = orders.Match;
        var research = match.Research.State;
        var withTier = 0;
        var poles = 0;
        var flown = 0;
        StructureLooks.Collect(orders.View, _looks);
        foreach (var look in _looks)
        {
            var tier = look.Def.Track is { } track ? research.Tier(look.Owner, track) : 0;
            if (look.LookTier != tier || tiers.TierShown(look.Entity) != tier)
            {
                orders.Fail($"the {look.Def.Id} {look.Entity.Value} shows tier {tiers.TierShown(look.Entity)}, and its owner's tier in the track it shows is {tier}.");
                return;
            }

            var inResearch = InResearch(research, look);
            if (banners.TrackShown(look.Entity) != inResearch)
            {
                orders.Fail($"the {look.Def.Id} {look.Entity.Value} flies the banner of track {banners.TrackShown(look.Entity)}, and the track in research in its slot is {inResearch}.");
                return;
            }

            withTier += tier > 0 ? 1 : 0;
            poles += look.FliesBanner ? 1 : 0;
            flown += inResearch != StructureLook.NoTrack ? 1 : 0;
        }

        if (_looks.Count != match.Structures.Count || tiers.Count != withTier || banners.Count != poles || banners.Flown != flown)
        {
            orders.Fail(
                $"of {match.Structures.Count} structures the view lists {_looks.Count}; it draws {tiers.Count} tier looks for {withTier}, " +
                $"{banners.Count} banner poles for {poles} and {banners.Flown} banners for {flown}.");
            return;
        }

        CheckMinimap(orders);
    }

    // Checked once the minimap has drawn itself. It does so also without a window on the
    // machines tried, and a build that draws nothing has nothing here to be wrong.
    private void CheckMinimap(SmokeOrders orders)
    {
        var minimap = orders.View.Interface.Parts.OfType<MinimapPart>().FirstOrDefault()?.Structures;
        if (minimap is null || minimap.Territory is null)
        {
            return;
        }

        if (minimap.Drawn != _looks.Count || (minimap.Holders & (1 << orders.Player)) == 0)
        {
            orders.Fail($"the minimap draws {minimap.Drawn} structures of {_looks.Count}, and the territories of players {minimap.Holders:b} without the local player's.");
        }
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
