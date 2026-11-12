using Sim.Data;
using Sim.Economy;
using Sim.Research;
using Sim.Tech;

namespace Game;

/// <summary>
/// The local player's research as the match has it now, read for the grid: tiers, slots, what the
/// stockpile covers, and the preview of each track's next tier. It reads and never changes.
/// </summary>
public sealed class ResearchReading
{
    private readonly MatchView _view;
    private readonly ResearchPreview?[] _previews;
    private PlayerTech? _previewsOf;

    public ResearchReading(MatchView view)
    {
        _view = view;
        _previews = new ResearchPreview?[view.Match.Data.Tracks.Count];
    }

    public GameData Data => _view.Match.Data;

    public int Player => _view.Player;

    public int TrackCount => Data.Tracks.Count;

    /// <summary>The tick the match has run to.</summary>
    public int Tick => _view.Match.World.Tick;

    /// <summary>Everything that follows from the player's tiers. Replaced, never changed, when a research finishes.</summary>
    public PlayerTech Tech => _view.Match.Research.Tech(Player);

    public int Epoch => Tech.Epoch;

    public int EpochCap => Tech.EpochCap;

    /// <summary>The player's Materials, in whole resources, as a price is compared with them.</summary>
    public long Materials => _view.Match.Stockpiles.Materials(Player) / Stockpiles.PerResource;

    /// <summary>The player's Energy, in whole resources.</summary>
    public long Energy => _view.Match.Stockpiles.Energy(Player) / Stockpiles.PerResource;

    /// <summary>The tier a track is at.</summary>
    public int Tier(int track) => _view.Match.Research.State.Tier(Player, track);

    /// <summary>Whether the next tier of a track may be started now, and the first reason if not.</summary>
    public ResearchStartCheck Check(int track) => _view.Match.Research.CheckStart(Player, track);

    /// <summary>One of the player's slots, from 0 to <see cref="ResearchState.SlotsPerPlayer"/> - 1.</summary>
    public ResearchSlot Slot(int number) => _view.Match.Research.State.Slot(Player, number);

    /// <summary>The slot that is researching a track, if one is.</summary>
    public bool TryGetSlot(int track, out ResearchSlot slot)
    {
        var number = _view.Match.Research.State.SlotResearching(Player, track);
        slot = number >= 0 ? Slot(number) : default;
        return number >= 0;
    }

    /// <summary>The number of slots the player has, busy or free.</summary>
    public int OpenSlots()
    {
        var open = 0;
        for (var number = 0; number < ResearchState.SlotsPerPlayer; number++)
        {
            open += Slot(number).IsOpen ? 1 : 0;
        }

        return open;
    }

    /// <summary>
    /// What the next tier of a track would change, or null at the top tier. The resolver works the
    /// whole table out twice for it, so it is kept until the player's tiers change.
    /// </summary>
    public ResearchPreview? Preview(int track)
    {
        var tech = Tech;
        if (!ReferenceEquals(_previewsOf, tech))
        {
            Array.Clear(_previews);
            _previewsOf = tech;
        }

        return _previews[track] ??= TechResolver.Preview(Data, tech.Tiers, track, tech.EpochCap);
    }
}
