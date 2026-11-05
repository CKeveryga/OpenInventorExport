using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

/// <summary>
/// Real snapshot from Inventor 2027: Part5 (inside a Purchased assembly) had its file renamed, so it is unresolved.
/// No Inventor totals exist for it (Inventor cannot save a BOM with unresolved files), so these tests check
/// behavior that follows from verified rules, not numbers copied from the tool.
/// </summary>
public class UnresolvedFixtureTests
{
    private const string FixtureName = "unresolved-test-snapshot.json";
    private const string Unresolved = "Part5.ipt";

    private static Snapshot Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "samples", "snapshots")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return SnapshotJson.FromJson(File.ReadAllText(Path.Combine(dir!.FullName, "samples", "snapshots", FixtureName)))!;
    }

    private static ExportTask StepTask()
    {
        var t = new ExportTask
        {
            Name = "Steps",
            Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } }
        };
        t.Selection.DocumentTypes.AddRange(new[] { "Part", "SheetMetal" });
        return t;
    }

    [Fact]
    public void TheUnresolvedPart_IsRecordedFlagged_WithNoProperties()
    {
        var c = Load().Components.Single(x => x.FileName == Unresolved);

        Assert.True(c.IsUnresolved);
        Assert.Equal("Unknown", c.DocumentType);
        Assert.NotNull(c.FullPath);
        Assert.Empty(c.PropertySets);
    }

    [Fact]
    public void ExactlyOneComponentBlocks_AndItIsTheUnresolvedPart()
    {
        var s = Load();

        var blocking = Assert.Single(SnapshotChecks.BlockingProblems(s));
        Assert.Equal(SkipReason.FileMissing, blocking.Reason);
        Assert.EndsWith("Part5:1", blocking.OccurrencePath);
        Assert.Contains("Part5", blocking.Detail);
    }

    [Fact]
    public void Structured_ListsTheUnresolvedPart_FlaggedInTheCsv()
    {
        var rows = BomTotals.StructuredRows(Load());

        var row = rows.Single(r => r.Component.FileName == Unresolved);
        Assert.True(row.Component.IsUnresolved);

        var csv = CsvTableWriter.Write(rows, Array.Empty<(string Set, string Name)>());
        Assert.Contains(Unresolved + ",1,Unresolved", csv);
    }

    [Fact]
    public void PartsOnly_HidesTheUnresolvedPart_BecauseItsParentIsPurchased()
    {
        // Verified for Purchased assemblies against Inventor 2027: Parts Only shows the assembly, not its children.
        var rows = BomTotals.PartsOnlyRows(Load());

        Assert.DoesNotContain(rows, r => r.Component.FileName == Unresolved);
        Assert.Contains(rows, r => r.Component.BomStructure == "kPurchasedBOMStructure");
    }

    [Fact]
    public void Planner_NeverSelectsTheUnresolvedPart_ButStillExportsTheReadableOnes()
    {
        var jobs = ExportPlanner.Plan(Load(), StepTask(), @"C:\out");

        Assert.DoesNotContain(jobs, j => j.ModelPath.EndsWith(Unresolved));
        Assert.Equal(3, jobs.Count);   // Part1, Part2, Part6: Phantom children kept, Reference children left out
        Assert.All(jobs, j => Assert.Equal(JobState.Planned, j.State));
    }

    [Fact]
    public void ExclusionReport_DoesNotMentionTheUnresolvedPart()
    {
        // Unresolved components are handled by the export gate and the log, not by the exclusion report.
        var excl = new List<ExclusionRecord>();
        ExportPlanner.Plan(Load(), StepTask(), @"C:\out", excl);

        Assert.DoesNotContain(excl, e => e.FileName == Unresolved);
    }

    [Fact]
    public void TaskTable_LeavesOutTheUnresolvedPart_ByDecision()
    {
        // Decided 2026-11-04: its type and properties are unknown, so it cannot be assigned to any task.
        var task = StepTask();
        task.Outputs.Add(new OutputSpec { Format = ExportFormat.Csv });

        var rows = TaskTable.Rows(Load(), task, task.Outputs[1], new ExportConfig());

        Assert.DoesNotContain(rows, r => r.Component.FileName == Unresolved);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void UnreadableComponent_GetsItsOwnLogLine()
    {
        var k = SnapshotChecks.BlockingProblems(Load()).Single();

        var r = ExportResult.ForUnreadable(k);

        Assert.Equal(ExportStatus.Unreadable, r.Status);
        Assert.Contains("Part5", r.Detail);
    }

    [Fact]
    public void Fixture_CarriesTheActiveProject()
    {
        Assert.False(string.IsNullOrEmpty(Load().ProjectFile));
    }
}