using Godot;

namespace Game;

/// <summary>
/// Counts the errors the engine logs while the smoke test runs, so that the smoke test can fail
/// on them. Godot catches an exception that leaves a method it called, a <c>_Ready</c>, a
/// <c>_Process</c> or the handler of a signal, logs it as an error and goes on, and a match can
/// then run to its end with a part of the view missing. Warnings are not counted.
/// </summary>
/// <remarks>
/// The engine calls a logger from any thread, so the count is kept under a lock.
/// </remarks>
public partial class SmokeLog : Logger
{
    private readonly Lock _gate = new();
    private int _errors;
    private string _first = string.Empty;

    /// <summary>The number of errors logged since the logger was added.</summary>
    public int Errors
    {
        get
        {
            lock (_gate)
            {
                return _errors;
            }
        }
    }

    /// <summary>The first line of the first error, or an empty string when there was none.</summary>
    public string First
    {
        get
        {
            lock (_gate)
            {
                return _first;
            }
        }
    }

    public override void _LogError(
        string function,
        string file,
        int line,
        string code,
        string rationale,
        bool editorNotify,
        int errorType,
        Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if ((ErrorType)errorType == ErrorType.Warning)
        {
            return;
        }

        lock (_gate)
        {
            if (_errors++ == 0)
            {
                // The engine has printed the whole error already. An exception's text goes on
                // for many lines, and the first names it.
                var text = string.IsNullOrEmpty(rationale) ? code : rationale;
                var end = text.IndexOfAny(['\r', '\n']);
                _first = end < 0 ? text : text[..end];
            }
        }
    }
}
