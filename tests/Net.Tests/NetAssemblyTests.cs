namespace Net.Tests;

public class NetAssemblyTests
{
    /// <summary>
    /// The session runs without the engine, in tests and on a server: it may use the simulation
    /// and the .NET base libraries and nothing else. The real network transport is a library of
    /// its own, <c>Net.Udp</c>, behind <see cref="IConnection"/>: sockets, the LiteNetLib package
    /// and its threads live there.
    /// </summary>
    [Fact]
    public void NetReferencesOnlyTheSimulationAndTheDotNetBaseLibraries()
    {
        var references = typeof(IConnection).Assembly.GetReferencedAssemblies();

        Assert.Contains(references, reference => reference.Name == "Sim");
        Assert.All(references, reference =>
            Assert.True(
                reference.Name == "Sim" || reference.Name!.StartsWith("System.", StringComparison.Ordinal),
                $"Net must not depend on '{reference.Name}'. It may only use Sim and the .NET base libraries, never Godot."));
    }

    /// <summary>
    /// The sessions take time from their caller. A session that read a clock of its own could not
    /// be tested tick for tick, and two runs of one test could differ.
    /// </summary>
    [Fact]
    public void NetReadsNoClockAndStartsNoThread()
    {
        var referenced = new HashSet<string>();
        using (var file = File.OpenRead(typeof(IConnection).Assembly.Location))
        using (var image = new System.Reflection.PortableExecutable.PEReader(file))
        {
            var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(image);
            foreach (var handle in reader.TypeReferences)
            {
                var type = reader.GetTypeReference(handle);
                referenced.Add(reader.GetString(type.Namespace) + "." + reader.GetString(type.Name));
            }
        }

        Assert.Contains("System.TimeSpan", referenced);
        Assert.DoesNotContain("System.DateTime", referenced);
        Assert.DoesNotContain("System.DateTimeOffset", referenced);
        Assert.DoesNotContain("System.Environment", referenced);
        Assert.DoesNotContain("System.Diagnostics.Stopwatch", referenced);
        Assert.DoesNotContain("System.TimeProvider", referenced);
        Assert.DoesNotContain("System.Random", referenced);
        Assert.DoesNotContain(referenced, name => name.StartsWith("System.Threading.", StringComparison.Ordinal) && name != "System.Threading.Interlocked");
        Assert.DoesNotContain(referenced, name => name.StartsWith("System.Net.", StringComparison.Ordinal));
    }
}
