using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Tech;

namespace Sim.Research;

/// <summary>
/// Research in a match (design 1.3 and 1.4): what each player's tiers are, what their slots are
/// doing, and what follows from the tiers. It starts, cancels and finishes research by the rules,
/// and keeps the table of <see cref="PlayerTech"/> per player that every other system reads a
/// player's blueprints, epoch, supply cap and income from.
/// </summary>
/// <remarks>
/// <para>
/// The state is in <see cref="State"/> and in the stockpiles. The table is worked out from the
/// tiers and is not state: <see cref="RefreshTech"/> renews it, and
/// <see cref="Setup.Match.RefreshDerivedState"/> calls that.
/// </para>
/// <para>
/// The price, the reach rule and the epoch cap are applied once, when a research starts. A
/// research in progress is never stopped or repriced by anything that happens later, except that
/// losing its slot cancels it with a full refund.
/// </para>
/// <para>
/// A slot holds one research at a time and there is no queue: an order to research while no slot
/// is free is dropped, and the player gives it again when one is.
/// </para>
/// </remarks>
public sealed class MatchResearch : IPlayerTechSource
{
    private readonly GameData _data;
    private readonly World _world;
    private readonly Stockpiles _stockpiles;
    private readonly PlayerTech[] _tech;
    private readonly List<ITechChangeHandler> _handlers = [];

