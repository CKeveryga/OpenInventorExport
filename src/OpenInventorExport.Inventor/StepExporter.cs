using Inv = global::Inventor;

namespace OpenInventorExport.Inventor;

public static class StepExporter
{
    internal const string TranslatorId = "{90AF7F40-0C01-11D5-8E83-0010B541CD80}";

    /// <summary>Exports the ACTIVE document to a STEP file. Probe only.</summary>
    public static string ExportActiveDocument(string outputPath)
    {
        var app = InventorConnection.GetRunningInventor();
        Save(app, Translators.Get(app, TranslatorId), app.ActiveDocument, outputPath);
        return outputPath;
    }

    internal static void Save(Inv.Application app, Inv.TranslatorAddIn translator,
        Inv.Document doc, string path)
    {
        var context = app.TransientObjects.CreateTranslationContext();
        context.Type = Inv.IOMechanismEnum.kFileBrowseIOMechanism;

        var options = app.TransientObjects.CreateNameValueMap();
        if (translator.HasSaveCopyAsOptions[doc, context, options])
        {
            options.Value["ApplicationProtocolType"] = 3; // 3 = AP214
        }

        var data = app.TransientObjects.CreateDataMedium();
        data.FileName = path;
        translator.SaveCopyAs(doc, context, options, data);
    }
}