using OpenInventorExport.Core.Model;
using System;
using System.Collections.Generic;
using Inv = global::Inventor;

namespace OpenInventorExport.Inventor;

public class AssemblyTraverser
{
    private readonly Snapshot _snapshot = new();

    public Snapshot Traverse(Inv.Application app)
    {
        if (app.ActiveDocument is not Inv.AssemblyDocument asm)
            throw new InvalidOperationException(
                "The active document in Inventor must be an assembly (.iam).");

        _snapshot.InventorVersion = app.SoftwareVersion.DisplayName;
        _snapshot.SourceFile = asm.FullFileName;
        _snapshot.ModelState = TryGetModelState(asm);

        // Record the top-level assembly itself as level 0
        string rootName = Path.GetFileName(asm.FullFileName);
        var root = new ComponentRecord
        {
            OccurrencePath = rootName,
            FileName = rootName,
            FullPath = asm.FullFileName,
            DocumentType = "Assembly",
            Level = 0
        };
        ReadProperties((Inv.Document)asm, root);
        _snapshot.Components.Add(root);

        // Collect the first-level occurrences, then walk down
        var occs = asm.ComponentDefinition.Occurrences;
        var list = new List<Inv.ComponentOccurrence>();
        for (int i = 1; i <= occs.Count; i++) list.Add(occs[i]);

        Walk(list, rootName, 1);
        return _snapshot;
    }

    private void Walk(List<Inv.ComponentOccurrence> occurrences, string parentPath, int level)
    {
        foreach (var occ in occurrences)
        {
            string path = $"{parentPath}/{SafeName(occ)}";
            try
            {
                if (occ.Suppressed)
                {
                    AddSkipped(path, SkipReason.Suppressed, "Component is suppressed");
                    continue;
                }

                var rec = new ComponentRecord
                {
                    OccurrencePath = path,
                    Level = level,
                    IsSuppressed = false,
                    IsVirtual = occ.Definition is Inv.VirtualComponentDefinition,
                    BomStructure = occ.BOMStructure.ToString(),
                    ParentPath = parentPath
                };

                rec.DocumentType = occ.DefinitionDocumentType switch
                {
                    Inv.DocumentTypeEnum.kPartDocumentObject => "Part",
                    Inv.DocumentTypeEnum.kAssemblyDocumentObject => "Assembly",
                    var other => other.ToString()
                };

                if (!rec.IsVirtual)
                {
                    try
                    {
                        rec.FullPath = occ.ReferencedDocumentDescriptor.FullDocumentName;
                        rec.FileName = Path.GetFileName(rec.FullPath);

                        if (occ.Definition.Type == Inv.ObjectTypeEnum.kSheetMetalComponentDefinitionObject)
                            rec.SubType = "SheetMetal";
                        else if (occ.Definition is Inv.VirtualComponentDefinition)
                            rec.SubType = "Virtual";
                    }
                    catch
                    {
                        rec.FileName = SafeName(occ);
                        AddSkipped(path, SkipReason.FileMissing,
                            "Could not resolve the referenced file");
                    }

                    if (occ.Definition.Document is Inv.Document doc)
                        ReadProperties(doc, rec);
                }
                else
                {
                    rec.FileName = SafeName(occ);
                }

                _snapshot.Components.Add(rec);

                // Recurse into sub-assemblies
                if (occ.DefinitionDocumentType == Inv.DocumentTypeEnum.kAssemblyDocumentObject)
                {
                    var subs = occ.SubOccurrences;
                    var subList = new List<Inv.ComponentOccurrence>();
                    for (int i = 1; i <= subs.Count; i++) subList.Add(subs[i]);
                    Walk(subList, path, level + 1);
                }
            }
            catch (Exception ex)
            {
                // One bad component must never abort the whole export
                AddSkipped(path, SkipReason.Error, ex.Message);
            }
        }
    }

    private static void ReadProperties(Inv.Document doc, ComponentRecord rec)
    {
        string[] setNames =
        {
            "Design Tracking Properties",
            "Inventor User Defined Properties"
        };

        foreach (string setName in setNames)
        {
            try
            {
                Inv.PropertySet set = doc.PropertySets[setName];
                for (int i = 1; i <= set.Count; i++)
                {
                    try
                    {
                        Inv.Property prop = set[i];
                        object? value = prop.Value;
                        rec.Properties[prop.Name] = ToPropertyValue(value);
                    }
                    catch { /* skip unreadable property */ }
                }
            }
            catch { /* property set missing: fine */ }
        }
    }

    private static PropertyValue ToPropertyValue(object? value)
    {
        if (value is DateTime dt)
        {
            return new PropertyValue
            {
                DataType = PropertyDataType.Date,
                Value = dt.Year <= 1601 ? null : dt.ToString("yyyy-MM-ddTHH:mm:ss")
            };
        }

        var type = value switch
        {
            bool => PropertyDataType.Boolean,
            int or long or short or byte or float or double or decimal
                => PropertyDataType.Number,
            _ => PropertyDataType.Text
        };

        string? text = value is IFormattable f
            ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
            : value?.ToString();

        return new PropertyValue { DataType = type, Value = text };
    }

    private static string? TryGetModelState(Inv.AssemblyDocument asm)
    {
        try { return asm.ComponentDefinition.ModelStates.ActiveModelState.Name; }
        catch { return null; }
    }

    private static string SafeName(Inv.ComponentOccurrence occ)
    {
        try { return occ.Name; } catch { return "(unnamed)"; }
    }

    private void AddSkipped(string path, SkipReason reason, string detail) =>
        _snapshot.Skipped.Add(new SkippedRecord
        {
            OccurrencePath = path,
            Reason = reason,
            Detail = detail
        });
}