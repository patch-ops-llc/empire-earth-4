using System.Text;
using Sim.Data;

namespace Sim.Tests.Data;

/// <summary>
/// The content hash must be equal for data that means the same and different for data that does not,
/// because peers compare it before a match and replays record it.
/// </summary>
public class ContentHashTests
{
    private static string SampleHash => DataAssert.Valid(SampleData.Files()).ContentHash;

    [Fact]
    public void TheHashIsSha256AsLowercaseHex()
    {
        Assert.Matches("^[0-9a-f]{64}$", SampleHash);
    }

    /// <summary>
    /// The canonical form is part of what peers agree on, so a change to it must be deliberate. This
    /// value was checked with a separate implementation of the form described in docs/data-authoring.md.
    /// </summary>
    [Fact]
    public void TheHashOfTheSampleDataIsPinned()
    {
        Assert.Equal("0c7f363aa061f5b84f24a8888a62212566c93f70e69233718683d8254d33e3c2", SampleHash);
    }

    [Fact]
    public void CommentsSpacingAndLineEndingsDoNotCount()
    {
        var files = SampleData.Files();
        foreach (var path in files.ListFiles())
        {
            var text = Encoding.UTF8.GetString(files.ReadFile(path).Span);
            files.Set(path, "// A comment.\r\n" + text.Replace("\n", "  \r\n", StringComparison.Ordinal) + "\n/* Another. */\n");
        }

        Assert.Equal(SampleHash, DataAssert.Valid(files).ContentHash);
    }

    [Fact]
    public void TheOrderOfKeysDoesNotCount()
    {
        var files = SampleData.With(
            SampleData.TracksPath,
            "\"id\": \"firepower\", \"name\": \"Firepower\"",
            "\"name\": \"Firepower\", \"id\": \"firepower\"");
        files.Edit(SampleData.GuardPath, "\"materials\": 70, \"energy\": 10", "\"energy\": 10, \"materials\": 70");

        Assert.Equal(SampleHash, DataAssert.Valid(files).ContentHash);
    }

    [Fact]
    public void TheSchemaKeyAndAByteOrderMarkDoNotCount()
    {
        var files = SampleData.With(SampleData.GuardPath, "\"id\": \"guard\",", "\"$schema\": \"../../../schemas/line.schema.json\", \"id\": \"guard\",");
        files.Set(SampleData.RulesPath, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(SampleData.Rules)).ToArray());

        Assert.Equal(SampleHash, DataAssert.Valid(files).ContentHash);
    }

    [Fact]
    public void TheOrderFilesAreListedInDoesNotCount()
    {
        Assert.Equal(SampleHash, DataAssert.Valid(new ReversedFiles(SampleData.Files())).ContentHash);
    }

    [Theory]
    [InlineData(SampleData.GuardPath, "\"hp\": 130", "\"hp\": 131")]
    [InlineData(SampleData.GuardPath, "\"speed\": \"2.6\"", "\"speed\": \"2.7\"")]
    [InlineData(SampleData.GuardPath, "\"id\": \"hoplite\"", "\"id\": \"hoplites\"")]
    [InlineData(SampleData.GuardPath, "\"cannot_hit\": [\"air\"],", "")]
    [InlineData(SampleData.RulesPath, "\"reach\": 2", "\"reach\": 1")]
    [InlineData(SampleData.RulesPath, "\"manufacturing_cost\": \"0.90\"", "\"manufacturing_cost\": \"0.91\"")]
    [InlineData(SampleData.TracksPath, "\"F4\"", "\"F four\"")]
    public void EveryValueCounts(string path, string oldText, string newText)
    {
        var changed = DataAssert.Valid(SampleData.With(path, oldText, newText)).ContentHash;

        Assert.NotEqual(SampleHash, changed);
    }

    [Fact]
    public void TheOrderOfAListCounts()
    {
        var files = SampleData.With(SampleData.RulesPath, "[\"guard\", \"cavalry_armor\", \"air\"]", "[\"air\", \"cavalry_armor\", \"guard\"]");

        Assert.NotEqual(SampleHash, DataAssert.Valid(files).ContentHash);
    }

    [Fact]
    public void OneTileOfTerrainCounts()
    {
        var changed = DataAssert.Valid(SampleData.WithMap(map => map.SetBoth(0, 0, 'F'))).ContentHash;

        Assert.NotEqual(SampleHash, changed);
    }

    [Fact]
    public void AFileNameCounts()
    {
        var files = SampleData.WithMap(map => map.Id = "field");
        files.Set("maps/field.jsonc", files.ReadFile(SampleData.MapPath));
        files.Remove(SampleData.MapPath);

        Assert.NotEqual(SampleHash, DataAssert.Valid(files).ContentHash);
    }

    [Fact]
    public void AnotherFileCounts()
    {
        var files = SampleData.WithMap(map => map.Id = "field");
        files.Set("maps/field.jsonc", files.ReadFile(SampleData.MapPath));
        files.Set(SampleData.MapPath, new MapBuilder().ToJson());

        var data = DataAssert.Valid(files);

        Assert.Equal(["arena", "field"], data.Maps.Select(map => map.Id));
        Assert.NotEqual(SampleHash, data.ContentHash);
    }

    private sealed class ReversedFiles(InMemoryDataFiles files) : IDataFiles
    {
        public IReadOnlyList<string> ListFiles() => [.. files.ListFiles().Reverse()];

        public ReadOnlyMemory<byte> ReadFile(string path) => files.ReadFile(path);
    }
}
