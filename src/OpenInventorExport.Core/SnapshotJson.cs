using System.Text.Json;
using System.Text.Json.Serialization;
using OpenInventorExport.Core.Model;

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

    public static Snapshot? FromJson(string json) =>
        JsonSerializer.Deserialize<Snapshot>(json, Options);
}