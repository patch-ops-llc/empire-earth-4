using Sim.Core;

namespace Sim.Research;

/// <summary>
/// What an event of research on the event list says (<see cref="Simulation.Events"/>), read out
/// of the <see cref="SimEvent"/> that carries it: the kinds
/// <see cref="SimEventKind.ResearchStarted"/>, <see cref="SimEventKind.ResearchFinished"/>,
/// <see cref="SimEventKind.ResearchCancelled"/>, <see cref="SimEventKind.ResearchLost"/> and
/// <see cref="SimEventKind.EpochChanged"/>. For whoever shows the match: the epoch announcement,
/// sounds, the research panel.
/// </summary>
/// <remarks>
/// A <see cref="SimEvent"/> has one number, and research has three to tell. They share it:
/// the track in the lowest 16 bits of <see cref="SimEvent.Amount"/>, the tier in the next 16 and
/// the epoch in the 16 above those. <see cref="TryRead"/> takes them apart, so that no reader has
/// to know that.
/// </remarks>
/// <param name="Kind">What happened: one of the five kinds above.</param>
/// <param name="Tick">The tick it happened in.</param>
/// <param name="Player">The player whose research it is.</param>
/// <param name="Track">The track of the research. For <see cref="SimEventKind.EpochChanged"/>, the track whose research raised the epoch.</param>
/// <param name="Tier">The tier the research reaches or has reached.</param>
/// <param name="Epoch">The player's epoch after the event.</param>
/// <param name="Holder">
/// The entity the research's slot belongs to, or <see cref="EntityId.None"/> for the slot a player
/// has of their own and for <see cref="SimEventKind.EpochChanged"/>. When a slot is lost with its
/// holder, this is the holder that fell.
/// </param>
public readonly record struct ResearchEvent(SimEventKind Kind, int Tick, int Player, int Track, int Tier, int Epoch, EntityId Holder = default)
{
    private const int BitsPerPart = 16;
    private const long PartMask = (1L << BitsPerPart) - 1;

    /// <summary>Whether a kind of event is one of research's.</summary>
    public static bool IsResearch(SimEventKind kind) =>
        kind is SimEventKind.EpochChanged
            or SimEventKind.ResearchCancelled
            or SimEventKind.ResearchFinished
            or SimEventKind.ResearchLost
            or SimEventKind.ResearchStarted;

    /// <summary>Reads an event of research. False, for an event of any other kind.</summary>
    public static bool TryRead(in SimEvent simEvent, out ResearchEvent research)
    {
        if (!IsResearch(simEvent.Kind))
        {
            research = default;
            return false;
        }

        var amount = simEvent.Amount;
        research = new ResearchEvent(
            simEvent.Kind,
            simEvent.Tick,
            simEvent.Player,
            (int)(amount & PartMask),
            (int)((amount >> BitsPerPart) & PartMask),
            (int)((amount >> (2 * BitsPerPart)) & PartMask),
            simEvent.Entity);
        return true;
    }

    /// <summary>Adds an event of research to the list.</summary>
    internal static void Add(SimEventList events, SimEventKind kind, int player, int track, int tier, int epoch, EntityId holder) =>
        events.Add(kind, player, holder, amount: track + ((long)tier << BitsPerPart) + ((long)epoch << (2 * BitsPerPart)));
}
