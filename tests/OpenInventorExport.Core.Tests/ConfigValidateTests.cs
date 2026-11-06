using System.IO;
using OpenInventorExport.Core;
using Xunit;

namespace OpenInventorExport.Core.Tests;

/// <summary>
/// Parse + Validate: what the config editor uses to open a config that has problems
/// and list them, instead of refusing to open it.
/// </summary>
public class ConfigValidateTests
{
    private static ExportTask StepTask(string name) => new ExportTask
    {
        Name = name,
        Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } }
    };

    [Fact]
    public void ValidConfig_HasNoProblems()
    {
        var config = new ExportConfig { Tasks = { StepTask("A") } };

        Assert.Empty(ExportConfigJson.Validate(config));
    }

    [Fact]
    public void Validate_ReturnsEveryProblem_WithoutThrowing()
    {
        var config = new ExportConfig
        {
            Tasks =
            {
                StepTask("Parts"),
                StepTask("parts"),
                new ExportTask
                {
                    Name = "Bad",
                    Outputs = { new OutputSpec { Format = ExportFormat.Pdf, Source = ExportSource.Model } }
                }
            },
            BomColumns = { new BomColumn { Name = "" } }
        };

        var problems = ExportConfigJson.Validate(config);

        Assert.Contains(problems, p => p.Contains("more than once"));
        Assert.Contains(problems, p => p.Contains("cannot be exported"));
        Assert.Contains(problems, p => p.Contains("BOM column has no Name"));
    }

    [Fact]
    public void Parse_OpensAConfigWithProblems_AndValidateListsThem()
    {
        const string json = """
            { "SchemaVersion": 1, "Tasks": [
                { "Name": "Bad", "Outputs": [ { "Format": "Pdf", "Source": "Model" } ] } ] }
            """;

        var config = ExportConfigJson.Parse(json);

        Assert.Equal("Bad", config.Tasks[0].Name);
        Assert.Contains(ExportConfigJson.Validate(config), p => p.Contains("cannot be exported"));
    }

    [Fact]
    public void Parse_StillRejectsAMisspelledName()
    {
        const string json = """{ "SchemaVersion": 1, "Taks": [] }""";

        Assert.Throws<InvalidDataException>(() => ExportConfigJson.Parse(json));
    }

    [Fact]
    public void Parse_StillRejectsAWrongVersion()
    {
        const string json = """{ "SchemaVersion": 99, "Tasks": [] }""";

        Assert.Throws<InvalidDataException>(() => ExportConfigJson.Parse(json));
    }

    [Fact]
    public void NoTasks_IsAProblem()
    {
        Assert.Contains(ExportConfigJson.Validate(new ExportConfig()), p => p.Contains("no tasks"));
    }
}
