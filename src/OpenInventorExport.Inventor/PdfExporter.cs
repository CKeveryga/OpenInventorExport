using OpenInventorExport.Core;
using Inv = global::Inventor;

namespace OpenInventorExport.Inventor;

public static class PdfExporter
{
    // PDF translator add-in ID (from memory; verify if ItemById throws)
    private const string PdfTranslatorId = "{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}";

    /// <summary>
    /// For each model, finds its drawing, checks the drawing references that model,
    /// and exports the drawing to PDF. Returns one result per model.
    /// </summary>
    public static List<ExportResult> ExportDrawings(
        IEnumerable<string> modelPaths, string outputFolder, PdfSheetMode sheets)
    {
        var app = InventorConnection.GetRunningInventor();
        Directory.CreateDirectory(outputFolder);

        var translator = (Inv.TranslatorAddIn)app.ApplicationAddIns.ItemById[PdfTranslatorId];
        translator.Activate();

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

                // Open invisibly, unless the user already has it open (then leave it open).
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

                string pdfPath = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(drawingPath) + ".pdf");
                SaveAsPdf(app, translator, (Inv.DrawingDocument)drawing, pdfPath, sheets);

                result.OutputPath = pdfPath;
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

    private static void SaveAsPdf(Inv.Application app, Inv.TranslatorAddIn translator,
        Inv.DrawingDocument drw, string pdfPath, PdfSheetMode sheets)
    {
        var context = app.TransientObjects.CreateTranslationContext();
        context.Type = Inv.IOMechanismEnum.kFileBrowseIOMechanism;

        var options = app.TransientObjects.CreateNameValueMap();
        _ = translator.HasSaveCopyAsOptions[drw, context, options]; // fills defaults

        options.Value["All_Color_AS_Black"] = 0;
        options.Value["Remove_Line_Weights"] = 0;
        options.Value["Vector_Resolution"] = 400;

        if (sheets == PdfSheetMode.AllInOne)
        {
            options.Value["Sheet_Range"] = (int)Inv.PrintRangeEnum.kPrintAllSheets;
        }
        else
        {
            drw.Sheets[1].Activate();
            options.Value["Sheet_Range"] = (int)Inv.PrintRangeEnum.kPrintCurrentSheet;
        }

        var data = app.TransientObjects.CreateDataMedium();
        data.FileName = pdfPath;
        translator.SaveCopyAs(drw, context, options, data);
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