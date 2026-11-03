using System.Collections.Generic;
using System.Text.Json.Serialization;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public enum ExportFormat { Step, Pdf, Dwg, Dxf, Dwf }
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

/// <summary>One format to produce for each selected component.</summary>
public class OutputSpec
{
    public ExportFormat Format { get; set; }
    public ExportSource Source { get; set; }
    public PdfSheetMode PdfSheets { get; set; } = PdfSheetMode.AllInOne;
    /// <summary>DWG/DXF only. Optional layer/version mapping.</summary>
    public string? IniFile { get; set; }
}

/// <summary>A named deliverable: which components, which formats, where the files go.</summary>
public class ExportTask
{
    public string Name { get; set; } = "";
    public ExportSelection Selection { get; set; } = new();
    public List<OutputSpec> Outputs { get; set; } = new();
    /// <summary>Relative path under the output root. Tokens: {task}, {format}, {type}.</summary>
    public string FolderTemplate { get; set; } = "{task}";
    /// <summary>Optional. If present, the task also writes a table of what it delivers. Null = files only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackageSpec? Package { get; set; }
}

public enum TableFormat { Xlsx, Csv }

/// <summary>What a task delivers besides its files: a table, and later a zip.</summary>
public class PackageSpec
{
    /// <summary>Table files to write. Empty = none.</summary>
    public List<TableFormat> Table { get; set; } = new();
    /// <summary>Empty = the config's top-level BomColumns.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RollupOptions? Rollup { get; set; }
    /// <summary>Empty = the config's top-level BomColumns.</summary>
    public List<BomColumn> Columns { get; set; } = new();
    /// <summary>Zip the table and the exported files. NOT BUILT YET: read and validated only.</summary>
    public bool Zip { get; set; } = false;
}