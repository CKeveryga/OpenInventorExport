using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ExportPlannerTests
{
    private static ComponentRecord Rec(string path, string type, string? material = null, int level = 1)
    {
        var c = new ComponentRecord
        {
            OccurrencePath = path,
            FileName = System.IO.Path.GetFileName(path),
            FullPath = path,
            DocumentType = type,
            Level = level
        };
        if (material != null)
            c.Properties["Material"] = new PropertyValue { Value = material };
        return c;
    }

    private static Snapshot Sample()
    {
        var s = new Snapshot();
        s.Components.Add(Rec(@"C:\x\Top.iam", "Assembly", level: 0));
        s.Components.Add(Rec(@"C:\x\Plate.ipt", "SheetMetal", "Steel"));
        s.Components.Add(Rec(@"C:\x\Plate.ipt", "SheetMetal", "Steel"));   // second occurrence
        s.Components.Add(Rec(@"C:\x\Bracket.ipt", "SheetMetal", "Aluminum"));
        s.Components.Add(Rec(@"C:\x\Bolt.ipt", "Part"));
        return s;
    }

    private static ExportTask SheetMetalTask(string template) => new ExportTask
    {
        Name = "SheetMetal",
        FolderTemplate = template,
        Selection = { DocumentTypes = { "SheetMetal" } },
        Outputs =
        {
            new OutputSpec { Format = ExportFormat.Pdf, Source = ExportSource.Drawing },
            new OutputSpec { Format = ExportFormat.Dxf, Source = ExportSource.Drawing }
        }
    };

    [Fact]
    public void SelectsOneJobPerUniqueFileAndFormat()
    {
        var jobs = ExportPlanner.Plan(Sample(), SheetMetalTask("{task}"), @"C:\out");
        Assert.Equal(4, jobs.Count);   // 2 unique sheet metal files x 2 formats
        Assert.All(jobs, j => Assert.Equal(JobState.Planned, j.State));
    }

    [Fact]
    public void SingleFolderTemplate_PutsAllFormatsTogether()
    {
        var jobs = ExportPlanner.Plan(Sample(), SheetMetalTask("{task}"), @"C:\out");
        Assert.All(jobs, j => Assert.Equal(@"C:\out\SheetMetal", System.IO.Path.GetDirectoryName(j.OutputPath)));
    }

    [Fact]
    public void FormatTemplate_SplitsFormatsIntoFolders()
    {
        var jobs = ExportPlanner.Plan(Sample(), SheetMetalTask("{task}/{format}"), @"C:\out");
        Assert.Contains(jobs, j => j.OutputPath == @"C:\out\SheetMetal\pdf\Plate.pdf");
        Assert.Contains(jobs, j => j.OutputPath == @"C:\out\SheetMetal\dxf\Plate.dxf");
    }

    [Fact]
    public void PropertyFilter_NarrowsSelection()
    {
        var task = SheetMetalTask("{task}");
        task.Selection.Filters.Add(new PropertyFilter { Property = "Material", Operator = FilterOperator.Equals, Value = "steel" });
        var jobs = ExportPlanner.Plan(Sample(), task, @"C:\out");
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.EndsWith("Plate.pdf", j.OutputPath.Replace(".dxf", ".pdf")));
    }

    [Fact]
    public void TopLevelOnly_SelectsTheRoot()
    {
        var task = new ExportTask
        {
            Name = "Top",
            Selection = { Scope = ExportScope.TopLevelOnly },
            Outputs = { new OutputSpec { Format = ExportFormat.Dwf, Source = ExportSource.Model } }
        };
        var jobs = ExportPlanner.Plan(Sample(), task, @"C:\out");
        Assert.Equal(@"C:\x\Top.iam", jobs.Single().ModelPath);
    }

    [Fact]
    public void SameBaseNameFromDifferentFolders_IsReportedAsConflict()
    {
        var s = new Snapshot();
        s.Components.Add(Rec(@"C:\a\X.ipt", "Part"));
        s.Components.Add(Rec(@"C:\b\X.ipt", "Part"));
        var task = new ExportTask
        {
            Name = "T",
            Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } }
        };

        var jobs = ExportPlanner.Plan(s, task, @"C:\out");

        Assert.Equal(1, jobs.Count(j => j.State == JobState.Planned));
        Assert.Equal(1, jobs.Count(j => j.State == JobState.Conflict));
    }

    [Fact]
    public void Validator_RejectsPdfFromModelAndUnknownTokens()
    {
        var task = new ExportTask
        {
            Name = "Bad",
            FolderTemplate = "{nope}",
            Outputs = { new OutputSpec { Format = ExportFormat.Pdf, Source = ExportSource.Model } }
        };
        var problems = ExportTaskValidator.Validate(task);
        Assert.Equal(2, problems.Count);
    }
}