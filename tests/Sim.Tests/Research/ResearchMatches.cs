using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Research;
using Sim.Setup;
using Sim.Tech;
using Sim.Tests.Data;
using Sim.Tests.Map;
using Sim.Tests.Tech;

namespace Sim.Tests.Research;

/// <summary>
/// Matches for the research tests: built by the real match setup with the design's nine tiers and
/// prices, for players who start with nothing, each of whom is then given the slot of their own
/// and a full stockpile.
/// </summary>
internal static class ResearchMatches
{
    public const int Plenty = 10_000;

    /// <summary>The design's price of tier 2 at the frontier: 100 Materials, 100 Energy, 45 seconds.</summary>
    public const int TicksToTier2 = 900;

    public static int Firepower { get; } = DesignData.Track("firepower");

    public static int Protection { get; } = DesignData.Track("protection");

    public static int Mobility { get; } = DesignData.Track("mobility");

    public static int Manufacturing { get; } = DesignData.Track("manufacturing");

    public static int Logistics { get; } = DesignData.Track("logistics");

    public static int Extraction { get; } = DesignData.Track("extraction");

    /// <summary>The design's data with the first playable's epoch cap of 4.</summary>
    public static GameData CappedData { get; } =
        DataAssert.Valid(DesignData.Files().Edit(DesignData.RulesPath, "\"epoch_cap\": 9", "\"epoch_cap\": 4"));

    public static Match Create(int players = 2, GameData? data = null)
    {
        var match = MatchSetup.Create(data ?? DesignData.Data, TestMaps.Open(24, 24), 1, [.. Enumerable.Repeat(PlayerSetup.WithoutStart, players)]);
        for (var player = 0; player < players; player++)
        {
            match.Research.OpenPlayerSlot(player);
            match.Stockpiles.Add(player, Plenty, Plenty);
        }

        return match;
    }

    /// <summary>Queues the order to research the next tier of a track, for the next tick to run.</summary>
    public static void OrderResearch(this Match match, int player, int track) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.StartResearch, player, contentId: track));

    /// <summary>Queues the order to cancel the research of a track, for the next tick to run.</summary>
    public static void OrderCancel(this Match match, int player, int track) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.CancelResearch, player, contentId: track));

    /// <summary>Everything the match reports about research from now on, in order.</summary>
    public static List<ResearchEvent> Events(this Match match)
    {
        var events = new List<ResearchEvent>();
        match.Research.Reported += events.Add;
        return events;
    }

    /// <summary>A player's stockpile in whole resources.</summary>
    public static (long Materials, long Energy) Stock(this Match match, int player)
    {
        var (materials, energy) = (match.Stockpiles.Materials(player), match.Stockpiles.Energy(player));
        Assert.Equal((0, 0), (materials % 1000, energy % 1000));
        return (materials / 1000, energy / 1000);
    }

    /// <summary>Something that stands on the map and can hold a research slot, as an Academy will.</summary>
    public static EntityId PlaceHolder(this Match match, int player, int tileX = 3, int tileY = 3) =>
        match.SpawnUnit(player, MapGrid.TileCenter(tileX, tileY), Fixed.Zero);

    public static ResearchSlot Slot(this Match match, int player, int slot = 0) => match.Research.State.Slot(player, slot);
}

/// <summary>Compares what follows from two players' tiers, value by value.</summary>
internal static class TechAssert
{
    public static void Same(PlayerTech expected, PlayerTech actual)
    {
        Assert.Equal(expected.Tiers, actual.Tiers);
        Assert.Equal((expected.Epoch, expected.EpochCap, expected.SupplyCap), (actual.Epoch, actual.EpochCap, actual.SupplyCap));
        Assert.Equal(expected.IncomeMultiplier, actual.IncomeMultiplier);
        Assert.Equal(expected.Research, actual.Research);
        Assert.Equal(expected.Blueprints.Count, actual.Blueprints.Count);
        for (var chassis = 0; chassis < expected.Blueprints.Count; chassis++)
        {
            var (one, other) = (expected.Blueprints[chassis], actual.Blueprints[chassis]);
            Assert.Equal(one.Chassis, other.Chassis);
            Assert.Equal(one.Unlocked, other.Unlocked);
            Assert.Equal(one.EffectiveTiers, other.EffectiveTiers);
            Assert.Equal(one.Accents, other.Accents);
            Assert.Equal(
                (one.HitPoints, one.DamagePerSecond, one.DamagePerHit, one.SpeedPerTick, one.Range, one.BlastRadius),
                (other.HitPoints, other.DamagePerSecond, other.DamagePerHit, other.SpeedPerTick, other.Range, other.BlastRadius));
            Assert.Equal(
                (one.Sight, one.Supply, one.Materials, one.Energy, one.BuildTicks, one.BatchSize, one.BatchBuildTicks),
                (other.Sight, other.Supply, other.Materials, other.Energy, other.BuildTicks, other.BatchSize, other.BatchBuildTicks));
        }
    }
}

/// <summary>
/// A walker that restores a store to the values it has, except for one field, which gets another
/// value: what a damaged snapshot does to a store.
/// </summary>
/// <param name="field">The text of the field as the store hands it over, such as <c>slot.Tier</c>.</param>
/// <param name="occurrence">Which of the fields with that text, counted from 0.</param>
/// <param name="replacement">The value to give it. For a <see cref="bool"/>, anything but 0 is true.</param>
internal sealed class ChangingWalker(string field, int occurrence, long replacement) : IStateWalker
{
    private int _seen;

    public bool IsRestoring => true;

    /// <summary>Whether the field was reached, so that a test does not pass by naming a field that is not there.</summary>
    public bool Changed { get; private set; }

    public bool Field(bool value, string name = "") => IsTheOne(name) ? replacement != 0 : value;

    public int Field(int value, string name = "") => IsTheOne(name) ? checked((int)replacement) : value;

    public uint Field(uint value, string name = "") => value;

    public long Field(long value, string name = "") => IsTheOne(name) ? replacement : value;

    public ulong Field(ulong value, string name = "") => value;

    public Int128 Field(Int128 value, string name = "") => value;

    public EntityId Field(EntityId value, string name = "") => IsTheOne(name) ? new EntityId(checked((int)replacement)) : value;

    public Fixed Field(Fixed value, string name = "") => value;

    public Angle Field(Angle value, string name = "") => value;

    public Fraction Field(Fraction value, string name = "") => value;

    public int Count(int count, int minimumBytesPerItem = 1, string name = "") => count;

    private bool IsTheOne(string name)
    {
        if (name != field || _seen++ != occurrence)
        {
            return false;
        }

        Changed = true;
        return true;
    }
}
