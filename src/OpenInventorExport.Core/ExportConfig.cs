using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenInventorExport.Core;

/// <summary>The top-level config file. More sections (schema, rollup, columns) are added in later steps.</summary>
public class ExportConfig
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Must be written in the file. 0 means "missing".</summary>
    public int SchemaVersion { get; set; }
    public List<ExportTask> Tasks { get; set; } = new();
}

/// <summary>Reads and writes the config file. Every mistake is an error, never silently ignored.</summary>
public static class ExportConfigJson
{
    public static string ToJson(ExportConfig config) =>
        JsonSerializer.Serialize(
            new ExportConfig { SchemaVersion = ExportConfig.CurrentSchemaVersion, Tasks = config.Tasks },
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

        if (problems.Count > 0)
            throw new InvalidDataException("Config file has problems: " + string.Join(" ", problems));

        return config;
    }
}