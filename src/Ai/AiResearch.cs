using Sim.Core;
using Sim.Data;
using Sim.Tech;

namespace Ai;

/// <summary>
/// Research by personality (design 5.5, item 2). For every free slot it starts the research with
/// the highest weight among those it may start and can pay for. A half-price catch-up tier goes
/// before any other. Tracks with the same weight go in the order of the data.
/// </summary>
internal sealed class AiResearch
{
    private readonly AiSight _sight;
    private readonly AiPersonalityDef _personality;
    private readonly List<ResearchOffer> _offers = [];
    private readonly Comparison<ResearchOffer> _byPreference;

    public AiResearch(AiSight sight, AiPersonalityDef personality)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(personality);
        _sight = sight;
        _personality = personality;
        _byPreference = (left, right) =>
        {
            var leftCatchUp = left.Price!.Value.Band == ResearchBand.CatchUp;
            var rightCatchUp = right.Price!.Value.Band == ResearchBand.CatchUp;
            if (leftCatchUp != rightCatchUp)
            {
                return leftCatchUp ? -1 : 1;
            }

            var byWeight = _personality.TrackWeights[right.Track].CompareTo(_personality.TrackWeights[left.Track]);
            return byWeight != 0 ? byWeight : left.Track.CompareTo(right.Track);
        };
    }

    /// <summary>
    /// True after <see cref="Think"/> when every slot is busy and the stockpile covers the research
    /// it would start next: what it lacks is a slot.
    /// </summary>
    public bool LacksASlot { get; private set; }

    /// <summary>
    /// The researches it would start, most wanted first: every track whose next tier may be
    /// researched and is not in research already.
    /// </summary>
    public IReadOnlyList<ResearchOffer> Offers => _offers;

    public void Think(AiBudget budget, List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(commands);

        _offers.Clear();
        foreach (var offer in _sight.Tech.Research)
        {
            if (offer.IsAvailable && !_sight.IsResearching(offer.Track))
            {
                _offers.Add(offer);
            }
        }

        _offers.Sort(_byPreference);

        // The purchases to earn for: as many researches as it has slots.
        var slots = _sight.OpenResearchSlots;
        for (var i = 0; i < _offers.Count && i < slots; i++)
        {
            var price = _offers[i].Price!.Value;
            budget.Want(price.Materials, price.Energy);
        }

        var free = _sight.FreeResearchSlots;
        LacksASlot = free == 0
            && slots > 0
            && _offers.Count > 0
            && budget.Covers(_offers[0].Price!.Value.Materials, _offers[0].Price!.Value.Energy);

        var cheapestMaterials = int.MaxValue;
        var cheapestEnergy = int.MaxValue;
        foreach (var offer in _offers)
        {
            if (free == 0)
            {
                break;
            }

            var price = offer.Price!.Value;
            if (budget.Covers(price.Materials, price.Energy))
            {
                budget.Pay(price.Materials, price.Energy);
                commands.Add(new Command(CommandType.StartResearch, _sight.Player, contentId: offer.Track));
                free--;
            }
            else
            {
                cheapestMaterials = Math.Min(cheapestMaterials, price.Materials);
                cheapestEnergy = Math.Min(cheapestEnergy, price.Energy);
            }
        }

        // A slot stays free because nothing can be paid: keep the price of the cheapest research
        // out of the hands of everything else.
        if (free > 0 && cheapestMaterials != int.MaxValue)
        {
            budget.Hold(cheapestMaterials, cheapestEnergy);
        }
    }
}
