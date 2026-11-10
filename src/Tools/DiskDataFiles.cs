using Sim.Data;

namespace Tools;

/// <summary>
/// Data files read from a folder on disk, for the tools. Files and folders whose names start with a
/// dot, such as <c>.DS_Store</c>, are not data and are skipped.
/// </summary>
public sealed class DiskDataFiles : IDataFiles
{
    private readonly string _root;

    public DiskDataFiles(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public IReadOnlyList<string> ListFiles()
    {
        var paths = new List<string>();
        Collect(_root, "", paths);
        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    public ReadOnlyMemory<byte> ReadFile(string path) =>
        File.ReadAllBytes(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));

    private static void Collect(string folder, string prefix, List<string> paths)
    {
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            if (entry.Name.StartsWith('.'))
            {
                continue;
            }

            if (entry is DirectoryInfo)
            {
                Collect(entry.FullName, $"{prefix}{entry.Name}/", paths);
            }
            else
            {
                paths.Add(prefix + entry.Name);
            }
        }
    }
}
