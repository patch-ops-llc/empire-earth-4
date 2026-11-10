namespace Sim.Data;

/// <summary>
/// The only way the loader reaches files. The game reads from the packed export, tools read from disk
/// and tests read from memory, and all three share the loader.
/// </summary>
public interface IDataFiles
{
    /// <summary>
    /// Every file under the data root as a path relative to it, with <c>/</c> between segments. The
    /// order does not matter; the loader sorts.
    /// </summary>
    IReadOnlyList<string> ListFiles();

    /// <summary>The raw bytes of one listed file.</summary>
    ReadOnlyMemory<byte> ReadFile(string path);
}
