using System.Globalization;
using Sim.Core;
using Sim.Setup;

namespace Tools.Replay;

/// <summary>What the scripts of one recording are started with: the match and the seed of the orders.</summary>
internal sealed class ScriptSetup(Match match, ulong seed)
{
    private readonly HashSet<ulong> _generators = [];
    private readonly Dictionary<Type, object> _shared = [];

    /// <summary>The match the orders are for. A script reads it and never changes it.</summary>
    public Match Match { get; } = match;

    /// <summary>
    /// A random generator for one script: the seed of the orders with a number of the script's
    /// own mixed in, so that every script draws a sequence of its own and the same seed gives the
    /// same orders for the same match. Two scripts that ask with the same number would draw the
    /// same sequence, so the second is refused.
    /// </summary>
    public DeterministicRandom Generator(ulong own)
    {
        if (!_generators.Add(own))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Two scripts ask for the generator {own:x16}. A script that adds a kind of order picks a number no other has."));
        }

        return new DeterministicRandom(seed ^ own);
    }

    /// <summary>
    /// Something that several scripts of this recording use together, made when the first of
    /// them asks. Only <see cref="Scripts.SharedTurns"/> is, and nothing new should be: a
    /// script that shares its draws cannot be added without changing what the others get.
    /// </summary>
    public T Shared<T>(Func<ScriptSetup, T> create)
        where T : class
    {
        if (!_shared.TryGetValue(typeof(T), out var shared))
        {
            shared = create(this);
            _shared.Add(typeof(T), shared);
        }

        return (T)shared;
    }
}
