using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class RollupTests
{
    private static ComponentRecord Rec(string path, string? parent, string file, string type, int level) =>
        new()
        {
            OccurrencePath = path,
            ParentPath = parent,
            FileName = file,
            FullPath = @"C:\x\" + file,
            DocumentType = type,
            Level = level,
            BomStructure = "kNormalBOMStructure"
        };

    private static Snapshot Sample()
    {
        var s = new Snapshot();
        s.Components.Add(Rec("Top.iam", null, "Top.iam", "Assembly", 0));
        foreach (var n in new[] { 1, 2 })
        {
            string sub = $"Top.iam/Sub:{n}";
            s.Components.Add(Rec(sub, "Top.iam", "Sub.iam", "Assembly", 1));
            s.Components.Add(Rec(sub + "/Bolt:1", sub, "Bolt.ipt", "Part", 2));
            s.Components.Add(Rec(sub + "/Bolt:2", sub, "Bolt.ipt", "Part", 2));
        }
        s.Components.Add(Rec("Top.iam/Plate:1", "Top.iam", "Plate.ipt", "Part", 1));
        return s;
    }

    [Fact]
    public void Occurrence_GivesOneRowPerOccurrence()
    {
        var rows = Rollup.Apply(Sample(), new RollupOptions { Mode = RollupMode.Occurrence });
        Assert.Equal(7, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Quantity));
    }

    [Fact]
    public void Flat_PartsOnly_MultipliesThroughLevels()
    {
        var rows = Rollup.Apply(Sample(), new RollupOptions
        {
            Mode = RollupMode.Flat,
            IncludeAssemblies = false
        });
        Assert.Equal(2, rows.Count);
        Assert.Equal(4, rows.Single(r => r.Component.FileName == "Bolt.ipt").Quantity);
        Assert.Equal(1, rows.Single(r => r.Component.FileName == "Plate.ipt").Quantity);
    }

    [Fact]
    public void Structured_CountsWithinEachParent()
    {
        var rows = Rollup.Apply(Sample(), new RollupOptions { Mode = RollupMode.Structured });
        Assert.Equal(3, rows.Count);
        Assert.Equal(2, rows.Single(r => r.Component.FileName == "Sub.iam").Quantity);
        Assert.Equal(2, rows.Single(r => r.Component.FileName == "Bolt.ipt").Quantity);
        Assert.Equal(5, rows.Sum(r => r.Quantity));
    }

    [Fact]
    public void Csv_EscapesCommasAndQuotes()
    {
        var rec = Rec("Top.iam/X:1", "Top.iam", "X.ipt", "Part", 1);
        rec.SetProperty(PropertySetNames.DesignTracking, "Description", new PropertyValue { Value = "Bolt, 5\" long" });
        var csv = CsvTableWriter.Write(
            new[] { new ExportRow(rec, 1, null) },
            new[] { (PropertySetNames.DesignTracking, "Description") });
        Assert.Contains("\"Bolt, 5\"\" long\"", csv);
    }

    [Fact]
    public void Structured_ListsChildrenUnderEachDistinctParentChain()
    {
        var s = Sample(); // Top holds Sub x2, each with 2 bolts, plus a plate
        s.Components.Add(Rec("Top.iam/Other:1", "Top.iam", "Other.iam", "Assembly", 1));
        s.Components.Add(Rec("Top.iam/Other:1/Sub:1", "Top.iam/Other:1", "Sub.iam", "Assembly", 2));
        s.Components.Add(Rec("Top.iam/Other:1/Sub:1/Bolt:1", "Top.iam/Other:1/Sub:1", "Bolt.ipt", "Part", 3));
        s.Components.Add(Rec("Top.iam/Other:1/Sub:1/Bolt:2", "Top.iam/Other:1/Sub:1", "Bolt.ipt", "Part", 3));

        var rows = Rollup.Apply(s, new RollupOptions { Mode = RollupMode.Structured });

        // Bolt now appears under two different Sub placements
        Assert.Equal(2, rows.Count(r => r.Component.FileName == "Bolt.ipt"));
    }

    [Fact]
    public void Inseparable_AsSingleItem_KeepsAssemblyHidesChildren()
    {
        var s = Sample();
        foreach (var c in s.Components.Where(c => c.FileName == "Sub.iam"))
            c.BomStructure = "kInseparableBOMStructure";

        var rows = Rollup.Apply(s, new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false });

        Assert.DoesNotContain(rows, r => r.Component.FileName == "Bolt.ipt");
        Assert.Equal(2, rows.Single(r => r.Component.FileName == "Sub.iam").Quantity);
    }

    [Fact]
    public void Inseparable_IncludeChildren_ListsEverything()
    {
        var s = Sample();
        foreach (var c in s.Components.Where(c => c.FileName == "Sub.iam"))
            c.BomStructure = "kInseparableBOMStructure";

        var rows = Rollup.Apply(s, new RollupOptions
        {
            Mode = RollupMode.Flat,
            IncludeAssemblies = false,
            Inseparable = InseparableHandling.IncludeChildren
        });

        Assert.Equal(4, rows.Single(r => r.Component.FileName == "Bolt.ipt").Quantity);
    }

    [Fact]
    public void Structured_DefaultsToListingInseparableChildren()
    {
        var s = Sample();
        foreach (var c in s.Components.Where(c => c.FileName == "Sub.iam"))
            c.BomStructure = "kInseparableBOMStructure";

        var rows = Rollup.Apply(s, new RollupOptions { Mode = RollupMode.Structured });

        Assert.Contains(rows, r => r.Component.FileName == "Bolt.ipt");
    }

    [Fact]
    public void Root_IsExcludedByDefault()
    {
        var rows = Rollup.Apply(Sample(), new RollupOptions { Mode = RollupMode.Occurrence });
        Assert.DoesNotContain(rows, r => r.Component.Level == 0);
    }

    [Fact]
    public void IncludeRoot_IsIgnoredWhenAssembliesAreExcluded()
    {
        var rows = Rollup.Apply(Sample(), new RollupOptions
        {
            Mode = RollupMode.Flat,
            IncludeAssemblies = false,
            IncludeRoot = true
        });

        Assert.DoesNotContain(rows, r => r.Component.Level == 0);
        Assert.Equal(4, rows.Single(r => r.Component.FileName == "Bolt.ipt").Quantity);
    }

    [Fact]
    public void IncludeRoot_AddsTopLevelAssemblyWithQtyOne_WhenAssembliesAreIncluded()
    {
        var rows = Rollup.Apply(Sample(), new RollupOptions
        {
            Mode = RollupMode.Flat,
            IncludeAssemblies = true,
            IncludeRoot = true
        });

        Assert.Equal(1, rows.Single(r => r.Component.FileName == "Top.iam").Quantity);
    }

    [Fact]
    public void IncludeRoot_Structured_KeepsChildGroupingIntact()
    {
        var rows = Rollup.Apply(Sample(), new RollupOptions
        {
            Mode = RollupMode.Structured,
            IncludeRoot = true
        });

        Assert.Equal(4, rows.Count);                          // root + Sub + Bolt + Plate
        Assert.Equal(6, rows.Sum(r => r.Quantity));           // previous 5 + root
    }

    private static ComponentRecord WithPn(ComponentRecord c, string pn)
    {
        c.SetProperty(PropertySetNames.DesignTracking, "Part Number",
            new PropertyValue { DataType = PropertyDataType.Text, Value = pn });
        return c;
    }

    private static Snapshot TwoFilesOnePartNumber()
    {
        var s = new Snapshot();
        s.Components.Add(Rec("Top.iam", null, "Top.iam", "Assembly", 0));
        s.Components.Add(WithPn(Rec("Top.iam/A:1", "Top.iam", "A1.ipt", "Part", 1), "X-1"));
        s.Components.Add(WithPn(Rec("Top.iam/A:2", "Top.iam", "A2.ipt", "Part", 1), "X-1"));
        return s;
    }

    [Fact]
    public void Flat_PartNumberKey_MergesDifferentFilesWithTheSamePartNumber()
    {
        var rows = Rollup.Apply(TwoFilesOnePartNumber(),
            new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false });

        Assert.Equal(2, Assert.Single(rows).Quantity);
    }

    [Fact]
    public void Flat_FilePathKey_KeepsDifferentFilesSeparate()
    {
        var rows = Rollup.Apply(TwoFilesOnePartNumber(),
            new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false, Key = RollupKey.FilePath });

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void PartNumberKey_EmptyPartNumber_FallsBackToTheFile()
    {
        // Sample() has no part numbers: Bolt.ipt x4 and Plate.ipt x1 must stay two rows.
        var rows = Rollup.Apply(Sample(), new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false });

        Assert.Equal(2, rows.Count);
        Assert.Equal(4, rows.Single(r => r.Component.FileName == "Bolt.ipt").Quantity);
    }

    [Fact]
    public void Structured_PartNumberKey_MergesWithinAParent_ButListsUnderEachAssembly()
    {
        var s = new Snapshot();
        s.Components.Add(Rec("Top.iam", null, "Top.iam", "Assembly", 0));
        s.Components.Add(WithPn(Rec("Top.iam/S1:1", "Top.iam", "S1.iam", "Assembly", 1), "S-1"));
        s.Components.Add(WithPn(Rec("Top.iam/S2:1", "Top.iam", "S2.iam", "Assembly", 1), "S-2"));
        // S1 holds two different files with the same Part Number; S2 holds one of them.
        s.Components.Add(WithPn(Rec("Top.iam/S1:1/A:1", "Top.iam/S1:1", "A1.ipt", "Part", 2), "X-1"));
        s.Components.Add(WithPn(Rec("Top.iam/S1:1/A:2", "Top.iam/S1:1", "A2.ipt", "Part", 2), "X-1"));
        s.Components.Add(WithPn(Rec("Top.iam/S2:1/A:1", "Top.iam/S2:1", "A1.ipt", "Part", 2), "X-1"));

        var rows = Rollup.Apply(s, new RollupOptions { Mode = RollupMode.Structured });

        var xRows = rows.Where(r => r.Component.Level == 2).ToList();
        Assert.Equal(2, xRows.Count);   // once under S1, once under S2
        Assert.Equal(new[] { 1, 2 }, xRows.Select(r => r.Quantity).OrderBy(q => q).ToArray());
    }
}