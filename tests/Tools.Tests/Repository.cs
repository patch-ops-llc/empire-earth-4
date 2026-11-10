namespace Tools.Tests;

/// <summary>Finds files of the repository from wherever the tests run.</summary>
internal static class Repository
{
    public static string Root { get; } = FindRoot();

    public static string DataFolder => Path.Combine(Root, "game", "data");

    public static string SchemasFolder => Path.Combine(Root, "schemas");

    /// <summary>A copy of <c>game/data</c> in a new temporary folder, for tests that break it.</summary>
    public static TemporaryFolder CopyOfData()
    {
        var copy = new TemporaryFolder();
        foreach (var source in Directory.EnumerateFiles(DataFolder, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(copy.Path, Path.GetRelativePath(DataFolder, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }

        return copy;
    }

    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "global.json")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException($"No global.json above {AppContext.BaseDirectory}.");
    }
}

internal sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder()
    {
        Path = Directory.CreateTempSubdirectory("datacheck-test-").FullName;
    }

    public string Path { get; }

    /// <summary>Replaces one piece of text in one file. The text must be there, so no test passes by a typo.</summary>
    public void Edit(string file, string oldText, string newText)
    {
        var path = System.IO.Path.Combine(Path, file);
        var text = File.ReadAllText(path);
        Assert.Contains(oldText, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(oldText, newText, StringComparison.Ordinal));
    }

    public void Write(string file, string text)
    {
        var path = System.IO.Path.Combine(Path, file);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
