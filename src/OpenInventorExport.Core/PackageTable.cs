using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

/// <summary>The table that goes with a task's files (its "package").</summary>
public static class PackageTable
{
    /// <summary>
    /// The BOM rollup (the package's own Rollup, else the config's), kept only for rows whose file the task selects.
    /// If one row merges several files (same Part Number), the row stands for its first file.
    /// The root assembly is listed only if the rollup's IncludeRoot is on.
    /// </summary>
    public static List<ExportRow> Rows(Snapshot snapshot, ExportTask task, ExportConfig config)
    {
        var options = task.Package?.Rollup ?? config.Rollup;
        var selected = ExportPlanner.SelectedFiles(snapshot, task);

        return Rollup.Apply(snapshot, options)
            .Where(r => r.Component.FullPath != null
                     && selected.Contains(PathUtil.StripMemberSuffix(r.Component.FullPath)))
            .ToList();
    }

    /// <summary>The package's own columns, or the config's BomColumns if it names none.</summary>
    public static List<(string Set, string Name)> Columns(ExportTask task, ExportConfig config)
    {
        var cols = task.Package != null && task.Package.Columns.Count > 0
            ? task.Package.Columns
            : config.BomColumns;
        return cols.Select(c => (c.Set, c.Name)).ToList();
    }

    /// <summary>
    /// "Yes" if every job of this task for the row's file was exported, else "No (Dxf: NoDrawing; ...)".
    /// A vendor table must never claim a file that is not in the package.
    /// </summary>
    public static Func<ExportRow, string> Delivered(ExportTask task, IEnumerable<ExportResult> results)
    {
        var byModel = results
            .Where(r => r.TaskName == task.Name)
            .GroupBy(r => r.ModelPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return row =>
        {
            string model = row.Component.FullPath == null ? "" : PathUtil.StripMemberSuffix(row.Component.FullPath);
            if (!byModel.TryGetValue(model, out var list))
                return "No (no export job)";
            var bad = list.Where(r => r.Status != ExportStatus.Exported).ToList();
            return bad.Count == 0
                ? "Yes"
                : "No (" + string.Join("; ", bad.Select(r => $"{r.Format}: {r.Status}")) + ")";
        };
    }

    /// <summary>A task name made safe to use as a file or folder name.</summary>
    public static string SafeName(string name)
    {
        foreach (char bad in Path.GetInvalidFileNameChars())
            name = name.Replace(bad, '_');
        return name;
    }
}