using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenInventorExport.Core;

public enum BomStructureKind { Normal, Inseparable, Phantom, Reference, Purchased }

public enum ChildHandling { Include, Exclude }

/// <summary>
/// Overrides for one BOM structure. A field left out keeps the default for that structure.
/// Include = whether the item itself is listed/exported. Children = whether what is inside it is kept.
/// </summary>
public class BomStructureRule
{
    public BomStructureKind Structure { get; set; }
    public bool? Include { get; set; }
    public ChildHandling? Children { get; set; }
}

/// <summary>
/// Resolves the defaults plus any rule overrides. Verified against Inventor 2027: Phantom and Reference items
/// are left out; Phantom children are kept (promoted to the parent); Reference children are left out too.
/// Inseparable and Purchased have no fixed default for their children (Parts Only hides them,
/// Structured lists them), so the caller supplies it from the rollup mode.
/// </summary>
public sealed class BomStructurePolicy
{
    private readonly Dictionary<BomStructureKind, bool> _item = new Dictionary<BomStructureKind, bool>();
    private readonly Dictionary<BomStructureKind, ChildHandling?> _children = new Dictionary<BomStructureKind, ChildHandling?>();

    public BomStructurePolicy(IEnumerable<BomStructureRule>? overrides)
    {
        foreach (BomStructureKind k in Enum.GetValues(typeof(BomStructureKind)))
        {
            _item[k] = k != BomStructureKind.Phantom && k != BomStructureKind.Reference;
            _children[k] = DefaultChildren(k);
        }

        if (overrides == null) return;
        foreach (var r in overrides)
        {
            if (r.Include.HasValue) _item[r.Structure] = r.Include.Value;
            if (r.Children.HasValue) _children[r.Structure] = r.Children;
        }
    }

    /// <summary>Maps the snapshot's Inventor name (e.g. "kPhantomBOMStructure"). Null for the root or an unrecognized value.</summary>
    public static BomStructureKind? KindOf(string? bomStructure)
    {
        switch (bomStructure)
        {
            case "kNormalBOMStructure": return BomStructureKind.Normal;
            case "kInseparableBOMStructure": return BomStructureKind.Inseparable;
            case "kPhantomBOMStructure": return BomStructureKind.Phantom;
            case "kReferenceBOMStructure": return BomStructureKind.Reference;
            case "kPurchasedBOMStructure": return BomStructureKind.Purchased;
            default: return null;
        }
    }

    /// <summary>True if the item itself is listed. An unrecognized structure (null) is always listed.</summary>
    public bool ItemListed(BomStructureKind? kind) => kind == null || _item[kind.Value];

    /// <summary>Whether the children of an item with this structure are kept.</summary>
    public ChildHandling ChildrenOf(BomStructureKind kind, ChildHandling modeDefault) =>
    _children[kind] ?? modeDefault;

    /// <summary>Problems in a rule list: unknown structure values and structures listed twice.</summary>
    public static List<string> Validate(IEnumerable<BomStructureRule> rules, string where)
    {
        var problems = new List<string>();
        var list = rules.ToList();

        foreach (var r in list.Where(r => !Enum.IsDefined(typeof(BomStructureKind), r.Structure)))
            problems.Add($"{where}: unknown BOM structure value {(int)r.Structure}.");

        foreach (var dup in list.GroupBy(r => r.Structure).Where(g => g.Count() > 1))
            problems.Add($"{where}: BOM structure '{dup.Key}' is listed more than once.");

        return problems;
    }

    private static ChildHandling? DefaultChildren(BomStructureKind k)
    {
        switch (k)
        {
            case BomStructureKind.Reference:
                return ChildHandling.Exclude;
            case BomStructureKind.Inseparable:
            case BomStructureKind.Purchased:
                return null;   // depends on the BOM view
            default:
                return ChildHandling.Include;
        }
    }
}