using System;
using System.IO;
using ClosedXML.Excel;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class XlsxTableWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oie-xlsx-" + Guid.NewGuid().ToString("N"));
    public XlsxTableWriterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static ExportRow Row(string file, int qty, string pn, bool unresolved = false)
    {
        var c = new ComponentRecord { FileName = file, IsUnresolved = unresolved };
        c.SetProperty(PropertySetNames.DesignTracking, "Part Number",
            new PropertyValue { DataType = PropertyDataType.Text, Value = pn });
        c.SetProperty(PropertySetNames.DesignTracking, "Cost",
            new PropertyValue { DataType = PropertyDataType.Number, Value = "12.5" });
        return new ExportRow(c, qty, null);
    }

    private static readonly (string Set, string Name)[] Cols =
    {
        (PropertySetNames.DesignTracking, "Part Number"),
        (PropertySetNames.DesignTracking, "Cost")
    };

    [Fact]
    public void Cells_HaveTheRightTypes_AndTextKeepsLeadingZeros()
    {
        string path = Path.Combine(_dir, "a.xlsx");
        XlsxTableWriter.Write(path, new[] { Row("A.ipt", 3, "001") }, Cols);

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet(XlsxTableWriter.BomSheet);

        Assert.Equal("File Name", ws.Cell(1, 1).GetString());
        Assert.True(ws.Cell(1, 1).Style.Font.Bold);
        Assert.Equal(XLDataType.Number, ws.Cell(2, 2).DataType);
        Assert.Equal(3, ws.Cell(2, 2).GetDouble());
        Assert.Equal(XLDataType.Text, ws.Cell(2, 3).DataType);
        Assert.Equal("001", ws.Cell(2, 3).GetString());
        Assert.Equal(XLDataType.Number, ws.Cell(2, 4).DataType);
    }

    [Fact]
    public void UnresolvedRow_SaysUnresolvedInStatus()
    {
        string path = Path.Combine(_dir, "b.xlsx");
        XlsxTableWriter.Write(path, new[] { Row("Gone.ipt", 1, "X", unresolved: true) }, Cols);

        using var wb = new XLWorkbook(path);
        Assert.Equal("Unresolved", wb.Worksheet(XlsxTableWriter.BomSheet).Cell(2, 5).GetString());
    }

    [Fact]
    public void ExclusionsGoOnASecondSheet_OnlyWhenThereAreAny()
    {
        string none = Path.Combine(_dir, "c.xlsx");
        XlsxTableWriter.Write(none, new[] { Row("A.ipt", 1, "1") }, Cols, new ExclusionRecord[0]);
        using (var wb = new XLWorkbook(none))
            Assert.False(wb.Worksheets.Contains(XlsxTableWriter.ExcludedSheet));

        string some = Path.Combine(_dir, "d.xlsx");
        var excl = new[] { new ExclusionRecord { Context = "BOM", Reason = ExclusionReason.BomStructure, OccurrencePath = "P", FileName = "P.ipt", Detail = "Phantom" } };
        XlsxTableWriter.Write(some, new[] { Row("A.ipt", 1, "1") }, Cols, excl);
        using (var wb = new XLWorkbook(some))
            Assert.Equal("BomStructure", wb.Worksheet(XlsxTableWriter.ExcludedSheet).Cell(2, 2).GetString());
    }
}