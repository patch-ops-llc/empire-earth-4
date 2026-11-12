using Sim.Core;

namespace Sim.Research;

/// <summary>
/// One research slot of a player (design 1.3). A slot is open while the player has what gives it:
/// the first while they have a finished Capitol, each other while its Academy stands. An open slot
/// is either free or busy raising one track by one tier.
/// </summary>
public struct ResearchSlot
{
    /// <summary>Whether the player has this slot. A slot that is not open holds nothing.</summary>
    public bool IsOpen { get; internal set; }

    /// <summary>
    /// The entity the slot belongs to, such as an Academy: the slot closes when it dies.
    /// <see cref="EntityId.None"/> for the player's own slot and for a slot that is not open.
    /// </summary>
    public EntityId Holder { get; internal set; }

    /// <summary>The track in research, as an index into the data's tracks. 0 while the slot is free.</summary>
    public int Track { get; internal set; }

    /// <summary>The tier the research reaches, or 0 while the slot is free.</summary>
    public int Tier { get; internal set; }

    /// <summary>The ticks the research still takes. It finishes in the tick this reaches 0.</summary>
    public int TicksLeft { get; internal set; }

    /// <summary>The ticks the research takes in all, as priced when it started.</summary>
    public int Ticks { get; internal set; }

    /// <summary>The Materials paid when the research started, which cancelling gives back.</summary>
    public int Materials { get; internal set; }

    /// <summary>The Energy paid when the research started, which cancelling gives back.</summary>
    public int Energy { get; internal set; }

    public readonly bool IsBusy => Tier != 0;

    /// <summary>Open and with no research in it.</summary>
    public readonly bool IsFree => IsOpen && Tier == 0;
}
