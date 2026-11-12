using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Research;

namespace Game;

/// <summary>
/// What the view shows of one structure beyond its shape: whose it is, where it stands, the
/// tier its look shows and the banner it flies (design 2.5 and 3.2).
/// </summary>
/// <param name="Entity">The structure.</param>
/// <param name="Owner">The player it belongs to.</param>
/// <param name="Def">Its entry in the roster.</param>
/// <param name="X">The x of the lowest tile it covers.</param>
/// <param name="Y">The y of the lowest tile it covers.</param>
/// <param name="Size">The tiles it covers along each side.</param>
/// <param name="IsFinished">False while it is building itself. An unfinished structure shows no tier and flies no banner.</param>
/// <param name="LookTier">
/// Its owner's tier in the track whose look it shows, or zero for a structure that looks the
/// same at every tier.
/// </param>
/// <param name="Banner">
/// The track in research in the slot the structure gives, or <see cref="NoTrack"/>.
/// </param>
public readonly record struct StructureLook(
    EntityId Entity,
    int Owner,
    StructureDef Def,
    int X,
    int Y,
    int Size,
    bool IsFinished,
    int LookTier,
    int Banner)
{
    /// <summary>The <see cref="Banner"/> of a structure that flies none.</summary>
    public const int NoTrack = -1;

    /// <summary>The middle of its footprint on the ground, where its shape stands.</summary>
    public Vector3 Middle => new(X + (Size / 2f), 0f, Y + (Size / 2f));

    /// <summary>
    /// True for a structure that has a banner to fly: one that gives a research slot and shows
    /// no track of its own, which is the Academy (design 2.5). The Capitol's slot is the
    /// player's own and belongs to no structure, so the Capitol flies none.
    /// </summary>
    public bool FliesBanner => IsFinished && Def.ResearchSlots > 0 && Def.Track is null;
}

/// <summary>
/// The one place the view reads the tier a structure shows and the track its banner shows.
/// The layers that draw them, and the minimap, ask here and nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// Today it reads the match itself, so every structure of every player is listed as it is now.
/// That is right only while structures are not under the fog of war.
/// </para>
/// <para>
/// With fog for structures (17b) the list comes from the local player's view instead:
/// <c>PlayerView.CollectStructures</c> gives the player's own structures, those of others seen
/// now and those remembered, and each <c>SeenStructure</c> carries <c>LookTier</c>,
/// <c>Banner</c> and <c>IsFinished</c> worked out by the same rules as here, as they were when
/// the player last saw it. <see cref="Collect"/> then copies those fields and nothing else in
/// this piece changes.
/// </para>
/// </remarks>
public static class StructureLooks
{
    /// <summary>Fills a list with every structure the local player is shown, in the order of the match's store.</summary>
    public static void Collect(MatchView view, List<StructureLook> looks)
    {
        looks.Clear();
        var match = view.Match;
        var structures = match.Structures;
        var research = match.Research.State;
        for (var i = 0; i < structures.Count; i++)
        {
            var entity = structures.EntityAt(i);
            var placed = structures.ValueAt(i);
            if (!match.World.Entities.IsAlive(entity)
                || !match.Bodies.TryGet(entity, out var body)
                || match.Data.StructureOf(placed.Type) is not { } def)
            {
                continue;
            }

            looks.Add(new StructureLook(
                entity,
                body.Owner,
                def,
                placed.X,
                placed.Y,
                placed.Size,

                // Nothing stands unfinished until construction (15c) is in: a foundation is then
                // one that the match's foundations hold, and 17b says so in IsFinished.
                IsFinished: true,
                LookTier: def.Track is { } track ? research.Tier(body.Owner, track) : 0,
                Banner: BannerOf(research, body.Owner, entity)));
        }
    }

    // The track in research in the slot a structure gives its owner.
    private static int BannerOf(ResearchState research, int owner, EntityId entity)
    {
        for (var i = 0; i < ResearchState.SlotsPerPlayer; i++)
        {
            ref readonly var slot = ref research.Slot(owner, i);
            if (slot.IsOpen && slot.IsBusy && slot.Holder == entity)
            {
                return slot.Track;
            }
        }

        return StructureLook.NoTrack;
    }
}
