using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ExportTablesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oie-tables-" + Guid.NewGuid().ToString("N"));

    public ExportTablesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp folder: best effort */ }
    }

    private static ComponentRecord Rec(string path, string type) => new()
    {
        OccurrencePath = path,
        FileName = Path.GetFileName(path),
        FullPath = path,
        DocumentType = type,
        Level = 1,
        BomStructure = "kNormalBOMStructure"
    };

    private static Snapshot Sample()
    {
        var s = new Snapshot();
        s.Components.Add(Rec(@"C:\x\Plate.ipt", "SheetMetal"));
        s.Components.Add(Rec(@"C:\x\Plate.ipt", "SheetMetal"));   // second occurrence: qty 2
        s.Components.Add(Rec(@"C:\x\Bolt.ipt", "Part"));           // not selected by the task
        return s;
    }

    private static ExportTask TaskWith(params ExportFormat[] tables)
    {
        var t = new ExportTask { Name = "Pkg", Selection = { DocumentTypes = { "SheetMetal" } } };
        foreach (var f in tables) t.Outputs.Add(new OutputSpec { Format = f });
        return t;
    }

    [Fact]
    public void Csv_IsWritten_WithBom_AndTheResultSaysHowManyRows()
    {
        var task = TaskWith(ExportFormat.Csv);
        var jobs = ExportPlanner.PlanTables(new[] { task }, _dir);

        var result = Assert.Single(ExportTables.Write(Sample(), new ExportConfig(), new[] { task }, jobs));

        Assert.Equal(ExportStatus.Exported, result.Status);
        Assert.Equal("1 row(s)", result.Detail);
        Assert.Equal("(table)", result.ModelPath);
        Assert.Equal(jobs[0].OutputPath, result.OutputPath);

        var bytes = File.ReadAllBytes(result.OutputPath!);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());   // UTF-8 BOM, so Excel reads it right
        var text = File.ReadAllText(result.OutputPath!);
        Assert.Contains("Plate.ipt,2", text);
        Assert.DoesNotContain("Bolt.ipt", text);
    }

    [Fact]
    public void Xlsx_IsWritten()
    {
        var task = TaskWith(ExportFormat.Xlsx);
        var jobs = ExportPlanner.PlanTables(new[] { task }, _dir);

        var result = Assert.Single(ExportTables.Write(Sample(), new ExportConfig(), new[] { task }, jobs));

        Assert.Equal(ExportStatus.Exported, result.Status);
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public void ConflictJob_GetsAConflictResult_AndWritesNothing()
    {
        var task = TaskWith(ExportFormat.Csv);
        var path = Path.Combine(_dir, "dup.csv");
        var job = new TableJob
        {
            TaskName = "Pkg",
            Output = task.Outputs[0],
            OutputPath = path,
            State = JobState.Conflict,
            Detail = "Same output file as task 'Other'"
        };

        var result = Assert.Single(ExportTables.Write(Sample(), new ExportConfig(), new[] { task }, new[] { job }));

        Assert.Equal(ExportStatus.Conflict, result.Status);
        Assert.Contains("Other", result.Detail);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AFailingTable_IsReportedAsFailed_NotThrown_AndLaterTablesStillRun()
    {
        var good = TaskWith(ExportFormat.Csv);
        var orphan = new TableJob
        {
            TaskName = "NotInTheList",   // no such task: the lookup fails
            Output = new OutputSpec { Format = ExportFormat.Csv },
            OutputPath = Path.Combine(_dir, "orphan.csv"),
            State = JobState.Planned
        };
        var jobs = new List<TableJob> { orphan }.Concat(ExportPlanner.PlanTables(new[] { good }, _dir)).ToList();

        var results = ExportTables.Write(Sample(), new ExportConfig(), new[] { good }, jobs);

        Assert.Equal(ExportStatus.Failed, results[0].Status);
        Assert.False(File.Exists(orphan.OutputPath));
        Assert.Equal(ExportStatus.Exported, results[1].Status);
    }

    [Fact]
    public void Callback_IsCalledOncePerTable_InOrder()
    {
        var task = TaskWith(ExportFormat.Csv, ExportFormat.Xlsx);
        var jobs = ExportPlanner.PlanTables(new[] { task }, _dir);
        var seen = new List<ExportFormat?>();

        var results = ExportTables.Write(Sample(), new ExportConfig(), new[] { task }, jobs, r => seen.Add(r.Format));

        Assert.Equal(2, results.Count);
        Assert.Equal(new ExportFormat?[] { ExportFormat.Csv, ExportFormat.Xlsx }, seen);
    }
}