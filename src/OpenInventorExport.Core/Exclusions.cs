using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public enum ExclusionReason { Suppressed, BomStructure, ParentExcluded, DocumentType, PropertyFilter, Scope }

/// <summary>One component (BOM) or unique file (task) that a rule left out, and why.</summary>
public class ExclusionRecord
{
    /// <summary>"BOM" or the task name.</summary>
    public string Context { get; set; } = "";
    public string OccurrencePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public ExclusionReason Reason { get; set; }
    public string? Detail { get; set; }
}

public static class ExclusionReport
{
    public static string ToCsv(IEnumerable<ExclusionRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Context,Reason,Occurrence,File,Detail");
        foreach (var r in records)
        {
            var cells = new[] { r.Context, r.Reason.ToString(), r.OccurrencePath, r.FileName, r.Detail ?? "" };
            sb.AppendLine(string.Join(",", cells.Select(CsvTableWriter.Escape)));
        }
        return sb.ToString();
    }

    /// <summary>One line per context and reason, e.g. "BOM / BomStructure: 3".</summary>
    public static IEnumerable<string> Summary(IEnumerable<ExclusionRecord> records) =>
        records.GroupBy(r => r.Context + " / " + r.Reason)
               .OrderBy(g => g.Key)
               .Select(g => $"{g.Key}: {g.Count()}");
}

/// <summary>
/// Decides, for every component, whether the BOM structure rules leave it out: its own structure,
/// or an ancestor whose children are dropped. Shared by the rollup and the export planner.
/// </summary>
internal static class BomVisibility
{
    internal sealed class Verdict
    {
        public bool Listed { get; set; }
        public ExclusionReason Reason { get; set; }
        public string? Detail { get; set; }
    }

    internal static Dictionary<ComponentRecord, Verdict> Evaluate(
        Snapshot snapshot, BomStructurePolicy policy, bool excludeSuppressed, ChildHandling inseparableDefault)
    {
        var byPath = snapshot.Components
            .GroupBy(c => c.OccurrencePath)
            .ToDictionary(g => g.Key, g => g.First());

        var result = new Dictionary<ComponentRecord, Verdict>();
        foreach (var c in snapshot.Components)
            result[c] = Judge(c, byPath, policy, excludeSuppressed, inseparableDefault);
        return result;
    }

    private static Verdict Judge(
        ComponentRecord c, Dictionary<string, ComponentRecord> byPath,
        BomStructurePolicy policy, bool excludeSuppressed, ChildHandling inseparableDefault)
    {
        if (excludeSuppressed && c.IsSuppressed)
            return new Verdict { Reason = ExclusionReason.Suppressed, Detail = "Suppressed" };

        var kind = BomStructurePolicy.KindOf(c.BomStructure);
        if (!policy.ItemListed(kind))
            return new Verdict { Reason = ExclusionReason.BomStructure, Detail = kind.ToString() };

        var current = c;
        while (current.ParentPath != null && byPath.TryGetValue(current.ParentPath, out var parent))
        {
            var parentKind = BomStructurePolicy.KindOf(parent.BomStructure);
            if (parentKind != null && policy.ChildrenOf(parentKind.Value, inseparableDefault) == ChildHandling.Exclude)
                return new Verdict
                {
                    Reason = ExclusionReason.ParentExcluded,
                    Detail = $"Inside {parent.OccurrencePath} ({parentKind})"
                };
            current = parent;
        }
        return new Verdict { Listed = true };
    }
}