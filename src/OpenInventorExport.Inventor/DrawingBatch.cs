using OpenInventorExport.Core;
using Inv = global::Inventor;

namespace OpenInventorExport.Inventor;

internal static class DrawingBatch
{
    /// <summary>
    /// For each model: find its drawing, check it references the model, run the save action, close it.
    /// Returns one result per model.
    /// </summary>
    public static List<ExportResult> Run(
        Inv.Application app,
        IEnumerable<string> modelPaths,
        string outputFolder,
        string extension,
        Action<Inv.DrawingDocument, string> save)
    {
        Directory.CreateDirectory(outputFolder);
        var results = new List<ExportResult>();
        var doneDrawings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string model in modelPaths)
        {
            var result = new ExportResult { ModelPath = model };
            results.Add(result);
            Inv.Document? opened = null;

            try
            {
                string? drawingPath = DrawingLocator.Find(app, model);
                result.DrawingPath = drawingPath;
                if (drawingPath == null)
                {
                    result.Status = ExportStatus.NoDrawing;
                    result.Detail = "No .idw or .dwg with the model's name found on the project paths";
                    continue;
                }

                if (!doneDrawings.Add(drawingPath))
                {
                    result.Status = ExportStatus.Failed;
                    result.Detail = "Same drawing already exported for another model";
                    continue;
                }

                Inv.Document? drawing = FindOpenDocument(app, drawingPath);
                if (drawing == null)
                {
                    drawing = app.Documents.Open(drawingPath, false);
                    opened = drawing;
                }

                if (!References(drawing, model))
                {
                    result.Status = ExportStatus.DrawingMismatch;
                    result.Detail = "Drawing found by name does not reference this model";
                    continue;
                }

                string outPath = Path.Combine(outputFolder,
                    Path.GetFileNameWithoutExtension(drawingPath) + extension);
                save((Inv.DrawingDocument)drawing, outPath);

                result.OutputPath = outPath;
                result.Status = ExportStatus.Exported;
            }
            catch (Exception ex)
            {
                result.Status = ExportStatus.Failed;
                result.Detail = ex.Message;
            }
            finally
            {
                try { opened?.Close(true); } catch { /* closing is best effort */ }
            }
        }
        return results;
    }

    private static bool References(Inv.Document drawing, string modelPath)
    {
        string wanted = PathUtil.StripMemberSuffix(modelPath);
        var refs = drawing.ReferencedDocuments;
        for (int i = 1; i <= refs.Count; i++)
        {
            if (string.Equals(refs[i].FullFileName, wanted, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static Inv.Document? FindOpenDocument(Inv.Application app, string path)
    {
        for (int i = 1; i <= app.Documents.Count; i++)
        {
            var d = app.Documents[i];
            if (string.Equals(d.FullFileName, path, StringComparison.OrdinalIgnoreCase))
                return d;
        }
        return null;
    }
}