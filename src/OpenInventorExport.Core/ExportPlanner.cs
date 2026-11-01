using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

public enum JobState { Planned, Conflict }

/// <summary>One file to produce. Planned jobs are executed; Conflict jobs are only reported.</summary>
public class ExportJob
{
    public string TaskName { get; set; } = "";
    public string ModelPath { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public OutputSpec Output { get; set; } = new();
    public string OutputPath { get; set; } = "";
    public JobState State { get; set; }
    public string? Detail { get; set; }
}

public static class ExportPlanner
{
    public static List<ExportJob> Plan(Snapshot snapshot, ExportTask task, string outputRoot)
    {
        var problems = ExportTaskValidator.Validate(task);
        if (problems.Count > 0)
            throw new ArgumentException(string.Join(" ", problems));

        var selected = Select(snapshot, task.Selection);
        var jobs = new List<ExportJob>();
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // output path -> model that owns it

        foreach (var comp in selected)
        {
            string model = PathUtil.StripMemberSuffix(comp.FullPath!);
            foreach (var output in task.Outputs)
            {
                string folder = ResolveFolder(task, output, comp);
                string file = PathUtil.DrawingBaseName(model) + ExtensionFor(output.Format);
                string outPath = Path.Combine(outputRoot, folder, file);

                var job = new ExportJob
                {
                    TaskName = task.Name,
                    ModelPath = model,
                    DocumentType = comp.DocumentType,
                    Output = output,
                    OutputPath = outPath,
                    State = JobState.Planned
                };

                if (claimed.TryGetValue(outPath, out var owner))
                {
                    job.State = JobState.Conflict;
                    job.Detail = $"Same output file as {owner}";
                }
                else
                {
                    claimed[outPath] = model;
                }
                jobs.Add(job);
            }
        }
        return jobs;
    }

    /// <summary>One representative component per unique model file that passes the selection.</summary>
    private static List<ComponentRecord> Select(Snapshot snapshot, ExportSelection sel)
    {
        return snapshot.Components
            .Where(c => c.FullPath != null && !c.IsSuppressed && !c.IsVirtual && !c.IsUnresolved)
            .Where(c => sel.Scope == ExportScope.AllUniqueFiles || c.Level == 0)
            .Where(c => sel.DocumentTypes.Count == 0 ||
                        sel.DocumentTypes.Contains(c.DocumentType, StringComparer.OrdinalIgnoreCase))
            .Where(c => sel.Filters.All(f => Passes(c, f)))
            .GroupBy(c => PathUtil.StripMemberSuffix(c.FullPath!), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(c => c.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool Passes(ComponentRecord c, PropertyFilter f)
    {
        string value = c.FindProperty(f.Set, f.Property)?.Value ?? "";
        string wanted = f.Value ?? "";
        switch (f.Operator)
        {
            case FilterOperator.Equals: return string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase);
            case FilterOperator.NotEquals: return !string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase);
            case FilterOperator.Contains: return value.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
            case FilterOperator.IsEmpty: return value.Length == 0;
            case FilterOperator.IsNotEmpty: return value.Length > 0;
            default: return false;
        }
    }

    private static string ResolveFolder(ExportTask task, OutputSpec output, ComponentRecord comp)
    {
        string folder = task.FolderTemplate
            .Replace("{task}", Clean(task.Name))
            .Replace("{format}", output.Format.ToString().ToLowerInvariant())
            .Replace("{type}", Clean(comp.DocumentType));
        return folder.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string Clean(string s)
    {
        foreach (char bad in Path.GetInvalidFileNameChars())
            s = s.Replace(bad, '_');
        return s;
    }

    public static string ExtensionFor(ExportFormat f)
    {
        switch (f)
        {
            case ExportFormat.Step: return ".stp";
            case ExportFormat.Pdf: return ".pdf";
            case ExportFormat.Dwg: return ".dwg";
            case ExportFormat.Dxf: return ".dxf";
            default: return ".dwf";
        }
    }
}