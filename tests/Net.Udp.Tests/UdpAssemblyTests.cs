namespace Net.Udp.Tests;

public class UdpAssemblyTests
{
    /// <summary>
    /// The transport runs without the engine, in tests and on a server. It may use the session
    /// library, the simulation under it, LiteNetLib and the .NET base libraries, and nothing
    /// else.
    /// </summary>
    [Fact]
    public void NetUdpReferencesOnlyNetTheSimulationLiteNetLibAndTheDotNetBaseLibraries()
    {
        var references = typeof(UdpConnection).Assembly.GetReferencedAssemblies();

        Assert.Contains(references, reference => reference.Name == "Net");
        Assert.Contains(references, reference => reference.Name == "LiteNetLib");
        Assert.All(references, reference =>
            Assert.True(
                reference.Name is "Net" or "Sim" or "LiteNetLib" || reference.Name!.StartsWith("System.", StringComparison.Ordinal),
                $"Net.Udp must not depend on '{reference.Name}'. It may only use Net, Sim, LiteNetLib and the .NET base libraries, never Godot."));
    }

    /// <summary>
    /// The session library stays free of sockets and packages: the transport is added beside it,
    /// not into it.
    /// </summary>
    [Fact]
    public void NetItselfDoesNotReferenceTheTransportOrItsPackage()
    {
        var references = typeof(IConnection).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name is "Net.Udp" or "LiteNetLib");
    }
}
