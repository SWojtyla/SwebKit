using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Agents.Tools;

namespace SwebKit.Agents.Tools.ApiClient;

/// <summary>
/// Searches and lists API requests across all collections.
/// </summary>
public sealed class SearchApiRequestsTool : IAgentTool
{
    private readonly IApiClientAgentService _apiClient;

    public SearchApiRequestsTool(IApiClientAgentService apiClient) => _apiClient = apiClient;

    public string Name => "search_api_requests";
    public string Description => "Search and list API requests across all collections. Returns request IDs, names, methods, URLs, and collection origin.";

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
    {
        "type": "object",
        "properties": {
            "query": {
                "type": "string",
                "description": "Optional search query to filter by name, URL, or method."
            }
        },
        "additionalProperties": false
    }
    """);

    public FeatureArea FeatureArea => FeatureArea.ApiClient;

    public JsonElement ParametersSchema => Schema;

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var query = arguments.TryGetProperty("query", out var q) ? q.GetString() : null;
        var results = await _apiClient.SearchRequestsAsync(query, ct);

        if (results.Count == 0)
            return """{"count":0,"requests":[],"message":"No requests found."}""";

        var requests = results.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            method = r.Method.ToString(),
            url = r.Url,
            collection = r.CollectionName,
            collection_id = r.CollectionId,
            origin = r.CollectionOrigin,
            folder = r.FolderPath,
        });

        return JsonSerializer.Serialize(new { count = results.Count, requests });
    }
}

/// <summary>
/// Lists every collection with IDs and folder structure — the discovery call a model needs
/// before proposing a create, since <c>collection_id</c> targets by ID or name.
/// </summary>
public sealed class ListApiCollectionsTool : IAgentTool
{
    private readonly IApiClientAgentService _apiClient;

    public ListApiCollectionsTool(IApiClientAgentService apiClient) => _apiClient = apiClient;

    public string Name => "list_api_collections";
    public string Description => "List all API Client collections with their IDs, folder paths, and request counts. Call this before proposing a request create so the target collection and folder actually exist in the output.";

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
    {
        "type": "object",
        "properties": {},
        "additionalProperties": false
    }
    """);

    public FeatureArea FeatureArea => FeatureArea.ApiClient;

    public JsonElement ParametersSchema => Schema;

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var collections = await _apiClient.GetCollectionsAsync(ct);

        if (collections.Count == 0)
            return """{"count":0,"collections":[],"message":"No collections exist yet. A create proposal may name one — it is created on confirm."}""";

        var result = collections.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            origin = c.Origin,
            folders = c.FolderPaths,
            request_count = c.RequestCount,
        });

        return JsonSerializer.Serialize(new { count = collections.Count, collections = result });
    }
}

/// <summary>
/// Reads a single API request by ID with secrets masked.
/// </summary>
public sealed class GetApiRequestTool : IAgentTool
{
    private readonly IApiClientAgentService _apiClient;

    public GetApiRequestTool(IApiClientAgentService apiClient) => _apiClient = apiClient;

