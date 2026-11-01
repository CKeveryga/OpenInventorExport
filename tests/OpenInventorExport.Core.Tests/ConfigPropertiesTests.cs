using System.IO;
using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ConfigPropertiesTests
{
    private static string Wrap(string propertiesJson) => $$"""
        {
          "SchemaVersion": 1,
          "Tasks": [ { "Name": "A", "Outputs": [ { "Format": "Step", "Source": "Model" } ] } ],
          "Properties": {{propertiesJson}}
        }
        """;

    [Fact]
    public void Properties_SurviveRoundTrip()
    {
        var config = new ExportConfig
        {
            Tasks = { new ExportTask { Name = "A", Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } } } },
            Properties =
            {
                new SchemaProperty
                {
                    Set = PropertySetNames.UserDefined,
                    Name = "Finish",
                    DataType = PropertyDataType.Text,
                    Required = true,
                    AppliesTo = { "Part", "SheetMetal" }
                }
            }
        };

        var loaded = ExportConfigJson.FromJson(ExportConfigJson.ToJson(config));

        var p = Assert.Single(loaded.Properties);
        Assert.Equal(PropertySetNames.UserDefined, p.Set);
        Assert.Equal("Finish", p.Name);
        Assert.True(p.Required);
        Assert.Equal(new[] { "Part", "SheetMetal" }, p.AppliesTo);
    }

    [Fact]
    public void SetDefaultsToDesignTracking_AndTypeReadsFromText()
    {
        var loaded = ExportConfigJson.FromJson(Wrap("""[ { "Name": "Material", "DataType": "Text" } ]"""));
        Assert.Equal(PropertySetNames.DesignTracking, loaded.Properties[0].Set);
    }

    [Fact]
    public void NoPropertiesSection_IsFine()
    {
        const string json = """
            { "SchemaVersion": 1, "Tasks": [ { "Name": "A", "Outputs": [ { "Format": "Step", "Source": "Model" } ] } ] }
            """;
        Assert.Empty(ExportConfigJson.FromJson(json).Properties);
    }

    [Fact]
    public void UnknownSet_IsRejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("""[ { "Name": "Finish", "Set": "Design Tracking Property" } ]""")));
        Assert.Contains("unknown Set", ex.Message);
    }

    [Fact]
    public void BlankName_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("""[ { "Name": " " } ]""")));
    }

    [Fact]
    public void DuplicateProperty_IsRejected_ButSameNameInDifferentSetsIsFine()
    {
        Assert.Throws<InvalidDataException>(() =>
            ExportConfigJson.FromJson(Wrap("""[ { "Name": "Finish" }, { "Name": "finish" } ]""")));

        var ok = ExportConfigJson.FromJson(Wrap("""
            [ { "Name": "Part Number" },
              { "Name": "Part Number", "Set": "Inventor User Defined Properties" } ]
            """));
        Assert.Equal(2, ok.Properties.Count);
    }

    [Fact]
    public void ToPropertySchema_FeedsTheValidator()
    {
        var config = ExportConfigJson.FromJson(Wrap("""[ { "Name": "Finish", "Set": "Inventor User Defined Properties", "Required": true } ]"""));

        var s = new Snapshot();
        s.Components.Add(new ComponentRecord { OccurrencePath = "A", FileName = "A.ipt", FullPath = @"C:\x\A.ipt", DocumentType = "Part", Level = 1 });

        var issues = PropertySchemaValidator.Validate(s, config.ToPropertySchema());

        Assert.Equal(PropertyIssueKind.Missing, issues.Single().Kind);
    }
}