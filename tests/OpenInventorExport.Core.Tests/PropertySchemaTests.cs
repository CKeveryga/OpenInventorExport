using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class PropertySchemaTests
{
    private static Snapshot With(string path, string type, string? prop = null, PropertyValue? value = null)
    {
        var s = new Snapshot();
        var c = new ComponentRecord { OccurrencePath = path, FileName = path, FullPath = path, DocumentType = type, Level = 1 };
        if (prop != null) c.SetProperty(PropertySetNames.DesignTracking, prop, value!);
        s.Components.Add(c);
        return s;
    }

    private static PropertySchema Schema(bool required = false, params string[] appliesTo)
    {
        var def = new SchemaProperty { Name = "Finish", DataType = PropertyDataType.Text, Required = required };
        def.AppliesTo.AddRange(appliesTo);
        return new PropertySchema { Properties = { def } };
    }

    [Fact]
    public void MissingProperty_IsReported()
    {
        var issues = PropertySchemaValidator.Validate(With(@"C:\x\A.ipt", "Part"), Schema());
        Assert.Equal(PropertyIssueKind.Missing, issues.Single().Kind);
    }

    [Fact]
    public void WrongType_IsReported()
    {
        var s = With(@"C:\x\A.ipt", "Part", "Finish", new PropertyValue { DataType = PropertyDataType.Number, Value = "3" });
        Assert.Equal(PropertyIssueKind.WrongType, PropertySchemaValidator.Validate(s, Schema()).Single().Kind);
    }

    [Fact]
    public void EmptyValue_OnlyReportedWhenRequired()
    {
        var s = With(@"C:\x\A.ipt", "Part", "Finish", new PropertyValue { DataType = PropertyDataType.Text, Value = "" });
        Assert.Empty(PropertySchemaValidator.Validate(s, Schema(required: false)));
        Assert.Equal(PropertyIssueKind.Empty, PropertySchemaValidator.Validate(s, Schema(required: true)).Single().Kind);
    }

    [Fact]
    public void AppliesTo_SkipsOtherDocumentTypes()
    {
        var issues = PropertySchemaValidator.Validate(With(@"C:\x\A.ipt", "Part"), Schema(false, "SheetMetal"));
        Assert.Empty(issues);
    }

    [Fact]
    public void SameFileTwice_IsCheckedOnce()
    {
        var s = With(@"C:\x\A.ipt", "Part");
        s.Components.Add(s.Components[0]);
        Assert.Single(PropertySchemaValidator.Validate(s, Schema()));
    }
}