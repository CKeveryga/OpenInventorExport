using System.Text.Json;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class SnapshotSerializationTests
{
    [Fact]
    public void Snapshot_SurvivesRoundTripThroughJson()
    {
        var original = new Snapshot
        {
            InventorVersion = "2027",
            SourceFile = "TopLevel.iam"
        };

        var component = new ComponentRecord
        {
            OccurrencePath = "TopLevel/Bracket:1",
            FileName = "Bracket.ipt",
            DocumentType = "Part",
            Quantity = 2
        };
        // Same name in two sets must stay separate
        component.SetProperty(PropertySetNames.DesignTracking, "Part Number",
            new PropertyValue { DataType = PropertyDataType.Text, Value = "BRK-001" });
        component.SetProperty(PropertySetNames.UserDefined, "Part Number",
            new PropertyValue { DataType = PropertyDataType.Text, Value = "CUSTOM-9" });
        original.Components.Add(component);

        original.Skipped.Add(new SkippedRecord
        {
            OccurrencePath = "TopLevel/Bolt:3",
            Reason = SkipReason.Suppressed,
            Detail = "Component is suppressed"
        });

        string json = JsonSerializer.Serialize(original);
        Snapshot? loaded = JsonSerializer.Deserialize<Snapshot>(json);

        Assert.NotNull(loaded);
        Assert.Equal("2027", loaded!.InventorVersion);
        Assert.Single(loaded.Components);
        Assert.Equal("Bracket.ipt", loaded.Components[0].FileName);
        Assert.Equal(2, loaded.Components[0].Quantity);
        Assert.Equal("BRK-001", loaded.Components[0].FindProperty(PropertySetNames.DesignTracking, "Part Number")!.Value);
        Assert.Equal("CUSTOM-9", loaded.Components[0].FindProperty(PropertySetNames.UserDefined, "Part Number")!.Value);
        Assert.Single(loaded.Skipped);
        Assert.Equal(SkipReason.Suppressed, loaded.Skipped[0].Reason);
    }

    [Fact]
    public void FromJson_RejectsSnapshotsWithAnOlderSchemaVersion()
    {
        Assert.Throws<InvalidDataException>(() => SnapshotJson.FromJson("{\"SchemaVersion\":1}"));
    }
}