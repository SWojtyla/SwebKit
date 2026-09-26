using SwebKit.Sidecar.Services;
using System.Text.Json.Nodes;
using Json.Path;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Endpoints;

public static class ApiClientEndpoints
{
    /// <summary>Upper bound for the body handed to JSONPath evaluation — the request/response
    /// bodies sent here are already capped far below this, so the limit only guards against abuse.</summary>
    private const int MaxJsonPathBodyLength = 8 * 1024 * 1024;

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
                return ApiErrors.NotFound("Collection not found");

            collection ??= new ApiCollection();

            ApiEnvironment? activeEnvironment = null;
            if (!string.IsNullOrWhiteSpace(req.EnvironmentId))
            {
                activeEnvironment = environments.Environments.FirstOrDefault(e => e.Id == req.EnvironmentId);
                if (activeEnvironment is null)
                    return ApiErrors.NotFound("Environment not found");
            }

            // The global layer sits underneath the collection-scoped one, so a value
            // shared by a family of environments is defined once instead of copied
            // into each of them.
            ApiEnvironment? globalEnvironment = null;
            if (!string.IsNullOrWhiteSpace(req.GlobalEnvironmentId))
            {
                globalEnvironment = environments.Environments.FirstOrDefault(e => e.Id == req.GlobalEnvironmentId);
                if (globalEnvironment is null)
                    return ApiErrors.NotFound("Global environment not found");
            }

