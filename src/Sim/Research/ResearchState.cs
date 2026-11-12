using Sim.Core;

namespace Sim.Research;

/// <summary>
/// What research has done and is doing, for every player: the tier of each track and the
/// research slots. This is the state; <see cref="MatchResearch"/> changes it by the rules.
/// </summary>
public sealed class ResearchState : IStateStore
{
    public const string StoreName = "research";

    /// <summary>
    /// The most research slots a player can have (design 1.3): the player's own and one for each
    /// of two Academies.
    /// </summary>
    public const int SlotsPerPlayer = 3;

    /// <summary>
    /// The number of the slot that belongs to the player and not to a building: the one a player
    /// has for as long as they have a finished Capitol.
    /// </summary>
    public const int PlayerSlot = 0;

    private readonly int _tierCount;
    private readonly int[] _tiers;
    private readonly ResearchSlot[] _slots;

    /// <param name="players">The number of players in the match.</param>
    /// <param name="tracks">The number of tracks in the data.</param>
    /// <param name="tierCount">The number of tiers every track has.</param>
    /// <param name="startTier">The tier every track of every player starts at.</param>
    public ResearchState(int players, int tracks, int tierCount, int startTier)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(players);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tracks);
        ArgumentOutOfRangeException.ThrowIfLessThan(startTier, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startTier, tierCount);

        PlayerCount = players;
        TrackCount = tracks;
        _tierCount = tierCount;
        _tiers = new int[players * tracks];
        Array.Fill(_tiers, startTier);
        _slots = new ResearchSlot[players * SlotsPerPlayer];
    }

    public string Name => StoreName;

    public int PlayerCount { get; }

    public int TrackCount { get; }

    /// <summary>The tier a player's track is at.</summary>
    public int Tier(int player, int track)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(track, TrackCount);
        return Tiers(player)[track];
    }

    /// <summary>The tiers of a player's tracks, in the order of the data's tracks.</summary>
    public ReadOnlySpan<int> Tiers(int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        return _tiers.AsSpan(player * TrackCount, TrackCount);
    }

    /// <summary>One of a player's slots, from 0 to <see cref="SlotsPerPlayer"/> - 1.</summary>
    public ref readonly ResearchSlot Slot(int player, int slot) => ref SlotRef(player, slot);

    /// <summary>The number of the player's slot that is researching the track, or -1 if none is.</summary>
    public int SlotResearching(int player, int track)
    {
        for (var slot = 0; slot < SlotsPerPlayer; slot++)
        {
            ref readonly var candidate = ref Slot(player, slot);
            if (candidate.IsBusy && candidate.Track == track)
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>The lowest number of a slot the player has open with no research in it, or -1.</summary>
    public int FreeSlot(int player)
    {
        for (var slot = 0; slot < SlotsPerPlayer; slot++)
        {
            if (Slot(player, slot).IsFree)
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>Whether any player has a slot that belongs to this entity.</summary>
    public bool IsHolder(EntityId entity)
    {
        if (entity == EntityId.None)
        {
            return false;
        }

        foreach (ref readonly var slot in _slots.AsSpan())
        {
            if (slot.IsOpen && slot.Holder == entity)
            {
                return true;
            }
        }

        return false;
    }

    public void RemoveEntities(ReadOnlySpan<EntityId> destroyed)
    {
        // A slot whose holder died is closed by the rules, in MatchResearch.Advance, because the
        // research in it is refunded.
    }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        for (var player = 0; player < PlayerCount; player++)
        {
            var tiers = _tiers.AsSpan(player * TrackCount, TrackCount);
            for (var track = 0; track < TrackCount; track++)
            {
                tiers[track] = walker.Field(tiers[track]);
            }

            for (var number = 0; number < SlotsPerPlayer; number++)
            {
                ref var slot = ref SlotRef(player, number);
                slot.IsOpen = walker.Field(slot.IsOpen);
                slot.Holder = walker.Field(slot.Holder);
                slot.Track = walker.Field(slot.Track);
                slot.Tier = walker.Field(slot.Tier);
                slot.TicksLeft = walker.Field(slot.TicksLeft);
                slot.Ticks = walker.Field(slot.Ticks);
                slot.Materials = walker.Field(slot.Materials);
                slot.Energy = walker.Field(slot.Energy);
            }

            if (walker.IsRestoring)
            {
                Check(player);
            }
        }
    }

    internal ref ResearchSlot SlotRef(int player, int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, SlotsPerPlayer);
        return ref _slots[(player * SlotsPerPlayer) + slot];
    }

    internal void SetTier(int player, int track, int tier) => _tiers[(player * TrackCount) + track] = tier;

    /// <summary>Refuses what a snapshot can hold and the rules can never produce.</summary>
    private void Check(int player)
    {
        var tiers = Tiers(player);
        foreach (var tier in tiers)
        {
            if (tier < 1 || tier > _tierCount)
            {
                throw new InvalidDataException($"A track of player {player} is at tier {tier}.");
            }
        }

        for (var number = 0; number < SlotsPerPlayer; number++)
        {
            ref readonly var slot = ref Slot(player, number);
            var sound = slot.IsBusy
                ? slot.IsOpen
                    && slot.Track >= 0 && slot.Track < TrackCount
                    && slot.Tier == tiers[slot.Track] + 1
                    && slot.Ticks >= 1 && slot.TicksLeft >= 1 && slot.TicksLeft <= slot.Ticks
                    && slot.Materials >= 0 && slot.Energy >= 0
                    && SlotResearching(player, slot.Track) == number
                : slot.Track == 0 && slot.TicksLeft == 0 && slot.Ticks == 0 && slot.Materials == 0 && slot.Energy == 0;
            if (!sound
                || (!slot.IsOpen && slot.Holder != EntityId.None)
                || (number == PlayerSlot && slot.Holder != EntityId.None))
            {
                throw new InvalidDataException($"Research slot {number} of player {player} holds values the rules cannot give it.");
            }
        }
    }
}