    /// <param name="data">The loaded game data.</param>
    /// <param name="world">The world of the match.</param>
    /// <param name="state">The store with the tiers and the slots, already registered with the world.</param>
    /// <param name="stockpiles">The store prices are paid from and refunded to.</param>
    /// <param name="epochCap">The match's epoch cap: no track may pass it.</param>
    public MatchResearch(GameData data, World world, ResearchState state, Stockpiles stockpiles, int epochCap)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stockpiles);
        if (state.TrackCount != data.Tracks.Count || stockpiles.PlayerCount != state.PlayerCount)
        {
            throw new ArgumentException("The research state, the stockpiles and the data do not belong together.", nameof(state));
        }

        _data = data;
        _world = world;
        State = state;
        _stockpiles = stockpiles;
        EpochCap = epochCap;
        _tech = new PlayerTech[state.PlayerCount];
        for (var player = 0; player < _tech.Length; player++)
        {
            _tech[player] = Resolve(player);
        }
    }

    /// <summary>
    /// Raised inside a tick for everything that happens to a research, and for a change of epoch.
    /// It is for showing the match. A listener must not change the match, and nothing in the
    /// simulation may depend on whether anyone listens.
    /// </summary>
    public event Action<ResearchEvent>? Reported;

    /// <summary>The tiers and the slots of every player.</summary>
    public ResearchState State { get; }

    /// <summary>The match's epoch cap: no track may pass it.</summary>
    public int EpochCap { get; }

    /// <summary>
    /// Everything that follows from a player's tiers as they are now: epoch, supply cap, income
    /// multiplier, the next research of each track with its price, and a blueprint for every
    /// chassis. The object is replaced, never changed, when one of the player's researches finishes.
    /// </summary>
    public PlayerTech Tech(int player) => _tech[player];

    /// <summary>
    /// Adds a part of the simulation that must act when a research finishes. Only before the first
    /// tick, and on every machine in the same order.
    /// </summary>
    public void AddTechChangeHandler(ITechChangeHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_world.Tick != 0)
        {
            throw new InvalidOperationException("Handlers can only be added before the first tick.");
        }

        _handlers.Add(handler);
    }

    /// <summary>
    /// Whether the player may start researching the next tier of the track now. The research panel
    /// asks this to show a button as it is, and <see cref="CommandType.StartResearch"/> is carried
    /// out exactly when the answer is <see cref="ResearchStartCheck.Allowed"/>.
    /// </summary>
    public ResearchStartCheck CheckStart(int player, int track)
    {
        if (player < 0 || player >= State.PlayerCount)
        {
            return ResearchStartCheck.UnknownPlayer;
        }

        if (track < 0 || track >= State.TrackCount)
        {
            return ResearchStartCheck.UnknownTrack;
        }

        if (State.SlotResearching(player, track) >= 0)
        {
            return ResearchStartCheck.InProgress;
        }

        var offer = _tech[player].Research[track];
        switch (offer.Status)
        {
            case ResearchStatus.AtTopTier:
                return ResearchStartCheck.AtTopTier;
            case ResearchStatus.AboveEpochCap:
                return ResearchStartCheck.AboveEpochCap;
            case ResearchStatus.BeyondReach:
                return ResearchStartCheck.BeyondReach;
        }

        if (State.FreeSlot(player) < 0)
        {
            return ResearchStartCheck.NoFreeSlot;
        }

        var price = offer.Price!.Value;
        return _stockpiles.Covers(player, price.Materials, price.Energy)
            ? ResearchStartCheck.Allowed
            : ResearchStartCheck.TooExpensive;
    }

    /// <summary>
    /// Opens the slot a player has of their own, the one design 1.3 gives for as long as the
    /// player has a finished Capitol. Does nothing if it is open. Call it before the first tick,
    /// or inside a tick from a system or a command handler.
    /// </summary>
    public void OpenPlayerSlot(int player) => State.SlotRef(player, ResearchState.PlayerSlot).IsOpen = true;

    /// <summary>
    /// Closes the slot a player has of their own, as when their last Capitol falls: a research in
    /// it is cancelled and its price given back. Does nothing if it is not open. Call it inside a
    /// tick from a system or a command handler, or before the first tick.
    /// </summary>
    public void ClosePlayerSlot(int player) => Close(player, ResearchState.PlayerSlot);

    /// <summary>
    /// Opens a slot that belongs to an entity, such as a finished Academy. The slot closes by
    /// itself in the first tick in which the entity is no longer alive, and a research in it is
    /// then cancelled with its price given back. Call it before the first tick, or inside a tick
    /// from a system or a command handler.
    /// </summary>
    /// <returns>
    /// False, having changed nothing, when the player already has
    /// <see cref="ResearchState.SlotsPerPlayer"/> - 1 such slots. Design 2.5 refuses a third
    /// Academy when it is placed, counting those under way, and that check belongs to whatever
    /// places structures; with it in place this never returns false.
    /// </returns>
    public bool OpenSlot(int player, EntityId holder)
    {
        if (!_world.Entities.IsAlive(holder))
        {
            throw new ArgumentException($"Entity {holder} is not alive.", nameof(holder));
        }

        if (State.IsHolder(holder))
        {
            throw new ArgumentException($"Entity {holder} already holds a research slot.", nameof(holder));
        }

        for (var number = ResearchState.PlayerSlot + 1; number < ResearchState.SlotsPerPlayer; number++)
        {
            ref var slot = ref State.SlotRef(player, number);
            if (!slot.IsOpen)
            {
                slot.IsOpen = true;
                slot.Holder = holder;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gives a player other tiers than the match started them with, one for each track. For a
    /// fixture that sets up armies of a given mix, and for a start epoch other than the data's.
    /// Call it before the first tick, and while the player has no research in progress: nothing
    /// is told of the change, so what already stands on the map keeps the values it has.
    /// </summary>
    public void SetTiers(int player, IReadOnlyList<int> tiers)
    {
        TechRules.CheckTiers(_data, tiers);
        for (var slot = 0; slot < ResearchState.SlotsPerPlayer; slot++)
        {
            if (State.Slot(player, slot).IsBusy)
            {
                throw new InvalidOperationException($"Player {player} has a research in progress.");
            }
        }

        for (var track = 0; track < tiers.Count; track++)
        {
            State.SetTier(player, track, tiers[track]);
        }

        _tech[player] = Resolve(player);
    }

    /// <summary>
    /// Works the table out again for every player whose tiers are not the ones it was made from.
    /// Needed after the state was replaced from outside a tick, as by restoring a snapshot.
    /// </summary>
    public void RefreshTech()
    {
        for (var player = 0; player < _tech.Length; player++)
        {
            if (!IsMadeFrom(_tech[player], State.Tiers(player)))
            {
                _tech[player] = Resolve(player);
            }
        }
    }

    /// <summary>Carries out <see cref="CommandType.StartResearch"/>. False, with nothing changed, when it may not start.</summary>
    internal bool Start(int player, int track)
    {
        if (CheckStart(player, track) != ResearchStartCheck.Allowed)
        {
            return false;
        }

        var offer = _tech[player].Research[track];
        var price = offer.Price!.Value;
        _stockpiles.TryPay(player, price.Materials, price.Energy);

        ref var slot = ref State.SlotRef(player, State.FreeSlot(player));
        slot.Track = track;
        slot.Tier = offer.Tier;
        slot.Ticks = price.Ticks;
        slot.TicksLeft = price.Ticks;
        slot.Materials = price.Materials;
        slot.Energy = price.Energy;

        Report(ResearchEventKind.Started, player, track, offer.Tier);
        return true;
    }

    /// <summary>Carries out <see cref="CommandType.CancelResearch"/>. False, with nothing changed, when the track is not in research.</summary>
    internal bool Cancel(int player, int track)
    {
        if (player < 0 || player >= State.PlayerCount || track < 0 || track >= State.TrackCount)
        {
            return false;
        }

        var number = State.SlotResearching(player, track);
        if (number < 0)
        {
            return false;
        }

        ref var slot = ref State.SlotRef(player, number);
        var tier = slot.Tier;
        Refund(player, ref slot);
        Report(ResearchEventKind.Cancelled, player, track, tier);
        return true;
    }

    /// <summary>
    /// One tick of research, for every player in order and every slot in order: a slot whose
    /// holder is no longer alive closes, and a research that is still there is one tick further.
    /// </summary>
    internal void Advance()
    {
        for (var player = 0; player < State.PlayerCount; player++)
        {
            for (var number = 0; number < ResearchState.SlotsPerPlayer; number++)
            {
                ref var slot = ref State.SlotRef(player, number);
                if (!slot.IsOpen)
                {
                    continue;
                }

                if (slot.Holder != EntityId.None && !_world.Entities.IsAlive(slot.Holder))
                {
                    Close(player, number);
                    continue;
                }

                if (slot.IsBusy && --slot.TicksLeft == 0)
                {
                    Finish(player, ref slot);
                }
            }
        }
    }

    private void Finish(int player, ref ResearchSlot slot)
    {
        var track = slot.Track;
        var tier = slot.Tier;
        Empty(ref slot);
        State.SetTier(player, track, tier);

        var before = _tech[player];
        var after = Resolve(player);
        _tech[player] = after;
        foreach (var handler in _handlers)
        {
            handler.TechChanged(_world, player, before, after);
        }

        Report(ResearchEventKind.Finished, player, track, tier);
        if (after.Epoch != before.Epoch)
        {
            Report(ResearchEventKind.EpochChanged, player, track, tier);
        }
    }

    private void Close(int player, int number)
    {
        ref var slot = ref State.SlotRef(player, number);
        if (slot.IsBusy)
        {
            var track = slot.Track;
            var tier = slot.Tier;
            Refund(player, ref slot);
            Report(ResearchEventKind.Lost, player, track, tier);
        }

        slot.IsOpen = false;
        slot.Holder = EntityId.None;
    }

    private void Refund(int player, ref ResearchSlot slot)
    {
        _stockpiles.Add(player, slot.Materials, slot.Energy);
        Empty(ref slot);
    }

    private static void Empty(ref ResearchSlot slot)
    {
        slot.Track = 0;
        slot.Tier = 0;
        slot.TicksLeft = 0;
        slot.Ticks = 0;
        slot.Materials = 0;
        slot.Energy = 0;
    }

    private static bool IsMadeFrom(PlayerTech tech, ReadOnlySpan<int> tiers)
    {
        for (var track = 0; track < tiers.Length; track++)
        {
            if (tech.Tiers[track] != tiers[track])
            {
                return false;
            }
        }

        return true;
    }

    private PlayerTech Resolve(int player) => TechResolver.Resolve(_data, State.Tiers(player).ToArray(), EpochCap);

    private void Report(ResearchEventKind kind, int player, int track, int tier) =>
        Reported?.Invoke(new ResearchEvent(kind, _world.Tick, player, track, tier, _tech[player].Epoch));
}
