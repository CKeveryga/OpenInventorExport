using System.IO;
using OpenInventorExport.Core;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ExportConfigTests
{
    private static ExportTask StepTask(string name) => new ExportTask
    {
        Name = name,
        Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } }
    };

    [Fact]
    public void Config_SurvivesRoundTrip_AndWritesTheVersion()
    {
        var config = new ExportConfig { Tasks = { StepTask("A"), StepTask("B") } };

        string json = ExportConfigJson.ToJson(config);
        var loaded = ExportConfigJson.FromJson(json);

        Assert.Contains("\"SchemaVersion\": 1", json);
        Assert.Equal(2, loaded.Tasks.Count);
        Assert.Equal("B", loaded.Tasks[1].Name);
    }

    [Fact]
    public void MissingVersion_IsRejected()
    {
        const string json = """
            { "Tasks": [ { "Name": "A", "Outputs": [ { "Format": "Step", "Source": "Model" } ] } ] }
            """;
        var ex = Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson(json));
        Assert.Contains("SchemaVersion", ex.Message);
    }

    [Fact]
    public void WrongVersion_IsRejected()
    {
        const string json = """{ "SchemaVersion": 99, "Tasks": [] }""";
        Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson(json));
    }

    [Fact]
    public void DuplicateTaskNames_AreRejected_IgnoringCase()
    {
        var config = new ExportConfig { Tasks = { StepTask("Parts"), StepTask("parts") } };
        var ex = Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson(ExportConfigJson.ToJson(config)));
        Assert.Contains("more than once", ex.Message);
    }

    [Fact]
    public void ProblemsInAnyTask_AreReported()
    {
        const string json = """
            { "SchemaVersion": 1, "Tasks": [
                { "Name": "Bad", "Outputs": [ { "Format": "Pdf", "Source": "Model" } ] } ] }
            """;
        var ex = Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson(json));
        Assert.Contains("cannot be exported", ex.Message);
    }

    [Fact]
    public void NoTasks_IsRejected()
    {
        const string json = """{ "SchemaVersion": 1, "Tasks": [] }""";
        Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson(json));
    }
}