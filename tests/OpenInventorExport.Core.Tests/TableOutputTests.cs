using System.IO;
using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class TableOutputTests
{
    private static ComponentRecord Rec(string path, string type, string? vendor = null, int level = 1)
    {
        var c = new ComponentRecord
        {
            OccurrencePath = path,
            FileName = System.IO.Path.GetFileName(path),
            FullPath = path,
            DocumentType = type,
            Level = level,
            BomStructure = level == 0 ? null : "kNormalBOMStructure"
        };
        if (vendor != null)
            c.SetProperty(PropertySetNames.DesignTracking, "Vendor", new PropertyValue { Value = vendor });
        return c;
    }

    private static Snapshot Sample()
    {
        var s = new Snapshot();
        s.Components.Add(Rec(@"C:\x\Top.iam", "Assembly", level: 0));
        s.Components.Add(Rec(@"C:\x\Plate.ipt", "SheetMetal", "X"));
        s.Components.Add(Rec(@"C:\x\Plate.ipt", "SheetMetal", "X"));
        s.Components.Add(Rec(@"C:\x\Bracket.ipt", "SheetMetal", "Y"));
        s.Components.Add(Rec(@"C:\x\Bolt.ipt", "Part"));
        return s;
    }

    private static OutputSpec Drw(ExportFormat f, string folder = "") =>
        new OutputSpec { Format = f, Source = ExportSource.Drawing, Folder = folder };

    private static OutputSpec Tbl(ExportFormat f, string folder = "", string? name = null) =>
        new OutputSpec { Format = f, Folder = folder, Name = name };

    private static ExportTask SheetMetalX(params OutputSpec[] outputs)
    {
        var t = new ExportTask
        {
            Name = "Pkg",
            Selection =
            {
                DocumentTypes = { "SheetMetal" },
                Filters = { new PropertyFilter { Property = "Vendor", Operator = FilterOperator.Equals, Value = "X" } }
            }
        };
        t.Outputs.AddRange(outputs);
        return t;
    }

    private static string Wrap(string output) => $$"""
        { "SchemaVersion": 1,
          "Tasks": [ { "Name": "A", "Outputs": [ {{output}} ] } ] }
        """;

    // ------------------------------------------------------------ rows

    [Fact]
    public void Rows_AreAllSelectedFiles_WithQuantity_WhateverTheFolder()
    {
        var task = SheetMetalX(Drw(ExportFormat.Dxf, "DXF"), Tbl(ExportFormat.Csv, "PDF"));

        var row = Assert.Single(TaskTable.Rows(Sample(), task, new ExportConfig()));

        Assert.Equal("Plate.ipt", row.Component.FileName);
        Assert.Equal(2, row.Quantity);
    }

    [Fact]
    public void Rows_ListAnAssemblyTheTaskSelects_EvenIfTheConfigHidesAssemblies()
    {
        var task = new ExportTask { Name = "Asm", Outputs = { Tbl(ExportFormat.Csv) } };
        task.Selection.DocumentTypes.Add("Assembly");
        var config = new ExportConfig { Rollup = new RollupOptions { IncludeAssemblies = false } };

        var row = Assert.Single(TaskTable.Rows(Sample(), task, config));

        Assert.Equal("Top.iam", row.Component.FileName);
        Assert.Equal(1, row.Quantity);
    }

    [Fact]
    public void Columns_FallBackToTheConfig_UnlessTheOutputNamesItsOwn()
    {
        var config = new ExportConfig { BomColumns = { new BomColumn { Name = "Material" } } };

        Assert.Equal("Material", Assert.Single(TaskTable.Columns(Tbl(ExportFormat.Csv), config)).Name);

        var own = Tbl(ExportFormat.Csv);
        own.Columns.Add(new BomColumn { Name = "Vendor" });
        Assert.Equal("Vendor", Assert.Single(TaskTable.Columns(own, config)).Name);
    }

    // --------------------------------------------------------- planning

    [Fact]
    public void Folder_PutsFilesAndTablesBelowTheTaskFolder()
    {
        var task = SheetMetalX(Drw(ExportFormat.Dxf, "DXF"), Tbl(ExportFormat.Csv, "DXF"), Tbl(ExportFormat.Xlsx));

        var files = ExportPlanner.Plan(Sample(), task, @"C:\out");
        var tables = ExportPlanner.PlanTables(new[] { task }, @"C:\out");

        Assert.Equal(Path.Combine(@"C:\out", "Pkg", "DXF", "Plate.dxf"), Assert.Single(files).OutputPath);
        Assert.Equal(Path.Combine(@"C:\out", "Pkg", "DXF", "Pkg.csv"), tables[0].OutputPath);
        Assert.Equal(Path.Combine(@"C:\out", "Pkg", "Pkg.xlsx"), tables[1].OutputPath);
    }

    [Fact]
    public void TableName_DefaultsToTheTask_AndCanBeSet()
    {
        var task = SheetMetalX(Drw(ExportFormat.Dxf), Tbl(ExportFormat.Csv, "", "VendorX-list"));

        var t = Assert.Single(ExportPlanner.PlanTables(new[] { task }, @"C:\out"));

        Assert.Equal(Path.Combine(@"C:\out", "Pkg", "VendorX-list.csv"), t.OutputPath);
    }

    [Fact]
    public void TablesAreNotPlannedAsComponentJobs()
    {
        var task = SheetMetalX(Drw(ExportFormat.Dxf), Tbl(ExportFormat.Csv));

        var jobs = ExportPlanner.Plan(Sample(), task, @"C:\out");

        Assert.All(jobs, j => Assert.Equal(ExportFormat.Dxf, j.Output.Format));
    }

    [Fact]
    public void SameFileInTwoFolders_IsFine()
    {
        var task = SheetMetalX(Drw(ExportFormat.Pdf, "ForVendor"), Drw(ExportFormat.Pdf, "Archive"));

        var jobs = ExportPlanner.Plan(Sample(), task, @"C:\out");

        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(JobState.Planned, j.State));
    }

    [Fact]
    public void TwoTasksWritingTheSameTable_IsAConflict()
    {
        var a = SheetMetalX(Drw(ExportFormat.Dxf), Tbl(ExportFormat.Csv, "", "shared"));
        var b = SheetMetalX(Drw(ExportFormat.Dxf), Tbl(ExportFormat.Csv, "", "shared"));
        b.Name = "Other";
        a.FolderTemplate = b.FolderTemplate = "same";

        var tables = ExportPlanner.PlanTables(new[] { a, b }, @"C:\out");

        Assert.Equal(JobState.Planned, tables[0].State);
        Assert.Equal(JobState.Conflict, tables[1].State);
        Assert.Contains("Pkg", tables[1].Detail);
    }

    // ------------------------------------------------------- validation

    [Fact]
    public void SameFormatInTheSameFolder_IsRejected() =>
        Assert.Contains(ExportTaskValidator.Validate(SheetMetalX(Drw(ExportFormat.Pdf, "A"), Drw(ExportFormat.Pdf, "a/"))),
            p => p.Contains("same folder"));

    [Fact]
    public void TwoTablesWithDifferentNames_AreFine() =>
        Assert.Empty(ExportTaskValidator.Validate(
            SheetMetalX(Drw(ExportFormat.Pdf), Tbl(ExportFormat.Csv, "", "one"), Tbl(ExportFormat.Csv, "", "two"))));

    [Theory]
    [InlineData("..")]
    [InlineData("a/../b")]
    [InlineData(@"C:\elsewhere")]
    public void BadFolder_IsRejected(string folder) =>
        Assert.Contains(ExportTaskValidator.Validate(SheetMetalX(Drw(ExportFormat.Pdf, folder))),
            p => p.Contains("relative path"));

    [Fact]
    public void NameAndColumns_OnAFileOutput_AreRejected()
    {
        var o = Drw(ExportFormat.Pdf);
        o.Name = "x";
        Assert.Contains(ExportTaskValidator.Validate(SheetMetalX(o)), p => p.Contains("only apply to Csv and Xlsx"));
    }

    [Fact]
    public void TypeTokenWithATable_IsRejected()
    {
        var task = SheetMetalX(Drw(ExportFormat.Pdf), Tbl(ExportFormat.Csv));
        task.FolderTemplate = "{task}/{type}";
        Assert.Contains(ExportTaskValidator.Validate(task), p => p.Contains("{type}"));
    }

    // ----------------------------------------------------------- config

    [Fact]
    public void TableOutput_NeedsNoSource()
    {
        var c = ExportConfigJson.FromJson(Wrap("""{ "Format": "Csv" }"""));
        Assert.Equal(ExportFormat.Csv, c.Tasks[0].Outputs[0].Format);
    }

    [Fact]
    public void FolderNameAndColumns_SurviveRoundTrip()
    {
        var table = Tbl(ExportFormat.Xlsx, "PDF", "list");
        table.Columns.Add(new BomColumn { Name = "Vendor" });
        var config = new ExportConfig { Tasks = { SheetMetalX(Drw(ExportFormat.Pdf, "PDF"), table) } };

        var loaded = ExportConfigJson.FromJson(ExportConfigJson.ToJson(config));

        var o = loaded.Tasks[0].Outputs[1];
        Assert.Equal("PDF", o.Folder);
        Assert.Equal("list", o.Name);
        Assert.Equal("Vendor", Assert.Single(o.Columns).Name);
    }

    [Fact]
    public void BadTableColumn_IsRejected() =>
        Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("""{ "Format": "Csv", "Columns": [ { "Name": " " } ] }""")));

    [Fact]
    public void TheOldPackageSection_IsRejected_NotIgnored() =>
        Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson("""
            { "SchemaVersion": 1,
              "Tasks": [ { "Name": "A", "Outputs": [ { "Format": "Step", "Source": "Model" } ],
                           "Package": { "Table": [ "Csv" ] } } ] }
            """));
}