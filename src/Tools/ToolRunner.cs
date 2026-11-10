namespace Tools;

/// <summary>Finds every <see cref="ITool"/> in this program and runs the one named on the command line.</summary>
public static class ToolRunner
{
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        var tools = FindTools();
        if (arguments.Count == 0)
        {
            WriteUsage(tools, errors);
            return ExitCodes.Usage;
        }

        if (arguments[0] is "help" or "--help" or "-h")
        {
            WriteUsage(tools, output);
            return ExitCodes.Ok;
        }

        var tool = tools.FirstOrDefault(candidate => candidate.Name == arguments[0]);
        if (tool is null)
        {
            errors.WriteLine($"Unknown tool '{arguments[0]}'.");
            WriteUsage(tools, errors);
            return ExitCodes.Usage;
        }

        return tool.Run([.. arguments.Skip(1)], output, errors);
    }

    public static IReadOnlyList<ITool> FindTools()
    {
        var tools = typeof(ToolRunner).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ITool).IsAssignableFrom(type))
            .Select(type => (ITool)Activator.CreateInstance(type)!)
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToList();

        var repeated = tools.GroupBy(tool => tool.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        return repeated is null
            ? tools
            : throw new InvalidOperationException($"Two tools are named '{repeated.Key}'.");
    }

    private static void WriteUsage(IReadOnlyList<ITool> tools, TextWriter writer)
    {
        writer.WriteLine("Usage: dotnet run --project src/Tools -- <tool> [arguments]");
        writer.WriteLine();
        writer.WriteLine("Tools:");
        var width = tools.Max(tool => tool.Name.Length);
        foreach (var tool in tools)
        {
            writer.WriteLine($"  {tool.Name.PadRight(width)}  {tool.Summary}");
        }
    }
}
