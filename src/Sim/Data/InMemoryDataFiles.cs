using System.Text;

namespace Sim.Data;

/// <summary>Data files held in memory, for tests and for data built by code.</summary>
public sealed class InMemoryDataFiles : IDataFiles
{
    private readonly SortedDictionary<string, ReadOnlyMemory<byte>> _files = new(StringComparer.Ordinal);

    /// <summary>Adds or replaces a file. The text is stored as UTF-8.</summary>
    public InMemoryDataFiles Set(string path, string text)
    {
        _files[path] = Encoding.UTF8.GetBytes(text);
        return this;
    }

    public InMemoryDataFiles Set(string path, ReadOnlyMemory<byte> bytes)
    {
        _files[path] = bytes;
        return this;
    }

    public bool Remove(string path) => _files.Remove(path);

    public IReadOnlyList<string> ListFiles() => [.. _files.Keys];

    public ReadOnlyMemory<byte> ReadFile(string path) =>
        _files.TryGetValue(path, out var bytes)
            ? bytes
            : throw new FileNotFoundException($"No data file named '{path}'.", path);
}
