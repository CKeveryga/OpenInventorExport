using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        foreach (var o in task.Outputs)
        {
            bool ok = o.Format switch
            {
                ExportFormat.Step => o.Source == ExportSource.Model,
                ExportFormat.Dwf => true,
                _ => o.Source == ExportSource.Drawing   // Pdf, Dwg, Dxf
            };
            if (!ok)
                problems.Add($"{o.Format} cannot be exported from a {o.Source}.");
        }

        string t = task.FolderTemplate ?? "";
        if (Path.IsPathRooted(t) || t.Contains(".."))
            problems.Add("FolderTemplate must be a relative path without '..'.");
        foreach (Match m in Regex.Matches(t, @"\{([^}]*)\}"))
            if (!KnownTokens.Contains(m.Groups[1].Value))
                problems.Add($"Unknown token {m.Value} in FolderTemplate.");

        foreach (var type in task.Selection.DocumentTypes.Where(t => !DocumentTypes.IsKnown(t)))
            problems.Add($"Unknown document type '{type}'. Known types: {string.Join(", ", DocumentTypes.All)}.");

        problems.AddRange(BomStructurePolicy.Validate(task.Selection.BomStructures, $"Task '{task.Name}'"));

        return problems;
    }
}