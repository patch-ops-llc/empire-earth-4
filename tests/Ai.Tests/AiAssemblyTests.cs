using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Sim.Tests;

namespace Ai.Tests;

/// <summary>
/// What the computer player orders goes into the simulation of every peer, so the rules that
/// keep the simulation the same on every machine hold for it too. These read the compiled
/// assembly, so they also see what the compiler generated.
/// </summary>
public class AiAssemblyTests
{
    /// <summary>It plays through the simulation and nothing else: the engine never comes into it.</summary>
    [Fact]
    public void AiReferencesOnlyTheSimulationAndTheDotNetBaseLibraries()
    {
        var references = typeof(AiPlayer).Assembly.GetReferencedAssemblies();

        Assert.Contains(references, reference => reference.Name == "Sim");
        Assert.All(references, reference =>
            Assert.True(
                reference.Name == "Sim" || reference.Name!.StartsWith("System.", StringComparison.Ordinal),
                $"Ai must not depend on '{reference.Name}'. It may only use Sim and the .NET base libraries, never Godot."));
    }

    [Fact]
    public void AiUsesNoFloatingPointTypes()
    {
        var scan = AssemblyScan.Run(typeof(AiPlayer).Assembly, AssemblyScan.FloatingPointTypes, reportFloatingPointInstructions: true);

        Assert.True(scan.MethodBodies >= 50, $"The scan read only {scan.MethodBodies} method bodies of Ai, so it proves nothing.");
        Assert.True(
            scan.Findings.Count == 0,
            "Ai must not use float, double, decimal or any other floating-point type (docs/determinism.md, rule 1). "
            + "Use Sim.Numerics.Fixed or whole numbers.\n" + string.Join("\n", scan.Findings));
    }

    [Fact]
    public void AiDoesNotUseSystemRandom()
    {
        var scan = AssemblyScan.Run(typeof(AiPlayer).Assembly, AssemblyScan.RandomTypes, reportFloatingPointInstructions: false);

        Assert.True(
            scan.Findings.Count == 0,
            "Ai must not use System.Random (docs/determinism.md, rule 3): what it orders depends on the match alone.\n"
            + string.Join("\n", scan.Findings));
    }

    /// <summary>
    /// Its time is the tick of the match. With a clock, a thread, a file or anything of the
    /// machine it runs on, two runs of one match could give different orders.
    /// </summary>
    [Fact]
    public void AiReadsNoClockAndNothingOfTheMachine()
    {
        var referenced = new HashSet<string>();
        using (var file = File.OpenRead(typeof(AiPlayer).Assembly.Location))
        using (var image = new PEReader(file))
        {
            var reader = image.GetMetadataReader();
            foreach (var handle in reader.TypeReferences)
            {
                var type = reader.GetTypeReference(handle);
                referenced.Add(reader.GetString(type.Namespace) + "." + reader.GetString(type.Name));
            }
        }

        // The scan read something: these it does use.
        Assert.Contains("Sim.Core.Command", referenced);
        Assert.Contains("System.Math", referenced);

        Assert.DoesNotContain("System.DateTime", referenced);
        Assert.DoesNotContain("System.DateTimeOffset", referenced);
        Assert.DoesNotContain("System.TimeSpan", referenced);
        Assert.DoesNotContain("System.TimeProvider", referenced);
        Assert.DoesNotContain("System.Diagnostics.Stopwatch", referenced);
        Assert.DoesNotContain("System.Environment", referenced);
        Assert.DoesNotContain("System.Guid", referenced);
        Assert.DoesNotContain("System.HashCode", referenced);
        Assert.DoesNotContain(referenced, name => name.StartsWith("System.Threading.", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("System.IO.", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("System.Net.", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("System.Security.Cryptography.", StringComparison.Ordinal));
    }
}
