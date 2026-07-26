using System.Text.Json;

namespace Hms.Web;

/// <summary>One stored measurement: the value, and the range and flag it was judged by.</summary>
public sealed record StoredValue(string Value, string Unit, string Flag, string RefUsed);

/// <summary>
/// Reads the `lis.result.values` jsonb written by result entry. The stored flag is authoritative —
/// re-deriving it later against a revised range would silently change a released report (edge 22).
/// </summary>
public static class ResultValues
{
    public static IReadOnlyDictionary<string, StoredValue> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, StoredValue>();
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, StoredValue>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var v = prop.Value;
            result[prop.Name] = new StoredValue(
                Text(v, "value"), Text(v, "unit"), Text(v, "flag"), Text(v, "ref_used"));
        }
        return result;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? "" : "";
}
