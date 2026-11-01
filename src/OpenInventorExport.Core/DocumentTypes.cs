using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenInventorExport.Core;

/// <summary>The document types the traverser produces. One list, used by config validation and the snapshot summary.</summary>
public static class DocumentTypes
{
    public const string Part = "Part";
    public const string SheetMetal = "SheetMetal";
    public const string Assembly = "Assembly";   // includes weldments
    public const string Virtual = "Virtual";
    public const string Unknown = "Unknown";     // e.g. suppressed occurrences whose definition cannot be read

    public static readonly IReadOnlyList<string> All = new[] { Part, SheetMetal, Assembly, Virtual, Unknown };

    public static bool IsKnown(string? type) =>
        type != null && All.Contains(type, StringComparer.OrdinalIgnoreCase);
}