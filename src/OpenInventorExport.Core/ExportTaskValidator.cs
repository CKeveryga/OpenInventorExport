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

        if (task.Package != null)
        {
            var pkg = task.Package;
            string who = $"Task '{task.Name}' package";

            if (pkg.Table.Count == 0 && !pkg.Zip)
                problems.Add($"{who}: does nothing. Add a Table format or set Zip.");
            if (pkg.Table.Count == 0 && (pkg.Columns.Count > 0 || pkg.Rollup != null))
                problems.Add($"{who}: Columns or Rollup are set but Table is empty, so no table would be written.");
            if (pkg.Table.Distinct().Count() != pkg.Table.Count)
                problems.Add($"{who}: Table lists a format more than once.");
            if (pkg.Rollup != null)
                problems.AddRange(BomStructurePolicy.Validate(pkg.Rollup.BomStructures, who));
        }

        return problems;
    }
}