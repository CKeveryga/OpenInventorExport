using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ProjectCheckTests
{
    private const string Robot = @"C:\proj\Robot\Robot.ipj";
    private const string Engine = @"C:\proj\Engine\Engine.ipj";

    [Fact]
    public void SameProject_NoWarning_IgnoringCase()
    {
        var s = new Snapshot { ProjectFile = Robot };
        Assert.Null(SnapshotChecks.ProjectMismatch(s, Robot.ToUpperInvariant()));
    }

    [Fact]
    public void DifferentProject_Warns_NamingBoth()
    {
        var s = new Snapshot { ProjectFile = Robot };
        var warning = SnapshotChecks.ProjectMismatch(s, Engine);
        Assert.NotNull(warning);
        Assert.Contains("'Robot'", warning);
        Assert.Contains("'Engine'", warning);
    }

    [Fact]
    public void SnapshotWithoutProject_CannotBeCompared()
    {
        Assert.Null(SnapshotChecks.ProjectMismatch(new Snapshot(), Engine));
    }

    [Fact]
    public void UnknownActiveProject_CannotBeCompared()
    {
        Assert.Null(SnapshotChecks.ProjectMismatch(new Snapshot { ProjectFile = Robot }, null));
    }

    [Fact]
    public void OlderSnapshot_WithoutTheField_ReadsAsNotRecorded()
    {
        const string json = """{ "SchemaVersion": 2, "Components": [] }""";
        Assert.Null(SnapshotJson.FromJson(json)!.ProjectFile);
    }

    [Fact]
    public void ProjectFile_SurvivesRoundTrip()
    {
        var loaded = SnapshotJson.FromJson(SnapshotJson.ToJson(new Snapshot { ProjectFile = Robot }))!;
        Assert.Equal(Robot, loaded.ProjectFile);
    }

    [Fact]
    public void Summary_ShowsTheProject_OrNotRecorded()
    {
        Assert.Contains("Project: Robot.ipj", SnapshotSummary.Describe(new Snapshot { ProjectFile = Robot }));
        Assert.Contains("Project: (not recorded)", SnapshotSummary.Describe(new Snapshot()));
    }
}