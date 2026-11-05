using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

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
        sb.AppendLine("Task,Format,Status,Model,Drawing,Output,Detail,Seconds,OpenSeconds");
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
                r.Detail ?? "",
                r.Seconds?.ToString("F1", CultureInfo.InvariantCulture) ?? "",
                r.OpenSeconds?.ToString("F1", CultureInfo.InvariantCulture) ?? ""
            };
            sb.AppendLine(string.Join(",", cells.Select(CsvTableWriter.Escape)));
        }
        return sb.ToString();
    }

    /// <summary>One line per status, e.g. "Exported: 40".</summary>
    public static IEnumerable<string> Summary(IEnumerable<ExportResult> results) =>
        results.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}");

    /// <summary>
    /// One line per format. Only jobs that were exported and timed count: a quick NoDrawing failure
    /// would otherwise pull the average down.
    /// </summary>
    public static IEnumerable<string> TimingSummary(IEnumerable<ExportResult> results)
    {
        var timed = results.Where(r => r.Status == ExportStatus.Exported && r.Seconds != null);
        foreach (var g in timed.GroupBy(r => r.Format?.ToString() ?? "(none)").OrderBy(g => g.Key))
        {
            double total = g.Sum(r => r.Seconds!.Value);
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0}: {1} job(s), {2:F1} s total, {3:F1} s average, slowest {4:F1} s",
                g.Key, g.Count(), total, total / g.Count(), g.Max(r => r.Seconds!.Value));

            var opens = g.Where(r => r.OpenSeconds != null).Select(r => r.OpenSeconds!.Value).ToList();
            if (opens.Count > 0)
                line += string.Format(CultureInfo.InvariantCulture,
                    ", opening the file {0:F1} s average", opens.Average());
            yield return line;
        }
    }
}