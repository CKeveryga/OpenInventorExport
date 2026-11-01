using System.IO;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ConfigRollupTests
{
    private static string Wrap(string sections) => $$"""
        {
          "SchemaVersion": 1,
          "Tasks": [ { "Name": "A", "Outputs": [ { "Format": "Step", "Source": "Model" } ] } ],
          {{sections}}
        }
        """;

    [Fact]
    public void NoRollupSection_GivesTheDefaults()
    {
        var c = ExportConfigJson.FromJson(Wrap("\"Properties\": []"));

        Assert.Equal(RollupMode.Flat, c.Rollup.Mode);
        Assert.Equal(RollupKey.PartNumber, c.Rollup.Key);
        Assert.Empty(c.BomColumns);
    }

    [Fact]
    public void RollupSection_IsRead()
    {
        var c = ExportConfigJson.FromJson(Wrap("""
            "Rollup": { "Mode": "Structured", "Key": "FilePath", "IncludeAssemblies": false, "Inseparable": "IncludeChildren" }
            """));

        Assert.Equal(RollupMode.Structured, c.Rollup.Mode);
        Assert.Equal(RollupKey.FilePath, c.Rollup.Key);
        Assert.False(c.Rollup.IncludeAssemblies);
        Assert.Equal(InseparableHandling.IncludeChildren, c.Rollup.Inseparable);
    }

    [Fact]
    public void RollupAndColumns_SurviveRoundTrip()
    {
        var config = new ExportConfig
        {
            Tasks = { new ExportTask { Name = "A", Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } } } },
            Rollup = new RollupOptions { Mode = RollupMode.Occurrence, Key = RollupKey.FilePath, IncludeRoot = true },
            BomColumns = { new BomColumn { Name = "Finish", Set = PropertySetNames.UserDefined } }
        };

        var loaded = ExportConfigJson.FromJson(ExportConfigJson.ToJson(config));

        Assert.Equal(RollupMode.Occurrence, loaded.Rollup.Mode);
        Assert.Equal(RollupKey.FilePath, loaded.Rollup.Key);
        Assert.True(loaded.Rollup.IncludeRoot);
        Assert.Equal("Finish", Assert.Single(loaded.BomColumns).Name);
        Assert.Equal(PropertySetNames.UserDefined, loaded.BomColumns[0].Set);
    }

    [Fact]
    public void MisspelledRollupName_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("\"Rollup\": { \"Moode\": \"Flat\" }")));
    }

    [Fact]
    public void UnknownKeyValue_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("\"Rollup\": { \"Key\": \"PartNumbr\" }")));
    }

    [Fact]
    public void BadColumns_AreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("\"BomColumns\": [ { \"Name\": \" \" } ]")));
        Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("\"BomColumns\": [ { \"Name\": \"Finish\", \"Set\": \"Design Tracking Property\" } ]")));
        Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("\"BomColumns\": [ { \"Name\": \"Material\" }, { \"Name\": \"material\" } ]")));
    }

    [Fact]
    public void Columns_FeedTheCsvWriter()
    {
        var c = ExportConfigJson.FromJson(Wrap("""
            "BomColumns": [ { "Name": "Material" }, { "Name": "Finish", "Set": "Inventor User Defined Properties" } ]
            """));
        var rec = new ComponentRecord { FileName = "A.ipt" };
        rec.SetProperty(PropertySetNames.DesignTracking, "Material", new PropertyValue { Value = "Steel" });

        var csv = CsvTableWriter.Write(new[] { new ExportRow(rec, 3, null) }, c.ToCsvColumns());

        Assert.Contains("File Name,Qty,Material,Finish", csv);
        Assert.Contains("A.ipt,3,Steel,", csv);
    }
}