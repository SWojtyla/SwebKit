using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Agents.Tools.ApiClient;

/// <summary>
/// Applies confirmed API Client actions (create/update/duplicate/move/delete a request, execute an
/// HTTP request). The <see cref="IAgentActionExecutor"/> implementation for the API Client area —
/// see <c>AgentActionApplier</c> for how executors are dispatched by <c>AgentActionType</c>.
/// </summary>
public sealed class ApiClientActionExecutor : IAgentActionExecutor
{
    private readonly IApiClientAgentService _apiClient;
    private readonly ICredentialStore _credentials;

    public ApiClientActionExecutor(IApiClientAgentService apiClient, ICredentialStore? credentials = null)
    {
        _apiClient = apiClient;
        _credentials = credentials ?? new NullCredentialStore();
    }

    public bool CanHandle(AgentActionType type) => type is
        AgentActionType.CreateRequest or
        AgentActionType.UpdateRequest or
        AgentActionType.DeleteRequest or
        AgentActionType.DuplicateRequest or
        AgentActionType.MoveRequest or
        AgentActionType.RenameFolder or
        AgentActionType.DeleteFolder or
        AgentActionType.SetCollectionVariable or
        AgentActionType.ExecuteHttpRequest;

    public Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct) => action.Type switch
    {
        AgentActionType.CreateRequest => ApplyCreateAsync(action, ct),
        AgentActionType.UpdateRequest => ApplyUpdateAsync(action, ct),
        AgentActionType.DeleteRequest => ApplyDeleteAsync(action, ct),
        AgentActionType.DuplicateRequest => ApplyDuplicateAsync(action, ct),
        AgentActionType.MoveRequest => ApplyMoveAsync(action, ct),
        AgentActionType.SetCollectionVariable => ApplySetCollectionVariableAsync(action, ct),
        AgentActionType.ExecuteHttpRequest => ApplyExecuteHttpAsync(action, ct),
        // No tool proposes RenameFolder/DeleteFolder yet (ApiClientTools.cs has no folder-rename/
        // delete proposal tool), so these are unreachable today — handled explicitly rather than
        // silently falling through, so a future tool that *does* propose one gets a clear signal
        // this executor needs a branch added, not a confusing generic failure.
        _ => Task.FromResult(Fail($"'{action.Type}' is not yet implemented in {nameof(ApiClientActionExecutor)}.")),
    };

    private async Task<AgentActionResult> ApplyCreateAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload for create.");

        var collectionId = GetString(payload, "collection_id");
        var name = GetString(payload, "name");
        if (string.IsNullOrEmpty(collectionId) || string.IsNullOrEmpty(name))
            return Fail("Missing 'collection_id' or 'name' in the proposed action's payload.");

        var method = TryGetMethod(payload) ?? ApiRequestMethod.Get;
        var url = GetString(payload, "url") ?? "";
        var folderPath = GetString(payload, "folder_path");
        var details = ParseDetails(payload);

        var result = await _apiClient.CreateRequestAsync(collectionId, folderPath, name, method, url, details, ct);
        return ToResult(result, result.IsSuccess ? $"Created request '{name}'" : null);
    }

    private async Task<AgentActionResult> ApplyUpdateAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload for update.");

        var requestId = GetString(payload, "request_id");
        if (string.IsNullOrEmpty(requestId))
            return Fail("Missing 'request_id' in the proposed action's payload.");

        var result = await _apiClient.UpdateRequestAsync(
            requestId,
            name: GetString(payload, "name"),
            method: TryGetMethod(payload),
            url: GetString(payload, "url"),
            details: ParseDetails(payload),
            ct: ct);
        return ToResult(result, result.IsSuccess ? "Request updated" : null);
    }

    private async Task<AgentActionResult> ApplyDeleteAsync(PendingAgentAction action, CancellationToken ct)
    {
        var requestId = ExtractRequestIdFromTarget(action.Target);
        var result = await _apiClient.DeleteRequestAsync(requestId, ct);
        return ToResult(result, result.IsSuccess ? $"Deleted request '{requestId}'" : null);
    }

    private async Task<AgentActionResult> ApplyDuplicateAsync(PendingAgentAction action, CancellationToken ct)
    {
        var requestId = ExtractRequestIdFromTarget(action.Target);
        var result = await _apiClient.DuplicateRequestAsync(requestId, ct);
        return ToResult(result, result.IsSuccess ? "Request duplicated" : null);
    }

    private async Task<AgentActionResult> ApplyMoveAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload for move.");

        var requestId = GetString(payload, "request_id") ?? ExtractRequestIdFromTarget(action.Target);
        var folderPath = GetString(payload, "folder_path");
        var newIndex = payload.TryGetProperty("new_index", out var ni) && ni.ValueKind == System.Text.Json.JsonValueKind.Number
            ? ni.GetInt32()
            : (int?)null;

        var result = await _apiClient.MoveRequestAsync(requestId, folderPath, newIndex, ct);
        return ToResult(result, result.IsSuccess ? "Request moved" : null);
    }

    private async Task<AgentActionResult> ApplySetCollectionVariableAsync(PendingAgentAction action, CancellationToken ct)
    {
        if (action.Payload is not { } payload)
            return Fail("Missing structured payload for set_collection_variable.");

        var collectionId = GetString(payload, "collection_id");
        var key = GetString(payload, "key");
        if (string.IsNullOrEmpty(collectionId) || string.IsNullOrEmpty(key))
            return Fail("Missing 'collection_id' or 'key' in the proposed action's payload.");

        VariableGeneratorKind? generator = null;
        if (payload.TryGetProperty("generator", out var g) && g.ValueKind == System.Text.Json.JsonValueKind.String
            && Enum.TryParse<VariableGeneratorKind>(g.GetString(), ignoreCase: true, out var kind))
            generator = kind;

        var enabled = !payload.TryGetProperty("enabled", out var en)
            || en.ValueKind != System.Text.Json.JsonValueKind.False;

        var result = await _apiClient.SetCollectionVariableAsync(
            collectionId, key, GetString(payload, "value"), generator, enabled, ct);
        return ToResult(result, result.IsSuccess ? $"Variable '{key}' set" : null);
    }

    private async Task<AgentActionResult> ApplyExecuteHttpAsync(PendingAgentAction action, CancellationToken ct)
    {
        var requestId = ExtractRequestIdFromTarget(action.Target);
        var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
        if (snapshot is null)
            return Fail("Request not found.");

        if (action.ExpectedFingerprint is not null)
        {
            var currentFingerprint = snapshot.UpdatedAt.ToString("O");
            if (currentFingerprint != action.ExpectedFingerprint)
                return Fail("Request has changed since the preview was generated. Please regenerate the proposal.");
        }

        // Deliberately still not implemented: IApiClientAgentService only exposes a masked
        // ApiRequestSnapshot, not the full HttpRequestEntry/ApiCollection/active-environment
        // IHttpRequestExecutor.ExecuteAsync needs. Doing this properly means either adding a new
        // method to IApiClientAgentService to resolve those, or resolving them directly from
        // CollectionRepository/EnvironmentRepository — a bigger, security-sensitive addition
        // (real outbound HTTP against a possibly-external server) that deserves its own careful
        // pass rather than being rushed alongside the rest of this module. See
        // docs/features/active/ai-augmented-app/technical-plan.md Module 3.
        return Fail(
            "HTTP execution requires the full request entry and active environment, which " +
            "IApiClientAgentService doesn't expose yet — not implemented in this pass.");
    }

    /// <summary>
    /// Parses the optional detail fields out of a proposed-action payload. Presence of a property
    /// is what matters — an explicitly empty array means "clear the list". A plaintext
    /// <c>credential_secret</c> is moved into the OS credential store under a fresh
    /// <c>sw-secret:</c> key here, so the persisted auth config only ever references the key.
    /// </summary>
    private ApiRequestDetails ParseDetails(System.Text.Json.JsonElement payload)
    {
        var details = new ApiRequestDetails
        {
            Headers = ParsePairs(payload, "headers"),
            QueryParams = ParsePairs(payload, "query_params"),
            Body = ParseBody(payload),
            Auth = ParseAuth(payload),
            CaptureRules = ParseCaptureRules(payload),
            GraphQlQuery = GetString(payload, "graphql_query"),
            GraphQlVariables = GetString(payload, "graphql_variables"),
            GraphQlSelectedOperation = GetString(payload, "graphql_operation"),
            WsSubProtocol = GetString(payload, "ws_sub_protocol"),
        };
        return details;
    }

    private static List<KeyValuePair<string>>? ParsePairs(System.Text.Json.JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Array)
            return null;

        return el.EnumerateArray()
            .Where(i => i.ValueKind == System.Text.Json.JsonValueKind.Object)
            .Select(i => new KeyValuePair<string>
            {
                Key = GetString(i, "key") ?? "",
                Value = GetString(i, "value"),
                IsEnabled = !i.TryGetProperty("enabled", out var en) || en.ValueKind != System.Text.Json.JsonValueKind.False,
            })
            .Where(p => p.Key.Length > 0)
            .ToList();
    }

    /// <summary><c>form_data</c> rows carry an optional <c>type: "file"</c> — a file row's
    /// <c>value</c> is a local path sent as a real multipart file part (may carry {{vars}}).</summary>
    private static List<FormDataField>? ParseFormData(System.Text.Json.JsonElement payload)
    {
        if (!payload.TryGetProperty("form_data", out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Array)
            return null;

        return el.EnumerateArray()
            .Where(i => i.ValueKind == System.Text.Json.JsonValueKind.Object)
            .Select(i => new FormDataField
            {
                Key = GetString(i, "key") ?? "",
                Value = GetString(i, "value"),
                IsEnabled = !i.TryGetProperty("enabled", out var en) || en.ValueKind != System.Text.Json.JsonValueKind.False,
                IsFile = string.Equals(GetString(i, "type"), "file", StringComparison.OrdinalIgnoreCase),
            })
            .Where(p => p.Key.Length > 0)
            .ToList();
    }

    private static RequestBody? ParseBody(System.Text.Json.JsonElement payload)
    {
        RequestBodyMode mode = RequestBodyMode.None;
        var hasMode = payload.TryGetProperty("body_mode", out var modeEl)
            && Enum.TryParse<RequestBodyMode>(modeEl.GetString(), ignoreCase: true, out mode);
        var raw = GetString(payload, "body");
        var contentType = GetString(payload, "body_content_type");
        var formData = ParseFormData(payload);
        var filePath = GetString(payload, "file_path");

        if (!hasMode && raw is null && contentType is null && formData is null && filePath is null)
            return null;

        return new RequestBody
        {
            Mode = mode,
            RawContent = raw,
            ContentType = contentType ?? DefaultContentType(mode),
            FormData = formData ?? [],
            FilePath = filePath,
        };
    }

    private static string? DefaultContentType(RequestBodyMode mode) => mode switch
    {
        RequestBodyMode.Json => "application/json",
        RequestBodyMode.Xml => "application/xml",
        RequestBodyMode.Text => "text/plain",
        _ => null,
    };

    private AuthConfig? ParseAuth(System.Text.Json.JsonElement payload)
    {
        if (!payload.TryGetProperty("auth", out var auth) || auth.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;
        if (!Enum.TryParse<AuthType>(GetString(auth, "type"), ignoreCase: true, out var type))
            return null;

        var credentialKey = GetString(auth, "credential_key");
        // The model may carry a plaintext secret it got from the user's chat — park it in the OS
        // credential store under a generated key exactly like the request editor does, so the
        // collection file only ever holds the reference.
        var inlineSecret = GetString(auth, "credential_secret");
        if (!string.IsNullOrEmpty(inlineSecret))
        {
            credentialKey = $"sw-secret:{Guid.NewGuid():N}";
            _credentials.Save(credentialKey, inlineSecret);
        }

        return new AuthConfig
        {
            Type = type,
            CredentialKey = credentialKey,
            ApiKeyParamName = GetString(auth, "api_key_param_name"),
            ApiKeyLocation = Enum.TryParse<ApiKeyLocation>(GetString(auth, "api_key_location"), ignoreCase: true, out var loc)
                ? loc
                : ApiKeyLocation.Header,
            BasicUsername = GetString(auth, "basic_username"),
            OAuth2ClientId = GetString(auth, "oauth2_client_id"),
            OAuth2GrantType = Enum.TryParse<OAuth2GrantType>(GetString(auth, "oauth2_grant_type"), ignoreCase: true, out var grant)
                ? grant
                : OAuth2GrantType.ClientCredentials,
            OAuth2TokenUrl = GetString(auth, "oauth2_token_url"),
            OAuth2AuthUrl = GetString(auth, "oauth2_auth_url"),
            OAuth2Scopes = GetString(auth, "oauth2_scopes"),
        };
    }

    private static List<CaptureRule>? ParseCaptureRules(System.Text.Json.JsonElement payload)
    {
        if (!payload.TryGetProperty("capture_rules", out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Array)
            return null;

        return el.EnumerateArray()
            .Where(i => i.ValueKind == System.Text.Json.JsonValueKind.Object)
            .Select(i => new CaptureRule
            {
                Id = Guid.NewGuid().ToString("N"),
                TargetVariable = GetString(i, "target_variable") ?? "",
                TargetScope = GetString(i, "target_scope") ?? "collection",
                Source = Enum.TryParse<CaptureSource>(GetString(i, "source"), ignoreCase: true, out var src)
                    ? src
                    : CaptureSource.BodyJsonPath,
                JsonPath = GetString(i, "json_path"),
                HeaderName = GetString(i, "header_name"),
                IsEnabled = !i.TryGetProperty("enabled", out var en) || en.ValueKind != System.Text.Json.JsonValueKind.False,
            })
            .Where(r => r.TargetVariable.Length > 0)
            .ToList();
    }

    private static string? GetString(System.Text.Json.JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    private static ApiRequestMethod? TryGetMethod(System.Text.Json.JsonElement payload) =>
        payload.TryGetProperty("method", out var m) && Enum.TryParse<ApiRequestMethod>(m.GetString(), out var parsed)
            ? parsed
            : null;

    private static string ExtractRequestIdFromTarget(string target)
    {
        // Target format: "Request 'Name' (id)"
        var start = target.LastIndexOf('(');
        var end = target.LastIndexOf(')');
        if (start > 0 && end > start)
            return target.Substring(start + 1, end - start - 1);
        return target;
    }

    private static AgentActionResult Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };

    private static AgentActionResult ToResult(ApiClientMutationResult result, string? successSummary) => new()
    {
        IsSuccess = result.IsSuccess,
        ErrorMessage = result.ErrorMessage,
        ResultSummary = result.IsSuccess ? successSummary : null,
    };

    /// <summary>Used when no <see cref="ICredentialStore"/> is injected (unit tests of the
    /// non-auth paths). Silently drops saves — an auth-bearing proposal applied against this
    /// store keeps the generated key but resolves to no secret.</summary>
    private sealed class NullCredentialStore : ICredentialStore
    {
        public void Save(string key, string secret) { }
        public string? Get(string key) => null;
        public void Delete(string key) { }
        public IReadOnlyList<string> ListKeys(string prefix = "") => [];
    }
}
