using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ContentCenterFlagTests
{
    [Fact]
    public void OlderSnapshot_WithoutTheField_ReadsAsNotRecorded()
    {
        const string json = """
            { "SchemaVersion": 2, "Components": [ { "OccurrencePath": "A", "FileName": "A.ipt" } ] }
            """;
        var s = SnapshotJson.FromJson(json)!;
        Assert.Null(s.Components[0].IsContentCenter);
    }

    [Fact]
    public void Flag_SurvivesRoundTrip()
    {
        var s = new Snapshot();
        s.Components.Add(new ComponentRecord { OccurrencePath = "A", FileName = "A.ipt", IsContentCenter = true });
        s.Components.Add(new ComponentRecord { OccurrencePath = "B", FileName = "B.ipt", IsContentCenter = false });

        var loaded = SnapshotJson.FromJson(SnapshotJson.ToJson(s))!;

        Assert.True(loaded.Components[0].IsContentCenter);
        Assert.False(loaded.Components[1].IsContentCenter);
    }

    [Fact]
    public void Summary_CountsContentCenterParts()
    {
        var s = new Snapshot();
        s.Components.Add(new ComponentRecord { OccurrencePath = "A", FileName = "A.ipt", DocumentType = "Part", IsContentCenter = true });
        s.Components.Add(new ComponentRecord { OccurrencePath = "B", FileName = "B.ipt", DocumentType = "Part" });

        Assert.Contains(SnapshotSummary.Describe(s), l => l.StartsWith("Content Center parts: 1"));
    }
}