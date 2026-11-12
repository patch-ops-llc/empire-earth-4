using Sim.Data;
using Sim.Serialization;
using Sim.Setup;

namespace Net.Tests;

/// <summary>The real <c>game/data</c>, read from the repository, and matches described on it.</summary>
internal static class TestData
{
    public const string GameVersion = "test-build";

    private static readonly Lazy<GameData> Loaded = new(Load);

    /// <summary>The game data. Loaded once: it is never changed.</summary>
    public static GameData Data => Loaded.Value;

    /// <summary>A match of two players on the map <c>ridge</c>, as a host would describe it.</summary>
    public static MatchDescription Description(ulong seed = 7, string gameVersion = GameVersion, int players = 2)
    {
        var setups = new PlayerSetup[players];
        for (var player = 0; player < players; player++)
        {
            setups[player] = player < 2 ? new PlayerSetup(1 - player) : PlayerSetup.WithoutStart;
        }

        return new MatchDescription(gameVersion, Data.ContentHash, "ridge", seed, setups);
    }

    /// <summary>The data with one value changed, as a build with other rules would have it.</summary>
    public static GameData ChangedData()
    {
        const string Rule = "\"damage\": \"1.25\"";
        var files = Files();
        var rules = System.Text.Encoding.UTF8.GetString(files.ReadFile("rules.jsonc").Span);
        Assert.Contains(Rule, rules, StringComparison.Ordinal);
        files.Set("rules.jsonc", rules.Replace(Rule, "\"damage\": \"1.3\"", StringComparison.Ordinal));
        return DataLoader.Load(files).Data ?? throw new InvalidOperationException("The changed data does not load.");
    }

    private static GameData Load()
    {
        var result = DataLoader.Load(Files());
        return result.Data ?? throw new InvalidOperationException($"game/data does not load: {result.Errors[0]}");
    }

    private static InMemoryDataFiles Files()
    {
        var files = new InMemoryDataFiles();
        Collect(new DirectoryInfo(Path.Combine(FindRepositoryRoot(), "game", "data")), "", files);
        return files;
    }

    private static void Collect(DirectoryInfo folder, string prefix, InMemoryDataFiles files)
    {
        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
            if (entry.Name.StartsWith('.'))
            {
                continue;
            }

            if (entry is DirectoryInfo subfolder)
            {
                Collect(subfolder, $"{prefix}{entry.Name}/", files);
            }
            else
            {
                files.Set(prefix + entry.Name, File.ReadAllBytes(entry.FullName));
            }
        }
    }

    private static string FindRepositoryRoot()
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