            // No catch here on purpose: the global exception handler in Program.cs logs the failure
            // and maps it (an InvalidOperationException from request building stays a 400, an
            // UnauthorizedAccessException stays a 401), whereas catching it here collapsed
            // everything into a 500 that echoed a raw, unlogged ex.Message back to the client.
            var result = await executor.ExecuteAsync(req.Request, collection, activeEnvironment, globalEnvironment, ct);
            return Results.Ok(Map(result));
        });

        app.MapPost("/api/api-client/preview-keyvault-secret", (
            PreviewKeyVaultSecretRequest req,
            IKeyVaultSecretResolver resolver,
            CancellationToken cancellationToken) => PreviewKeyVaultSecretAsync(req, resolver, cancellationToken));

        // Environment-variable "Secret Store" values live in the OS credential store
        // (ICredentialStore) — the same store VariableSubstitutionService resolves
        // WindowsCredentialStore variables from at send time. These endpoints are the only
        // in-app write path; without them the variable editor could name a key but never
        // put a value behind it, so {{var}} resolved to null and went out literally.
        app.MapPost("/api/api-client/credentials", (SaveCredentialRequest req, ICredentialStore store) =>
            SaveCredential(req, store));

        app.MapDelete("/api/api-client/credentials/{key}", (string key, ICredentialStore store) =>
            DeleteCredential(key, store));

        // Query-param variant: a route segment cannot carry keys containing '/'.
        app.MapDelete("/api/api-client/credentials", (string? key, ICredentialStore store) =>
            DeleteCredential(key ?? string.Empty, store));

        app.MapPost("/api/api-client/preview-credential", (PreviewCredentialRequest req, ICredentialStore store) =>
            PreviewCredential(req, store));

        app.MapPost("/api/api-client/evaluate-jsonpath", EvaluateJsonPathAsync);

        // OAuth 2.0 authorization-code + PKCE: the frontend asks for an authorize URL, opens it in
        // the system browser, and the provider redirects back to the loopback callback below — the
        // sidecar *is* a localhost server, so no deep-link/protocol registration is needed.
        app.MapPost("/api/api-client/oauth/authorize", (
            OAuth2PkceFlowService.StartRequest req,
            HttpRequest http,
            OAuth2PkceFlowService flow) =>
        {
            try
            {
                var result = flow.Start(req, $"{http.Scheme}://{http.Host}");
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return ApiErrors.BadRequest(ex.Message);
            }
        });

        app.MapGet("/api/api-client/oauth/callback", async (
            string? code,
            string? state,
            string? error,
            string? error_description,
            OAuth2PkceFlowService flow,
            CancellationToken ct) =>
        {
            var result = await flow.HandleCallbackAsync(code, state, error, error_description, ct);
            // The browser stays on this page after the redirect — a readable HTML close-me page
            // instead of raw JSON so the user isn't staring at a protocol blob.
            var ok = result.Status == "done";
            var html = $$"""
                <!doctype html><html><head><title>SwebKit sign-in</title>
                <style>body{font-family:system-ui;display:grid;place-items:center;height:100vh;margin:0;background:#111;color:#eee}</style>
                </head><body><div>
                <h2>{{(ok ? "Signed in" : "Sign-in failed")}}</h2>
                <p>{{(ok ? "You can close this tab and return to SwebKit." : result.Error)}}</p>
                </div></body></html>
                """;
            return Results.Content(html, "text/html");
        });

        app.MapGet("/api/api-client/oauth/result/{transactionId}", (
            string transactionId,
            OAuth2PkceFlowService flow) => Results.Ok(flow.GetResult(transactionId)));
    }

    internal static IResult EvaluateJsonPathAsync(EvaluateJsonPathRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.JsonPath))
            return ApiErrors.BadRequest("JSONPath is required.");

        if (req.Body?.Length > MaxJsonPathBodyLength)
            return ApiErrors.BadRequest("Body exceeds the 8 MB limit.");

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(req.Body ?? "{}");
        }
        catch (Exception ex)
        {
            return Results.Ok(new JsonPathEvaluationResponse { Value = null, Error = $"Invalid JSON: {ex.Message}" });
        }

        if (node is null)
            return Results.Ok(new JsonPathEvaluationResponse { Value = null, Error = "Invalid JSON." });

        if (!JsonPath.TryParse(req.JsonPath, out var path))
            return Results.Ok(new JsonPathEvaluationResponse { Value = null, Error = "Invalid JSONPath expression." });

        var results = path.Evaluate(node);
        var first = results.Matches?.FirstOrDefault();
        if (first?.Value is null)
            return Results.Ok(new JsonPathEvaluationResponse { Value = null, Error = null });

        var value = first.Value switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v => v.ToJsonString(),
            _ => first.Value.ToJsonString(),
        };

        return Results.Ok(new JsonPathEvaluationResponse { Value = value, Error = null });
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
            return ApiErrors.BadRequest("Secret name is required");

        if (!resolver.IsAvailable)
            return ApiErrors.Status(StatusCodes.Status500InternalServerError, "No key vaults are configured");

        var raw = await resolver.GetSecretAsync(req.SecretName, req.KeyVaultName, cancellationToken).ConfigureAwait(false);

        if (raw is null)
        {
            return Results.Ok(new KeyVaultPreviewResponse("error", null,
                $"Secret '{req.SecretName}' was not found or the vault could not be reached."));
        }

        return Results.Ok(new KeyVaultPreviewResponse("ok", MaskSecret(raw), null));
    }

    /// <summary>Named for unit testing — writes a secret into the OS credential store under the
    /// given key, the same store <see cref="VariableSubstitutionService"/> resolves
    /// WindowsCredentialStore environment variables from at send time.</summary>
    internal static IResult SaveCredential(SaveCredentialRequest req, ICredentialStore store)
    {
        if (string.IsNullOrWhiteSpace(req.Key))
            return ApiErrors.BadRequest("Credential key is required.");
        store.Save(req.Key, req.Secret ?? string.Empty);
        return Results.Ok(new { saved = true });
    }

    internal static IResult DeleteCredential(string key, ICredentialStore store)
    {
        if (string.IsNullOrWhiteSpace(key))
            return ApiErrors.BadRequest("Credential key is required.");
        store.Delete(key);
        return Results.Ok(new { deleted = true });
    }

    /// <summary>Existence check + masked preview. Never returns the raw secret.</summary>
    internal static IResult PreviewCredential(PreviewCredentialRequest req, ICredentialStore store)
    {
        if (string.IsNullOrWhiteSpace(req.Key))
            return ApiErrors.BadRequest("Credential key is required.");
        var value = store.Get(req.Key);
        return Results.Ok(value is null
            ? new KeyVaultPreviewResponse("error", null, "No credential found under that key.")
            : new KeyVaultPreviewResponse("ok", MaskSecret(value), null));
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
            result.GraphQlErrors,
            result.SentHeaders.Select(h => new ResponseHeaderDto(h.Name, h.Value)).ToList(),
            result.SentBody);
}

public sealed class ExecuteRequestRequest
{
    public HttpRequestEntry Request { get; set; } = new();
    public string? CollectionId { get; set; }

    /// <summary>The collection-scoped environment layer. Overrides <see cref="GlobalEnvironmentId"/>.</summary>
    public string? EnvironmentId { get; set; }

    /// <summary>The global environment layer, applied underneath <see cref="EnvironmentId"/>.</summary>
    public string? GlobalEnvironmentId { get; set; }
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
    IReadOnlyList<GraphQlError>? GraphQlErrors,
    IReadOnlyList<ResponseHeaderDto> SentHeaders,
    string? SentBody);

public sealed record ResponseHeaderDto(string Name, string Value);

public sealed record PreviewKeyVaultSecretRequest(string? KeyVaultName, string SecretName);

public sealed record SaveCredentialRequest(string Key, string? Secret);

public sealed record PreviewCredentialRequest(string Key);

public sealed record KeyVaultPreviewResponse(
    string Status,
    string? MaskedValue,
    string? Error);

public sealed class EvaluateJsonPathRequest
{
    public string? Body { get; set; }
    public string? JsonPath { get; set; }
}

public sealed class JsonPathEvaluationResponse
{
    public string? Value { get; set; }
    public string? Error { get; set; }
}
