using OpenInventorExport.Core;

namespace OpenInventorExport.ConfigEditor;

/// <summary>
/// Which settings each output format uses, so the form shows only those. Mirrors the rules in
/// ExportTaskValidator; the problems list stays the final check.
/// </summary>
public static class OutputRules
{
    public static bool IsTable(ExportFormat f) => ExportPlanner.IsTable(f);
    public static bool UsesIniFile(ExportFormat f) => f is ExportFormat.Dwg or ExportFormat.Dxf;
    public static bool UsesSheets(ExportFormat f) => f == ExportFormat.Pdf;

    /// <summary>The only Source a format allows, or null when the user may choose (DWF) or it does not matter (tables).</summary>
    public static ExportSource? RequiredSource(ExportFormat f) => f switch
    {
        ExportFormat.Step => ExportSource.Model,
        ExportFormat.Pdf or ExportFormat.Dwg or ExportFormat.Dxf => ExportSource.Drawing,
        _ => null
    };

    /// <summary>The source a new output of this format starts with.</summary>
    public static ExportSource DefaultSource(ExportFormat f) => RequiredSource(f) ?? ExportSource.Drawing;

    /// <summary>
    /// Settings in this output that its format cannot use (they can only come from a hand-edited file,
    /// or from "Duplicate as" another format). Empty = nothing to fix.
    /// </summary>
    public static List<string> Leftovers(OutputSpec o)
    {
        var found = new List<string>();
        if (RequiredSource(o.Format) is { } source && o.Source != source)
            found.Add($"Source is {o.Source}, but {o.Format} always comes from the {source.ToString().ToLowerInvariant()}");
        if (!IsTable(o.Format))
        {
            if (!string.IsNullOrWhiteSpace(o.Name)) found.Add("a table Name");
            if (o.Mode != null) found.Add("a table Mode");
            if (o.Key != null) found.Add("a table Key");
            if (o.Columns.Count > 0) found.Add("table Columns");
        }
        if (!UsesIniFile(o.Format) && !string.IsNullOrWhiteSpace(o.IniFile))
            found.Add("an ini file (only DWG and DXF use one)");
        return found;
    }

    /// <summary>Sets the required source and clears every setting the format does not use.</summary>
    public static void Fix(OutputSpec o)
    {
        if (RequiredSource(o.Format) is { } source) o.Source = source;
        if (!IsTable(o.Format))
        {
            o.Name = null;
            o.Mode = null;
            o.Key = null;
            o.Columns.Clear();
        }
        if (!UsesIniFile(o.Format)) o.IniFile = null;
        if (!UsesSheets(o.Format)) o.PdfSheets = PdfSheetMode.AllInOne;
    }
}
