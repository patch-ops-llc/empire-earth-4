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
}
