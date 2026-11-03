using System.Collections.Generic;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

/// <summary>Step, Pdf, Dwg, Dxf, Dwf are files per component. Csv and Xlsx are one table per output. New values go at the end.</summary>
public enum ExportFormat { Step, Pdf, Dwg, Dxf, Dwf, Csv, Xlsx }
public enum ExportSource { Model, Drawing }
public enum ExportScope { TopLevelOnly, AllUniqueFiles }
public enum FilterOperator { Equals, NotEquals, Contains, IsEmpty, IsNotEmpty }

/// <summary>One property test, e.g. Material Equals "Steel". Comparisons ignore case.</summary>
public class PropertyFilter
{
    public string Property { get; set; } = "";
    public FilterOperator Operator { get; set; } = FilterOperator.Equals;
    public string? Value { get; set; }
    /// <summary>Which Inventor property set the property is in. Default: Design Tracking.</summary>
    public string Set { get; set; } = PropertySetNames.DesignTracking;
}

/// <summary>Which components a task exports. A component must pass every test (AND).</summary>
public class ExportSelection
{
    public ExportScope Scope { get; set; } = ExportScope.AllUniqueFiles;
    /// <summary>Empty = any type. Otherwise e.g. "SheetMetal", "Part", "Assembly".</summary>
    public List<string> DocumentTypes { get; set; } = new();
    public List<PropertyFilter> Filters { get; set; } = new();
    /// <summary>
    /// Per-structure overrides. Empty = Inventor's defaults: Phantom and Reference items are left out,
    /// everything else is selected, children are kept. Example: Purchased with Children Exclude.
    /// </summary>
    public List<BomStructureRule> BomStructures { get; set; } = new();
    /// <summary>Include (default) = ignore Content Center. Exclude = skip Content Center parts. Only = select nothing else.</summary>
    public ContentCenterMode ContentCenter { get; set; } = ContentCenterMode.Include;
}

/// <summary>
/// One thing to produce in the task's folder tree: a file per selected component (Step, Pdf, Dwg, Dxf, Dwf)
/// or one table of everything the task selects (Csv, Xlsx).
/// </summary>
public class OutputSpec
{
    public ExportFormat Format { get; set; }
    /// <summary>Ignored for Csv and Xlsx.</summary>
    public ExportSource Source { get; set; }
    public PdfSheetMode PdfSheets { get; set; } = PdfSheetMode.AllInOne;
    /// <summary>DWG/DXF only. Optional layer/version mapping.</summary>
    public string? IniFile { get; set; }
    /// <summary>Subfolder of the task's folder where this output goes. Relative, no '..'. Empty = the task's folder itself.</summary>
    public string Folder { get; set; } = "";
    /// <summary>Csv/Xlsx only. File name without extension. Empty = the task name.</summary>
    public string? Name { get; set; }
    /// <summary>Csv/Xlsx only. Property columns after File Name and Qty. Empty = the config's BomColumns.</summary>
    public List<BomColumn> Columns { get; set; } = new();
}

/// <summary>A named deliverable: which components, which outputs, where they go.</summary>
public class ExportTask
{
    public string Name { get; set; } = "";
    public ExportSelection Selection { get; set; } = new();
    public List<OutputSpec> Outputs { get; set; } = new();
    /// <summary>The task's root folder under the output root. Tokens: {task}, {format}, {type}. Each output's Folder goes below it.</summary>
    public string FolderTemplate { get; set; } = "{task}";
}