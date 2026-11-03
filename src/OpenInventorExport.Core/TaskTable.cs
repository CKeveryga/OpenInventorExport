using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

/// <summary>The table (Csv/Xlsx output) of a task: one row per file the task selects, wherever the table sits.</summary>
public static class TaskTable
{
    /// <summary>
    /// The config's rollup Mode and Key, with the task's own BOM structure and Content Center rules, and with
    /// assemblies and the root switched on: the task's selection decides what is listed, not the BOM view.
    /// Children of inseparable assemblies are kept for the same reason.
    /// </summary>
    public static List<ExportRow> Rows(Snapshot snapshot, ExportTask task, OutputSpec output, ExportConfig config)
    {
        var cfg = config.Rollup;
        var options = new RollupOptions
        {
            Mode = output.Mode ?? cfg.Mode,
            Key = output.Key ?? cfg.Key,
            ExcludeSuppressed = cfg.ExcludeSuppressed,
            IncludeAssemblies = true,
            IncludeRoot = true,
            Inseparable = InseparableHandling.IncludeChildren,
            ContentCenter = task.Selection.ContentCenter,
            BomStructures = task.Selection.BomStructures
        };

        var selected = ExportPlanner.SelectedFiles(snapshot, task);
        return Rollup.Apply(snapshot, options)
            .Where(r => r.Component.FullPath != null
                     && selected.Contains(PathUtil.StripMemberSuffix(r.Component.FullPath)))
            .ToList();
    }

    /// <summary>The output's own columns, or the config's BomColumns if it names none.</summary>
    public static List<(string Set, string Name)> Columns(OutputSpec output, ExportConfig config)
    {
        var cols = output.Columns.Count > 0 ? output.Columns : config.BomColumns;
        return cols.Select(c => (c.Set, c.Name)).ToList();
    }
}