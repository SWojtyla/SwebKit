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
    ILogger<PostRequestCaptureExecutor> logger) : IPostRequestCaptureExecutor
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
                    WriteToCollection(collection, rule.TargetVariable, extracted);
                    collectionDirty = true;
                }
                else if (activeEnvironment is not null &&
                         string.Equals(rule.TargetScope, activeEnvironment.Id, StringComparison.Ordinal))
                {
                    WriteToEnvironment(activeEnvironment, rule.TargetVariable, extracted);
                    environmentDirty = true;
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

        // Persist mutations asynchronously (fire-and-forget errors logged, not surfaced to user)
        if (collectionDirty)
        {
            try { await collectionRepository.UpdateCollectionAsync(collection).ConfigureAwait(false); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to persist collection after capture"); }
        }

        if (environmentDirty && activeEnvironment is not null)
        {
            try { await environmentRepository.UpdateEnvironmentAsync(activeEnvironment).ConfigureAwait(false); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to persist environment after capture"); }
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

    private static void WriteToCollection(ApiCollection collection, string key, string value)
    {
        var existing = collection.Variables.FirstOrDefault(v => v.Key == key);
        if (existing is not null)
        {
            existing.Value = value;
        }
        else
        {
            collection.Variables.Add(new CollectionVariable { Key = key, Value = value, IsEnabled = true });
        }
    }

    private static void WriteToEnvironment(ApiEnvironment env, string key, string value)
    {
        var existing = env.Variables.FirstOrDefault(v => v.Key == key);
        if (existing is not null)
        {
            existing.Value = value;
        }
        else
        {
            env.Variables.Add(new EnvironmentVariable
            {
                Key = key,
                Value = value,
                SecretSource = EnvironmentVariableSecretSource.Plain,
                IsEnabled = true,
            });
        }
    }
}