    public string Name => "get_api_request";
    public string Description => "Read a single API request by ID. Returns full request details with secrets masked.";

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
    {
        "type": "object",
        "properties": {
            "request_id": {
                "type": "string",
                "description": "The ID of the request to read."
            }
        },
        "required": ["request_id"],
        "additionalProperties": false
    }
    """);

    public FeatureArea FeatureArea => FeatureArea.ApiClient;

    public JsonElement ParametersSchema => Schema;

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("request_id", out var idProp))
            return """{"error":"Missing required parameter 'request_id'."}""";

        var requestId = idProp.GetString();
        if (string.IsNullOrEmpty(requestId))
            return """{"error":"Parameter 'request_id' must be a non-empty string."}""";

        var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
        if (snapshot is null)
            return $$"""{"error":"Request '{{requestId}}' not found."}""";

        var result = new
        {
            id = snapshot.Id,
            name = snapshot.Name,
            method = snapshot.Method.ToString(),
            url = snapshot.Url,
            collection = snapshot.CollectionName,
            origin = snapshot.CollectionOrigin,
            folder = snapshot.FolderPath,
            headers = snapshot.Headers.Select(h => new { key = h.Key, value = h.Value }),
            query_params = snapshot.QueryParams.Select(q => new { key = q.Key, value = q.Value }),
            body_content_type = snapshot.BodyContentType,
            body_preview = snapshot.BodyPreview,
            auth_type = snapshot.AuthType,
            capture_rules = snapshot.CaptureRules.Select(r => new
            {
                target_variable = r.TargetVariable,
                target_scope = r.TargetScope,
                source = r.Source.ToString(),
                json_path = r.JsonPath,
                header_name = r.HeaderName,
                enabled = r.IsEnabled,
            }),
            updated_at = snapshot.UpdatedAt.ToString("yyyy-MM-dd HH:mm UTC"),
        };

        return JsonSerializer.Serialize(result);
    }
}

/// <summary>
/// Proposes a change (create/update/duplicate/move/rename) without applying it.
/// Returns a pending action for user confirmation.
/// </summary>
public sealed class ProposeApiRequestChangeTool : IAgentTool
{
    private readonly IApiClientAgentService _apiClient;
    private readonly IAgentActionCoordinator _coordinator;

    public ProposeApiRequestChangeTool(IApiClientAgentService apiClient, IAgentActionCoordinator coordinator)
    {
        _apiClient = apiClient;
        _coordinator = coordinator;
    }

    public string Name => "propose_api_request_change";
    public string Description =>
        "Propose a change to API Client requests (create, update, duplicate, or move). Returns a pending action for user confirmation — nothing is applied until confirmed. " +
        "Create and update accept the full request surface, not just name+URL: headers and query_params ([{key,value,enabled}]); a body via body_mode + body/body_content_type/form_data/file_path; auth (bearerToken, apiKey, basic, oauth2, inherited, none); capture_rules that extract response values into variables for request chaining; and GraphQL documents via method GraphQl + graphql_query/variables/operation. " +
        "Every string field may contain {{variable}} references, resolved at send time from collection/environment variables — use them for chaining: have one request's capture_rules write {{token}} from the login response body (e.g. source bodyJsonPath, json_path '$.access_token'), then reference {{token}} in the next request's headers or auth. " +
        "Secrets go in auth.credential_secret (stored in the OS credential store on confirm, never in the collection file) or auth.credential_key to reference an existing sw-secret:* key — credential_key also accepts a {{variable}} reference so a captured token can act as the bearer secret. " +
        "For update, any field not supplied is left unchanged; a supplied list replaces the existing one entirely.";
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.Low;

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
    {
        "type": "object",
        "properties": {
            "operation": {
                "type": "string",
                "enum": ["create", "update", "duplicate", "move"],
                "description": "The type of change to propose."
            },
            "request_id": {
                "type": "string",
                "description": "ID of the request (for update, duplicate, move)."
            },
            "collection_id": {
                "type": "string",
                "description": "ID or exact name of the target collection (for create) — call list_api_collections to see what exists. If nothing matches, a new collection with this name is created when the action is confirmed."
            },
            "folder_path": {
                "type": "string",
                "description": "Folder path within the collection (for create, move). Missing segments are created on confirm for 'create'; 'move' requires an existing folder."
            },
            "name": {
                "type": "string",
                "description": "New name for the request (for create, update)."
            },
            "method": {
                "type": "string",
                "enum": ["Get", "Post", "Put", "Patch", "Delete", "Head", "Options", "GraphQl", "WebSocket"],
                "description": "Request method (for create, update). GraphQl and WebSocket use their own payload fields instead of a body."
            },
            "url": {
                "type": "string",
                "description": "Request URL (for create, update). May contain {{variables}}."
            },
            "new_index": {
                "type": "integer",
                "description": "Target position for move (0-based)."
            },
            "headers": {
                "type": "array",
                "description": "HTTP headers (for create, update). Replaces the whole list on update.",
                "items": {
                    "type": "object",
                    "properties": {
                        "key": { "type": "string" },
                        "value": { "type": "string", "description": "May contain {{variables}}." },
                        "enabled": { "type": "boolean" }
                    },
                    "required": ["key"]
                }
            },
            "query_params": {
                "type": "array",
                "description": "Query parameters (for create, update). Replaces the whole list on update.",
                "items": {
                    "type": "object",
                    "properties": {
                        "key": { "type": "string" },
                        "value": { "type": "string", "description": "May contain {{variables}}." },
                        "enabled": { "type": "boolean" }
                    },
                    "required": ["key"]
                }
            },
            "body_mode": {
                "type": "string",
                "enum": ["none", "json", "xml", "text", "formData", "binary"],
                "description": "Body mode (for create, update). Pairs with body / form_data / file_path."
            },
            "body": {
                "type": "string",
                "description": "Raw body content for json/xml/text modes. May contain {{variables}}."
            },
            "body_content_type": {
                "type": "string",
                "description": "Content-Type for the raw body; defaults per body_mode (application/json, application/xml, text/plain)."
            },
            "form_data": {
                "type": "array",
                "description": "Form fields when body_mode is formData. May contain {{variables}} in values.",
                "items": {
                    "type": "object",
                    "properties": {
                        "key": { "type": "string" },
                        "value": { "type": "string" },
                        "enabled": { "type": "boolean" }
                    },
                    "required": ["key"]
                }
            },
            "file_path": {
                "type": "string",
                "description": "Local file path for binary uploads (body_mode = binary)."
            },
            "auth": {
                "type": "object",
                "description": "Auth config (for create, update). Omit to inherit from folder/collection. Never put a plaintext secret in credential_key — use credential_secret.",
                "properties": {
                    "type": {
                        "type": "string",
                        "enum": ["none", "inherited", "bearerToken", "apiKey", "basic", "oauth2"]
                    },
                    "credential_key": {
                        "type": "string",
                        "description": "Existing credential-store key (sw-secret:*) to reuse, or a {{variable}} reference resolving to the secret (e.g. a token captured by another request's capture_rules)."
                    },
                    "credential_secret": {
                        "type": "string",
                        "description": "Plaintext secret (token/password/api key/client secret). Stored in the OS credential store under a generated key on confirm — never written into the collection file."
                    },
                    "api_key_param_name": { "type": "string", "description": "Header or query-param name for apiKey auth." },
                    "api_key_location": { "type": "string", "enum": ["header", "queryParam"] },
                    "basic_username": { "type": "string" },
                    "oauth2_client_id": { "type": "string" },
                    "oauth2_grant_type": { "type": "string", "enum": ["clientCredentials", "authorizationCode"] },
                    "oauth2_token_url": { "type": "string" },
                    "oauth2_auth_url": { "type": "string", "description": "Authorization endpoint (authorizationCode grant only)." },
                    "oauth2_scopes": { "type": "string", "description": "Space-separated scopes." }
                },
                "required": ["type"]
            },
            "capture_rules": {
                "type": "array",
                "description": "Post-response capture rules (for create, update) — the request-chaining mechanism. Each rule extracts a value from the response and stores it in a variable that later requests reference as {{variable}}.",
                "items": {
                    "type": "object",
                    "properties": {
                        "target_variable": { "type": "string", "description": "Variable key the captured value is stored under." },
                        "target_scope": { "type": "string", "description": "'collection' (default) stores it as a collection variable; any other value is treated as an environment name." },
                        "source": { "type": "string", "enum": ["bodyJsonPath", "responseHeader", "statusCode"], "description": "What to extract from. Default bodyJsonPath." },
                        "json_path": { "type": "string", "description": "JSONPath into the response body, e.g. '$.access_token' (source = bodyJsonPath)." },
                        "header_name": { "type": "string", "description": "Response header name (source = responseHeader)." },
                        "enabled": { "type": "boolean" }
                    },
                    "required": ["target_variable"]
                }
            },
            "graphql_query": {
                "type": "string",
                "description": "GraphQL query/mutation document (method = GraphQl)."
            },
            "graphql_variables": {
                "type": "string",
                "description": "GraphQL variables as a JSON string (method = GraphQl)."
            },
            "graphql_operation": {
                "type": "string",
                "description": "Operation name to run when the document defines several (method = GraphQl)."
            },
            "ws_sub_protocol": {
                "type": "string",
                "description": "WebSocket subprotocol sent in the upgrade header (method = WebSocket)."
            }
        },
        "required": ["operation"],
        "additionalProperties": false
    }
    """);

    public FeatureArea FeatureArea => FeatureArea.ApiClient;

    public JsonElement ParametersSchema => Schema;

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("operation", out var opProp))
            return """{"error":"Missing required parameter 'operation'."}""";

        var operation = opProp.GetString();
        if (string.IsNullOrEmpty(operation))
            return """{"error":"Parameter 'operation' must be a non-empty string."}""";

        var actionId = Guid.NewGuid().ToString("N");
        string summary, target, preview;
        AgentActionType actionType;
        AgentActionRisk risk = AgentActionRisk.Low;

        switch (operation.ToLowerInvariant())
        {
            case "create":
            {
                if (!arguments.TryGetProperty("collection_id", out var collId) || collId.GetString() is not { Length: > 0 } collectionRef)
                    return """{"error":"Missing required parameter 'collection_id' for create operation."}""";
                if (!arguments.TryGetProperty("name", out var nameProp) || nameProp.GetString() is not { } name)
                    return """{"error":"Missing required parameter 'name' for create operation."}""";
                var method = arguments.TryGetProperty("method", out var m) && Enum.TryParse<ApiRequestMethod>(m.GetString(), out var parsed) ? parsed : ApiRequestMethod.Get;
                var url = arguments.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var folderPath = arguments.TryGetProperty("folder_path", out var f) ? f.GetString() : null;

                // Resolve the target now so the preview tells the user exactly what confirm will
                // create — the collection itself and/or folder segments that don't exist yet.
                var collections = await _apiClient.GetCollectionsAsync(ct);
                var match = collections.FirstOrDefault(c =>
                    c.Id == collectionRef ||
                    c.Name.Equals(collectionRef, StringComparison.OrdinalIgnoreCase));

                var willCreate = new List<string>();
                if (match is null)
                {
                    willCreate.Add($"collection '{collectionRef}'");
                }
                else if (!string.IsNullOrEmpty(folderPath))
                {
                    var missing = MissingFolderSegments(match.FolderPaths, folderPath);
                    if (missing.Count > 0)
                        willCreate.Add($"folder{(missing.Count > 1 ? "s" : "")} '{string.Join("', '", missing)}'");
                }

                actionType = AgentActionType.CreateRequest;
                target = $"Collection {match?.Name ?? collectionRef}" + (folderPath is not null ? $"/{folderPath}" : "");
                summary = $"Create request '{name}' ({method} {url})";
                preview = $"Name: {name}\nMethod: {method}\nURL: {url}\nLocation: {target}"
                    + DetailLines(arguments)
                    + (willCreate.Count > 0 ? $"\nWill be created: {string.Join("; ", willCreate)}" : "");
                break;
            }

            case "update":
            {
                if (!arguments.TryGetProperty("request_id", out var reqId) || reqId.GetString() is not { } requestId)
                    return """{"error":"Missing required parameter 'request_id' for update operation."}""";

                var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
                if (snapshot is null)
                    return $$"""{"error":"Request '{{requestId}}' not found."}""";

                actionType = AgentActionType.UpdateRequest;
                target = $"Request '{snapshot.Name}' ({snapshot.Id})";
                var changes = new List<string>();
                if (arguments.TryGetProperty("name", out var n) && n.GetString() is { } newName) changes.Add($"name: {snapshot.Name} → {newName}");
                if (arguments.TryGetProperty("method", out var m) && Enum.TryParse<ApiRequestMethod>(m.GetString(), out var newMethod)) changes.Add($"method: {snapshot.Method} → {newMethod}");
                if (arguments.TryGetProperty("url", out var u) && u.GetString() is { } newUrl) changes.Add($"url: {snapshot.Url} → {newUrl}");
                foreach (var (prop, label) in DetailLabels)
                    if (arguments.TryGetProperty(prop, out _))
                        changes.Add($"{label}: (replaced)");

                summary = $"Update request '{snapshot.Name}': {string.Join(", ", changes)}";
                preview = $"Changes:\n{string.Join("\n", changes)}";
                break;
            }

            case "duplicate":
            {
                if (!arguments.TryGetProperty("request_id", out var reqId) || reqId.GetString() is not { } requestId)
                    return """{"error":"Missing required parameter 'request_id' for duplicate operation."}""";

                var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
                if (snapshot is null)
                    return $$"""{"error":"Request '{{requestId}}' not found."}""";

                actionType = AgentActionType.DuplicateRequest;
                target = $"Request '{snapshot.Name}' ({snapshot.Id})";
                summary = $"Duplicate request '{snapshot.Name}' as '{snapshot.Name} (copy)'";
                preview = $"Source: {snapshot.Name} ({snapshot.Method} {snapshot.Url})\nCopy will be: {snapshot.Name} (copy)";
                break;
            }

            case "move":
            {
                if (!arguments.TryGetProperty("request_id", out var reqId) || reqId.GetString() is not { } requestId)
                    return """{"error":"Missing required parameter 'request_id' for move operation."}""";

                var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
                if (snapshot is null)
                    return $$"""{"error":"Request '{{requestId}}' not found."}""";

                var targetFolder = arguments.TryGetProperty("folder_path", out var f) ? f.GetString() : null;
                var newIndex = arguments.TryGetProperty("new_index", out var ni) ? ni.GetInt32() : (int?)null;

                actionType = AgentActionType.MoveRequest;
                target = $"Request '{snapshot.Name}' ({snapshot.Id})";
                summary = $"Move request '{snapshot.Name}' to {(targetFolder ?? "root")}{(newIndex is not null ? $" at index {newIndex}" : "")}";
                preview = $"From: {snapshot.FolderPath ?? "root"}\nTo: {targetFolder ?? "root"}{(newIndex is not null ? $" (index {newIndex})" : "")}";
                break;
            }

            default:
                return $$"""{"error":"Unknown operation '{{operation}}'. Supported: create, update, duplicate, move."}""";
        }

        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = actionType,
            Summary = summary,
            Target = target,
            Risk = risk,
            Preview = preview,
            ExpectedFingerprint = null, // Set at apply time for freshness check
            // The applier (ApiClientActionExecutor) reads exact field values back out of this at
            // apply time rather than re-parsing `preview`'s human-readable diff text.
            Payload = arguments.Clone(),
        };

        _coordinator.RegisterAction(action);

        return JsonSerializer.Serialize(new
        {
            action_id = actionId,
            status = "pending_confirmation",
            summary,
            preview,
            risk = risk.ToString(),
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Action proposed. User must confirm before it is applied.",
        });
    }

    /// <summary>Detail fields the confirmation card summarizes beyond name/method/url —
    /// (payload property, human label) pairs shared by the create and update previews.</summary>
    private static readonly (string Prop, string Label)[] DetailLabels =
    [
        ("headers", "headers"),
        ("query_params", "query params"),
        ("body_mode", "body"),
        ("auth", "auth"),
        ("capture_rules", "capture rules"),
        ("graphql_query", "GraphQL document"),
        ("ws_sub_protocol", "WebSocket subprotocol"),
    ];

    /// <summary>Builds the extra preview lines describing which detail fields a proposal carries,
    /// so the confirm card shows e.g. "capture rules: 2" rather than looking like a bare URL
    /// request. Never echoes secret values — auth is described by type only.</summary>
    private static string DetailLines(JsonElement arguments)
    {
        var lines = new List<string>();
        if (TryGetArray(arguments, "headers", out var headers)) lines.Add($"Headers: {headers.GetArrayLength()}");
        if (TryGetArray(arguments, "query_params", out var qp)) lines.Add($"Query params: {qp.GetArrayLength()}");
        if (arguments.TryGetProperty("body_mode", out var bm) && bm.GetString() is { } bodyMode) lines.Add($"Body: {bodyMode}");
        if (arguments.TryGetProperty("auth", out var auth) && auth.TryGetProperty("type", out var at)) lines.Add($"Auth: {at.GetString()}");
        if (TryGetArray(arguments, "capture_rules", out var captures))
        {
            var targets = captures.EnumerateArray()
                .Select(r => r.TryGetProperty("target_variable", out var tv) ? tv.GetString() : null)
                .Where(t => t is not null);
            lines.Add($"Capture rules: {captures.GetArrayLength()}{(targets.Any() ? $" → {{{{{string.Join("}}, {{", targets)}}}}}" : "")}");
        }
        if (arguments.TryGetProperty("graphql_query", out _)) lines.Add("GraphQL document provided");
        if (arguments.TryGetProperty("ws_sub_protocol", out var ws)) lines.Add($"WebSocket subprotocol: {ws.GetString()}");

        return lines.Count == 0 ? "" : "\n" + string.Join("\n", lines);
    }

    private static bool TryGetArray(JsonElement arguments, string property, out JsonElement array)
    {
        array = default;
        return arguments.TryGetProperty(property, out array) && array.ValueKind == JsonValueKind.Array;
    }

    /// <summary>Path prefixes of <paramref name="folderPath"/> with no existing folder, e.g. a path
    /// "Signing/Onboarding" where only "Signing" exists reports ["Signing/Onboarding"] — the
    /// preview can then name exactly what the confirm will create.</summary>
    private static List<string> MissingFolderSegments(IReadOnlyList<string> existingPaths, string folderPath)
    {
        var missing = new List<string>();
        var parts = folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var prefix = "";
        foreach (var part in parts)
        {
            prefix = prefix.Length == 0 ? part : $"{prefix}/{part}";
            if (!existingPaths.Contains(prefix, StringComparer.Ordinal))
                missing.Add(prefix);
        }
        return missing;
    }
}

