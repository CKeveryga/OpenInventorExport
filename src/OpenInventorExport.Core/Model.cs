using System;
using System.Collections.Generic;

namespace OpenInventorExport.Core.Model;

public enum PropertyDataType { Text, Number, Date, Boolean }

public enum SkipReason
{
    Suppressed,
    Virtual,
    ReferenceOnly,
    FileMissing,
    Error,
    ExcludedByRule
}

public class PropertyValue
{
    public PropertyDataType DataType { get; set; }
    public string? Value { get; set; }
}

public class ComponentRecord
{
    public string OccurrencePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? FullPath { get; set; }
    public string DocumentType { get; set; } = "";   // Part, Assembly...
    public string? SubType { get; set; }              // SheetMetal, Weldment...
    public int Quantity { get; set; } = 1;
    public int Level { get; set; }
    public bool IsSuppressed { get; set; }
    public bool IsVirtual { get; set; }
    public string? BomStructure { get; set; }
    public string? ParentPath { get; set; }
    public Dictionary<string, PropertyValue> Properties { get; set; } = new();
}

public class SkippedRecord
{
    public string OccurrencePath { get; set; } = "";
    public SkipReason Reason { get; set; }
    public string? Detail { get; set; }
}

public class Snapshot
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? InventorVersion { get; set; }
    public string? SourceFile { get; set; }
    public string? ModelState { get; set; }
    public List<ComponentRecord> Components { get; set; } = new();
    public List<SkippedRecord> Skipped { get; set; } = new();
}