using System.Collections.Generic;
using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class UnitFilterTests
{
    private const string Dt = PropertySetNames.DesignTracking;
    private const string Ud = PropertySetNames.UserDefined;

    private static ExportTask Task(string set, string prop, FilterOperator op, string wanted)
    {
        var t = new ExportTask { Name = "T", Outputs = { new OutputSpec { Format = ExportFormat.Step, Source = ExportSource.Model } } };
        t.Selection.Filters.Add(new PropertyFilter { Set = set, Property = prop, Operator = op, Value = wanted });
        return t;
    }

    private static Snapshot With(string set, string prop, string stored)
    {
        var s = new Snapshot();
        var c = new ComponentRecord
        {
            OccurrencePath = "P",
            FileName = "P.ipt",
            FullPath = @"C:\x\P.ipt",
            DocumentType = "Part",
            Level = 1,
            BomStructure = "kNormalBOMStructure"
        };
        c.SetProperty(set, prop, new PropertyValue { Value = stored });
        s.Components.Add(c);
        return s;
    }

    private static bool Selected(string set, string prop, string stored, FilterOperator op, string wanted) =>
        ExportPlanner.Plan(With(set, prop, stored), Task(set, prop, op, wanted), @"C:\out").Count == 1;

    [Fact]
    public void ExportedParameterText_InInches_ComparesWithMillimetres()
    {
        Assert.True(Selected(Ud, "Height", "2.000 in", FilterOperator.LessThan, "60 mm"));      // 50.8 mm
        Assert.False(Selected(Ud, "Height", "2.000 in", FilterOperator.LessThan, "50 mm"));
        Assert.True(Selected(Ud, "Height", "2.000 in", FilterOperator.GreaterThan, "50 mm"));
    }

    [Fact]
    public void Mass_IsStoredInGrams_AndComparesWithPounds()
    {
        const string stored = "92.61968572800001";   // Inventor displayed 0.204 lbmass
        Assert.True(Selected(Dt, "Mass", stored, FilterOperator.LessThan, "0.25 lb"));
        Assert.False(Selected(Dt, "Mass", stored, FilterOperator.LessThan, "0.2 lb"));
        Assert.True(Selected(Dt, "Mass", stored, FilterOperator.LessThan, "100"));   // no unit: raw grams
    }

    [Fact]
    public void FlatPatternLength_IsStoredInCentimetres()
    {
        Assert.True(Selected(Dt, "Flat Pattern Length", "7.619999999999999", FilterOperator.LessThan, "3.1 in"));   // 7.874 cm
        Assert.False(Selected(Dt, "Flat Pattern Length", "7.619999999999999", FilterOperator.LessThan, "2.9 in"));   // 7.366 cm
    }

    [Fact]
    public void PlainNumber_WithNoUnit_StillComparesRaw()
    {
        Assert.True(Selected(Ud, "Plain", "2", FilterOperator.LessThan, "5"));
    }

    [Theory]
    [InlineData("2", "5 mm", "plain number")]            // stored has no unit
    [InlineData("2.000 in", "5 kg", "Length")]           // wrong kind
    [InlineData("2.000 in", "5", "needs a unit")]        // stored has a unit, filter has none
    [InlineData("2.000 furlong", "5 mm", "not supported")]
    public void Mismatches_FailTheFilter_AndSayWhy(string stored, string wanted, string reasonPart)
    {
        var excl = new List<ExclusionRecord>();
        var jobs = ExportPlanner.Plan(With(Ud, "X", stored), Task(Ud, "X", FilterOperator.LessThan, wanted), @"C:\out", excl);

        Assert.Empty(jobs);
        Assert.Contains(reasonPart, excl.Single().Detail);
    }

    [Fact]
    public void UnknownFilterUnit_IsRejectedOnLoad() =>
        Assert.Contains(ExportTaskValidator.Validate(Task(Ud, "X", FilterOperator.LessThan, "5 furlongs")),
            p => p.Contains("not supported"));

    [Fact]
    public void FilterValueWithAUnit_IsAcceptedOnLoad() =>
        Assert.Empty(ExportTaskValidator.Validate(Task(Ud, "X", FilterOperator.LessThan, "200 mm")));
}