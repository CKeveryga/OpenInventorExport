using Inv = global::Inventor;

namespace OpenInventorExport.Inventor;

public static class StepExporter
{
    // STEP translator add-in ID (from memory; verify if ItemById throws)
    private const string StepTranslatorId = "{90AF7F40-0C01-11D5-8E83-0010B541CD80}";

    /// <summary>Exports the ACTIVE document to a STEP file. Probe only; will be generalized.</summary>
    public static string ExportActiveDocument(string outputPath)
    {
        var app = InventorConnection.GetRunningInventor();
        var doc = app.ActiveDocument;

        var translator = (Inv.TranslatorAddIn)app.ApplicationAddIns.ItemById[StepTranslatorId];
        translator.Activate();

        var context = app.TransientObjects.CreateTranslationContext();
        context.Type = Inv.IOMechanismEnum.kFileBrowseIOMechanism;

        var options = app.TransientObjects.CreateNameValueMap();
        if (translator.HasSaveCopyAsOptions[doc, context, options])
        {
            options.Value["ApplicationProtocolType"] = 3; // 3 = AP214
        }

        var data = app.TransientObjects.CreateDataMedium();
        data.FileName = outputPath;

        translator.SaveCopyAs(doc, context, options, data);
        return outputPath;
    }
}