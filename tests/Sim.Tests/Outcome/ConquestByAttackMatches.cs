using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Outcome;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Construction;
using Sim.Tests.Data;
using Sim.Tests.Map;

namespace Sim.Tests.Outcome;

/// <summary>
/// Matches for the tests of conquest by attack: the data of <see cref="SiegeMatches"/>, which
/// has the design's numbers for units and structures, with an Outpost, on open ground with three
/// starts far apart. Every player starts as design 5.2 says: a Capitol of 4 by 4 tiles with its
/// lowest tile on the start, and six workers in the row of tiles above it.
/// </summary>
internal static class ConquestByAttackMatches
{
    public const string Outpost = "outpost";

    private static readonly Lazy<GameData> Loaded = new(() =>
        DataAssert.Valid(SiegeMatches.Files().Set("structures/outpost.jsonc", ConstructionMatches.Outpost)));

    public static GameData Data => Loaded.Value;

    public static MapDef Map => TestMaps.Open(64, 40) with { Starts = [new(8, 18), new(52, 18), new(30, 10)] };

    public static Match WithStarts(int players = 2) =>
        MatchSetup.Create(Data, Map, 1, [.. Enumerable.Range(0, players).Select(player => new PlayerSetup(player))]);

    /// <summary>
    /// A row of units below a structure, so many tiles from its lower edge, one tile apart and
    /// centered on it. Nothing of a start stands there: the workers are in the row above.
    /// </summary>
    public static EntityId[] Besiegers(this Match match, int player, string chassis, int count, EntityId structure, string distance)
    {
        var tiles = match.Structures.Get(structure).Tiles;
        var index = match.Data.Chassis.Single(candidate => candidate.Id == chassis).Index;
        var y = Fixed.FromInt(tiles.Y) - Fraction.Parse(distance).ToFixed();
        var first = Fixed.FromInt(tiles.X) + (Fixed.FromInt(tiles.Size - count) / 2) + (Fixed.One / 2);
        return [.. Enumerable.Range(0, count).Select(i => match.SpawnChassis(player, index, new FixedVector2(first + Fixed.FromInt(i), y)))];
    }

    /// <summary>
    /// Runs the match until a structure is destroyed and returns what the tick of its death
    /// reported. Up to that tick the damage of every hit on it is added up, and the structure
    /// must fall in the very tick in which the sum reaches its hit points: no sooner, and not a
    /// tick later.
    /// </summary>
    public static Fall RunUntilItFalls(this Match match, EntityId structure, int limit = 6000)
    {
        var left = match.Health.Get(structure).HitPoints;
        var hits = 0;
        for (var i = 0; i < limit; i++)
        {
            var standingsBefore = match.Standings.All();
            match.Simulation.Tick();
            var events = match.Simulation.Events.ToArray();
            foreach (var reported in events)
            {
                if (reported.Entity == structure && reported.Kind is SimEventKind.MeleeHit or SimEventKind.ShotHit)
                {
                    left -= reported.Amount;
                    hits++;
                }
            }

            var died = events.Any(reported => reported.Kind == SimEventKind.Death && reported.Entity == structure);
            Assert.True(died == (left <= 0), $"In tick {match.World.Tick - 1} the hits had left the structure {left} thousandths of a hit point, and it {(died ? "fell" : "stood")}.");
            if (died)
            {
                return new Fall(match.World.Tick - 1, events, standingsBefore, hits);
            }
        }

        throw new Xunit.Sdk.XunitException($"The structure still stands after {limit} ticks, with {left} thousandths of a hit point by the count of the hits.");
    }
}

/// <summary>The tick in which a structure was destroyed.</summary>
/// <param name="Tick">The number of the tick.</param>
/// <param name="Events">Everything the tick reported, in order.</param>
/// <param name="StandingsBefore">Where every player stood before the tick.</param>
/// <param name="Hits">The hits it took in all.</param>
internal sealed record Fall(int Tick, SimEvent[] Events, PlayerStanding[] StandingsBefore, int Hits)
{
    /// <summary>
    /// A structure whose hit points ran out is a <see cref="SimEventKind.Death"/> first and a
    /// <see cref="SimEventKind.StructureRemoved"/> after it, once each, in this tick.
    /// </summary>
    public void AssertDeathThenRemoved(EntityId structure, int owner)
    {
        var about = Events.Where(reported => reported.Entity == structure && reported.Kind is SimEventKind.Death or SimEventKind.StructureRemoved).ToArray();
        Assert.Equal([SimEventKind.Death, SimEventKind.StructureRemoved], about.Select(reported => reported.Kind));
        Assert.All(about, reported => Assert.Equal((Tick, owner), (reported.Tick, reported.Player)));
    }
}
