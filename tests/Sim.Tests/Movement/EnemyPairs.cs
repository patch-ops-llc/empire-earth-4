using Sim.Core;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Setup;

namespace Sim.Tests.Movement;

/// <summary>
/// The end-of-tick check of design 2.4 for the units of two sides, those that have died left out: call
/// <see cref="AssertNoPairCameTooClose"/> after every tick.
/// </summary>
internal sealed class EnemyPairs
{
    private readonly Match _match;
    private readonly EntityId[] _first;
    private readonly EntityId[] _second;
    private readonly long[] _before;

    public EnemyPairs(Match match, EntityId[] first, EntityId[] second)
    {
        _match = match;
        _first = first;
        _second = second;
        _before = new long[first.Length * second.Length];
        Measure((_, _, pair, distance) => _before[pair] = distance);
    }

    public void AssertNoPairCameTooClose()
    {
        Measure((one, other, pair, distance) =>
        {
            var spacing = SpacingRule.Between(_match.Movers.Get(one), _match.Movers.Get(other));
            Assert.True(
                distance >= FixedVector2.SquareRaw(spacing) || distance >= _before[pair],
                $"Tick {_match.World.Tick}: enemies {one} at {_match.PositionOf(one)} and {other} at {_match.PositionOf(other)} came closer than {spacing}.");
            _before[pair] = distance;
        });
    }

    private void Measure(Action<EntityId, EntityId, int, long> each)
    {
        for (var i = 0; i < _first.Length; i++)
        {
            for (var j = 0; j < _second.Length; j++)
            {
                if (_match.Bodies.Contains(_first[i]) && _match.Bodies.Contains(_second[j]))
                {
                    each(_first[i], _second[j], (i * _second.Length) + j, FixedVector2.DistanceSquaredRaw(_match.PositionOf(_first[i]), _match.PositionOf(_second[j])));
                }
            }
        }
    }
}
