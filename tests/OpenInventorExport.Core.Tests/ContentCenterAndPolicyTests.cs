using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ContentCenterAndPolicyTests
{
    private static ComponentRecord Rec(string path, string? parent, string file, string type, int level, bool? cc) => new()
    {
        OccurrencePath = path,
        ParentPath = parent,
        FileName = file,
        FullPath = @"C:\x\" + file,
        DocumentType = type,
        Level = level,
        BomStructure = level == 0 ? null : "kNormalBOMStructure",
        IsContentCenter = cc
    };

    private static Snapshot Sample()
    {
        var s = new Snapshot();
        s.Components.Add(Rec("Top.iam", null, "Top.iam", "Assembly", 0, null));
        s.Components.Add(Rec("Top.iam/Plate:1", "Top.iam", "Plate.ipt", "Part", 1, false));
        s.Components.Add(Rec("Top.iam/Bolt:1", "Top.iam", "Bolt.ipt", "Part", 1, true));
        s.Components.Add(Rec("Top.iam/Bolt:2", "Top.iam", "Bolt.ipt", "Part", 1, true));
        s.Components.Add(Rec("Top.iam/Old:1", "Top.iam", "Old.ipt", "Part", 1, null));   // flag not recorded
        return s;
    }

    private static RollupOptions Flat(ContentCenterMode mode) =>
        new RollupOptions { Mode = RollupMode.Flat, IncludeAssemblies = false, ContentCenter = mode };

    private static ExportTask PartsTask(ContentCenterMode mode)
    {
        var t = new ExportTask
        {
            Name = "T",
            Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } }
        };
        t.Selection.DocumentTypes.Add("Part");
        t.Selection.ContentCenter = mode;
        return t;
    }

    private const string OneTask = """
        "Tasks": [ { "Name": "A", "Outputs": [ { "Format": "Step", "Source": "Model" } ] } ]
        """;

    // -------------------------------------------------- Content Center

    [Fact]
    public void Rollup_Include_ChangesNothing()
    {
        var excl = new List<ExclusionRecord>();
        var rows = Rollup.Apply(Sample(), Flat(ContentCenterMode.Include), excl);

        Assert.Equal(3, rows.Count);
        Assert.Empty(excl);
    }

    [Fact]
    public void Rollup_Exclude_DropsContentCenter_AndReportsEachOccurrence()
    {
        var excl = new List<ExclusionRecord>();
        var rows = Rollup.Apply(Sample(), Flat(ContentCenterMode.Exclude), excl);

        Assert.DoesNotContain(rows, r => r.Component.FileName == "Bolt.ipt");
        Assert.Contains(rows, r => r.Component.FileName == "Old.ipt");   // not recorded = kept
        Assert.Equal(2, excl.Count);
        Assert.All(excl, e => Assert.Equal(ExclusionReason.ContentCenter, e.Reason));
    }

    [Fact]
    public void Rollup_Only_KeepsJustContentCenter_AndSaysWhenTheFlagWasNotRecorded()
    {
        var excl = new List<ExclusionRecord>();
        var rows = Rollup.Apply(Sample(), Flat(ContentCenterMode.Only), excl);

        Assert.Equal(2, Assert.Single(rows).Quantity);
        Assert.Equal(2, excl.Count);   // Plate and Old
        Assert.Contains("not recorded", excl.Single(e => e.FileName == "Old.ipt").Detail);
    }

    [Fact]
    public void Planner_Exclude_SkipsTheFile_AndReportsItOnce()
    {
        var excl = new List<ExclusionRecord>();
        var jobs = ExportPlanner.Plan(Sample(), PartsTask(ContentCenterMode.Exclude), @"C:\out", excl);

        Assert.Equal(2, jobs.Count);
        Assert.DoesNotContain(jobs, j => j.ModelPath.EndsWith("Bolt.ipt"));
        var e = Assert.Single(excl, x => x.Reason == ExclusionReason.ContentCenter);
        Assert.Equal("Bolt.ipt", e.FileName);
        Assert.Equal(ExclusionReason.ContentCenter, e.Reason);
        Assert.Equal("T", e.Context);
    }

    [Fact]
    public void Planner_Only_SelectsJustContentCenterFiles()
    {
        var excl = new List<ExclusionRecord>();
        var jobs = ExportPlanner.Plan(Sample(), PartsTask(ContentCenterMode.Only), @"C:\out", excl);

        Assert.EndsWith("Bolt.ipt", Assert.Single(jobs).ModelPath);
        Assert.Equal(2, excl.Count(e => e.Reason == ExclusionReason.ContentCenter));   // Plate, Old
    }

    [Fact]
    public void NotRecorded_IgnoresSuppressedUnresolvedAndAssemblies()
    {
        var s = Sample();   // Old.ipt is the one real "not recorded" part
        s.Components.Add(new ComponentRecord { OccurrencePath = "S", FileName = "S.ipt", DocumentType = "Part", IsSuppressed = true });
        s.Components.Add(new ComponentRecord { OccurrencePath = "U", FileName = "U.ipt", DocumentType = "Part", IsUnresolved = true });

        Assert.Equal(1, ContentCenterFilter.NotRecorded(s));
    }

    [Fact]
    public void Config_ReadsContentCenter_InTasksAndRollup()
    {
        const string json = """
            { "SchemaVersion": 1,
              "Tasks": [ { "Name": "A", "Selection": { "ContentCenter": "Exclude" },
                           "Outputs": [ { "Format": "Step", "Source": "Model" } ] } ],
              "Rollup": { "ContentCenter": "Only" } }
            """;

        var c = ExportConfigJson.FromJson(json);

        Assert.Equal(ContentCenterMode.Exclude, c.Tasks[0].Selection.ContentCenter);
        Assert.Equal(ContentCenterMode.Only, c.Rollup.ContentCenter);
    }

    [Fact]
    public void Config_RejectsAMisspelledContentCenterValue()
    {
        const string json = """
            { "SchemaVersion": 1,
              "Tasks": [ { "Name": "A", "Selection": { "ContentCenter": "Excluude" },
                           "Outputs": [ { "Format": "Step", "Source": "Model" } ] } ] }
            """;

        Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson(json));
    }

    // ----------------------------------------------------- Unresolved

    [Fact]
    public void Unresolved_DefaultsToBlock()
    {
        var c = ExportConfigJson.FromJson("{ \"SchemaVersion\": 1, " + OneTask + " }");
        Assert.Equal(UnresolvedPolicy.Block, c.Unresolved);
    }

    [Fact]
    public void Unresolved_WarnIsRead_AndSurvivesRoundTrip()
    {
        var c = ExportConfigJson.FromJson("{ \"SchemaVersion\": 1, \"Unresolved\": \"Warn\", " + OneTask + " }");
        Assert.Equal(UnresolvedPolicy.Warn, c.Unresolved);

        var again = ExportConfigJson.FromJson(ExportConfigJson.ToJson(c));
        Assert.Equal(UnresolvedPolicy.Warn, again.Unresolved);
    }

    [Fact]
    public void Unresolved_RejectsAnUnknownValue()
    {
        Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson("{ \"SchemaVersion\": 1, \"Unresolved\": \"Skip\", " + OneTask + " }"));
    }

    [Fact]
    public void ForUnreadable_GivesAnUnreadableLine_ThatIsCounted()
    {
        var k = new SkippedRecord
        {
            OccurrencePath = "Top.iam/Gone:1",
            Reason = SkipReason.FileMissing,
            Detail = @"Referenced file could not be opened: C:\x\Gone.ipt"
        };

        var r = ExportResult.ForUnreadable(k);

        Assert.Equal(ExportStatus.Unreadable, r.Status);
        Assert.Equal("Top.iam/Gone:1", r.ModelPath);
        Assert.Contains("Gone.ipt", r.Detail);
        Assert.Equal("Unreadable: 1", ExportResultLog.Summary(new[] { r }).Single());
    }
}