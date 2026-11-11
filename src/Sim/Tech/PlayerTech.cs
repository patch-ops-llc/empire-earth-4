using Sim.Numerics;

namespace Sim.Tech;

/// <summary>
/// Everything that follows from one player's track tiers. <see cref="TechResolver.Resolve(Sim.Data.GameData, IReadOnlyList{int})"/>
/// makes it, and nothing in it changes afterwards. Lists that go by track are in the order of
/// <see cref="Sim.Data.GameData.Tracks"/>.
/// </summary>
public sealed class PlayerTech
{
    internal PlayerTech(
        IReadOnlyList<int> tiers,
        int epoch,
        int epochCap,
        int supplyCap,
        Fraction incomeMultiplier,
        IReadOnlyList<ResearchOffer> research)
    {
        Tiers = tiers;
        Epoch = epoch;
        EpochCap = epochCap;
        SupplyCap = supplyCap;
        IncomeMultiplier = incomeMultiplier;
        Research = research;
    }

    /// <summary>The tiers this was resolved from, one per track.</summary>
    public IReadOnlyList<int> Tiers { get; }

    public int Epoch { get; }

    /// <summary>The match's epoch cap that <see cref="Research"/> was resolved against.</summary>
    public int EpochCap { get; }

    public int SupplyCap { get; }

    /// <summary>What worker income is multiplied by, exactly.</summary>
    public Fraction IncomeMultiplier { get; }

    /// <summary>The next tier of each track, one entry per track.</summary>
    public IReadOnlyList<ResearchOffer> Research { get; }
}
