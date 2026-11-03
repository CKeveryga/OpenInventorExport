using System.IO;
using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class PackageTests
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
        s.Components.Add(Rec(@"C:\x\Plate.ipt", "SheetMetal", "X"));     // second occurrence
        s.Components.Add(Rec(@"C:\x\Bracket.ipt", "SheetMetal", "Y"));
        s.Components.Add(Rec(@"C:\x\Bolt.ipt", "Part"));
        return s;
    }

    private static ExportTask VendorTask(PackageSpec? package) => new ExportTask
    {
        Name = "VendorX",
        Selection =
        {
            DocumentTypes = { "SheetMetal" },
            Filters = { new PropertyFilter { Property = "Vendor", Operator = FilterOperator.Equals, Value = "X" } }
        },
        Outputs =
        {
            new OutputSpec { Format = ExportFormat.Dxf, Source = ExportSource.Drawing },
            new OutputSpec { Format = ExportFormat.Pdf, Source = ExportSource.Drawing }
        },
        Package = package
    };

    private static string Wrap(string package) => $$"""
        { "SchemaVersion": 1,
          "Tasks": [ { "Name": "A", "Outputs": [ { "Format": "Step", "Source": "Model" } ],
                       "Package": {{package}} } ] }
        """;

    // ---------------------------------------------------------- rows

    [Fact]
    public void Rows_AreOnlyTheSelectedFiles_WithTheirQuantity()
    {
        var rows = PackageTable.Rows(Sample(), VendorTask(new PackageSpec { Table = { TableFormat.Xlsx } }), new ExportConfig());

        var row = Assert.Single(rows);
        Assert.Equal("Plate.ipt", row.Component.FileName);
        Assert.Equal(2, row.Quantity);
    }

    [Fact]
    public void Rows_PackageRollupOverridesTheConfigRollup()
    {
        var pkg = new PackageSpec
        {
            Table = { TableFormat.Csv },
            Rollup = new RollupOptions { Mode = RollupMode.Occurrence }
        };

        var rows = PackageTable.Rows(Sample(), VendorTask(pkg), new ExportConfig());

        Assert.Equal(2, rows.Count);   // one row per Plate occurrence
    }

    [Fact]
    public void Columns_FallBackToTheConfig_UnlessThePackageNamesItsOwn()
    {
        var config = new ExportConfig { BomColumns = { new BomColumn { Name = "Material" } } };

        var inherited = PackageTable.Columns(VendorTask(new PackageSpec { Table = { TableFormat.Csv } }), config);
        Assert.Equal("Material", Assert.Single(inherited).Name);

        var own = PackageTable.Columns(
            VendorTask(new PackageSpec { Table = { TableFormat.Csv }, Columns = { new BomColumn { Name = "Vendor" } } }), config);
        Assert.Equal("Vendor", Assert.Single(own).Name);
    }

    // ------------------------------------------------------ delivered

    [Fact]
    public void Delivered_SaysYesOnlyWhenEveryJobForTheFileWasExported()
    {
        var task = VendorTask(new PackageSpec { Table = { TableFormat.Csv } });
        var row = PackageTable.Rows(Sample(), task, new ExportConfig()).Single();

        var ok = PackageTable.Delivered(task, new[]
        {
            new ExportResult { TaskName = "VendorX", Format = ExportFormat.Dxf, ModelPath = @"C:\x\Plate.ipt", Status = ExportStatus.Exported },
            new ExportResult { TaskName = "VendorX", Format = ExportFormat.Pdf, ModelPath = @"C:\x\Plate.ipt", Status = ExportStatus.Exported }
        });
        Assert.Equal("Yes", ok(row));

        var partly = PackageTable.Delivered(task, new[]
        {
            new ExportResult { TaskName = "VendorX", Format = ExportFormat.Dxf, ModelPath = @"C:\x\Plate.ipt", Status = ExportStatus.NoDrawing },
            new ExportResult { TaskName = "VendorX", Format = ExportFormat.Pdf, ModelPath = @"C:\x\Plate.ipt", Status = ExportStatus.Exported }
        });
        Assert.Equal("No (Dxf: NoDrawing)", partly(row));

        var none = PackageTable.Delivered(task, new ExportResult[0]);
        Assert.StartsWith("No", none(row));
    }

    [Fact]
    public void Delivered_IgnoresResultsOfOtherTasks()
    {
        var task = VendorTask(new PackageSpec { Table = { TableFormat.Csv } });
        var row = PackageTable.Rows(Sample(), task, new ExportConfig()).Single();

        var f = PackageTable.Delivered(task, new[]
        {
            new ExportResult { TaskName = "Other", Format = ExportFormat.Dxf, ModelPath = @"C:\x\Plate.ipt", Status = ExportStatus.Exported }
        });

        Assert.StartsWith("No", f(row));
    }

    [Fact]
    public void Csv_AddsADeliveredColumn_OnlyWhenAskedTo()
    {
        var rows = PackageTable.Rows(Sample(), VendorTask(new PackageSpec { Table = { TableFormat.Csv } }), new ExportConfig());

        var plain = CsvTableWriter.Write(rows, new (string Set, string Name)[0]);
        var withDelivered = CsvTableWriter.Write(rows, new (string Set, string Name)[0], _ => "Yes");

        Assert.StartsWith("File Name,Qty,Status", plain);
        Assert.StartsWith("File Name,Qty,Delivered,Status", withDelivered);
        Assert.Contains("Plate.ipt,2,Yes,", withDelivered);
    }

    // --------------------------------------------------------- config

    [Fact]
    public void Package_SurvivesRoundTrip()
    {
        var task = VendorTask(new PackageSpec
        {
            Table = { TableFormat.Xlsx, TableFormat.Csv },
            Columns = { new BomColumn { Name = "Vendor" } },
            Rollup = new RollupOptions { Mode = RollupMode.Occurrence },
            Zip = true
        });
        var config = new ExportConfig { Tasks = { task } };

        var loaded = ExportConfigJson.FromJson(ExportConfigJson.ToJson(config));

        var pkg = loaded.Tasks[0].Package!;
        Assert.Equal(new[] { TableFormat.Xlsx, TableFormat.Csv }, pkg.Table);
        Assert.Equal("Vendor", Assert.Single(pkg.Columns).Name);
        Assert.Equal(RollupMode.Occurrence, pkg.Rollup!.Mode);
        Assert.True(pkg.Zip);
    }

    [Fact]
    public void NoPackage_IsNotWritten_AndReadsAsNull()
    {
        var config = new ExportConfig
        {
            Tasks = { new ExportTask { Name = "A", Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } } } }
        };

        string json = ExportConfigJson.ToJson(config);

        Assert.DoesNotContain("Package", json);
        Assert.Null(ExportConfigJson.FromJson(json).Tasks[0].Package);
    }

    [Fact]
    public void Package_DefaultsToNoZip()
    {
        var c = ExportConfigJson.FromJson(Wrap("""{ "Table": [ "Xlsx" ] }"""));
        Assert.False(c.Tasks[0].Package!.Zip);
    }

    [Fact]
    public void UnknownTableFormat_IsRejected() =>
        Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson(Wrap("""{ "Table": [ "Xlxs" ] }""")));

    [Fact]
    public void MisspelledPackageName_IsRejected() =>
        Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson(Wrap("""{ "Tabel": [ "Xlsx" ] }""")));

    [Fact]
    public void PackageThatDoesNothing_IsRejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson(Wrap("{}")));
        Assert.Contains("does nothing", ex.Message);
    }

    [Fact]
    public void ColumnsWithoutATable_AreRejected() =>
        Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("""{ "Zip": true, "Columns": [ { "Name": "Vendor" } ] }""")));

    [Fact]
    public void DuplicateTableFormat_IsRejected() =>
        Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("""{ "Table": [ "Csv", "Csv" ] }""")));

    [Fact]
    public void BadPackageColumn_IsRejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("""{ "Table": [ "Csv" ], "Columns": [ { "Name": "Vendor", "Set": "Design Tracking Property" } ] }""")));
        Assert.Contains("unknown Set", ex.Message);
    }
}