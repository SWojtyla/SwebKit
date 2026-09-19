using System.Text.Json.Nodes;
using Json.Path;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Applies post-request capture rules against the HTTP response,
/// writing extracted values back into collection or environment variables.
/// </summary>
public sealed class PostRequestCaptureExecutor(
    CollectionRepository collectionRepository,
    EnvironmentRepository environmentRepository,
    ILogger<PostRequestCaptureExecutor> logger,
    ILinkedStoreWriter? linkedStoreWriter = null) : IPostRequestCaptureExecutor
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ExecuteAsync(
        HttpRequestResult result,
        HttpRequestEntry request,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default)
    {
        var enabledRules = request.CaptureRules.Where(r => r.IsEnabled).ToList();
        if (enabledRules.Count == 0) return [];

        var warnings = new List<string>();
        var collectionDirty = false;
        var environmentDirty = false;

        foreach (var rule in enabledRules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var extracted = ExtractValue(result, rule);
                if (extracted is null)
                {
                    warnings.Add($"Capture '{rule.TargetVariable}': no match for {rule.Source} rule.");
                    continue;
                }

                // Write to the target scope
                if (string.Equals(rule.TargetScope, "collection", StringComparison.OrdinalIgnoreCase))
                {
                    var skipped = WriteToCollection(collection, rule.TargetVariable, extracted);
                    if (skipped is not null) warnings.Add($"Capture '{rule.TargetVariable}': {skipped}");
                    else collectionDirty = true;
                }
                else if (activeEnvironment is not null &&
                         string.Equals(rule.TargetScope, activeEnvironment.Id, StringComparison.Ordinal))
                {
                    var skipped = WriteToEnvironment(activeEnvironment, rule.TargetVariable, extracted);
                    if (skipped is not null) warnings.Add($"Capture '{rule.TargetVariable}': {skipped}");
                    else environmentDirty = true;
                }
                else
                {
                    warnings.Add($"Capture '{rule.TargetVariable}': target scope '{rule.TargetScope}' not found.");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Capture rule '{Target}' failed", rule.TargetVariable);
                warnings.Add($"Capture '{rule.TargetVariable}': {ex.Message}");
            }
        }

        if (collectionDirty)
        {
            try
            {
                // Linked collections persist to their folder's collection.json — writing them to
                // collections.json would fork the collection into app storage.
                var routed = collection.LinkedRootId is not null && linkedStoreWriter is not null &&
                    await linkedStoreWriter.TryWriteCollectionAsync(collection, cancellationToken).ConfigureAwait(false);
                if (!routed)
                    await collectionRepository.UpdateCollectionAsync(collection).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to persist collection after capture");
                warnings.Add("Captured collection variables were updated for this session but could not be saved to disk.");
            }
        }

        if (environmentDirty && activeEnvironment is not null)
        {
            try
            {
                var routed = activeEnvironment.LinkedRootId is not null && linkedStoreWriter is not null &&
                    await linkedStoreWriter.TryWriteEnvironmentAsync(activeEnvironment, cancellationToken).ConfigureAwait(false);
                if (!routed)
                    await environmentRepository.UpdateEnvironmentAsync(activeEnvironment).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to persist environment after capture");
                warnings.Add("Captured environment variables were updated for this session but could not be saved to disk.");
            }
        }

        return warnings;
    }

    // ── Extraction ─────────────────────────────────────────────────────────────

    private static string? ExtractValue(HttpRequestResult result, CaptureRule rule)
    {
        return rule.Source switch
        {
            CaptureSource.StatusCode => result.StatusCode.ToString(),
            CaptureSource.ResponseHeader => ExtractHeader(result, rule.HeaderName),
            CaptureSource.BodyJsonPath => ExtractJsonPath(result.ResponseBody, rule.JsonPath),
            _ => null,
        };
    }

    private static string? ExtractHeader(HttpRequestResult result, string? headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName)) return null;
        return result.ResponseHeaders
            .FirstOrDefault(h => string.Equals(h.Name, headerName, StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private static string? ExtractJsonPath(string? body, string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(jsonPath))
            return null;

        JsonNode? node;
        try { node = JsonNode.Parse(body); }
        catch { return null; }

        if (node is null) return null;

        if (!JsonPath.TryParse(jsonPath, out var path))
            return null;

        var results = path.Evaluate(node);
        var first = results.Matches?.FirstOrDefault();
        if (first?.Value is null) return null;

        return first.Value switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v => v.ToJsonString(),
            _ => first.Value.ToJsonString(),
        };
    }

    // ── Mutation helpers ───────────────────────────────────────────────────────

    /// <returns>A skip reason when the target exists but is not a plain variable, otherwise null.</returns>
    private static string? WriteToCollection(ApiCollection collection, string key, string value)
    {
        var existing = collection.Variables.FirstOrDefault(v => v.Key == key);
        if (existing is not null)
        {
            // A captured Value would be dead data on a generated variable — the generator still
            // wins at scope-build time, so writing here only pretends the capture worked.
            if (existing.Generator is not null)
                return "target is a generated variable — re-point the rule at a plain variable or remove the generator.";
            existing.Value = value;
            return null;
        }

        collection.Variables.Add(new CollectionVariable { Key = key, Value = value, IsEnabled = true });
        return null;
    }

    /// <returns>A skip reason when the target exists but is not a plain variable, otherwise null.</returns>
    private static string? WriteToEnvironment(ApiEnvironment env, string key, string value)
    {
        var existing = env.Variables.FirstOrDefault(v => v.Key == key);
        if (existing is not null)
        {
            // Same dead-write problem for credential-store, Key Vault and generated variables:
            // resolution ignores Value for every non-plain source.
            if (existing.SecretSource != EnvironmentVariableSecretSource.Plain)
                return $"target is a {existing.SecretSource} variable — re-point the rule at a plain variable.";
            existing.Value = value;
            return null;
        }

        env.Variables.Add(new EnvironmentVariable
        {
            Key = key,
            Value = value,
            SecretSource = EnvironmentVariableSecretSource.Plain,
            IsEnabled = true,
        });
        return null;
    }
}
