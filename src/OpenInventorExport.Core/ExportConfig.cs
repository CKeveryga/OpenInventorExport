using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.Core;

/// <summary>One property column in the BOM table, after File Name and Qty.</summary>
public class BomColumn
{
    public string Name { get; set; } = "";
    /// <summary>Which Inventor property set the property is in. Default: Design Tracking.</summary>
    public string Set { get; set; } = PropertySetNames.DesignTracking;
}

/// <summary>The top-level config file.</summary>
public class ExportConfig
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Must be written in the file. 0 means "missing".</summary>
    public int SchemaVersion { get; set; }
    public List<ExportTask> Tasks { get; set; } = new();
    /// <summary>Optional. Properties every applicable file should have. Empty = no property check.</summary>
    public List<SchemaProperty> Properties { get; set; } = new();
    /// <summary>Optional. How the BOM is rolled up. Missing = the defaults of RollupOptions.</summary>
    public RollupOptions Rollup { get; set; } = new();
    /// <summary>Optional. Property columns of the BOM table.</summary>
    public List<BomColumn> BomColumns { get; set; } = new();

    /// <summary>The same list as a PropertySchema, ready for PropertySchemaValidator.</summary>
    public PropertySchema ToPropertySchema() => new PropertySchema { Properties = Properties };

    /// <summary>The BOM columns in the form CsvTableWriter takes.</summary>
    public List<(string Set, string Name)> ToCsvColumns() =>
        BomColumns.Select(c => (c.Set, c.Name)).ToList();
}

/// <summary>Reads and writes the config file. Every mistake is an error, never silently ignored.</summary>
public static class ExportConfigJson
{
    public static string ToJson(ExportConfig config) =>
        JsonSerializer.Serialize(
            new ExportConfig
            {
                SchemaVersion = ExportConfig.CurrentSchemaVersion,
                Tasks = config.Tasks,
                Properties = config.Properties,
                Rollup = config.Rollup,
                BomColumns = config.BomColumns
            },
            ExportTaskJson.Options);

    /// <summary>Parses and validates. Throws InvalidDataException listing every problem found.</summary>
    public static ExportConfig FromJson(string json)
    {
        ExportConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<ExportConfig>(json, ExportTaskJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Config file is not valid: " + ex.Message, ex);
        }

        if (config == null)
            throw new InvalidDataException("Config file is empty.");

        if (config.SchemaVersion != ExportConfig.CurrentSchemaVersion)
            throw new InvalidDataException(
                config.SchemaVersion == 0
                    ? $"Config file has no SchemaVersion. Add \"SchemaVersion\": {ExportConfig.CurrentSchemaVersion}."
                    : $"Config has schema version {config.SchemaVersion}; this build reads version {ExportConfig.CurrentSchemaVersion}.");

        var problems = new List<string>();

        if (config.Tasks.Count == 0)
            problems.Add("Config has no tasks.");

        foreach (var dup in config.Tasks
                     .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                     .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
            problems.Add($"Task name '{dup.Key}' is used more than once.");

        foreach (var task in config.Tasks)
            problems.AddRange(ExportTaskValidator.Validate(task));

        problems.AddRange(ValidateProperties(config.Properties));
        problems.AddRange(ValidateColumns(config.BomColumns));

        if (problems.Count > 0)
            throw new InvalidDataException("Config file has problems: " + string.Join(" ", problems));

        return config;
    }

    // Only the sets the traverser reads. Add new sets here when the traverser learns them.
    private static readonly HashSet<string> KnownSets = new HashSet<string>
    {
        PropertySetNames.DesignTracking,
        PropertySetNames.UserDefined
    };

    private static List<string> ValidateProperties(List<SchemaProperty> properties)
    {
        var problems = new List<string>();

        foreach (var p in properties)
        {
            if (string.IsNullOrWhiteSpace(p.Name))
                problems.Add("A property entry has no Name.");
            if (!KnownSets.Contains(p.Set))
                problems.Add($"Property '{p.Name}': unknown Set '{p.Set}'. Known sets: {string.Join(", ", KnownSets)}.");

            foreach (var type in p.AppliesTo.Where(t => !DocumentTypes.IsKnown(t)))
                problems.Add($"Property '{p.Name}': unknown document type '{type}' in AppliesTo. Known types: {string.Join(", ", DocumentTypes.All)}.");
        }

        foreach (var dup in properties
                     .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                     .GroupBy(p => p.Set + "|" + p.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
            problems.Add($"Property '{dup.First().Name}' is listed more than once in '{dup.First().Set}'.");

        return problems;
    }

    private static List<string> ValidateColumns(List<BomColumn> columns)
    {
        var problems = new List<string>();

        foreach (var c in columns)
        {
            if (string.IsNullOrWhiteSpace(c.Name))
                problems.Add("A BOM column has no Name.");
            if (!KnownSets.Contains(c.Set))
                problems.Add($"BOM column '{c.Name}': unknown Set '{c.Set}'. Known sets: {string.Join(", ", KnownSets)}.");
        }

        foreach (var dup in columns
                     .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                     .GroupBy(c => c.Set + "|" + c.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
            problems.Add($"BOM column '{dup.First().Name}' is listed more than once in '{dup.First().Set}'.");

        return problems;
    }
}