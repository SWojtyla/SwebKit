using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Services;

/// <summary>
/// Exports a collection to SwebKit's own versioned JSON format.
/// This is the lossless format — every field is preserved on round-trip.
/// </summary>
public sealed class SwebKitCollectionExporter : ICollectionExporter
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string FileExtension => ".sweb.json";
    public string FormatName => "SwebKit JSON";

    public Task<byte[]> ExportAsync(
        ApiCollection collection,
        IReadOnlyList<ApiEnvironment> environments,
        CancellationToken cancellationToken = default)
    {
        var bundle = new SwebKitCollectionBundle
        {
            SchemaVersion = 1,
            ExportedAt = DateTimeOffset.UtcNow,
            Collection = collection,
            Environments = environments.ToList(),
        };

        var json = JsonSerializer.Serialize(bundle, Options);
        return Task.FromResult(Encoding.UTF8.GetBytes(json));
    }
}

/// <summary>
/// Imports a SwebKit-format collection bundle.
/// </summary>
public sealed class SwebKitCollectionImporter : ICollectionImporter
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public bool CanImport(byte[] payload)
    {
        try
        {
            var json = Encoding.UTF8.GetString(payload).TrimStart();
            if (!json.StartsWith("{", StringComparison.Ordinal)) return false;
            using var doc = JsonDocument.Parse(json);
            // SwebKit bundles always have both "schemaVersion" and "collection" at the root
            return doc.RootElement.TryGetProperty("schemaVersion", out _) &&
                   doc.RootElement.TryGetProperty("collection", out _);
        }
        catch { return false; }
    }

    public Task<CollectionImportResult> ImportAsync(
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        try
        {
            var json = Encoding.UTF8.GetString(payload);
            var bundle = JsonSerializer.Deserialize<SwebKitCollectionBundle>(json, Options);

            if (bundle is null || bundle.Collection is null)
                return Task.FromResult(new CollectionImportResult { Warnings = ["Could not parse SwebKit collection bundle."] });

            var requestCount = CountRequests(bundle.Collection.Nodes);
            var captureCount = CountCaptures(bundle.Collection.Nodes);

            return Task.FromResult(new CollectionImportResult
            {
                Collections = [bundle.Collection],
                Environments = bundle.Environments ?? [],
                RequestCount = requestCount,
                CaptureRuleCount = captureCount,
                AuthConfigsRequiringReEntry = CountAuthConfigs(bundle.Collection.Nodes),
                Warnings = warnings,
            });
        }
        catch (JsonException ex)
        {
            return Task.FromResult(new CollectionImportResult
            {
                Warnings = [$"JSON parse error: {ex.Message}"],
            });
        }
    }

    private static int CountRequests(List<ApiCollectionNode> nodes)
    {
        var count = 0;
        foreach (var n in nodes)
        {
            if (n.Type == ApiCollectionNodeType.Request) count++;
            else if (n.Type == ApiCollectionNodeType.Folder) count += CountRequests(n.Children);
        }
        return count;
    }

    private static int CountCaptures(List<ApiCollectionNode> nodes)
    {
        var count = 0;
        foreach (var n in nodes)
        {
            if (n.Type == ApiCollectionNodeType.Request) count += n.Request?.CaptureRules.Count(r => r.IsEnabled) ?? 0;
            else if (n.Type == ApiCollectionNodeType.Folder) count += CountCaptures(n.Children);
        }
        return count;
    }

    private static int CountAuthConfigs(List<ApiCollectionNode> nodes)
    {
        // Count requests that have a non-None auth config requiring a credential key
        var count = 0;
        foreach (var n in nodes)
        {
            if (n.Type == ApiCollectionNodeType.Request &&
                n.Request?.Auth is { Type: not AuthType.None and not AuthType.Inherited, CredentialKey: not null })
                count++;
            else if (n.Type == ApiCollectionNodeType.Folder)
                count += CountAuthConfigs(n.Children);
        }
        return count;
    }
}

/// <summary>
/// Imports a standalone SwebKit environments file (just an <see cref="EnvironmentsStore"/> JSON).
/// </summary>
public sealed class SwebKitEnvironmentImporter : IEnvironmentImporter
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public bool CanImport(byte[] payload)
    {
        try
        {
            var json = Encoding.UTF8.GetString(payload);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("environments", out _) ||
                   doc.RootElement.TryGetProperty("Environments", out _);
        }
        catch { return false; }
    }

    public Task<EnvironmentImportResult> ImportAsync(
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = Encoding.UTF8.GetString(payload);
            var store = JsonSerializer.Deserialize<EnvironmentsStore>(json, Options);
            if (store is null)
                return Task.FromResult(new EnvironmentImportResult { Warnings = ["Could not parse environment file."] });

            return Task.FromResult(new EnvironmentImportResult
            {
                Environments = store.Environments,
            });
        }
        catch (JsonException ex)
        {
            return Task.FromResult(new EnvironmentImportResult
            {
                Warnings = [$"JSON parse error: {ex.Message}"],
            });
        }
    }
}

// ── Bundle DTO (not a domain type; serialization artefact only) ───────────────

internal sealed class SwebKitCollectionBundle
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; }
    public ApiCollection? Collection { get; set; }
    public List<ApiEnvironment>? Environments { get; set; }
}
