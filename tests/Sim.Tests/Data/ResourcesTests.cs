using Sim.Data;

namespace Sim.Tests.Data;

/// <summary>What the loader reads from <c>resources.jsonc</c>, and what it rejects there.</summary>
public class ResourcesTests
{
    [Fact]
    public void TheResourcesAreTheNumbersOfTheDesign()
    {
        var resources = DataAssert.Valid(SampleData.Files()).Resources;

        Assert.Equal(new MaterialsRules(DataNumber.FromInteger(45), 8, 8000, Number("0.5")), resources.Materials);
        Assert.Equal(new EnergyRules(DataNumber.FromInteger(30), 4), resources.Energy);
        Assert.Equal(
            new WorkerProfile(
                HitPoints: DataNumber.FromInteger(50),
                Speed: DataNumber.FromInteger(3),
                Sight: 6,
                Supply: 1,
                Spacing: DataNumber.FromInteger(1),
                Materials: 50,
                Energy: 0,
                BuildTicks: 240),
            resources.Worker);
    }

    [Theory]
    [InlineData("\"per_worker_per_minute\": 45", "\"per_worker_per_minute\": 0", "$.materials.per_worker_per_minute", "must be above 0 and at most 100000, found 0")]
    [InlineData("\"per_worker_per_minute\": 45", "\"per_worker_per_minute\": 22.5", "$.materials.per_worker_per_minute", "write 22.5 as text")]
    [InlineData("\"workers_per_deposit\": 8", "\"workers_per_deposit\": 0", "$.materials.workers_per_deposit", "must be between 1 and 64, found 0")]
    [InlineData("\"full_rate_amount\": 8000", "\"full_rate_amount\": \"8000\"", "$.materials.full_rate_amount", "expected a whole number, found text")]
    [InlineData("\"depleted_rate\": \"0.5\"", "\"depleted_rate\": \"1.5\"", "$.materials.depleted_rate", "must be above 0 and at most 1, found 1.5")]
    [InlineData("\"depleted_rate\": \"0.5\"", "\"depleted_rate\": 0", "$.materials.depleted_rate", "must be above 0 and at most 1, found 0")]
    [InlineData(", \"depleted_rate\": \"0.5\"", "", "$.materials", "missing key \"depleted_rate\"")]
    [InlineData("\"per_worker_per_minute\": 30", "\"per_worker_per_minute\": 100001", "$.energy.per_worker_per_minute", "must be above 0 and at most 100000, found 100001")]
    [InlineData("\"workers_per_generator\": 4", "\"workers_per_generator\": 65", "$.energy.workers_per_generator", "must be between 1 and 64, found 65")]
    [InlineData("\"workers_per_generator\": 4", "\"workers_per_generator\": 4, \"slots\": 4", "$.energy.slots", "unknown key \"slots\"")]
    [InlineData("\"hp\": 50", "\"hp\": 0", "$.worker.hp", "must be above 0")]
    [InlineData("\"speed\": 3", "\"speed\": 101", "$.worker.speed", "must be above 0 and at most 100, found 101")]
    [InlineData("\"sight\": 6", "\"sight\": 0", "$.worker.sight", "must be between 1 and 64, found 0")]
    [InlineData("\"supply\": 1", "\"supply\": 0", "$.worker.supply", "must be between 1 and 100, found 0")]
    [InlineData("\"spacing\": 1", "\"spacing\": 0", "$.worker.spacing", "must be above 0")]
    [InlineData("\"materials\": 50", "\"materials\": -50", "$.worker.cost.materials", "must be between 0 and 1000000, found -50")]
    [InlineData("\"energy\": 0 }", "\"energy\": 0, \"supply\": 1 }", "$.worker.cost.supply", "unknown key \"supply\"")]
    [InlineData("\"build_seconds\": 12", "\"build_seconds\": \"12.01\"", "$.worker.build_seconds", "12.01 s is not a whole number of ticks")]
    [InlineData("\"sight\": 6, ", "", "$.worker", "missing key \"sight\"")]
    public void TheResourcesMustBeCompleteAndSane(string oldText, string newText, string location, string message)
    {
        var files = SampleData.With(SampleData.ResourcesPath, oldText, newText);

        DataAssert.RejectedOnce(files, SampleData.ResourcesPath, location, message);
    }

    [Fact]
    public void ARateMayHaveAFractionWrittenAsText()
    {
        var files = SampleData.With(SampleData.ResourcesPath, "\"per_worker_per_minute\": 30", "\"per_worker_per_minute\": \"22.5\"");

        Assert.Equal(Number("22.5"), DataAssert.Valid(files).Resources.Energy.PerWorkerPerMinute);
    }

    [Fact]
    public void TheFileMustHoldAnObjectWithAllThreeParts()
    {
        DataAssert.RejectedOnce(SampleData.Files().Set(SampleData.ResourcesPath, "[]"), SampleData.ResourcesPath, "$", "expected an object");

        var files = SampleData.Files().Set(SampleData.ResourcesPath, "{}");
        DataAssert.Rejected(files, SampleData.ResourcesPath, "$", "missing key \"materials\"");
        DataAssert.Rejected(files, SampleData.ResourcesPath, "$", "missing key \"energy\"");
        DataAssert.Rejected(files, SampleData.ResourcesPath, "$", "missing key \"worker\"");
    }

    private static DataNumber Number(string text)
    {
        Assert.True(DataNumber.TryParse(text, out var number));
        return number;
    }
}
