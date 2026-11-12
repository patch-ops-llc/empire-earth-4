using Sim.Core;
using Sim.Data;
using Sim.Tech;

namespace Ai;

/// <summary>
/// Research by personality (design 5.5, item 2). For every free slot it starts the research with
/// the highest weight among those it may start and can pay for. A half-price catch-up tier goes
/// before any other. Tracks with the same weight go in the order of the data.
/// </summary>
/// <remarks>
/// The response table (item 4) can put a track before all of that: what a row in force
/// researches goes first, rows in the order of the file, and it starts nothing else while it
/// cannot pay for it, because the table says it researches that track next. A row that has it
/// buy catch-up tiers before anything else puts those before even that, and holds every other
/// research back in the same way.
/// </remarks>
internal sealed class AiResearch
{
    private readonly AiSight _sight;
    private readonly AiPersonalityDef _personality;
    private readonly AiResponses? _responses;
    private readonly List<ResearchOffer> _offers = [];
    private readonly Comparison<ResearchOffer> _byPreference;

    public AiResearch(AiSight sight, AiPersonalityDef personality, AiResponses? responses = null)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(personality);
        _sight = sight;
        _personality = personality;
        _responses = responses;
        _byPreference = (left, right) =>
        {
            var byRank = Rank(left).CompareTo(Rank(right));
            if (byRank != 0)
            {
                return byRank;
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

    /// <summary>The ranks from this one on are those of its personality alone: a catch-up tier, then the weights.</summary>
    private const int FirstOrdinaryRank = 1_000;

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
            else if (Rank(offer) < FirstOrdinaryRank)
            {
                // The response table has it research this next: nothing else is started in its place.
                budget.Hold(price.Materials, price.Energy);
                return;
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

    /// <summary>The place of an offer before its weight is asked: lower goes first.</summary>
    private int Rank(ResearchOffer offer)
    {
        var isCatchUp = offer.Price!.Value.Band == ResearchBand.CatchUp;
        if (_responses is not null)
        {
            if (isCatchUp && _responses.CatchUpFirst)
            {
                return 0;
            }

            for (var i = 0; i < _responses.ResearchFirst.Count; i++)
            {
                if (_responses.ResearchFirst[i] == offer.Track)
                {
                    return 1 + i;
                }
            }
        }

        return isCatchUp ? FirstOrdinaryRank : FirstOrdinaryRank + 1;
    }
}
