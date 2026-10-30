namespace OpenInventorExport.Core;

public enum PdfSheetMode { AllInOne, FirstOnly }

public enum ExportStatus { Exported, NoDrawing, DrawingMismatch, Failed, Conflict }

/// <summary>The outcome of one file-export task. Every task produces one of these, so nothing goes missing silently.</summary>
public class ExportResult
{
    public string ModelPath { get; set; } = "";
    public string? DrawingPath { get; set; }
    public string? OutputPath { get; set; }
    public ExportStatus Status { get; set; }
    public string? Detail { get; set; }
}