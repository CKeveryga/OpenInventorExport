using System.Collections.Generic;
using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public static class SnapshotSummary
{
    public static IEnumerable<string> Describe(Snapshot s)
    {
        yield return $"Total components: {s.Components.Count}";

        var groups = s.Components
            .GroupBy(c => $"{c.DocumentType} | BOM: {c.BomStructure ?? "(root)"} | Virtual: {c.IsVirtual}")
            .OrderBy(g => g.Key);

        foreach (var g in groups)
            yield return $"  {g.Key}: {g.Count()}";
    }
}