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
        var translator = (Inv.TranslatorAddIn)app.ApplicationAddIns.ItemById[PdfTranslatorId];
        translator.Activate();

        return DrawingBatch.Run(app, modelPaths, outputFolder, ".pdf",
            (drw, path) => SaveAsPdf(app, translator, drw, path, sheets));
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
}