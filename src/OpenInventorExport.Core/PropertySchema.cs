using System;
using System.Collections.Generic;
using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public enum PropertyIssueKind { Missing, Empty, WrongType }

/// <summary>One property every (applicable) file should have.</summary>
public class SchemaProperty
{
    public string Name { get; set; } = "";
    public PropertyDataType DataType { get; set; } = PropertyDataType.Text;
    /// <summary>If true, an existing but empty value is reported too.</summary>
    public bool Required { get; set; } = false;
    /// <summary>Empty = every document type. Otherwise e.g. "Part", "SheetMetal".</summary>
    public List<string> AppliesTo { get; set; } = new();
    /// <summary>Which Inventor property set the property is in. Default: Design Tracking.</summary>
    public string Set { get; set; } = PropertySetNames.DesignTracking;
}

public class PropertySchema
{
    public int SchemaVersion { get; set; } = 1;
    public List<SchemaProperty> Properties { get; set; } = new();
}

public class PropertyIssue
{
    public string FullPath { get; set; } = "";
    public string Property { get; set; } = "";
    public PropertyIssueKind Kind { get; set; }
    public string? Detail { get; set; }
    public string Set { get; set; } = "";
}

public static class PropertySchemaValidator
{
    /// <summary>Checks each unique model file once against the schema. Read-only.</summary>
    public static List<PropertyIssue> Validate(Snapshot snapshot, PropertySchema schema)
    {
        var issues = new List<PropertyIssue>();

        var files = snapshot.Components
            .Where(c => c.FullPath != null && !c.IsSuppressed && !c.IsVirtual && !c.IsUnresolved)
            .GroupBy(c => PathUtil.StripMemberSuffix(c.FullPath!), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First());

        foreach (var comp in files)
        {
            string path = PathUtil.StripMemberSuffix(comp.FullPath!);
            foreach (var def in schema.Properties)
            {
                if (def.AppliesTo.Count > 0 &&
                    !def.AppliesTo.Contains(comp.DocumentType, StringComparer.OrdinalIgnoreCase))
                    continue;

                var pv = comp.FindProperty(def.Set, def.Name);
                if (pv == null)
                    issues.Add(new PropertyIssue { FullPath = path, Set = def.Set, Property = def.Name, Kind = PropertyIssueKind.Missing });
                else if (pv.DataType != def.DataType)
                    issues.Add(new PropertyIssue
                    {
                        FullPath = path,
                        Set = def.Set,
                        Property = def.Name,
                        Kind = PropertyIssueKind.WrongType,
                        Detail = $"Is {pv.DataType}, schema says {def.DataType}"
                    });
                else if (def.Required && string.IsNullOrEmpty(pv.Value))
                    issues.Add(new PropertyIssue { FullPath = path, Set = def.Set, Property = def.Name, Kind = PropertyIssueKind.Empty });
            }
        }
        return issues;
    }
}