using System.Reflection;
using System.Runtime.CompilerServices;
using Sim.Core;

namespace Sim.Tests.Core;

/// <summary>
/// Determinism rule 7: whole-number overflow is an error, not a wrap. Directory.Build.props turns
/// the check on for every project in every build except the two optimized for speed, Release and
/// ExportRelease.
/// </summary>
public class OverflowCheckingTests
{
    [Fact]
    public void ArithmeticThatIsNotMarkedThrowsOnOverflowInAnUnoptimizedBuild()
    {
        var largest = Largest();
        if (IsOptimizedForSpeed(typeof(OverflowCheckingTests).Assembly))
        {
            // The switch is off here on purpose. Fixed and Fraction still check themselves.
            Assert.Equal(int.MinValue, largest + 1);
        }
        else
        {
            Assert.Throws<OverflowException>(() => largest + 1);
        }
    }

    [Fact]
    public void ArithmeticMarkedUncheckedWrapsInEveryBuild()
    {
        var largest = Largest();

        Assert.Equal(int.MinValue, unchecked(largest + 1));
    }

    [Fact]
    public void TheSimulationIsBuiltLikeItsTests()
    {
        // The test above reads the switch in this assembly. It speaks for Sim only while both are
        // built with the same configuration, and so with the same settings.
        Assert.Equal(Configuration(typeof(OverflowCheckingTests).Assembly), Configuration(typeof(World).Assembly));
    }

    private static bool IsOptimizedForSpeed(Assembly assembly) => Configuration(assembly) is "Release" or "ExportRelease";

    private static string Configuration(Assembly assembly)
    {
        var configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
        Assert.False(string.IsNullOrEmpty(configuration));
        return configuration;
    }

    // Kept out of line so that the compiler and the JIT cannot work the sum out in advance.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Largest() => int.MaxValue;
}
