using System.Globalization;

namespace Sim.Tests;

public class SimAssemblyTests
{
    [Fact]
    public void SimReferencesOnlyTheDotNetBaseLibraries()
    {
        var references = typeof(SimInfo).Assembly.GetReferencedAssemblies();

        Assert.NotEmpty(references);
        Assert.All(references, reference =>
            Assert.True(
                reference.Name!.StartsWith("System.", StringComparison.Ordinal),
                $"Sim must not depend on '{reference.Name}'. It may only use the .NET base libraries, never Godot."));
    }

    /// <summary>
    /// Determinism rule 1. Floating-point results can differ between processors and operating
    /// systems, and one differing bit is a desync. The scan reads the compiled assembly, so it also
    /// sees what the compiler generated and what a method only uses in passing.
    /// </summary>
    [Fact]
    public void SimUsesNoFloatingPointTypes()
    {
        var scan = AssemblyScan.Run(typeof(SimInfo).Assembly, AssemblyScan.FloatingPointTypes, reportFloatingPointInstructions: true);

        Assert.True(scan.MethodBodies >= 50, $"The scan read only {scan.MethodBodies} method bodies of Sim, so it proves nothing.");
        Assert.True(
            scan.Findings.Count == 0,
            "Sim must not use float, double, decimal or any other floating-point type (docs/determinism.md, rule 1). "
            + "Use Sim.Numerics.Fixed or Sim.Numerics.Fraction.\n" + string.Join("\n", scan.Findings));
    }

    /// <summary>Determinism rule 3. The only random generator is the seeded one the world owns.</summary>
    [Fact]
    public void SimDoesNotUseSystemRandom()
    {
        var scan = AssemblyScan.Run(typeof(SimInfo).Assembly, AssemblyScan.RandomTypes, reportFloatingPointInstructions: false);

        Assert.True(
            scan.Findings.Count == 0,
            "Sim must not use System.Random (docs/determinism.md, rule 3). Use World.Random.\n" + string.Join("\n", scan.Findings));
    }

    /// <summary>
    /// A scan that finds nothing is only worth something if it would have found something. This
    /// runs it over samples in this test assembly, one for each way a floating-point type can
    /// get into compiled code.
    /// </summary>
    [Fact]
    public void TheScanFindsEveryWayAFloatingPointTypeCanBeUsed()
    {
        var scan = AssemblyScan.Run(typeof(SimAssemblyTests).Assembly, AssemblyScan.FloatingPointTypes, reportFloatingPointInstructions: true);
        var prefix = typeof(FloatingPointSamples).FullName + ".";

        bool Found(string member, params string[] parts) => scan.Findings.Any(finding =>
            finding.StartsWith(prefix + member + ":", StringComparison.Ordinal) && parts.All(finding.Contains));

        Assert.True(Found(nameof(FloatingPointSamples.Field), "field of", "System.Double"));
        Assert.True(Found(nameof(FloatingPointSamples.Property), "property of", "System.Single"));
        Assert.True(Found(nameof(FloatingPointSamples.Parameter), "signature", "System.Single"));
        Assert.True(Found(nameof(FloatingPointSamples.ReturnValue), "signature", "System.Double"));
        Assert.True(Found(nameof(FloatingPointSamples.Local), "local variable", "System.Double"));
        Assert.True(Found(nameof(FloatingPointSamples.ArithmeticOnly), "instruction conv.r8"));
        Assert.True(Found(nameof(FloatingPointSamples.ArithmeticOnly), "instruction ldc.r8"));
        Assert.True(Found(nameof(FloatingPointSamples.CallOnly), "use of", "System.Math.Sign", "System.Double"));
        Assert.True(Found(nameof(FloatingPointSamples.GenericType), "use of", "List`1<System.Single>"));
        Assert.True(Found(nameof(FloatingPointSamples.GenericMethod), "use of", "Empty", "<System.Double>"));
        Assert.True(Found(nameof(FloatingPointSamples.ArrayOfFloats), "use of", "System.Single"));
        Assert.True(Found(nameof(FloatingPointSamples.DecimalNumber), "use of", "System.Decimal"));
        Assert.True(Found(nameof(FloatingPointSamples.TypeOf), "use of", "System.Half"));

        // The compiler moves a lambda into a class of its own, nested in the one it was written in.
        Assert.Contains(scan.Findings, finding =>
            finding.StartsWith(typeof(FloatingPointSamples).FullName + "+", StringComparison.Ordinal)
            && finding.Contains(nameof(FloatingPointSamples.Lambda)) && finding.Contains("instruction conv.r8"));

        // The part of the scan that is not tied to a method: everything the assembly refers to.
        Assert.Contains(scan.Findings, finding => finding.StartsWith("Assembly: reference to type System.Decimal", StringComparison.Ordinal));
        Assert.Contains(scan.Findings, finding => finding.StartsWith("Assembly: reference to member System.Math.Sign", StringComparison.Ordinal));

        // And it does not cry wolf over whole-number code.
        Assert.DoesNotContain(scan.Findings, finding => finding.Contains(nameof(FloatingPointSamples.WholeNumbersOnly)));
    }

    [Fact]
    public void TheScanFindsSystemRandom()
    {
        var scan = AssemblyScan.Run(typeof(SimAssemblyTests).Assembly, AssemblyScan.RandomTypes, reportFloatingPointInstructions: false);
        var prefix = typeof(FloatingPointSamples).FullName + ".";

        Assert.Contains(scan.Findings, finding => finding.StartsWith(prefix + nameof(FloatingPointSamples.RandomGenerator) + ":", StringComparison.Ordinal));
        Assert.DoesNotContain(scan.Findings, finding => finding.StartsWith(prefix + nameof(FloatingPointSamples.ArithmeticOnly) + ":", StringComparison.Ordinal));
    }
}

/// <summary>Code that breaks the determinism rules on purpose, for the scan to find. Nothing calls it.</summary>
internal static class FloatingPointSamples
{
    internal static double Field = 1;

    internal static float Property => 0;

    internal static int Parameter(float value) => value > 0 ? 1 : 0;

    internal static double ReturnValue() => 0;

    internal static bool Local(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

    /// <summary>No signature, local or call mentions a floating-point type here. Only the instructions do.</summary>
    internal static int ArithmeticOnly(int value) => (int)(value * 0.5);

    /// <summary>No floating-point instruction here. Only the methods it calls take and return one.</summary>
    internal static int CallOnly(string text) => Math.Sign(double.Parse(text, CultureInfo.InvariantCulture));

    internal static int GenericType() => new List<float>().Count;

    internal static int GenericMethod() => Array.Empty<double>().Length;

    internal static int ArrayOfFloats(int length) => new float[length].Length;

    internal static int DecimalNumber(int value) => decimal.ToInt32(value);

    internal static Type TypeOf() => typeof(Half);

    internal static int Lambda(int[] values) => values.Count(value => value * 0.5 > 1);

    internal static int RandomGenerator() => new Random(1).Next();

    internal static long WholeNumbersOnly(int[] values)
    {
        var total = Math.BigMul(values.Length, 3L);
        foreach (var value in values.Where(value => value % 2 == 0).OrderBy(value => value))
        {
            total += Math.Max(value, 1) * (long)Math.Abs(value);
        }

        var text = $"{total} from {values.Length} values";
        return (long)(total / new List<long> { 7 }[0]) + text.Length;
    }
}
