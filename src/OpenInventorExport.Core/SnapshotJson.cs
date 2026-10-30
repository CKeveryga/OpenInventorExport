using OpenInventorExport.Core.Model;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenInventorExport.Core;

public static class SnapshotJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ToJson(Snapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static Snapshot? FromJson(string json)
    {
        var snapshot = JsonSerializer.Deserialize<Snapshot>(json, Options);
        if (snapshot != null && snapshot.SchemaVersion != Snapshot.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Snapshot has schema version {snapshot.SchemaVersion}; this build reads version " +
                $"{Snapshot.CurrentSchemaVersion}. Re-collect it from Inventor.");
        return snapshot;
    }
}