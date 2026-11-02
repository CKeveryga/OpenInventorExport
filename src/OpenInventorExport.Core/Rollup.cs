using System;
using System.Collections.Generic;
using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public enum RollupKey { PartNumber, FilePath }

public enum RollupMode { Occurrence, Structured, Flat }
public enum InseparableHandling { AsSingleItem, IncludeChildren }

public class RollupOptions
{
    public RollupMode Mode { get; set; } = RollupMode.Flat;
    /// <summary>Adds the top-level assembly (Level 0) as a row with qty 1. Off by default because Inventor's BOM does not list it. Has no effect when IncludeAssemblies is false.</summary>
    public bool IncludeRoot { get; set; } = false;
    public bool IncludeAssemblies { get; set; } = true;
    public bool ExcludeSuppressed { get; set; } = true;
    /// <summary>Include (default) = ignore Content Center. Exclude = leave Content Center parts out. Only = list nothing else.</summary>
    public ContentCenterMode ContentCenter { get; set; } = ContentCenterMode.Include;

    /// <summary>
    /// Per-structure overrides. Empty = Inventor's defaults: Phantom and Reference items are left out,
    /// everything else is listed, children are kept.
    /// </summary>
    public List<BomStructureRule> BomStructures { get; set; } = new();

    /// <summary>
    /// Null = Inventor's behavior for the mode: Flat hides children of inseparable assemblies, other modes list them.
    /// A BomStructures rule for Inseparable with Children set takes precedence over this.
    /// </summary>
    public InseparableHandling? Inseparable { get; set; }

    /// <summary>What makes two components the same row. PartNumber matches Inventor's BOM; FilePath lists each file separately.</summary>
    public RollupKey Key { get; set; } = RollupKey.PartNumber;

    /// <summary>The grouping key of a component under the current Key setting. A missing or empty Part Number falls back to the file path.</summary>
    public string KeyOf(ComponentRecord c)
    {
        if (Key == RollupKey.PartNumber)
        {
            string pn = c.FindProperty(PropertySetNames.DesignTracking, "Part Number")?.Value ?? "";
            if (pn.Length > 0) return "PN:" + pn;
        }
        return "File:" + (c.FullPath ?? c.FileName);
    }
}

public class ExportRow
{
    public ExportRow(ComponentRecord component, int quantity, string? parentKey)
    {
        Component = component;
        Quantity = quantity;
        ParentKey = parentKey;
    }

    public ComponentRecord Component { get; }
    public int Quantity { get; }
    public string? ParentKey { get; }
}

public static class Rollup
{
    /// <param name="exclusions">Optional. Receives one record per occurrence that a rule left out.</param>
    public static List<ExportRow> Apply(Snapshot snapshot, RollupOptions options, List<ExclusionRecord>? exclusions = null)
    {
        var inseparable = options.Inseparable
            ?? (options.Mode == RollupMode.Flat ? InseparableHandling.AsSingleItem : InseparableHandling.IncludeChildren);
        var inseparableDefault = inseparable == InseparableHandling.IncludeChildren
            ? ChildHandling.Include
            : ChildHandling.Exclude;

        var policy = new BomStructurePolicy(options.BomStructures);
        bool inseparableAsSingleItem =
            policy.ChildrenOf(BomStructureKind.Inseparable, inseparableDefault) == ChildHandling.Exclude;

        var verdicts = BomVisibility.Evaluate(snapshot, policy, options.ExcludeSuppressed, inseparableDefault);

        bool KeptInseparableAssembly(ComponentRecord c) =>
            BomStructurePolicy.KindOf(c.BomStructure) == BomStructureKind.Inseparable && inseparableAsSingleItem;

        // The top-level assembly (Level 0) is the thing being exported, not a row in it.
        var candidates = new List<ComponentRecord>();
        foreach (var c in snapshot.Components)
        {
            if (c.Level == 0 && !options.IncludeRoot) continue;

            var verdict = verdicts[c];
            if (!verdict.Listed)
            {
                exclusions?.Add(new ExclusionRecord
                {
                    Context = "BOM",
                    OccurrencePath = c.OccurrencePath,
                    FileName = c.FileName,
                    Reason = verdict.Reason,
                    Detail = verdict.Detail
                });
                continue;
            }

            if (!ContentCenterFilter.Passes(c, options.ContentCenter))
            {
                exclusions?.Add(new ExclusionRecord
                {
                    Context = "BOM",
                    OccurrencePath = c.OccurrencePath,
                    FileName = c.FileName,
                    Reason = ExclusionReason.ContentCenter,
                    Detail = ContentCenterFilter.Describe(c, options.ContentCenter)
                });
                continue;
            }

            if (!(options.IncludeAssemblies || c.DocumentType != "Assembly" || KeptInseparableAssembly(c)))
                continue;

            candidates.Add(c);
        }

        switch (options.Mode)
        {
            case RollupMode.Occurrence:
                return candidates.Select(c => new ExportRow(c, 1, null)).ToList();

            case RollupMode.Flat:
                // Counting occurrences across the whole tree automatically
                // multiplies quantities through the levels.
                return candidates
                    .GroupBy(options.KeyOf)
                    .Select(g => new ExportRow(g.First(), g.Count(), null))
                    .ToList();

            case RollupMode.Structured:
                return Structured(snapshot, candidates, options);

            default:
                throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static List<ExportRow> Structured(
        Snapshot snapshot, List<ComponentRecord> candidates, RollupOptions options)
    {
        var byPath = snapshot.Components
            .GroupBy(c => c.OccurrencePath)
            .ToDictionary(g => g.Key, g => g.First());

        // A parent is identified by its full chain of ancestors, e.g. "/Top/Sub".
        var structKeys = new Dictionary<string, string>();
        string StructKey(ComponentRecord c)
        {
            if (structKeys.TryGetValue(c.OccurrencePath, out var cached)) return cached;
            string parentPart = c.ParentPath != null && byPath.TryGetValue(c.ParentPath, out var p)
                ? StructKey(p)
                : "";
            string key = parentPart + "/" + options.KeyOf(c);
            structKeys[c.OccurrencePath] = key;
            return key;
        }

        var rows = new List<ExportRow>();
        var seen = new HashSet<(string parent, string child)>();

        foreach (var parentGroup in candidates.GroupBy(c => c.ParentPath ?? ""))
        {
            string parentKey = byPath.TryGetValue(parentGroup.Key, out var parent)
                ? StructKey(parent)
                : "";

            foreach (var child in parentGroup.GroupBy(options.KeyOf))
            {
                if (!seen.Add((parentKey, child.Key))) continue;
                rows.Add(new ExportRow(child.First(), child.Count(), parentKey));
            }
        }
        return rows;
    }
}