/// <summary>
/// Proposes deletion of a request. Separate tool to make destruction explicit.
/// </summary>
public sealed class ProposeApiRequestDeleteTool : IAgentTool
{
    private readonly IApiClientAgentService _apiClient;
    private readonly IAgentActionCoordinator _coordinator;

    public ProposeApiRequestDeleteTool(IApiClientAgentService apiClient, IAgentActionCoordinator coordinator)
    {
        _apiClient = apiClient;
        _coordinator = coordinator;
    }

    public string Name => "propose_api_request_delete";
    public string Description => "Propose deletion of an API request. This is a separate tool to make destruction explicit. Returns a pending action for user confirmation.";
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.High;

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
    {
        "type": "object",
        "properties": {
            "request_id": {
                "type": "string",
                "description": "ID of the request to delete."
            }
        },
        "required": ["request_id"],
        "additionalProperties": false
    }
    """);

    public FeatureArea FeatureArea => FeatureArea.ApiClient;

    public JsonElement ParametersSchema => Schema;

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("request_id", out var reqId) || reqId.GetString() is not { } requestId)
            return """{"error":"Missing required parameter 'request_id'."}""";

        var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
        if (snapshot is null)
            return $$"""{"error":"Request '{{requestId}}' not found."}""";

        var actionId = Guid.NewGuid().ToString("N");
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.DeleteRequest,
            Summary = $"Delete request '{snapshot.Name}'",
            Target = $"Request '{snapshot.Name}' ({snapshot.Id})",
            Risk = AgentActionRisk.High,
            Preview = $"Name: {snapshot.Name}\nMethod: {snapshot.Method}\nURL: {snapshot.Url}\nCollection: {snapshot.CollectionName}\nFolder: {snapshot.FolderPath ?? "root"}",
            ExpectedFingerprint = snapshot.UpdatedAt.ToString("O"),
        };

        _coordinator.RegisterAction(action);

        return JsonSerializer.Serialize(new
        {
            action_id = actionId,
            status = "pending_confirmation",
            summary = action.Summary,
            preview = action.Preview,
            risk = "High",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Deletion proposed. User must explicitly confirm before the request is removed.",
        });
    }
}

/// <summary>
/// Prepares HTTP request execution — resolves variables, masks secrets, creates confirmable action.
/// </summary>
public sealed class PrepareApiRequestExecutionTool : IAgentTool
{
    private readonly IApiClientAgentService _apiClient;
    private readonly IAgentActionCoordinator _coordinator;

    public PrepareApiRequestExecutionTool(IApiClientAgentService apiClient, IAgentActionCoordinator coordinator)
    {
        _apiClient = apiClient;
        _coordinator = coordinator;
    }

    public string Name => "prepare_api_request_execution";
    public string Description => "Prepare execution of an API request. Resolves variables, masks auth/secrets, and creates a confirmable action. No HTTP request is sent until the user confirms.";
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.High;

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
    {
        "type": "object",
        "properties": {
            "request_id": {
                "type": "string",
                "description": "ID of the request to execute."
            }
        },
        "required": ["request_id"],
        "additionalProperties": false
    }
    """);

    public FeatureArea FeatureArea => FeatureArea.ApiClient;

    public JsonElement ParametersSchema => Schema;

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("request_id", out var reqId) || reqId.GetString() is not { } requestId)
            return """{"error":"Missing required parameter 'request_id'."}""";

        var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
        if (snapshot is null)
            return $$"""{"error":"Request '{{requestId}}' not found."}""";

        var actionId = Guid.NewGuid().ToString("N");
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.ExecuteHttpRequest,
            Summary = $"Execute {snapshot.Method} {snapshot.Url}",
            Target = $"Request '{snapshot.Name}' ({snapshot.Id})",
            Risk = AgentActionRisk.High,
            Preview = $"Method: {snapshot.Method}\nURL: {snapshot.Url}\nHeaders: {snapshot.Headers.Count} (secrets masked)\nBody: {snapshot.BodyContentType ?? "none"}\n\nWARNING: This will send a real HTTP request to an external server.",
            ExpectedFingerprint = snapshot.UpdatedAt.ToString("O"),
        };

        _coordinator.RegisterAction(action);

        return JsonSerializer.Serialize(new
        {
            action_id = actionId,
            status = "pending_confirmation",
            summary = action.Summary,
            preview = action.Preview,
            risk = "High",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Execution prepared. User must confirm before the HTTP request is sent.",
        });
    }
}
