using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenInventorExport.Core;

public static class CsvTableWriter
{
    public static string Write(IEnumerable<ExportRow> rows, IReadOnlyList<(string Set, string Name)> propertyColumns)
    {
        var sb = new StringBuilder();
        var header = new List<string> { "File Name", "Qty" };
        header.AddRange(propertyColumns.Select(c => c.Name));
        sb.AppendLine(string.Join(",", header.Select(Escape)));

        foreach (var row in rows)
        {
            var cells = new List<string>
            {
                row.Component.FileName,
                row.Quantity.ToString()
            };
            foreach (var col in propertyColumns)
                cells.Add(row.Component.FindProperty(col.Set, col.Name)?.Value ?? "");
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }
        return sb.ToString();
    }

    internal static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}