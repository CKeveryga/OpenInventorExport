using OpenInventorExport.Core.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenInventorExport.Core;

public class ExportTables
{
    /// <summary>
    /// Writes each planned table. Every table lists all the files its task selects, whatever was exported,
    /// and gets its own line in the export log (ModelPath "(table)").
    /// </summary>
    public static void WriteTables(Snapshot snapshot, ExportConfig config, IEnumerable<ExportTask> tasks,
        List<TableJob> tableJobs, List<ExportResult> results)
    {
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
                Console.WriteLine($"[{result.Status}] {job.TaskName}: table {job.OutputPath} {job.Detail}");
                continue;
            }

            try
            {
                var task = tasks.First(t => t.Name == job.TaskName);
                var rows = TaskTable.Rows(snapshot, task, job.Output, config);
                var columns = TaskTable.Columns(job.Output, config);

                Directory.CreateDirectory(Path.GetDirectoryName(job.OutputPath)!);
                if (job.Output.Format == ExportFormat.Xlsx)
                    XlsxTableWriter.Write(job.OutputPath, rows, columns);
                else
                    File.WriteAllText(job.OutputPath, CsvTableWriter.Write(rows, columns), new UTF8Encoding(true));

                result.Status = ExportStatus.Exported;
                result.Detail = $"{rows.Count} row(s)";
            }
            catch (Exception ex)
            {
                result.Status = ExportStatus.Failed;
                result.Detail = ex.Message;
            }
            Console.WriteLine($"[{result.Status}] {job.TaskName}: table {job.OutputPath} {result.Detail}");
        }
    }
}
