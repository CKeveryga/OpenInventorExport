using System.Linq;
using OpenInventorExport.Core;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ExportResultTests
{
    private static ExportJob Job(JobState state, string? detail = null) => new ExportJob
    {
        TaskName = "T",
        ModelPath = @"C:\x\A.ipt",
        Output = new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model },
        OutputPath = @"C:\out\T\A.stp",
        State = state,
        Detail = detail
    };

    [Fact]
    public void ForJob_Conflict_IsReportedAsConflict()
    {
        var r = ExportResult.ForJob(Job(JobState.Conflict, "Same output file as B"));
        Assert.Equal(ExportStatus.Conflict, r.Status);
        Assert.Equal("Same output file as B", r.Detail);
    }

    [Fact]
    public void ForJob_Planned_NeverLooksExportedUntilRun()
    {
        var r = ExportResult.ForJob(Job(JobState.Planned));
        Assert.NotEqual(ExportStatus.Exported, r.Status);
    }

    [Fact]
    public void Csv_HasOneRowPerResult_AndEscapes()
    {
        var a = ExportResult.ForJob(Job(JobState.Planned));
        a.Status = ExportStatus.Exported;
        a.Detail = "has, comma";
        var b = ExportResult.ForJob(Job(JobState.Conflict, "dup"));

        var lines = ExportResultLog.ToCsv(new[] { a, b })
            .Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);   // header + 2
        Assert.Contains("\"has, comma\"", lines[1]);
    }

    [Fact]
    public void Json_WritesStatusAsText()
    {
        var a = ExportResult.ForJob(Job(JobState.Planned));
        a.Status = ExportStatus.Exported;
        Assert.Contains("\"Status\": \"Exported\"", ExportResultLog.ToJson(new[] { a }));
    }

    [Fact]
    public void Summary_CountsByStatus()
    {
        var a = ExportResult.ForJob(Job(JobState.Conflict));
        var b = ExportResult.ForJob(Job(JobState.Conflict));
        Assert.Equal("Conflict: 2", ExportResultLog.Summary(new[] { a, b }).Single());
    }
}