using System.IO;
using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class BomStructureTests
{
    private const string Normal = "kNormalBOMStructure";
    private const string Phantom = "kPhantomBOMStructure";
    private const string Reference = "kReferenceBOMStructure";
    private const string Purchased = "kPurchasedBOMStructure";
    private const string Inseparable = "kInseparableBOMStructure";

    private static ComponentRecord Rec(string path, string? parent, string file, string type, int level, string? bom = Normal) => new()
    {
        OccurrencePath = path,
        ParentPath = parent,
        FileName = file,
        FullPath = @"C:\x\" + file,
        DocumentType = type,
        Level = level,
        BomStructure = bom
    };

    /// <summary>Top holds Sub (with the given structure, containing 2 bolts) and a plate.</summary>
    private static Snapshot With(string subStructure)
    {
        var s = new Snapshot();
        s.Components.Add(Rec("Top.iam", null, "Top.iam", "Assembly", 0, null));
        s.Components.Add(Rec("Top.iam/Sub:1", "Top.iam", "Sub.iam", "Assembly", 1, subStructure));
        s.Components.Add(Rec("Top.iam/Sub:1/Bolt:1", "Top.iam/Sub:1", "Bolt.ipt", "Part", 2));
        s.Components.Add(Rec("Top.iam/Sub:1/Bolt:2", "Top.iam/Sub:1", "Bolt.ipt", "Part", 2));
        s.Components.Add(Rec("Top.iam/Plate:1", "Top.iam", "Plate.ipt", "Part", 1));
        return s;
    }

    private static RollupOptions Flat(params BomStructureRule[] rules)
    {
        var o = new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = true };
        o.BomStructures.AddRange(rules);
        return o;
    }

    private static ExportTask StepTask(params BomStructureRule[] rules)
    {
        var t = new ExportTask
        {
            Name = "T",
            Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } }
        };
        t.Selection.BomStructures.AddRange(rules);
        return t;
    }

    // ------------------------------------------------------------ BOM

    [Fact]
    public void Phantom_ItemIsLeftOut_ChildrenStay_AndItIsReported()
    {
        var excl = new System.Collections.Generic.List<ExclusionRecord>();
        var rows = Rollup.Apply(With(Phantom), Flat(), excl);

        Assert.DoesNotContain(rows, r => r.Component.FileName == "Sub.iam");
        Assert.Equal(2, rows.Single(r => r.Component.FileName == "Bolt.ipt").Quantity);
        var e = Assert.Single(excl);
        Assert.Equal(ExclusionReason.BomStructure, e.Reason);
        Assert.Equal("Top.iam/Sub:1", e.OccurrencePath);
        Assert.Equal("BOM", e.Context);
    }

    [Fact]
    public void Reference_ItemIsLeftOut()
    {
        var rows = Rollup.Apply(With(Reference), Flat());
        Assert.DoesNotContain(rows, r => r.Component.FileName == "Sub.iam");
    }

    [Fact]
    public void Purchased_ChildrenAreListedByDefault()
    {
        var rows = Rollup.Apply(With(Purchased), Flat());

        Assert.Equal(1, rows.Single(r => r.Component.FileName == "Sub.iam").Quantity);
        Assert.Equal(2, rows.Single(r => r.Component.FileName == "Bolt.ipt").Quantity);
    }

    [Fact]
    public void Purchased_ChildrenExcluded_WhenConfigured()
    {
        var excl = new System.Collections.Generic.List<ExclusionRecord>();
        var rows = Rollup.Apply(With(Purchased),
            Flat(new BomStructureRule { Structure = BomStructureKind.Purchased, Children = ChildHandling.Exclude }), excl);

        Assert.Equal(1, rows.Single(r => r.Component.FileName == "Sub.iam").Quantity);
        Assert.DoesNotContain(rows, r => r.Component.FileName == "Bolt.ipt");
        Assert.Equal(2, excl.Count);
        Assert.All(excl, e =>
        {
            Assert.Equal(ExclusionReason.ParentExcluded, e.Reason);
            Assert.Contains("Sub:1", e.Detail);
        });
    }

    [Fact]
    public void Phantom_CanBeIncluded()
    {
        var rows = Rollup.Apply(With(Phantom),
            Flat(new BomStructureRule { Structure = BomStructureKind.Phantom, Include = true }));

        Assert.Contains(rows, r => r.Component.FileName == "Sub.iam");
        Assert.Contains(rows, r => r.Component.FileName == "Bolt.ipt");
    }

    [Fact]
    public void OnlyTheNamedFieldChanges()
    {
        var excl = new System.Collections.Generic.List<ExclusionRecord>();
        var rows = Rollup.Apply(With(Phantom),
            Flat(new BomStructureRule { Structure = BomStructureKind.Phantom, Children = ChildHandling.Exclude }), excl);

        // Include was not named, so the phantom item stays out; its children now go too.
        Assert.DoesNotContain(rows, r => r.Component.FileName == "Sub.iam");
        Assert.DoesNotContain(rows, r => r.Component.FileName == "Bolt.ipt");
        Assert.Equal(3, excl.Count);   // the phantom + 2 bolts
    }

    [Fact]
    public void Inseparable_ExplicitChildrenRule_BeatsTheModeDefault()
    {
        // Flat normally hides the children of an inseparable assembly.
        var hidden = Rollup.Apply(With(Inseparable), Flat());
        Assert.DoesNotContain(hidden, r => r.Component.FileName == "Bolt.ipt");

        var shown = Rollup.Apply(With(Inseparable),
            Flat(new BomStructureRule { Structure = BomStructureKind.Inseparable, Children = ChildHandling.Include }));
        Assert.Equal(2, shown.Single(r => r.Component.FileName == "Bolt.ipt").Quantity);
    }

    [Fact]
    public void UnrecognizedStructure_IsListed()
    {
        var rows = Rollup.Apply(With("kSomethingNewBOMStructure"), Flat());
        Assert.Contains(rows, r => r.Component.FileName == "Sub.iam");
    }

    // -------------------------------------------------------- planner

    [Fact]
    public void Planner_PhantomFile_IsNotExportedByDefault_AndIsReported()
    {
        var excl = new System.Collections.Generic.List<ExclusionRecord>();
        var jobs = ExportPlanner.Plan(With(Phantom), StepTask(), @"C:\out", excl);

        Assert.DoesNotContain(jobs, j => j.ModelPath.EndsWith("Sub.iam"));
        Assert.Contains(jobs, j => j.ModelPath.EndsWith("Bolt.ipt"));   // phantom children stay
        Assert.Contains(excl, e => e.FileName == "Sub.iam" && e.Context == "T" && e.Reason == ExclusionReason.BomStructure);
    }

    [Fact]
    public void Planner_PhantomFile_IsExported_WhenIncluded()
    {
        var jobs = ExportPlanner.Plan(With(Phantom),
            StepTask(new BomStructureRule { Structure = BomStructureKind.Phantom, Include = true }), @"C:\out");

        Assert.Contains(jobs, j => j.ModelPath.EndsWith("Sub.iam"));
    }

    [Fact]
    public void Planner_FileIsKept_WhenAnotherOccurrenceSurvives()
    {
        var s = With(Purchased);
        s.Components.Add(Rec("Top.iam/Bolt:9", "Top.iam", "Bolt.ipt", "Part", 1));   // also directly under Top
        var excl = new System.Collections.Generic.List<ExclusionRecord>();

        var jobs = ExportPlanner.Plan(s,
            StepTask(new BomStructureRule { Structure = BomStructureKind.Purchased, Children = ChildHandling.Exclude }),
            @"C:\out", excl);

        Assert.Contains(jobs, j => j.ModelPath.EndsWith("Sub.iam"));    // the purchased assembly itself
        Assert.Contains(jobs, j => j.ModelPath.EndsWith("Bolt.ipt"));
        Assert.DoesNotContain(excl, e => e.FileName == "Bolt.ipt");
    }

    [Fact]
    public void Planner_FileIsLeftOut_WhenEveryOccurrenceIs()
    {
        var excl = new System.Collections.Generic.List<ExclusionRecord>();

        var jobs = ExportPlanner.Plan(With(Purchased),
            StepTask(new BomStructureRule { Structure = BomStructureKind.Purchased, Children = ChildHandling.Exclude }),
            @"C:\out", excl);

        Assert.DoesNotContain(jobs, j => j.ModelPath.EndsWith("Bolt.ipt"));
        var e = Assert.Single(excl, x => x.FileName == "Bolt.ipt");   // one record per unique file
        Assert.Equal(ExclusionReason.ParentExcluded, e.Reason);
    }

    [Fact]
    public void Planner_RecordsDocumentTypeReason()
    {
        var task = StepTask();
        task.Selection.DocumentTypes.Add("SheetMetal");
        var excl = new System.Collections.Generic.List<ExclusionRecord>();

        var jobs = ExportPlanner.Plan(With(Normal), task, @"C:\out", excl);

        Assert.Empty(jobs);
        Assert.Equal(4, excl.Count);   // Top, Sub, Bolt, Plate: one per unique file
        Assert.All(excl, e => Assert.Equal(ExclusionReason.DocumentType, e.Reason));
    }

    [Fact]
    public void Planner_RecordsPropertyFilterReason()
    {
        var task = StepTask();
        task.Selection.Filters.Add(new PropertyFilter { Property = "Vendor", Operator = FilterOperator.Equals, Value = "XXX" });
        var excl = new System.Collections.Generic.List<ExclusionRecord>();

        ExportPlanner.Plan(With(Normal), task, @"C:\out", excl);

        Assert.All(excl, e => Assert.Equal(ExclusionReason.PropertyFilter, e.Reason));
        Assert.Contains("Vendor", excl[0].Detail);
    }

    // ---------------------------------------------------------- config

    [Fact]
    public void DuplicateRules_AreRejected()
    {
        var task = StepTask(
            new BomStructureRule { Structure = BomStructureKind.Phantom },
            new BomStructureRule { Structure = BomStructureKind.Phantom, Include = true });

        Assert.Contains(ExportTaskValidator.Validate(task), p => p.Contains("more than once"));
    }

    [Fact]
    public void ConfigFile_ReadsRules()
    {
        const string json = """
            { "SchemaVersion": 1,
              "Tasks": [ { "Name": "A",
                           "Selection": { "BomStructures": [ { "Structure": "Purchased", "Children": "Exclude" } ] },
                           "Outputs": [ { "Format": "Step", "Source": "Model" } ] } ],
              "Rollup": { "BomStructures": [ { "Structure": "Phantom", "Include": true } ] } }
            """;

        var c = ExportConfigJson.FromJson(json);

        var taskRule = Assert.Single(c.Tasks[0].Selection.BomStructures);
        Assert.Equal(BomStructureKind.Purchased, taskRule.Structure);
        Assert.Equal(ChildHandling.Exclude, taskRule.Children);
        Assert.Null(taskRule.Include);
        Assert.True(Assert.Single(c.Rollup.BomStructures).Include);
    }

    [Fact]
    public void UnknownStructureName_IsRejected()
    {
        const string json = """
            { "SchemaVersion": 1,
              "Tasks": [ { "Name": "A", "Outputs": [ { "Format": "Step", "Source": "Model" } ] } ],
              "Rollup": { "BomStructures": [ { "Structure": "Phantomm" } ] } }
            """;

        Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson(json));
    }

    [Fact]
    public void Report_WritesCsvAndSummary()
    {
        var excl = new[]
        {
            new ExclusionRecord { Context = "T", Reason = ExclusionReason.BomStructure, OccurrencePath = "A", FileName = "A.ipt", Detail = "Phantom" },
            new ExclusionRecord { Context = "T", Reason = ExclusionReason.BomStructure, OccurrencePath = "B", FileName = "B.ipt", Detail = "Phantom" }
        };

        Assert.Contains("T,BomStructure,A,A.ipt,Phantom", ExclusionReport.ToCsv(excl));
        Assert.Equal("T / BomStructure: 2", ExclusionReport.Summary(excl).Single());
    }
}