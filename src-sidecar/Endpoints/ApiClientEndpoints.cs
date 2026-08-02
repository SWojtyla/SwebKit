using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class ApiClientEndpoints
{
    public static void MapApiClientEndpoints(this WebApplication app)
    {
        app.MapPost("/api/api-client/execute", async (
            ExecuteRequestRequest req,
            IHttpRequestExecutor executor,
            CollectionRepository collections,
            EnvironmentRepository environments,
            DemoModeService demo,
            CancellationToken ct) =>
        {
            var collection = await ResolveCollectionAsync(req.CollectionId, collections, demo);
            if (collection is null && req.CollectionId is not null)
                return Results.NotFound("Collection not found");

            collection ??= new ApiCollection();

            ApiEnvironment? activeEnvironment = null;
            if (!string.IsNullOrWhiteSpace(req.EnvironmentId))
            {
                activeEnvironment = environments.Environments.FirstOrDefault(e => e.Id == req.EnvironmentId);
                if (activeEnvironment is null)
                    return Results.NotFound("Environment not found");
            }

            try
            {
                var result = await executor.ExecuteAsync(req.Request, collection, activeEnvironment, ct);
                return Results.Ok(Map(result));
            }
            catch (Exception ex)
            {
                return Results.Problem($"Request failed: {ex.Message}");
            }
        });

        app.MapPost("/api/api-client/preview-keyvault-secret", (
            PreviewKeyVaultSecretRequest req,
            IKeyVaultSecretResolver resolver,
            CancellationToken cancellationToken) => PreviewKeyVaultSecretAsync(req, resolver, cancellationToken));
    }

    /// <summary>
    /// Handler body for the preview endpoint, extracted so it can be unit tested directly against a
    /// fake <see cref="IKeyVaultSecretResolver"/> without spinning up the ASP.NET pipeline.
    /// </summary>
    internal static async Task<IResult> PreviewKeyVaultSecretAsync(
        PreviewKeyVaultSecretRequest req,
        IKeyVaultSecretResolver resolver,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(req.SecretName))
            return Results.BadRequest(new { error = "Secret name is required" });

        if (!resolver.IsAvailable)
            return Results.Problem("No key vaults are configured");

        var raw = await resolver.GetSecretAsync(req.SecretName, req.KeyVaultName, cancellationToken).ConfigureAwait(false);

        if (raw.StartsWith("[KV_ERROR:", StringComparison.Ordinal) || raw.StartsWith("[KV_UNAVAILABLE:", StringComparison.Ordinal))
        {
            return Results.Ok(new KeyVaultPreviewResponse("error", null, raw));
        }

        return Results.Ok(new KeyVaultPreviewResponse("ok", MaskSecret(raw), null));
    }

    /// <summary>
    /// Masks a secret value for display. The dot count is clamped to a narrow range rather than
    /// reflecting the exact length, so the preview can't be used to infer the real secret's size.
    /// </summary>
    internal static string MaskSecret(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var dots = Math.Clamp(value.Length, 4, 16);
        return new string('•', dots);
    }

    private static async Task<ApiCollection?> ResolveCollectionAsync(string? collectionId, CollectionRepository collections, DemoModeService demo)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
            return null;

        if (demo.IsDemoMode && collectionId == DemoApiCollectionFactory.DemoCollectionId)
            return DemoApiCollectionFactory.CreateDemoCollection();

        // Load the latest persisted store so we get the full tree and variables.
        await collections.LoadAsync().ConfigureAwait(false);
        return collections.Collections.FirstOrDefault(c => c.Id == collectionId);
    }

    private static ApiClientExecutionResponse Map(HttpRequestResult result) =>
        new(
            result.ResolvedUrl,
            result.Method,
            result.StatusCode,
            result.StatusText,
            result.ErrorMessage,
            result.Elapsed.TotalMilliseconds,
            result.ContentLength,
            result.ContentType,
            result.ResponseBody,
            result.ResponseBodyTruncated,
            result.ResponseHeaders.Select(h => new ResponseHeaderDto(h.Name, h.Value)).ToList(),
            result.CaptureWarnings.ToList(),
            result.GraphQlErrors);
}

public sealed class ExecuteRequestRequest
{
    public HttpRequestEntry Request { get; set; } = new();
    public string? CollectionId { get; set; }
    public string? EnvironmentId { get; set; }
}

public sealed record ApiClientExecutionResponse(
    string ResolvedUrl,
    string Method,
    int StatusCode,
    string StatusText,
    string? ErrorMessage,
    double ElapsedMs,
    long ContentLength,
    string? ContentType,
    string? ResponseBody,
    bool ResponseBodyTruncated,
    IReadOnlyList<ResponseHeaderDto> Headers,
    IReadOnlyList<string> CaptureWarnings,
    IReadOnlyList<GraphQlError>? GraphQlErrors);

public sealed record ResponseHeaderDto(string Name, string Value);

public sealed record PreviewKeyVaultSecretRequest(string? KeyVaultName, string SecretName);

public sealed record KeyVaultPreviewResponse(
    string Status,
    string? MaskedValue,
    string? Error);
