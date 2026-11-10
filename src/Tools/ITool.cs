namespace Tools;

/// <summary>
/// One console tool. Put the class in its own folder under <c>src/Tools/</c> and give it a public
/// constructor without parameters; <see cref="ToolRunner"/> finds it by <see cref="Name"/>.
/// </summary>
public interface ITool
{
    /// <summary>The word that selects the tool on the command line, such as <c>datacheck</c>.</summary>
    string Name { get; }

    /// <summary>One line for the list of tools.</summary>
    string Summary { get; }

    /// <summary>
    /// Runs the tool with the arguments that follow its name. Returns <see cref="ExitCodes.Ok"/>,
    /// <see cref="ExitCodes.Failed"/> when the tool ran and found a problem, or
    /// <see cref="ExitCodes.Usage"/> when it could not run as asked.
    /// </summary>
    int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors);
}

public static class ExitCodes
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Usage = 2;
}
