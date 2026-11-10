using Sim.Data;

namespace Tools.Tests;

/// <summary>The real <c>game/data</c>, read from disk through the loader.</summary>
public class GameDataTests
{
    [Fact]
    public void TheGameDataLoadsWithoutErrors()
    {
        var result = DataLoader.Load(new DiskDataFiles(Repository.DataFolder));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.NotEmpty(result.Data.Tracks);
        Assert.NotEmpty(result.Data.Lines);
        Assert.NotEmpty(result.Data.Maps);
    }

    [Fact]
    public void TheHashDoesNotDependOnLineEndingsOnDisk()
    {
        using var copy = Repository.CopyOfData();
        foreach (var path in Directory.EnumerateFiles(copy.Path, "*", SearchOption.AllDirectories))
        {
            File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n", StringComparison.Ordinal));
        }

        var original = DataLoader.Load(new DiskDataFiles(Repository.DataFolder));
        var converted = DataLoader.Load(new DiskDataFiles(copy.Path));

        Assert.True(original.IsValid);
        Assert.True(converted.IsValid, string.Join(Environment.NewLine, converted.Errors));
        Assert.Equal(original.Data.ContentHash, converted.Data.ContentHash);
    }

    [Fact]
    public void DiskFilesAreListedWithForwardSlashesAndDotFilesAreSkipped()
    {
        using var folder = new TemporaryFolder();
        folder.Write("tracks.jsonc", "{}");
        folder.Write("lines/guard.jsonc", "{}");
        folder.Write("maps/ridge.jsonc", "{}");
        folder.Write(".DS_Store", "x");
        folder.Write("lines/.hidden.jsonc", "{}");
        folder.Write(".cache/rules.jsonc", "{}");

        var files = new DiskDataFiles(folder.Path);

        Assert.Equal(["lines/guard.jsonc", "maps/ridge.jsonc", "tracks.jsonc"], files.ListFiles());
        Assert.Equal("{}"u8.ToArray(), files.ReadFile("lines/guard.jsonc").ToArray());
    }
}
