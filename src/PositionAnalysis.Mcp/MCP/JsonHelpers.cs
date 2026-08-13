using System.Text.Json;

namespace PositionAnalysis.Mcp.MCP;

internal static class JsonHelpers
{
    public static string? GetStringOrNull(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    public static int? GetIntOrNull(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
            return value;

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
            return parsed;

        return null;
    }

    public static List<string> GetStringList(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return new List<string>();

        // Tolerate a single string (or comma-separated string) in place of an array,
        // since callers sometimes pass e.g. pdNumbers: "D06116" instead of ["D06116"].
        if (property.ValueKind == JsonValueKind.String)
        {
            var raw = property.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return new List<string>();

            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        if (property.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }
}
