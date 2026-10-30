using OpenInventorExport.Core;
using Inv = global::Inventor;

namespace OpenInventorExport.Inventor;

/// <summary>Executes the jobs produced by ExportPlanner. Returns exactly one result per job.</summary>
public static class JobRunner
{
    /// <param name="onResult">Optional progress callback, called after each job.</param>
    public static List<ExportResult> Run(IEnumerable<ExportJob> jobs, Action<ExportResult>? onResult = null)
    {
        var app = InventorConnection.GetRunningInventor();
        bool wasSilent = app.SilentOperation;
        app.SilentOperation = true;
        var results = new List<ExportResult>();
        try
        {
            foreach (var job in jobs)
            {
                var result = ExportResult.ForJob(job);   // Conflict jobs stay Conflict, never exported
                if (job.State == JobState.Planned)
                    RunOne(app, job, result);
                results.Add(result);
                onResult?.Invoke(result);
            }
        }
        finally
        {
            app.SilentOperation = wasSilent;
        }
        return results;
    }

    private static void RunOne(Inv.Application app, ExportJob job, ExportResult result)
    {
        Inv.Document? opened = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(job.OutputPath)!);

            bool fromDrawing = job.Output.Source == ExportSource.Drawing;
            string sourcePath;
            if (fromDrawing)
            {
                string? drawingPath = DrawingLocator.Find(app, job.ModelPath);
                result.DrawingPath = drawingPath;
                if (drawingPath == null)
                {
                    result.Status = ExportStatus.NoDrawing;
                    result.Detail = "No .idw or .dwg with the model's name found on the project paths";
                    return;
                }
                sourcePath = drawingPath;
            }
            else
            {
                if (!File.Exists(job.ModelPath))
                {
                    result.Status = ExportStatus.Failed;
                    result.Detail = "Model file not found on disk";
                    return;
                }
                sourcePath = job.ModelPath;
            }

            Inv.Document? existing = DrawingBatch.FindOpenDocument(app, sourcePath);
            Inv.Document doc = existing ?? app.Documents.Open(sourcePath, false);
            if (existing == null) opened = doc;

            string outDir = Path.GetDirectoryName(job.OutputPath)!;
            string baseName = Path.GetFileNameWithoutExtension(job.OutputPath);
            string plannedName = Path.GetFileName(job.OutputPath);
            var started = DateTime.Now.AddSeconds(-2);

            Save(app, job, doc);

            var written = Directory.GetFiles(outDir, baseName + "*" + Path.GetExtension(job.OutputPath))
                .Where(f => File.GetLastWriteTime(f) >= started)
                .Select(f => Path.GetFileName(f)!)
                .Where(n => n.Equals(plannedName, StringComparison.OrdinalIgnoreCase)
                         || n.StartsWith(baseName + "_Sheet", StringComparison.OrdinalIgnoreCase))
                .ToList();

            bool plannedExists = written.Contains(plannedName, StringComparer.OrdinalIgnoreCase);
            if (written.Count == 0)
            {
                result.Status = ExportStatus.Failed;
                result.Detail = "Translator reported success but wrote no file";
            }
            else
            {
                result.Status = ExportStatus.Exported;
                result.Detail = plannedExists && written.Count == 1
                    ? null
                    : "Files written: " + string.Join("; ", written);
            }
        }
        catch (Exception ex)
        {
            result.Status = ExportStatus.Failed;
            result.Detail = ex.Message;
        }
        finally
        {
            // true = skip save: we never modify the user's files
            try { opened?.Close(true); } catch { /* closing is best effort */ }
        }
    }

    private static void Save(Inv.Application app, ExportJob job, Inv.Document doc)
    {
        var spec = job.Output;
        switch (spec.Format)
        {
            case ExportFormat.Step:
                StepExporter.Save(app, Translators.Get(app, StepExporter.TranslatorId), doc, job.OutputPath);
                break;

            case ExportFormat.Pdf:
                PdfExporter.SaveAsPdf(app, Translators.Get(app, PdfExporter.PdfTranslatorId),
                    (Inv.DrawingDocument)doc, job.OutputPath, spec.PdfSheets);
                break;

            case ExportFormat.Dwg:
            case ExportFormat.Dxf:
            case ExportFormat.Dwf:
                {
                    var cad = spec.Format == ExportFormat.Dwg ? CadFormat.Dwg
                            : spec.Format == ExportFormat.Dxf ? CadFormat.Dxf
                            : CadFormat.Dwf;

                    // DWG/DXF with ALL SHEETS=No export the active sheet, so make that sheet 1.
                    Inv.Sheet? previous = null;
                    if (cad != CadFormat.Dwf && doc is Inv.DrawingDocument drw && drw.Sheets.Count > 0)
                    {
                        previous = drw.ActiveSheet;
                        drw.Sheets[1].Activate();
                    }
                    try
                    {
                        CadExporter.Save(app, Translators.Get(app, CadExporter.IdFor(cad)),
                            doc, job.OutputPath, cad, spec.IniFile);
                    }
                    finally
                    {
                        try { previous?.Activate(); } catch { /* best effort */ }
                    }
                    break;
                }

            default:
                throw new NotSupportedException($"Format {spec.Format} is not supported.");
        }
    }
}