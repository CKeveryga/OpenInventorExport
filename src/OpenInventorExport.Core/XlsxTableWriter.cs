using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

/// <summary>Writes the BOM table (and, if given, the exclusion report) as an .xlsx workbook.</summary>
public static class XlsxTableWriter
{
    public const string BomSheet = "BOM";
    public const string ExcludedSheet = "Left out";

    /// <param name="exclusions">Optional. If it has records, they go on a second sheet.</param>
    public static void Write(
    string path,
    IEnumerable<ExportRow> rows,
    IReadOnlyList<(string Set, string Name)> propertyColumns,
    IEnumerable<ExclusionRecord>? exclusions = null,
    Func<ExportRow, string>? delivered = null)
    {
        using (var wb = new XLWorkbook())
        {
            WriteBom(wb.Worksheets.Add(BomSheet), rows, propertyColumns, delivered);

            var excluded = exclusions?.ToList();
            if (excluded != null && excluded.Count > 0)
                WriteExclusions(wb.Worksheets.Add(ExcludedSheet), excluded);

            wb.SaveAs(path);
        }
    }

    private static void WriteBom(IXLWorksheet ws, IEnumerable<ExportRow> rows,
        IReadOnlyList<(string Set, string Name)> propertyColumns, Func<ExportRow, string>? delivered)
    {
        var header = new List<string> { "File Name", "Qty" };
        header.AddRange(propertyColumns.Select(c => c.Name));
        if (delivered != null) header.Add("Delivered");
        header.Add("Status");
        WriteHeader(ws, header);

        int next = 3 + propertyColumns.Count;
        int deliveredCol = delivered != null ? next : 0;
        int statusCol = delivered != null ? next + 1 : next;

        int r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.Component.FileName;
            ws.Cell(r, 2).Value = (double)row.Quantity;

            for (int i = 0; i < propertyColumns.Count; i++)
            {
                var col = propertyColumns[i];
                SetTyped(ws.Cell(r, 3 + i), row.Component.FindProperty(col.Set, col.Name));
            }

            if (delivered != null)
            {
                string d = delivered(row);
                ws.Cell(r, deliveredCol).Value = d;
                if (!d.StartsWith("Yes"))
                    ws.Range(r, 1, r, statusCol).Style.Fill.BackgroundColor = XLColor.LightSalmon;
            }

            if (row.Component.IsUnresolved)
            {
                ws.Cell(r, statusCol).Value = "Unresolved";
                ws.Range(r, 1, r, statusCol).Style.Fill.BackgroundColor = XLColor.LightYellow;
            }
            r++;
        }
        Finish(ws);
    }

    private static void WriteExclusions(IXLWorksheet ws, List<ExclusionRecord> records)
    {
        WriteHeader(ws, new List<string> { "Context", "Reason", "Occurrence", "File", "Detail" });
        int r = 2;
        foreach (var e in records)
        {
            ws.Cell(r, 1).Value = e.Context;
            ws.Cell(r, 2).Value = e.Reason.ToString();
            ws.Cell(r, 3).Value = e.OccurrencePath;
            ws.Cell(r, 4).Value = e.FileName;
            ws.Cell(r, 5).Value = e.Detail ?? "";
            r++;
        }
        Finish(ws);
    }

    /// <summary>Number and Date properties become real cells; everything else stays text (so "001" keeps its zeros).</summary>
    private static void SetTyped(IXLCell cell, PropertyValue? pv)
    {
        string? text = pv?.Value;
        if (string.IsNullOrEmpty(text)) return;

        if (pv!.DataType == PropertyDataType.Number &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
        {
            cell.Value = n;
        }
        else if (pv.DataType == PropertyDataType.Date &&
                 DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
        {
            cell.Value = d;
            cell.Style.DateFormat.Format = "yyyy-mm-dd";
        }
        else
        {
            cell.Value = text;
        }
    }

    private static void WriteHeader(IXLWorksheet ws, List<string> header)
    {
        for (int i = 0; i < header.Count; i++)
            ws.Cell(1, i + 1).Value = header[i];
        ws.Row(1).Style.Font.Bold = true;
    }

    private static void Finish(IXLWorksheet ws)
    {
        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()!.SetAutoFilter();
        ws.Columns().AdjustToContents();
    }
}