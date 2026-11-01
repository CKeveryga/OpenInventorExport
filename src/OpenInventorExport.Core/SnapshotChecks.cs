using System.Collections.Generic;
using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public static class SnapshotChecks
{
    /// <summary>
    /// Entries that mean the component list is incomplete: files that could not be found, and
    /// components that failed so badly they are not in Components at all. Exports must not run while any exist.
    /// Suppressed, virtual, reference and rule exclusions are deliberate, so they never block.
    /// </summary>
    public static List<SkippedRecord> BlockingProblems(Snapshot s)
    {
        var listed = new HashSet<string>(s.Components.Select(c => c.OccurrencePath));
        return s.Skipped
            .Where(k => k.Reason == SkipReason.FileMissing
                     || (k.Reason == SkipReason.Error && !listed.Contains(k.OccurrencePath)))
            .ToList();
    }
}