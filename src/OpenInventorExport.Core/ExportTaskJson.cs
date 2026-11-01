using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenInventorExport.Core;

/// <summary>Reads and writes an ExportTask as JSON. Mistakes are errors, never silently ignored.</summary>
public static class ExportTaskJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,   // allow // comments in hand-edited files
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,  // a misspelled name is an error
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ToJson(ExportTask task) =>
        JsonSerializer.Serialize(task, Options);

    /// <summary>Parses and validates a task. Throws InvalidDataException listing every problem.</summary>
    public static ExportTask FromJson(string json)
    {
        ExportTask? task;
        try
        {
            task = JsonSerializer.Deserialize<ExportTask>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Task file is not valid: " + ex.Message, ex);
        }

        if (task == null)
            throw new InvalidDataException("Task file is empty.");

        var problems = ExportTaskValidator.Validate(task);
        if (problems.Count > 0)
            throw new InvalidDataException("Task file has problems: " + string.Join(" ", problems));

        return task;
    }
}