using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
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

/// <summary>One table file to write. Planned jobs are written; Conflict jobs are only reported.</summary>
public class TableJob
{
    public string TaskName { get; set; } = "";
    public OutputSpec Output { get; set; } = new();
    public string OutputPath { get; set; } = "";
    public JobState State { get; set; }
    public string? Detail { get; set; }
}

public static class ExportPlanner
{
    /// <param name="exclusions">Optional. Receives one record per unique file the task's rules left out.</param>
    public static List<ExportJob> Plan(Snapshot snapshot, ExportTask task, string outputRoot,
        List<ExclusionRecord>? exclusions = null)
    {
        var problems = ExportTaskValidator.Validate(task);
        if (problems.Count > 0)
            throw new ArgumentException(string.Join(" ", problems));

        var selected = Select(snapshot, task, exclusions);
        var jobs = new List<ExportJob>();
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // output path -> model that owns it

        foreach (var comp in selected)
        {
            string model = PathUtil.StripMemberSuffix(comp.FullPath!);
            foreach (var output in task.Outputs)
            {
                if (IsTable(output.Format)) continue;   // tables are planned by PlanTables

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

    /// <summary>
    /// Plans every task and marks an output file that two different tasks would both write as
    /// a Conflict, so nothing is overwritten within a run.
    /// </summary>
    public static List<ExportJob> PlanAll(Snapshot snapshot, IEnumerable<ExportTask> tasks, string outputRoot,
        List<ExclusionRecord>? exclusions = null)
    {
        var jobs = new List<ExportJob>();
        foreach (var task in tasks)
            jobs.AddRange(Plan(snapshot, task, outputRoot, exclusions));

        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in jobs.Where(j => j.State == JobState.Planned))
        {
            if (claimed.TryGetValue(job.OutputPath, out var owner))
            {
                job.State = JobState.Conflict;
                job.Detail = $"Same output file as {owner}";
            }
            else
            {
                claimed[job.OutputPath] = $"task '{job.TaskName}' ({Path.GetFileName(job.ModelPath)})";
            }
        }
        return jobs;
    }

    /// <summary>
    /// One job per table output. A table that would overwrite another table (same path, in this or
    /// another task) is a Conflict. Tables never collide with component files: their extensions differ.
    /// </summary>
    public static List<TableJob> PlanTables(IEnumerable<ExportTask> tasks, string outputRoot)
    {
        var jobs = new List<TableJob>();
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks)
        {
            foreach (var output in task.Outputs.Where(o => IsTable(o.Format)))
            {
                string root = task.FolderTemplate
                    .Replace("{task}", Clean(task.Name))
                    .Replace("{format}", output.Format.ToString().ToLowerInvariant())
                    .Replace('/', Path.DirectorySeparatorChar);
                string name = Clean(string.IsNullOrWhiteSpace(output.Name) ? task.Name : output.Name!)
                              + ExtensionFor(output.Format);
                string path = Path.Combine(outputRoot, root, SubFolder(output), name);

                var job = new TableJob { TaskName = task.Name, Output = output, OutputPath = path, State = JobState.Planned };
                if (claimed.TryGetValue(path, out var owner))
                {
                    job.State = JobState.Conflict;
                    job.Detail = $"Same output file as task '{owner}'";
                }
                else
                {
                    claimed[path] = task.Name;
                }
                jobs.Add(job);
            }
        }
        return jobs;
    }

    /// <summary>
    /// One representative component per unique model file that passes the selection. A file is left out
    /// only if every one of its occurrences is rejected; the first rejection is the reason reported.
    /// Virtual and unresolved components have no exportable file and are not reported here.
    /// </summary>
    private static List<ComponentRecord> Select(Snapshot snapshot, ExportTask task, List<ExclusionRecord>? exclusions)
    {
        var sel = task.Selection;
        var policy = new BomStructurePolicy(sel.BomStructures);
        var verdicts = BomVisibility.Evaluate(snapshot, policy, excludeSuppressed: true, inseparableDefault: ChildHandling.Include);

        var files = snapshot.Components
            .Where(c => c.FullPath != null && !c.IsVirtual && !c.IsUnresolved)
            .GroupBy(c => PathUtil.StripMemberSuffix(c.FullPath!), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var chosen = new List<ComponentRecord>();
        foreach (var file in files)
        {
            ComponentRecord? pick = null;
            ExclusionRecord? firstRejection = null;

            foreach (var c in file)
            {
                var rejection = Reject(c, verdicts[c], sel);
                if (rejection == null)
                {
                    pick = c;
                    break;
                }
                if (firstRejection == null)
                    firstRejection = new ExclusionRecord
                    {
                        Context = task.Name,
                        OccurrencePath = c.OccurrencePath,
                        FileName = c.FileName,
                        Reason = rejection.Value.Reason,
                        Detail = rejection.Value.Detail
                    };
            }

            if (pick != null) chosen.Add(pick);
            else if (firstRejection != null) exclusions?.Add(firstRejection);
        }
        return chosen;
    }

    /// <summary>The unique model files (member suffix stripped) the task selects. Used by the task's tables.</summary>
    internal static HashSet<string> SelectedFiles(Snapshot snapshot, ExportTask task) =>
        new HashSet<string>(
            Select(snapshot, task, null).Select(c => PathUtil.StripMemberSuffix(c.FullPath!)),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Why this occurrence does not pass the selection, or null if it passes.</summary>
    private static (ExclusionReason Reason, string? Detail)? Reject(
        ComponentRecord c, BomVisibility.Verdict verdict, ExportSelection sel)
    {
        if (!verdict.Listed)
            return (verdict.Reason, verdict.Detail);

        if (sel.Scope == ExportScope.TopLevelOnly && c.Level != 0)
            return (ExclusionReason.Scope, "Not the top-level assembly");

        if (sel.DocumentTypes.Count > 0 &&
            !sel.DocumentTypes.Contains(c.DocumentType, StringComparer.OrdinalIgnoreCase))
            return (ExclusionReason.DocumentType, $"{c.DocumentType} is not in the selected types");

        if (!ContentCenterFilter.Passes(c, sel.ContentCenter))
            return (ExclusionReason.ContentCenter, ContentCenterFilter.Describe(c, sel.ContentCenter));

        foreach (var f in sel.Filters)
            if (!Passes(c, f))
                return (ExclusionReason.PropertyFilter, NotMet(c, f));

        return null;
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
            //case FilterOperator.LessThan: return TryNumbers(value, wanted, out var a1, out var b1) && a1 < b1;
            //case FilterOperator.LessThanOrEqual: return TryNumbers(value, wanted, out var a2, out var b2) && a2 <= b2;
            //case FilterOperator.GreaterThan: return TryNumbers(value, wanted, out var a3, out var b3) && a3 > b3;
            //case FilterOperator.GreaterThanOrEqual: return TryNumbers(value, wanted, out var a4, out var b4) && a4 >= b4;
            case FilterOperator.LessThan:
            case FilterOperator.LessThanOrEqual:
            case FilterOperator.GreaterThan:
            case FilterOperator.GreaterThanOrEqual:
                return NumberTest(f, value, wanted);
            default: return false;
        }
    }

    internal static bool IsNumeric(FilterOperator op) =>
        op == FilterOperator.LessThan || op == FilterOperator.LessThanOrEqual ||
        op == FilterOperator.GreaterThan || op == FilterOperator.GreaterThanOrEqual;

    private static bool NumberTest(PropertyFilter f, string value, string wanted)
    {
        if (!Units.TryCompare(f.Set, f.Property, value, wanted, out double a, out double b, out _))
            return false;
        switch (f.Operator)
        {
            case FilterOperator.LessThan: return a < b;
            case FilterOperator.LessThanOrEqual: return a <= b;
            case FilterOperator.GreaterThan: return a > b;
            case FilterOperator.GreaterThanOrEqual: return a >= b;
            default: return false;
        }
    }

    private static string NotMet(ComponentRecord c, PropertyFilter f)
    {
        string value = c.FindProperty(f.Set, f.Property)?.Value ?? "";
        if (IsNumeric(f.Operator) && !Units.TryCompare(f.Set, f.Property, value, f.Value ?? "", out _, out _, out var problem))
            return $"{f.Property} {problem}";
        return $"{f.Property} {f.Operator} \"{f.Value}\" is not met";
    }

    private static string ResolveFolder(ExportTask task, OutputSpec output, ComponentRecord comp)
    {
        string root = task.FolderTemplate
            .Replace("{task}", Clean(task.Name))
            .Replace("{format}", output.Format.ToString().ToLowerInvariant())
            .Replace("{type}", Clean(comp.DocumentType));
        return Path.Combine(root.Replace('/', Path.DirectorySeparatorChar), SubFolder(output));
    }

    private static string SubFolder(OutputSpec output) =>
        (output.Folder ?? "").Trim().Replace('/', Path.DirectorySeparatorChar);

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
            case ExportFormat.Csv: return ".csv";
            case ExportFormat.Xlsx: return ".xlsx";
            default: return ".dwf";
        }
    }

    /// <summary>Csv and Xlsx: one table per output instead of one file per component.</summary>
    public static bool IsTable(ExportFormat f) => f == ExportFormat.Csv || f == ExportFormat.Xlsx;
}