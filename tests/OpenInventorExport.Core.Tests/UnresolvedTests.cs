using System;
using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class UnresolvedTests
{
    private static ComponentRecord Rec(string path, string file, bool unresolved = false) => new()
    {
        OccurrencePath = path,
        ParentPath = "Top.iam",
        FileName = file,
        FullPath = @"C:\x\" + file,
        DocumentType = unresolved ? "Unknown" : "Part",
        Level = 1,
        BomStructure = "kNormalBOMStructure",
        IsUnresolved = unresolved
    };

    private static Snapshot Sample()
    {
        var s = new Snapshot();
        s.Components.Add(new ComponentRecord { OccurrencePath = "Top.iam", FileName = "Top.iam", FullPath = @"C:\x\Top.iam", DocumentType = "Assembly", Level = 0 });
        s.Components.Add(Rec("Top.iam/Good:1", "Good.ipt"));
        s.Components.Add(Rec("Top.iam/Gone:1", "Gone.ipt", unresolved: true));
        s.Components.Add(Rec("Top.iam/Gone:2", "Gone.ipt", unresolved: true));
        return s;
    }

    [Fact]
    public void Rollup_ListsUnresolvedComponents_Flagged()
    {
        var rows = Rollup.Apply(Sample(), new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false });

        var gone = rows.Single(r => r.Component.FileName == "Gone.ipt");
        Assert.Equal(2, gone.Quantity);
        Assert.True(gone.Component.IsUnresolved);
    }

    [Fact]
    public void Planner_NeverSelectsUnresolvedComponents()
    {
        var task = new ExportTask
        {
            Name = "T",
            Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } }
        };

        var jobs = ExportPlanner.Plan(Sample(), task, @"C:\out");

        Assert.Contains(jobs, j => j.ModelPath.EndsWith("Good.ipt"));
        Assert.DoesNotContain(jobs, j => j.ModelPath.EndsWith("Gone.ipt"));
    }

    [Fact]
    public void PropertyCheck_SkipsUnresolvedComponents()
    {
        var schema = new PropertySchema { Properties = { new SchemaProperty { Name = "Finish" } } };

        var issues = PropertySchemaValidator.Validate(Sample(), schema);

        Assert.DoesNotContain(issues, i => i.FullPath.EndsWith("Gone.ipt"));
        Assert.Contains(issues, i => i.FullPath.EndsWith("Good.ipt"));
    }

    [Fact]
    public void BomCsv_MarksUnresolvedRows()
    {
        var rows = Rollup.Apply(Sample(), new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false });

        var csv = CsvTableWriter.Write(rows, Array.Empty<(string Set, string Name)>());

        Assert.Contains("File Name,Qty,Status", csv);
        Assert.Contains("Gone.ipt,2,Unresolved", csv);
    }

    [Fact]
    public void FileMissing_Blocks()
    {
        var s = Sample();
        s.Skipped.Add(new SkippedRecord { OccurrencePath = "Top.iam/Gone:1", Reason = SkipReason.FileMissing });

        Assert.Single(SnapshotChecks.BlockingProblems(s));
    }

    [Fact]
    public void Error_BlocksOnlyWhenTheComponentIsNotInTheList()
    {
        var s = Sample();
        s.Skipped.Add(new SkippedRecord { OccurrencePath = "Top.iam/Lost:1", Reason = SkipReason.Error });   // not in Components
        s.Skipped.Add(new SkippedRecord { OccurrencePath = "Top.iam/Good:1", Reason = SkipReason.Error });   // listed, e.g. unreadable properties

        var blocking = SnapshotChecks.BlockingProblems(s);

        Assert.Equal("Top.iam/Lost:1", blocking.Single().OccurrencePath);
    }

    [Fact]
    public void Suppressed_DoesNotBlock()
    {
        var s = Sample();
        s.Skipped.Add(new SkippedRecord { OccurrencePath = "Top.iam/Off:1", Reason = SkipReason.Suppressed });

        Assert.Empty(SnapshotChecks.BlockingProblems(s));
    }
}