using Sim.Data;

namespace Ai.Tests;

/// <summary>The game's own data, <c>game/data</c>, loaded once for the tests that play a match with it.</summary>
internal static class GameFiles
{
    private static readonly Lazy<GameData> Loaded = new(Load);

    public static string Root { get; } = FindRoot();

    public static GameData Data => Loaded.Value;

    private static GameData Load()
    {
        var folder = Path.Combine(Root, "game", "data");
        var files = new InMemoryDataFiles();
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/');
            if (!name.Split('/').Any(part => part.StartsWith('.')))
            {
                files.Set(name, File.ReadAllBytes(path));
            }
        }

        var result = DataLoader.Load(files);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
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
