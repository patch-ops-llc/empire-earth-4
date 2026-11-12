using Sim.Core;
using Sim.Research;
using Sim.Setup;
using Tools.Battles;

namespace Tools.AiMatch;

/// <summary>What one player had at one moment of a headless match.</summary>
/// <param name="Tick">The tick the state was read after.</param>
/// <param name="ArmyValue">The price of its living fighting units, Materials and Energy added up, at its blueprints of the moment.</param>
/// <param name="Materials">Its stockpile of Materials, in whole resources.</param>
/// <param name="Energy">Its stockpile of Energy, in whole resources.</param>
public readonly record struct SeatSample(int Tick, int Workers, int Units, long ArmyValue, int SupplyUsed, int SupplyCap, long Materials, long Energy);

/// <summary>
/// What a headless match notes of one player while it runs, from outside: when it first ordered
/// an attack, what it paid for research on each track, when every track reached the epoch cap,
/// how long it stood at and above its supply cap, what its shells caught and its splash reached,
/// and what it had every ten seconds. It reads the state after each tick and the
/// commands the player gives, and changes nothing.
/// </summary>
/// <remarks>
/// <b>What research cost.</b> A slot holds the price that was paid for the research in it. A slot
/// that holds another research than a tick before has started one, and its price is added to
/// the track. A slot that no longer holds its research, when the track has not reached that
/// tier, was cancelled or lost with its holder, and the price comes off again: both give it back
/// in full (design 1.3). A research that is cancelled and started again within one tick looks
/// like one that went on.
/// </remarks>
public sealed class SeatWatch
{
    /// <summary>The ticks between two samples: ten seconds of game time.</summary>
    public const int SampleEvery = 10 * Sim.SimInfo.TicksPerSecond;

    private readonly Match _match;
    private readonly int _player;
    private readonly (int Track, int Tier, int Materials, int Energy)[] _slots = new (int, int, int, int)[ResearchState.SlotsPerPlayer];
    private readonly long[] _materials;
    private readonly long[] _energy;
    private readonly List<SeatSample> _samples = [];
    private readonly ShellCounter _shells;
    private readonly SplashCounter _splashes;

    public SeatWatch(Match match, int player)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, match.Players.Count);
        _match = match;
        _player = player;
        _materials = new long[match.Data.Tracks.Count];
        _energy = new long[match.Data.Tracks.Count];
        _shells = new ShellCounter(player);
        _splashes = new SplashCounter(player);
        ReadSlots();
    }

    /// <summary>The tick in which the player first gave an attack-move or an attack order, or -1.</summary>
    public int FirstAttackOrderTick { get; private set; } = -1;

    /// <summary>The tick after which every track of the player stood at the epoch cap of the match, or -1.</summary>
    public int EveryTrackAtCapTick { get; private set; } = -1;

    /// <summary>The ticks after which the player used all its supply cap or more, so that it could train nothing.</summary>
    public int TicksAtSupplyCap { get; private set; }

    /// <summary>The ticks after which the player used more supply than its cap.</summary>
    public int TicksAboveSupplyCap { get; private set; }

    /// <summary>The most supply the player used above its cap after any tick.</summary>
    public int MostAboveSupplyCap { get; private set; }

    /// <summary>The Materials paid for research on each track and not given back, in the order of the data.</summary>
    public IReadOnlyList<long> ResearchMaterials => _materials;

    /// <summary>The Energy paid for research on each track and not given back, in the order of the data.</summary>
    public IReadOnlyList<long> ResearchEnergy => _energy;

    /// <summary>The shells the player's units fired that have landed, and how many units each caught.</summary>
    public ShellCount Shells => _shells.Count;

    /// <summary>The hits of the player's units that splash, and how many other units each reached.</summary>
    public SplashCount Splashes => _splashes.Count;

    /// <summary>What the player had after every <see cref="SampleEvery"/> ticks, in order.</summary>
    public IReadOnlyList<SeatSample> Samples => _samples;

    /// <summary>Notes a command the player gives in <paramref name="tick"/>.</summary>
    public void Given(in Command command, int tick)
    {
        if (FirstAttackOrderTick < 0 && command.Type is CommandType.AttackMove or CommandType.Attack)
        {
            FirstAttackOrderTick = tick;
        }
    }

    /// <summary>Reads the state as the tick left it.</summary>
    public void AfterTick()
    {
        var state = _match.Research.State;
        for (var slot = 0; slot < _slots.Length; slot++)
        {
            var before = _slots[slot];
            ref readonly var now = ref state.Slot(_player, slot);
            var same = now.IsBusy && now.Track == before.Track && now.Tier == before.Tier;
            if (before.Tier != 0 && !same && state.Tier(_player, before.Track) < before.Tier)
            {
                _materials[before.Track] -= before.Materials;
                _energy[before.Track] -= before.Energy;
            }

            if (now.IsBusy && !same)
            {
                _materials[now.Track] += now.Materials;
                _energy[now.Track] += now.Energy;
            }
        }

        _shells.AfterTick(_match);
        _splashes.AfterTick(_match);
        ReadSlots();
        ReadTheRest();
    }

    private void ReadSlots()
    {
        var state = _match.Research.State;
        for (var slot = 0; slot < _slots.Length; slot++)
        {
            ref readonly var now = ref state.Slot(_player, slot);
            _slots[slot] = now.IsBusy ? (now.Track, now.Tier, now.Materials, now.Energy) : default;
        }
    }

    private void ReadTheRest()
    {
        var above = _match.Supply.Used(_player) - _match.Supply.Cap(_player);
        TicksAtSupplyCap += above >= 0 ? 1 : 0;
        if (above > 0)
        {
            TicksAboveSupplyCap++;
            MostAboveSupplyCap = Math.Max(MostAboveSupplyCap, above);
        }

        if (EveryTrackAtCapTick < 0)
        {
            var cap = _match.Research.EpochCap;
            var tiers = _match.Research.State.Tiers(_player);
            var all = true;
            foreach (var tier in tiers)
            {
                all &= tier >= cap;
            }

            if (all)
            {
                EveryTrackAtCapTick = _match.World.Tick;
            }
        }

        if (_match.World.Tick % SampleEvery == 0)
        {
            _samples.Add(Sample());
        }
    }

    private SeatSample Sample()
    {
        var blueprints = _match.Research.Tech(_player).Blueprints;
        var units = 0;
        long armyValue = 0;
        for (var i = 0; i < _match.Combatants.Count; i++)
        {
            if (_match.Bodies.Get(_match.Combatants.EntityAt(i)).Owner == _player)
            {
                var chassis = _match.Combatants.ValueAt(i).Chassis;
                units++;
                armyValue += blueprints[chassis].Materials + blueprints[chassis].Energy;
            }
        }

        var workers = 0;
        for (var i = 0; i < _match.Workers.Count; i++)
        {
            workers += _match.Bodies.Get(_match.Workers.EntityAt(i)).Owner == _player ? 1 : 0;
        }

        return new SeatSample(
            _match.World.Tick,
            workers,
            units,
            armyValue,
            _match.Supply.Used(_player),
            _match.Supply.Cap(_player),
            _match.Stockpiles.Materials(_player) / Sim.Economy.Stockpiles.PerResource,
            _match.Stockpiles.Energy(_player) / Sim.Economy.Stockpiles.PerResource);
    }
}
