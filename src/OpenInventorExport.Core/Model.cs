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

/// <summary>Inventor's own names for the property sets we read.</summary>
public static class PropertySetNames
{
    public const string DesignTracking = "Design Tracking Properties";
    public const string UserDefined = "Inventor User Defined Properties";
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
    /// <summary>True if the part is a Content Center member. Null = not recorded (older snapshot, or Inventor would not say).</summary>
    public bool? IsContentCenter { get; set; }
    /// <summary>True if Inventor could not open the referenced file (missing or moved). Listed with no properties; an unresolved assembly's children cannot be seen.</summary>
    public bool IsUnresolved { get; set; }
    public string? BomStructure { get; set; }
    public string? ParentPath { get; set; }

    /// <summary>Property set name (see PropertySetNames) -> property name -> value.</summary>
    public Dictionary<string, Dictionary<string, PropertyValue>> PropertySets { get; set; } = new();

    /// <summary>The property's value, or null if the set or the property is not there.</summary>
    public PropertyValue? FindProperty(string set, string name) =>
        PropertySets.TryGetValue(set, out var props) && props.TryGetValue(name, out var value) ? value : null;

    /// <summary>Stores a value in this in-memory record (creates the set if needed). Never touches Inventor.</summary>
    public void SetProperty(string set, string name, PropertyValue value)
    {
        if (!PropertySets.TryGetValue(set, out var props))
            PropertySets[set] = props = new Dictionary<string, PropertyValue>();
        props[name] = value;
    }
}

public class SkippedRecord
{
    public string OccurrencePath { get; set; } = "";
    public SkipReason Reason { get; set; }
    public string? Detail { get; set; }
}

public class Snapshot
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? InventorVersion { get; set; }
    public string? SourceFile { get; set; }
    public string? ModelState { get; set; }
    public List<ComponentRecord> Components { get; set; } = new();
    public List<SkippedRecord> Skipped { get; set; } = new();
}