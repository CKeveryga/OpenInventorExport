using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class NumericFilterTests
{
    private static ComponentRecord Rec(string name, string? length)
    {
        var c = new ComponentRecord
        {
            OccurrencePath = name,
            FileName = name,
            FullPath = @"C:\x\" + name,
            DocumentType = "Part",
            Level = 1,
            BomStructure = "kNormalBOMStructure"
        };
        if (length != null)
            c.SetProperty(PropertySetNames.UserDefined, "Length", new PropertyValue { Value = length });
        return c;
    }

    private static ExportTask Task(FilterOperator op, string value)
    {
        var t = new ExportTask { Name = "T", Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } } };
        t.Selection.Filters.Add(new PropertyFilter { Set = PropertySetNames.UserDefined, Property = "Length", Operator = op, Value = value });
        return t;
    }

    private static Snapshot Sample()
    {
        var s = new Snapshot();
        s.Components.Add(Rec("Short.ipt", "50"));
        s.Components.Add(Rec("Exact.ipt", "200"));
        s.Components.Add(Rec("Long.ipt", "350.5"));
        s.Components.Add(Rec("Text.ipt", "abc"));
        s.Components.Add(Rec("None.ipt", null));
        return s;
    }

    private static string[] Names(FilterOperator op, string value) =>
        ExportPlanner.Plan(Sample(), Task(op, value), @"C:\out")
            .Select(j => System.IO.Path.GetFileName(j.ModelPath)).OrderBy(n => n).ToArray();

    [Fact] public void LessThan() => Assert.Equal(new[] { "Short.ipt" }, Names(FilterOperator.LessThan, "200"));
    [Fact] public void LessThanOrEqual() => Assert.Equal(new[] { "Exact.ipt", "Short.ipt" }, Names(FilterOperator.LessThanOrEqual, "200"));
    [Fact] public void GreaterThan() => Assert.Equal(new[] { "Long.ipt" }, Names(FilterOperator.GreaterThan, "200"));
    [Fact] public void GreaterThanOrEqual() => Assert.Equal(new[] { "Exact.ipt", "Long.ipt" }, Names(FilterOperator.GreaterThanOrEqual, "200"));

    [Fact]
    public void NonNumericAndMissingValues_FailTheFilter_AndAreReported()
    {
        var excl = new System.Collections.Generic.List<ExclusionRecord>();
        ExportPlanner.Plan(Sample(), Task(FilterOperator.LessThan, "1000"), @"C:\out", excl);

        Assert.Contains("not a number", excl.Single(e => e.FileName == "Text.ipt").Detail);
        Assert.Contains(excl, e => e.FileName == "None.ipt");
    }

    [Fact]
    public void NumericOperator_WithANonNumericValue_IsRejectedOnLoad() =>
        Assert.Contains(ExportTaskValidator.Validate(Task(FilterOperator.LessThan, "heavy")), p => p.Contains("must be a number"));

    [Fact]
    public void NumericOperators_ReadFromJsonText()
    {
        var t = ExportTaskJson.FromJson("""
            { "Name": "T", "Selection": { "Filters": [ { "Property": "Length", "Operator": "LessThan", "Value": "200" } ] },
              "Outputs": [ { "Format": "Step", "Source": "Model" } ] }
            """);
        Assert.Equal(FilterOperator.LessThan, t.Selection.Filters[0].Operator);
    }
}