using SwebKit.Sidecar.Services;
using System.Text.Json;
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

        // Request runs (api-client-request-runs): dependency chains + batch runs streamed as SSE.
        app.MapPost("/api/api-client/run", (
            ApiRunRequest req,
            HttpContext httpContext,
            ApiClientRunService runs,
            CollectionRepository collections,
            EnvironmentRepository environments,
            LinkedCollectionRootRepository linkedRoots,
            LinkedCollectionFileService linkedFiles,
            DemoModeService demo) =>
            RunRequestsAsync(req, httpContext, runs, collections, environments, linkedRoots, linkedFiles, demo));

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

        // Paste-a-cURL import: parses only, the client inserts the returned request into the
        // collection tree itself via the normal collections store — no direct persistence here.
        app.MapPost("/api/api-client/import-curl", ImportCurl);

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

    /// <summary>Named for unit testing — the endpoint is a thin adapter over
    /// <see cref="ApiClientWorkflowService.ImportCurl"/>, which is covered in Core tests.</summary>
    internal static IResult ImportCurl(ImportCurlRequest req, ApiClientWorkflowService workflow)
    {
        var result = workflow.ImportCurl(req.Command ?? string.Empty);
        return result.IsSuccess
            ? Results.Ok(new ImportCurlResponse(result.Requests, result.Warnings))
            : ApiErrors.BadRequest(result.ErrorMessage ?? "Could not parse the cURL command.");
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

    /// <summary>
    /// Wire options for run SSE frames and plan-error payloads: camelCase with nulls dropped —
    /// <c>stepFailed</c>'s optional <c>status</c>/<c>response</c> and the plan error's optional
    /// <c>requestId</c>/<c>cycle</c>/<c>max</c> must not serialize as <c>null</c> noise.
    /// </summary>
    private static readonly JsonSerializerOptions RunEventJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>
    /// <c>POST /api/api-client/run</c> — streams <see cref="ApiRunEvent"/>s as
    /// <c>data: {json}\n\n</c> frames (the same write pattern as the agent chat stream). Every
    /// pre-flight failure — resolution, environments, <see cref="ApiClientRunService.BuildPlan"/>
    /// — answers as a JSON error before the first SSE byte, so a client never has to re-classify
    /// a mid-stream error frame. Plan errors pass their structured payload through untouched
    /// (<c>{"error":"dependency_cycle","cycle":[...]}</c> etc.) rather than being flattened into
    /// the plain <c>{error: message}</c> envelope.
    /// </summary>
    internal static async Task RunRequestsAsync(
        ApiRunRequest req,
        HttpContext httpContext,
        ApiClientRunService runs,
        CollectionRepository collections,
        EnvironmentRepository environments,
        LinkedCollectionRootRepository linkedRoots,
        LinkedCollectionFileService linkedFiles,
        DemoModeService demo)
    {
        var ct = httpContext.RequestAborted;

        var resolved = await ResolveRunCollectionAsync(req, collections, environments, linkedRoots, linkedFiles, demo, ct).ConfigureAwait(false);
        if (resolved.Collection is null)
        {
            await WriteRunJsonAsync(httpContext, resolved.ErrorStatus, resolved.ErrorPayload!).ConfigureAwait(false);
            return;
        }
        var collection = resolved.Collection;

        // Same environment lookups as /execute — for a linked-root collection the source list is
        // the root's on-disk environments, otherwise the internal repository's.
        ApiEnvironment? activeEnvironment = null;
        if (!string.IsNullOrWhiteSpace(req.ActiveEnvironmentId))
        {
            activeEnvironment = resolved.Environments.FirstOrDefault(e => e.Id == req.ActiveEnvironmentId);
            if (activeEnvironment is null)
            {
                await WriteRunJsonAsync(httpContext, StatusCodes.Status404NotFound, new { error = "Environment not found" }).ConfigureAwait(false);
                return;
            }
        }

        ApiEnvironment? globalEnvironment = null;
        if (!string.IsNullOrWhiteSpace(req.GlobalEnvironmentId))
        {
            globalEnvironment = resolved.Environments.FirstOrDefault(e => e.Id == req.GlobalEnvironmentId);
            if (globalEnvironment is null)
            {
                await WriteRunJsonAsync(httpContext, StatusCodes.Status404NotFound, new { error = "Global environment not found" }).ConfigureAwait(false);
                return;
            }
        }

        var planResult = runs.BuildPlan(collection, req);
        if (planResult.Plan is null)
        {
            // The run service already shaped the failure for the wire ({error:"dependency_cycle",
            // cycle:[...]}, missing dep, too_many_steps, empty_plan) — pass it through untouched
            // instead of flattening it into the plain {error: message} envelope.
            var payload = (object?)planResult.Error ?? new { error = "empty_plan" };
            await WriteRunJsonAsync(httpContext, StatusCodes.Status400BadRequest, payload).ConfigureAwait(false);
            return;
        }
        var plan = planResult.Plan;

        httpContext.Response.ContentType = "text/event-stream; charset=utf-8";
        httpContext.Response.Headers.CacheControl = "no-cache";
        // Disables response buffering on proxies that respect it (same header the agent chat
        // stream sends); a no-op on the direct localhost connection this app runs over.
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var evt in runs.RunAsync(plan, collection, activeEnvironment, globalEnvironment, req, ct).ConfigureAwait(false))
            {
                // Runtime-type serialization — ApiRunEvent is a flat record whose response member
                // already carries the /execute wire shape (ApiRunStepResponse).
                var json = JsonSerializer.Serialize(evt, evt.GetType(), RunEventJsonOptions);
                // Deliberately not the request token: the terminal 'aborted'/'done' frame is
                // emitted *because* cancellation fired — passing the token here would eat exactly
                // the event that explains how the run ended.
                await httpContext.Response.WriteAsync($"data: {json}\n\n").ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected mid-run — nobody left to answer; the stream just ends.
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Dead socket — same end state as a disconnect.
        }
    }

    /// <summary>The collection a run targets plus the environment list its ids resolve against.</summary>
    private sealed record RunCollectionResolution(
        ApiCollection? Collection,
        IReadOnlyList<ApiEnvironment> Environments,
        int ErrorStatus,
        object? ErrorPayload)
    {
        public static RunCollectionResolution Found(ApiCollection collection, IReadOnlyList<ApiEnvironment> environments) =>
            new(collection, environments, 0, null);

        public static RunCollectionResolution Fail(int status, string error) =>
            new(null, [], status, new { error });
    }

    /// <summary>
    /// Mirrors <see cref="ResolveCollectionAsync"/> — internal repository first, the demo
    /// collection when demo mode is on — plus the linked-root branch keyed by
    /// <see cref="ApiRunRequest.LinkedRootId"/>. Linked roots stay disabled in demo mode, matching
    /// the rest of the linked-roots surface.
    /// </summary>
    private static async Task<RunCollectionResolution> ResolveRunCollectionAsync(
        ApiRunRequest req,
        CollectionRepository collections,
        EnvironmentRepository environments,
        LinkedCollectionRootRepository linkedRoots,
        LinkedCollectionFileService linkedFiles,
        DemoModeService demo,
        CancellationToken ct)
    {
        // A run plans against a persisted tree — collectionId is required (unlike /execute,
        // which can run a transient request against an empty collection shell).
        if (string.IsNullOrWhiteSpace(req.CollectionId))
            return RunCollectionResolution.Fail(StatusCodes.Status400BadRequest, "collectionId is required.");

        if (!string.IsNullOrWhiteSpace(req.LinkedRootId))
        {
            if (demo.IsDemoMode)
                return RunCollectionResolution.Fail(StatusCodes.Status400BadRequest, "Linked roots are disabled in demo mode.");

            var root = linkedRoots.Roots.FirstOrDefault(r => r.Id == req.LinkedRootId);
            if (root is null)
                return RunCollectionResolution.Fail(StatusCodes.Status404NotFound, "Linked root not found.");

            if (!root.IsEnabled)
                return RunCollectionResolution.Fail(StatusCodes.Status400BadRequest, "Linked root is disabled. Enable it before running requests from it.");

            LinkedCollectionRootLoadResult loaded;
            try
            {
                loaded = await linkedFiles.LoadRootAsync(root, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return RunCollectionResolution.Fail(StatusCodes.Status400BadRequest, $"Linked root could not be loaded: {ex.Message}");
            }

            var linkedCollection = loaded.Collections.FirstOrDefault(c => c.Id == req.CollectionId);
            return linkedCollection is null
                ? RunCollectionResolution.Fail(StatusCodes.Status404NotFound, $"Collection '{req.CollectionId}' not found in the linked root.")
                : RunCollectionResolution.Found(linkedCollection, loaded.Environments);
        }

        var resolved = await ResolveCollectionAsync(req.CollectionId, collections, demo).ConfigureAwait(false);
        return resolved is null
            ? RunCollectionResolution.Fail(StatusCodes.Status404NotFound, "Collection not found")
            : RunCollectionResolution.Found(resolved, environments.Environments);
    }

    /// <summary>Writes a pre-stream error body — JSON, not SSE, since the stream never opened.</summary>
    private static Task WriteRunJsonAsync(HttpContext context, int statusCode, object payload)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(payload, RunEventJsonOptions, "application/json; charset=utf-8", CancellationToken.None);
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

public sealed record ImportCurlRequest(string? Command);

/// <summary>One parsed request per pasted <c>curl</c> invocation, plus non-fatal parser notes.</summary>
public sealed record ImportCurlResponse(IReadOnlyList<HttpRequestEntry> Requests, IReadOnlyList<string> Warnings);

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
