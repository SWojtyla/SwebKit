using System.Text.Json;

namespace SwebKit.Redis;

public static class RedisValueHelpers
{
    public static string MaskConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return string.Empty;

        var parts = connectionString.Split(',', StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!parts[i].StartsWith("password=", StringComparison.OrdinalIgnoreCase))
                continue;

            parts[i] = "password=***";
        }

        return string.Join(',', parts);
    }

    public static string TruncateValue(string? value, int maxLength = 10_240)
    {
        if (string.IsNullOrEmpty(value) || maxLength <= 0 || value.Length <= maxLength)
            return value ?? string.Empty;

        return value[..maxLength] + "\n... (truncated)";
    }

    public static string FormatJsonIfValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? string.Empty;

        var trimmed = value.Trim();
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('[')))
            return value;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return value;
        }
    }

    public static string TypeToBadgeClass(string? type) => type switch
    {
        "string" => "t-string",
        "hash" => "t-hash",
        "list" => "t-list",
        "set" => "t-set",
        "zset" => "t-zset",
        "stream" => "t-stream",
        _ => "t-unknown"
    };
}
