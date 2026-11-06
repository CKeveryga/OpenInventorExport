using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenInventorExport.Core;

public static class ExportTaskValidator
{
    private static readonly HashSet<string> KnownTokens = new HashSet<string> { "task", "format", "type" };

    /// <summary>Returns a list of problems. Empty = the task is valid.</summary>
    public static List<string> Validate(ExportTask task)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(task.Name))
            problems.Add("Task has no name.");
        if (task.Outputs.Count == 0)
            problems.Add($"Task '{task.Name}' has no outputs.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var o in task.Outputs)
        {
            bool table = ExportPlanner.IsTable(o.Format);

            bool ok = o.Format switch
            {
                ExportFormat.Step => o.Source == ExportSource.Model,
                ExportFormat.Dwf => true,
                ExportFormat.Csv => true,
                ExportFormat.Xlsx => true,
                _ => o.Source == ExportSource.Drawing   // Pdf, Dwg, Dxf
            };
            if (!ok)
                problems.Add($"{o.Format} cannot be exported from a {o.Source}.");

            string folder = (o.Folder ?? "").Trim();
            string normal = folder.Replace('\\', '/');
            if (Path.IsPathRooted(folder) || normal.Split('/').Contains(".."))
                problems.Add($"Task '{task.Name}': output Folder '{o.Folder}' must be a relative path without '..'.");
            if (ForbiddenIn(folder) is { } badFolder)
                problems.Add($"Task '{task.Name}': output Folder '{o.Folder}' has characters Windows does not allow in a folder name: {badFolder}.");

            if (!table && (o.Columns.Count > 0 || !string.IsNullOrWhiteSpace(o.Name) || o.Mode != null || o.Key != null))
                problems.Add($"Task '{task.Name}': Name, Columns, Mode and Key only apply to Csv and Xlsx outputs, not {o.Format}.");
            if (table && !string.IsNullOrWhiteSpace(o.Name) && o.Name!.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                problems.Add($"Task '{task.Name}': table name '{o.Name}' has characters not allowed in a file name.");

            string key = o.Format + "|" + normal.Trim('/') + "|" + (table ? o.Name ?? "" : "");
            if (!seen.Add(key))
                problems.Add($"Task '{task.Name}': two {o.Format} outputs would write to the same folder" +
                             (table ? " with the same name." : "."));
        }

        string template = task.FolderTemplate ?? "";
        if (Path.IsPathRooted(template) || template.Contains(".."))
            problems.Add("FolderTemplate must be a relative path without '..'.");
        if (ForbiddenIn(template) is { } badTemplate)
            problems.Add($"Task '{task.Name}': FolderTemplate '{template}' has characters Windows does not allow in a folder name: {badTemplate}.");
        foreach (Match m in Regex.Matches(template, @"\{([^}]*)\}"))
            if (!KnownTokens.Contains(m.Groups[1].Value))
                problems.Add($"Unknown token {m.Value} in FolderTemplate.");
        // {type} with a table is fine: a table covers the whole task, so ExportPlanner.PlanTables leaves out
        // every folder level of the FolderTemplate that contains {type}.

        foreach (var type in task.Selection.DocumentTypes.Where(x => !DocumentTypes.IsKnown(x)))
            problems.Add($"Unknown document type '{type}'. Known types: {string.Join(", ", DocumentTypes.All)}.");

        foreach (var f in task.Selection.Filters)
        {
            if (string.IsNullOrWhiteSpace(f.Property))
                problems.Add($"Task '{task.Name}': a filter has no Property.");
            if (ExportPlanner.IsNumeric(f.Operator))
            {
                if (!Units.TryParse(f.Value ?? "", out _, out string unit))
                    problems.Add($"Task '{task.Name}': filter on '{f.Property}' uses {f.Operator}, so Value must be a number, optionally with a unit ({Units.SupportedList}).");
                else if (unit.Length > 0 && !Units.IsKnown(unit))
                    problems.Add($"Task '{task.Name}': filter on '{f.Property}' has the unit '{unit}', which is not supported ({Units.SupportedList}).");
            }
        }

        problems.AddRange(BomStructurePolicy.Validate(task.Selection.BomStructures, $"Task '{task.Name}'"));

        return problems;
    }

    // Characters Windows does not allow in a file or folder name. A fixed list (not Path.GetInvalidFileNameChars)
    // so the result is the same on every platform. '/' and '\' are allowed: they separate nested folders.
    private static readonly char[] ForbiddenFolderChars = { '<', '>', ':', '"', '|', '?', '*' };

    /// <summary>The forbidden characters found in a folder path, e.g. "'?', '*'", or null when there are none.</summary>
    private static string? ForbiddenIn(string path)
    {
        var bad = path
            .Where(c => ForbiddenFolderChars.Contains(c) || char.IsControl(c))
            .Distinct()
            .Select(c => char.IsControl(c) ? $"control character {(int)c}" : $"'{c}'")
            .ToList();
        return bad.Count == 0 ? null : string.Join(", ", bad);
    }
}