using System.IO;
using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class DocumentTypesTests
{
    [Fact]
    public void IsKnown_IgnoresCase_AndRejectsTypos()
    {
        Assert.True(DocumentTypes.IsKnown("sheetmetal"));
        Assert.False(DocumentTypes.IsKnown("Sheet Metal"));
    }

    [Fact]
    public void TaskWithUnknownDocumentType_IsRejected()
    {
        const string json = """
            { "Name": "T", "Selection": { "DocumentTypes": [ "Sheet Metal" ] },
              "Outputs": [ { "Format": "Step", "Source": "Model" } ] }
            """;
        var ex = Assert.Throws<InvalidDataException>(() => ExportTaskJson.FromJson(json));
        Assert.Contains("Sheet Metal", ex.Message);
    }

    [Fact]
    public void PropertyWithUnknownAppliesTo_IsRejected()
    {
        const string json = """
            { "SchemaVersion": 1,
              "Tasks": [ { "Name": "A", "Outputs": [ { "Format": "Step", "Source": "Model" } ] } ],
              "Properties": [ { "Name": "Finish", "AppliesTo": [ "Sheet Metal" ] } ] }
            """;
        Assert.Throws<InvalidDataException>(() => ExportConfigJson.FromJson(json));
    }

    [Fact]
    public void Summary_WarnsAboutUnrecognizedTypes()
    {
        var s = new Snapshot();
        s.Components.Add(new ComponentRecord { OccurrencePath = "A", FileName = "A", DocumentType = "kSomethingNewObject" });
        Assert.Contains(SnapshotSummary.Describe(s), l => l.StartsWith("WARNING") && l.Contains("kSomethingNewObject"));
    }
}