using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Vision;

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
/// The layers that draw them ask here and nowhere else.
/// </summary>
/// <remarks>
/// The list comes from the local player's sight (17b), as the structure layer's does: its
/// <c>CollectStructures</c> gives the player's own structures, those of others seen now and
/// those remembered, and each <c>SeenStructure</c> carries <c>LookTier</c>, <c>Banner</c> and
/// <c>IsFinished</c> as they were when the player last saw it. A structure the player has
/// never seen is not listed, so nothing of it is drawn over the dark.
/// </remarks>
public static class StructureLooks
{
    private static readonly List<SeenStructure> Known = [];

    /// <summary>Fills a list with every structure the local player is shown, in ascending ID order.</summary>
    public static void Collect(MatchView view, List<StructureLook> looks)
    {
        looks.Clear();
        view.Sight.CollectStructures(Known);
        foreach (var seen in Known)
        {
            if (view.Match.Data.StructureOf(seen.Type) is { } def)
            {
                looks.Add(new StructureLook(seen.Entity, seen.Owner, def, seen.X, seen.Y, seen.Size, seen.IsFinished, seen.LookTier, seen.Banner));
            }
        }
    }
}
