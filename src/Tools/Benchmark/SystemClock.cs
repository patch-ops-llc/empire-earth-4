using System.Diagnostics;
using System.Reflection;
using Sim.Core;

namespace Tools.Benchmark;

/// <summary>
/// Times each system of a simulation by itself, so that the benchmark can say where the time of
/// a tick goes. It is put on a simulation before its first tick and takes the place of every
/// system in the simulation's list with one that runs the system between two readings of the
/// clock.
/// </summary>
/// <remarks>
/// The simulation has no way in for this, and is meant to have none: nothing inside a tick may
/// read a clock, and a hook for one would be there for every caller. So the list of systems is
/// reached by its name, here and nowhere else. If the simulation's field is renamed,
/// <see cref="Attach"/> says so, and the benchmark's tests fail.
/// </remarks>
internal sealed class SystemClock
{
    private const string SystemsField = "_systems";

    private readonly long[] _elapsed;

    private SystemClock(IReadOnlyList<string> names)
    {
        Names = names;
        _elapsed = new long[names.Count];
    }

    /// <summary>The name of each system, in the order they run.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>What each system took in the tick that ran last, in the clock's units.</summary>
    public ReadOnlySpan<long> Elapsed => _elapsed;

    /// <summary>The reading of the clock when the first system of the last tick began: the commands of the tick ran before it.</summary>
    public long FirstSystemBegan { get; private set; }

    public static SystemClock Attach(Simulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (typeof(Simulation).GetField(SystemsField, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(simulation) is not List<ISystem> systems)
        {
            throw new InvalidOperationException($"The simulation no longer keeps its systems in a list named {SystemsField}; the benchmark's clock has to follow it.");
        }

        var clock = new SystemClock([.. systems.Select(system => system.GetType().Name)]);
        for (var i = 0; i < systems.Count; i++)
        {
            systems[i] = new Timed(clock, i, systems[i]);
        }

        return clock;
    }

    private sealed class Timed(SystemClock clock, int index, ISystem system) : ISystem
    {
        public void Run(World world)
        {
            var before = Stopwatch.GetTimestamp();
            if (index == 0)
            {
                clock.FirstSystemBegan = before;
            }

            system.Run(world);
            clock._elapsed[index] = Stopwatch.GetTimestamp() - before;
        }
    }
}
