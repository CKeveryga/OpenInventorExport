using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

/// <summary>Writes the table outputs (Csv, Xlsx) of a run. No console output: the caller reports progress.</summary>
public static class ExportTables
{
    /// <summary>
    /// Writes each planned table. Every table lists all the files its task selects, whatever was exported.
    /// Returns exactly one result per table job (ModelPath "(table)"), in the same order.
    /// </summary>
    /// <param name="onResult">Optional progress callback, called after each table.</param>
    public static List<ExportResult> Write(
        Snapshot snapshot, ExportConfig config, IEnumerable<ExportTask> tasks,
        IEnumerable<TableJob> tableJobs, Action<ExportResult>? onResult = null)
    {
        var taskList = tasks.ToList();
        var results = new List<ExportResult>();

        foreach (var job in tableJobs)
        {
            var result = new ExportResult
            {
                TaskName = job.TaskName,
                Format = job.Output.Format,
                ModelPath = "(table)",
                OutputPath = job.OutputPath
            };
            results.Add(result);

            if (job.State == JobState.Conflict)
            {
                result.Status = ExportStatus.Conflict;
                result.Detail = job.Detail;
            }
            else
            {
                try
                {
                    var task = taskList.First(t => t.Name == job.TaskName);
                    var rows = TaskTable.Rows(snapshot, task, job.Output, config);
                    var columns = TaskTable.Columns(job.Output, config);

                    Directory.CreateDirectory(Path.GetDirectoryName(job.OutputPath)!);
                    if (job.Output.Format == ExportFormat.Xlsx)
                        XlsxTableWriter.Write(job.OutputPath, rows, columns);
                    else
                        // UTF-8 with BOM so Excel opens the CSV correctly.
                        File.WriteAllText(job.OutputPath, CsvTableWriter.Write(rows, columns), new UTF8Encoding(true));

                    result.Status = ExportStatus.Exported;
                    result.Detail = $"{rows.Count} row(s)";
                }
                catch (Exception ex)
                {
                    result.Status = ExportStatus.Failed;
                    result.Detail = ex.Message;
                }
            }

            onResult?.Invoke(result);
        }
        return results;
    }
}