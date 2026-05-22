using System.Text.Json;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public static class RedisConnectionImportParser
{
    public static RedisConnectionImportResult Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Redis connection import content is empty.");

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        var connectionElements = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToList(),
            JsonValueKind.Object when TryGetWrappedArray(root, out var wrappedArray) => wrappedArray.EnumerateArray().ToList(),
            JsonValueKind.Object => [root],
            _ => throw new InvalidOperationException("Unsupported Redis connection import format. Expected a JSON object or array.")
        };

        var result = new RedisConnectionImportResult();
        var separators = new List<string>();

        foreach (var element in connectionElements)
        {
            var host = ReadString(element, "host")?.Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                result.Warnings.Add("Skipped Redis connection entry without a host.");
                continue;
            }

            var port = ReadString(element, "port")?.Trim();
            var endpoint = string.IsNullOrWhiteSpace(port) ? host : $"{host}:{port}";
            var auth = ReadString(element, "auth")?.Trim();
            var username = ReadString(element, "username")?.Trim();
            var displayName = FirstNonEmpty(
                    ReadString(element, "connectionName"),
                    ReadString(element, "name"),
                    endpoint)
                ?.Trim() ?? endpoint;

            var separator = ReadString(element, "separator")?.Trim();
            if (!string.IsNullOrWhiteSpace(separator))
                separators.Add(separator);

            result.Caches.Add(new RedisCacheEntry
            {
                DisplayName = displayName,
                ConnectionString = BuildConnectionString(endpoint, auth, username),
                Database = 0
            });
        }

        if (result.Caches.Count == 0)
            throw new InvalidOperationException("No importable Redis connections were found in the file.");

        if (separators.Count > 0)
        {
            result.SuggestedSeparator = separators
                .GroupBy(static separator => separator, StringComparer.Ordinal)
                .OrderByDescending(static group => group.Count())
                .ThenBy(static group => group.Key, StringComparer.Ordinal)
                .First()
                .Key;
            result.HasMixedSeparators = separators.Distinct(StringComparer.Ordinal).Skip(1).Any();
        }

        return result;
    }

    private static bool TryGetWrappedArray(JsonElement element, out JsonElement array)
    {
        foreach (var propertyName in new[] { "connections", "items", "entries", "data" })
        {
            if (element.TryGetProperty(propertyName, out array) && array.ValueKind == JsonValueKind.Array)
                return true;
        }

        array = default;
        return false;
    }

    private static string BuildConnectionString(string endpoint, string? auth, string? username)
    {
        var segments = new List<string> { endpoint };
        if (!string.IsNullOrWhiteSpace(username))
            segments.Add($"user={username}");
        if (!string.IsNullOrWhiteSpace(auth))
            segments.Add($"password={auth}");

        if (TryGetPort(endpoint, out var port) && port == 6380)
            segments.Add("ssl=True");

        segments.Add("abortConnect=False");
        return string.Join(',', segments);
    }

    private static bool TryGetPort(string endpoint, out int port)
    {
        port = 0;
        var separatorIndex = endpoint.LastIndexOf(':');
        if (separatorIndex < 0 || separatorIndex == endpoint.Length - 1)
            return false;

        return int.TryParse(endpoint[(separatorIndex + 1)..], out port);
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? value.ToString()
            : null;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}