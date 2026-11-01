using System.IO;
using System.Collections.Generic;
using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public static class SnapshotSummary
{
    public static IEnumerable<string> Describe(Snapshot s)
    {
        yield return $"Total components: {s.Components.Count}";
        yield return $"Project: {(s.ProjectFile == null ? "(not recorded)" : Path.GetFileName(s.ProjectFile))}";

        yield return $"Unique files: {s.Components.Where(c => c.FullPath != null).Select(c => c.FullPath!.ToLowerInvariant()).Distinct().Count()}";
        yield return $"Unique assemblies: {s.Components.Where(c => c.DocumentType == "Assembly" && c.FullPath != null).Select(c => c.FullPath!.ToLowerInvariant()).Distinct().Count()}";

        var partTypes = new[] { DocumentTypes.Part, DocumentTypes.SheetMetal };
        yield return $"Content Center parts: {s.Components.Count(c => c.IsContentCenter == true)} (flag not recorded on {s.Components.Count(c => c.IsContentCenter == null && partTypes.Contains(c.DocumentType))} parts)";

        var groups = s.Components
            .GroupBy(c => $"{c.DocumentType} | BOM: {c.BomStructure ?? "(root)"} | Virtual: {c.IsVirtual}")
            .OrderBy(g => g.Key);

        foreach (var g in groups)
            yield return $"  {g.Key}: {g.Count()}";

        foreach (var g in s.Components.Where(c => !DocumentTypes.IsKnown(c.DocumentType)).GroupBy(c => c.DocumentType).OrderBy(g => g.Key))
            yield return $"WARNING: unrecognized document type '{g.Key}': {g.Count()}. If legitimate, add it to DocumentTypes.";
    }
}