using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public enum ContentCenterMode { Include, Exclude, Only }

/// <summary>
/// Content Center selection. A part whose flag is not recorded (null) counts as NOT Content Center,
/// so Exclude keeps it and Only drops it; NotRecorded lets the CLI say how many parts that affects.
/// </summary>
public static class ContentCenterFilter
{
    public static bool Passes(ComponentRecord c, ContentCenterMode mode)
    {
        switch (mode)
        {
            case ContentCenterMode.Exclude: return c.IsContentCenter != true;
            case ContentCenterMode.Only: return c.IsContentCenter == true;
            default: return true;
        }
    }

    /// <summary>The text for the exclusion report when Passes is false.</summary>
    public static string Describe(ComponentRecord c, ContentCenterMode mode)
    {
        if (mode == ContentCenterMode.Exclude) return "Content Center part";
        return c.IsContentCenter == null
            ? "Content Center flag not recorded (treated as not Content Center)"
            : "Not a Content Center part";
    }

    /// <summary>Parts and sheet metal parts with no flag. Suppressed and unresolved ones are null by design and not counted.</summary>
    public static int NotRecorded(Snapshot s) =>
        s.Components.Count(c => c.IsContentCenter == null && !c.IsSuppressed && !c.IsUnresolved
            && (c.DocumentType == DocumentTypes.Part || c.DocumentType == DocumentTypes.SheetMetal));
}