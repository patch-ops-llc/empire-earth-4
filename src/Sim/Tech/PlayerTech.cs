using Sim.Data;
using Sim.Numerics;

namespace Sim.Tech;

/// <summary>
/// Everything that follows from one player's track tiers. <see cref="TechResolver.Resolve(GameData, IReadOnlyList{int})"/>
/// makes it, and nothing in it changes afterwards. Lists that go by track are in the order of
/// <see cref="GameData.Tracks"/>.
/// </summary>
public sealed class PlayerTech
{
    internal PlayerTech(
        IReadOnlyList<int> tiers,
        int epoch,
        int epochCap,
        int supplyCap,
        Fraction incomeMultiplier,
        IReadOnlyList<ResearchOffer> research,
        IReadOnlyList<ChassisBlueprint> blueprints)
    {
        Tiers = tiers;
        Epoch = epoch;
        EpochCap = epochCap;
        SupplyCap = supplyCap;
        IncomeMultiplier = incomeMultiplier;
        Research = research;
        Blueprints = blueprints;
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

    /// <summary>
    /// One blueprint for every chassis in the data, unlocked or not, in the order of
    /// <see cref="GameData.Chassis"/>: the blueprint of a chassis is at its <see cref="ChassisDef.Index"/>.
    /// </summary>
    public IReadOnlyList<ChassisBlueprint> Blueprints { get; }
}
