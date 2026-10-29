using OpenInventorExport.Core;
using Inv = global::Inventor;

namespace OpenInventorExport.Inventor;

public enum CadFormat { Dwg, Dxf, Dwf }

public static class CadExporter
{
    private const string DwgTranslatorId = "{C24E3AC2-122E-11D5-8E91-0010B541CD80}";
    private const string DxfTranslatorId = "{C24E3AC4-122E-11D5-8E91-0010B541CD80}";
    private const string DwfTranslatorId = "{0AC6FD95-2F4D-42CE-8BE0-8AEA580399E4}";

    /// <param name="iniFile">Optional layer/version mapping for DWG/DXF. Null or empty = translator defaults. Ignored for DWF.</param>
    public static List<ExportResult> ExportDrawings(
        IEnumerable<string> modelPaths, string outputFolder, CadFormat format, string? iniFile = null)
    {
        if (!string.IsNullOrWhiteSpace(iniFile) && !File.Exists(iniFile))
            throw new FileNotFoundException("DWG/DXF ini file not found", iniFile);

        var app = InventorConnection.GetRunningInventor();
        var translator = (Inv.TranslatorAddIn)app.ApplicationAddIns.ItemById[IdFor(format)];
        if (!translator.Activated) translator.Activate();

        return DrawingBatch.Run(app, modelPaths, outputFolder, ExtensionFor(format),
            (drw, path) => Save(app, translator, drw, path, format, iniFile));
    }

    private static string IdFor(CadFormat f) => f switch
    {
        CadFormat.Dwg => DwgTranslatorId,
        CadFormat.Dxf => DxfTranslatorId,
        _ => DwfTranslatorId
    };

    private static string ExtensionFor(CadFormat f) => f switch
    {
        CadFormat.Dwg => ".dwg",
        CadFormat.Dxf => ".dxf",
        _ => ".dwf"
    };

    private static void Save(Inv.Application app, Inv.TranslatorAddIn translator,
        Inv.DrawingDocument drw, string path, CadFormat format, string? iniFile)
    {
        var context = app.TransientObjects.CreateTranslationContext();
        context.Type = Inv.IOMechanismEnum.kFileBrowseIOMechanism;

        var options = app.TransientObjects.CreateNameValueMap();
        if (format == CadFormat.Dwf)
        {
            options.Value["Launch_Viewer"] = 0;
            options.Value["Publish_All_Component_Props"] = 1;
            options.Value["Publish_All_Physical_Props"] = 1;
            options.Value["Publish_3D_Models"] = 1;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(iniFile))
                options.Add("Export_Acad_IniFile", iniFile);
            options.Value["USE_TRANSMITTAL"] = "No";
        }

        var data = app.TransientObjects.CreateDataMedium();
        data.FileName = path;
        translator.SaveCopyAs(drw, context, options, data);
    }
}