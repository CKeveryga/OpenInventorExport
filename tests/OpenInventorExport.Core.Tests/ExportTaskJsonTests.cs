using System.IO;
using OpenInventorExport.Core;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class ExportTaskJsonTests
{
    [Fact]
    public void Task_SurvivesRoundTripThroughJson()
    {
        var original = new ExportTask
        {
            Name = "Drawings",
            FolderTemplate = "{task}/{format}",
            Selection =
            {
                DocumentTypes = { "SheetMetal" },
                Filters = { new PropertyFilter { Property = "Material", Operator = FilterOperator.Contains, Value = "steel" } }
            },
            Outputs =
            {
                new OutputSpec { Format = ExportFormat.Pdf, Source = ExportSource.Drawing, PdfSheets = PdfSheetMode.FirstOnly },
                new OutputSpec { Format = ExportFormat.Dxf, Source = ExportSource.Drawing, IniFile = @"config\dxf-one-sheet.ini" }
            }
        };

        var loaded = ExportTaskJson.FromJson(ExportTaskJson.ToJson(original));

        Assert.Equal("Drawings", loaded.Name);
        Assert.Equal("{task}/{format}", loaded.FolderTemplate);
        Assert.Equal("SheetMetal", Assert.Single(loaded.Selection.DocumentTypes));
        Assert.Equal(FilterOperator.Contains, loaded.Selection.Filters[0].Operator);
        Assert.Equal(2, loaded.Outputs.Count);
        Assert.Equal(PdfSheetMode.FirstOnly, loaded.Outputs[0].PdfSheets);
        Assert.Equal(@"config\dxf-one-sheet.ini", loaded.Outputs[1].IniFile);
    }

    [Fact]
    public void MisspelledName_IsRejected_NotIgnored()
    {
        const string json = """
            { "Name": "T", "Selection": { "Filtres": [] },
              "Outputs": [ { "Format": "Step", "Source": "Model" } ] }
            """;
        Assert.Throws<InvalidDataException>(() => ExportTaskJson.FromJson(json));
    }

    [Fact]
    public void UnknownFormatName_IsRejected()
    {
        const string json = """
            { "Name": "T", "Outputs": [ { "Format": "Stepp", "Source": "Model" } ] }
            """;
        Assert.Throws<InvalidDataException>(() => ExportTaskJson.FromJson(json));
    }

    [Fact]
    public void InvalidCombination_IsRejectedByTheValidator()
    {
        const string json = """
            { "Name": "T", "Outputs": [ { "Format": "Pdf", "Source": "Model" } ] }
            """;
        var ex = Assert.Throws<InvalidDataException>(() => ExportTaskJson.FromJson(json));
        Assert.Contains("cannot be exported", ex.Message);
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreAllowed()
    {
        const string json = """
            {
              // one STEP per file
              "Name": "Models",
              "Outputs": [ { "Format": "Step", "Source": "Model" }, ],
            }
            """;
        Assert.Equal("Models", ExportTaskJson.FromJson(json).Name);
    }
}