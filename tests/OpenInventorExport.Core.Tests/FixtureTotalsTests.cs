using System;
using System.IO;
using System.Linq;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using Xunit;

namespace OpenInventorExport.Core.Tests;

public class FixtureTotalsTests
{
    private static string SnapshotDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "samples", "snapshots")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "samples", "snapshots");
    }

    private static Snapshot Load(string path) => SnapshotJson.FromJson(File.ReadAllText(path))!;

    // Expected values are Inventor's own BOM totals, not this tool's output.
    [Theory]
    [InlineData("robot-snapshot.json", 445, 421)]
    [InlineData("engine-snapshot.json", 1009, 820)]
    public void Totals_MatchInventorBom(string file, int partsOnly, int structured)
    {
        var s = Load(Path.Combine(SnapshotDir(), file));
        Assert.Equal(partsOnly, BomTotals.PartsOnly(s));
        Assert.Equal(structured, BomTotals.Structured(s));
    }

    [Fact]
    public void EveryFixture_LoadsAndRollsUp()
    {
        var files = Directory.GetFiles(SnapshotDir(), "*snapshot.json");
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            var s = Load(f);
            Assert.True(BomTotals.PartsOnly(s) > 0, f);
            Assert.True(BomTotals.Structured(s) > 0, f);
        }
    }

    [Fact]
    public void EveryFixture_UsesOnlyKnownDocumentTypes()
    {
        foreach (var f in Directory.GetFiles(SnapshotDir(), "*snapshot.json"))
        {
            var unknown = Load(f).Components.Select(c => c.DocumentType).Where(t => !DocumentTypes.IsKnown(t)).Distinct().ToList();
            Assert.True(unknown.Count == 0, $"{Path.GetFileName(f)}: {string.Join(", ", unknown)}");
        }
    }

    // Expected values are the row counts of Inventor's own BOM views, not this tool's output.
    [Theory]
    [InlineData("robot-snapshot.json", 125, 175)]
    [InlineData("engine-snapshot.json", 184, 239)]
    public void RowCounts_MatchInventorBom(string file, int partsOnlyRows, int structuredRows)
    {
        var s = Load(Path.Combine(SnapshotDir(), file));
        Assert.Equal(partsOnlyRows, BomTotals.PartsOnlyRows(s).Count);
        Assert.Equal(structuredRows, BomTotals.StructuredRows(s).Count);
    }

    [Fact]
    public void EveryFixture_NonSuppressedComponentsBelowTheRootHaveABomStructure()
    {
        foreach (var f in Directory.GetFiles(SnapshotDir(), "*snapshot.json"))
        {
            var missing = Load(f).Components
                .Where(c => c.Level > 0 && !c.IsSuppressed && c.BomStructure == null)
                .Select(c => c.OccurrencePath).ToList();
            Assert.True(missing.Count == 0, $"{Path.GetFileName(f)}: {string.Join(", ", missing)}");
        }
    }
}