using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenInventorExport.Core;

/// <summary>Writes the results of an export run as CSV (for people) or JSON (for tools).</summary>
public static class ExportResultLog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ToJson(IEnumerable<ExportResult> results) =>
        JsonSerializer.Serialize(results.ToList(), Options);

    public static string ToCsv(IEnumerable<ExportResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Task,Format,Status,Model,Drawing,Output,Detail");
        foreach (var r in results)
        {
            var cells = new[]
            {
                r.TaskName,
                r.Format?.ToString() ?? "",
                r.Status.ToString(),
                r.ModelPath,
                r.DrawingPath ?? "",
                r.OutputPath ?? "",
                r.Detail ?? ""
            };
            sb.AppendLine(string.Join(",", cells.Select(CsvTableWriter.Escape)));
        }
        return sb.ToString();
    }

    /// <summary>One line per status, e.g. "Exported: 40".</summary>
    public static IEnumerable<string> Summary(IEnumerable<ExportResult> results) =>
        results.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}");
}