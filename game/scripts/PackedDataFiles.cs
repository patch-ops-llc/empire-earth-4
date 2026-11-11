using Godot;
using Sim.Data;

namespace Game;

/// <summary>
/// The data files as the game sees them: everything under <c>res://data</c>, read through Godot's
/// file access. That is the project folder when the game runs from the editor and the packed files
/// when it runs as an exported build, and the loader cannot tell the two apart.
/// </summary>
public sealed class PackedDataFiles : IDataFiles
{
    public const string DefaultRoot = "res://data";

    private readonly string _root;

    public PackedDataFiles(string root = DefaultRoot)
    {
        _root = root.TrimEnd('/');
    }

    public IReadOnlyList<string> ListFiles()
    {
        var paths = new List<string>();
        Collect(_root, "", paths);
        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    public ReadOnlyMemory<byte> ReadFile(string path)
    {
        var fullPath = $"{_root}/{path}";
        using var file = Godot.FileAccess.Open(fullPath, Godot.FileAccess.ModeFlags.Read)
            ?? throw new IOException($"Cannot read {fullPath}: {Godot.FileAccess.GetOpenError()}.");
        return file.GetBuffer((long)file.GetLength());
    }

    // Names that start with a dot, such as .DS_Store, are not data. The tools skip them too.
    private static void Collect(string folder, string prefix, List<string> paths)
    {
        using var directory = DirAccess.Open(folder)
            ?? throw new IOException($"Cannot list {folder}: {DirAccess.GetOpenError()}.");

        foreach (var name in directory.GetFiles())
        {
            if (!name.StartsWith('.'))
            {
                paths.Add(prefix + name);
            }
        }

        foreach (var name in directory.GetDirectories())
        {
            if (!name.StartsWith('.'))
            {
                Collect($"{folder}/{name}", $"{prefix}{name}/", paths);
            }
        }
    }
}
