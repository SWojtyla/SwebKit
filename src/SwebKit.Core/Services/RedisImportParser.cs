using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public static class RedisImportParser
{
    public static IReadOnlyList<RedisImportEntry> Parse(string json)
    {
        var root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Redis import content is empty.");

        return root switch
        {
            JsonArray array => ParseEntryArray(array),
            JsonObject obj => ParseObjectRoot(obj),
            _ => throw new InvalidOperationException("Unsupported Redis import format. Expected a JSON object or array.")
        };
    }

    private static IReadOnlyList<RedisImportEntry> ParseObjectRoot(JsonObject obj)
    {
        foreach (var propertyName in new[] { "entries", "items", "keys", "data" })
        {
            if (obj[propertyName] is JsonArray array)
                return ParseEntryArray(array);

            if (obj[propertyName] is JsonObject nestedObject)
                return ParseObjectMap(nestedObject);
        }

        return ParseObjectMap(obj);
    }

    private static IReadOnlyList<RedisImportEntry> ParseObjectMap(JsonObject obj)
    {
        var entries = new List<RedisImportEntry>(obj.Count);
        foreach (var property in obj)
        {
            entries.Add(ParseKeyValueEntry(property.Key, property.Value, typeHint: null, ttl: null));
        }

        return entries;
    }

    private static IReadOnlyList<RedisImportEntry> ParseEntryArray(JsonArray array)
    {
        var entries = new List<RedisImportEntry>(array.Count);
        foreach (var node in array)
        {
            if (node is not JsonObject obj)
                throw new InvalidOperationException("Redis import entry arrays must contain objects.");

            var key = ReadFirstString(obj, "key", "name", "redisKey")
                ?? throw new InvalidOperationException("Redis import entry is missing a key.");
            var typeHint = ReadFirstString(obj, "type", "valueType", "redisType", "dataType");
            var ttl = ReadTtl(obj);

            JsonNode? payload = obj["value"]
                ?? obj["values"]
                ?? obj["fields"]
                ?? obj["members"]
                ?? obj["items"]
                ?? obj["entries"];

            payload ??= BuildImplicitPayload(obj);

            entries.Add(ParseKeyValueEntry(key, payload, typeHint, ttl));
        }

        return entries;
    }

    private static JsonNode? BuildImplicitPayload(JsonObject obj)
    {
        var implicitPayload = new JsonObject();
        foreach (var property in obj)
        {
            if (property.Key is "key" or "name" or "redisKey" or "type" or "valueType" or "redisType" or "dataType" or "ttl" or "ttlSeconds" or "expiresInSeconds" or "expirySeconds")
                continue;

            implicitPayload[property.Key] = property.Value?.DeepClone();
        }

        return implicitPayload.Count == 0 ? null : implicitPayload;
    }

    private static RedisImportEntry ParseKeyValueEntry(string key, JsonNode? node, string? typeHint, TimeSpan? ttl)
    {
        var normalizedType = NormalizeType(typeHint) ?? InferType(node);

        return normalizedType switch
        {
            "string" => new RedisImportEntry
            {
                Key = key,
                Type = "string",
                StringValue = NodeToScalarString(node),
                Ttl = ttl
            },
            "hash" => new RedisImportEntry
            {
                Key = key,
                Type = "hash",
                HashFields = ParseHash(node),
                Ttl = ttl
            },
            "list" => new RedisImportEntry
            {
                Key = key,
                Type = "list",
                ListItems = ParseStringArray(node),
                Ttl = ttl
            },
            "set" => new RedisImportEntry
            {
                Key = key,
                Type = "set",
                SetMembers = ParseStringArray(node),
                Ttl = ttl
            },
            "zset" => new RedisImportEntry
            {
                Key = key,
                Type = "zset",
                SortedSetMembers = ParseSortedSet(node),
                Ttl = ttl
            },
            _ => throw new InvalidOperationException($"Unsupported Redis value type '{normalizedType}'.")
        };
    }

    private static string InferType(JsonNode? node)
    {
        return node switch
        {
            JsonArray array when array.All(IsSortedSetEntry) => "zset",
            JsonArray => "list",
            JsonObject obj when obj.Any(property => property.Key is "fields" or "hash") => "hash",
            JsonObject => "hash",
            _ => "string"
        };
    }

    private static Dictionary<string, string> ParseHash(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["fields"] is JsonObject nestedFields)
                obj = nestedFields;
            else if (obj["hash"] is JsonObject nestedHash)
                obj = nestedHash;

            return obj.ToDictionary(
                static property => property.Key,
                static property => NodeToScalarString(property.Value),
                StringComparer.Ordinal);
        }

        if (node is JsonArray array)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in array)
            {
                if (item is not JsonObject fieldObject)
                    throw new InvalidOperationException("Hash imports with array payloads must use objects.");

                var field = ReadFirstString(fieldObject, "field", "name")
                    ?? throw new InvalidOperationException("Hash import entry is missing a field name.");
                var valueNode = fieldObject["value"] ?? fieldObject["fieldValue"];
                fields[field] = NodeToScalarString(valueNode);
            }

            return fields;
        }

        throw new InvalidOperationException("Hash imports must use an object or field array payload.");
    }

    private static List<string> ParseStringArray(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            node = obj["items"] ?? obj["members"] ?? obj["values"];
        }

        if (node is not JsonArray array)
            throw new InvalidOperationException("Collection imports must use a JSON array payload.");

        return array.Select(NodeToScalarString).ToList();
    }

    private static List<RedisSortedSetEntry> ParseSortedSet(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["members"] is JsonArray membersArray)
                node = membersArray;
            else
            {
                return obj.Select(property => new RedisSortedSetEntry
                {
                    Member = property.Key,
                    Score = ParseScore(property.Value)
                }).ToList();
            }
        }

        if (node is not JsonArray array)
            throw new InvalidOperationException("Sorted-set imports must use a JSON object or array payload.");

        return array.Select(item =>
        {
            if (item is not JsonObject memberObject)
                throw new InvalidOperationException("Sorted-set imports with array payloads must use objects.");

            var member = ReadFirstString(memberObject, "member", "value", "name")
                ?? throw new InvalidOperationException("Sorted-set import entry is missing a member.");

            return new RedisSortedSetEntry
            {
                Member = member,
                Score = ParseScore(memberObject["score"])
            };
        }).ToList();
    }

    private static bool IsSortedSetEntry(JsonNode? node)
        => node is JsonObject obj && (obj["member"] is not null || obj["score"] is not null);

    private static string? ReadFirstString(JsonObject obj, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (obj[propertyName] is not null)
                return NodeToScalarString(obj[propertyName]);
        }

        return null;
    }

    private static TimeSpan? ReadTtl(JsonObject obj)
    {
        var ttlRaw = ReadFirstString(obj, "ttlSeconds", "ttl", "expiresInSeconds", "expirySeconds");
        if (string.IsNullOrWhiteSpace(ttlRaw))
            return null;

        if (double.TryParse(ttlRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return TimeSpan.FromSeconds(seconds);

        throw new InvalidOperationException($"Redis import TTL '{ttlRaw}' is not a valid number of seconds.");
    }

    private static string? NormalizeType(string? typeHint)
    {
        if (string.IsNullOrWhiteSpace(typeHint))
            return null;

        return typeHint.Trim().ToLowerInvariant() switch
        {
            "string" => "string",
            "hash" => "hash",
            "list" => "list",
            "set" => "set",
            "zset" => "zset",
            "sortedset" => "zset",
            "sorted-set" => "zset",
            "sorted_set" => "zset",
            _ => typeHint.Trim().ToLowerInvariant()
        };
    }

    private static string NodeToScalarString(JsonNode? node)
    {
        if (node is null)
            return string.Empty;

        var element = node.Deserialize<JsonElement>();
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText()
        };
    }

    private static double ParseScore(JsonNode? node)
    {
        var raw = NodeToScalarString(node);
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
            return score;

        throw new InvalidOperationException($"Sorted-set score '{raw}' is not a valid number.");
    }
}