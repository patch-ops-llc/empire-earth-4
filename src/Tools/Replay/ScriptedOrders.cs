using System.Reflection;
using Sim.Core;
using Sim.Setup;

namespace Tools.Replay;

/// <summary>
/// Gives the orders of a match that nobody plays, for <c>replay record</c>: every few ticks one of
/// the players sends some of its units somewhere, starts a research or puts workers on a deposit.
/// The orders come from random generators of their own, so the same seed gives the same orders
/// for the same match.
/// </summary>
/// <remarks>
/// Each kind of order is an <see cref="OrderScript"/> in <c>Scripts/</c>, found here by
/// reflection. A recording uses the scripts it names, or all of them. In a tick the scripts add
/// their orders in the order of their names.
/// <para>
/// A milestone that adds a kind of command adds a script for it, with a clock and a generator of
/// its own, and records a new replay; replays recorded before keep their commands. Recording an
/// old replay again would not give its match back even with the scripts it was recorded with,
/// because a script reads the match, and the rules that move the match on have changed since.
/// </para>
/// </remarks>
internal sealed class ScriptedOrders
{
    private readonly List<OrderScript> _scripts = [];

    /// <param name="match">The match the orders are for.</param>
    /// <param name="seed">The seed of the orders.</param>
    /// <param name="scripts">The names of the scripts to use, each once, in any order.</param>
    public ScriptedOrders(Match match, ulong seed, IEnumerable<string> scripts)
    {
        var found = FindScripts();
        var setup = new ScriptSetup(match, seed);
        var named = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in scripts)
        {
            if (!found.ContainsKey(name))
            {
                throw new ArgumentException($"There is no script '{name}'.", nameof(scripts));
            }

            if (!named.Add(name))
            {
                throw new ArgumentException($"The script '{name}' is named twice.", nameof(scripts));
            }
        }

        foreach (var name in named)
        {
            _scripts.Add((OrderScript)Activator.CreateInstance(found[name], setup)!);
        }
    }

    /// <summary>The names of every script in this program, in the order the scripts run in a tick.</summary>
    public static IReadOnlyList<string> ScriptNames() => [.. FindScripts().Keys];

    /// <summary>Adds the commands to schedule for a tick, which is the next to run, in the order they run.</summary>
    public void AddOrdersFor(int tick, List<Command> commands)
    {
        foreach (var script in _scripts)
        {
            script.AddOrdersFor(tick, commands);
        }
    }

    private static SortedDictionary<string, Type> FindScripts()
    {
        var found = new SortedDictionary<string, Type>(StringComparer.Ordinal);
        var types = typeof(ScriptedOrders).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsSubclassOf(typeof(OrderScript)));
        foreach (var type in types)
        {
            var name = type.GetCustomAttribute<OrderScriptAttribute>()?.Name
                ?? throw new InvalidOperationException($"The script {type.Name} has no {nameof(OrderScriptAttribute)} with its name.");
            if (type.GetConstructor([typeof(ScriptSetup)]) is null)
            {
                throw new InvalidOperationException($"The script {type.Name} has no public constructor that takes a {nameof(ScriptSetup)}.");
            }

            if (!found.TryAdd(name, type))
            {
                throw new InvalidOperationException($"Two scripts are named '{name}'.");
            }
        }

        return found;
    }
}
