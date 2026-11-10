namespace Tools.Tests;

public class ToolRunnerTests
{
    [Fact]
    public void FindsDataCheckByName()
    {
        var tools = ToolRunner.FindTools();

        Assert.Contains(tools, tool => tool.Name == "datacheck");
        Assert.Equal(tools.Select(tool => tool.Name).Order(StringComparer.Ordinal), tools.Select(tool => tool.Name));
    }

    [Fact]
    public void WithoutAToolItPrintsTheListAndFails()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();

        var exitCode = ToolRunner.Run([], output, errors);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output.ToString());
        Assert.Contains("datacheck", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownToolFails()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();

        var exitCode = ToolRunner.Run(["datachek"], output, errors);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Contains("Unknown tool 'datachek'.", errors.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    public void HelpPrintsTheListAndSucceeds(string argument)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();

        var exitCode = ToolRunner.Run([argument], output, errors);

        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Contains("datacheck", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("", errors.ToString());
    }
}
