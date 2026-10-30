namespace OpenInventorExport.Core;

public enum PdfSheetMode { AllInOne, FirstOnly }

public enum ExportStatus { Exported, NoDrawing, DrawingMismatch, Failed, Conflict }

/// <summary>The outcome of one file-export task. Every task produces one of these, so nothing goes missing silently.</summary>
public class ExportResult
{
    public string TaskName { get; set; } = "";
    public ExportFormat? Format { get; set; }
    public string ModelPath { get; set; } = "";
    public string? DrawingPath { get; set; }
    /// <summary>The planned output path. Only a file on disk if Status is Exported.</summary>
    public string? OutputPath { get; set; }
    public ExportStatus Status { get; set; }
    public string? Detail { get; set; }

    /// <summary>
    /// Starts a result for a job. Conflict jobs become Conflict; every other job starts as
    /// Failed ("Not run") so a job that never executed can never look like a success.
    /// </summary>
    public static ExportResult ForJob(ExportJob job)
    {
        var r = new ExportResult
        {
            TaskName = job.TaskName,
            Format = job.Output.Format,
            ModelPath = job.ModelPath,
            OutputPath = job.OutputPath
        };
        if (job.State == JobState.Conflict)
        {
            r.Status = ExportStatus.Conflict;
            r.Detail = job.Detail;
        }
        else
        {
            r.Status = ExportStatus.Failed;
            r.Detail = "Not run";
        }
        return r;
    }
}