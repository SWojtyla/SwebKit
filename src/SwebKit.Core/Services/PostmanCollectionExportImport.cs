using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Exports a collection to Postman Collection v2.1 JSON format.
/// </summary>
public sealed class PostmanCollectionExporter : ICollectionExporter
{
    public string FileExtension => ".postman_collection.json";
    public string FormatName => "Postman v2.1";

    public Task<byte[]> ExportAsync(
        ApiCollection collection,
        IReadOnlyList<ApiEnvironment> environments,
        CancellationToken cancellationToken = default)
    {
        var root = new JsonObject
        {
            ["info"] = new JsonObject
            {
                ["_postman_id"] = collection.Id,
                ["name"] = collection.Name,
                ["schema"] = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
            },
            ["item"] = BuildItems(collection.Nodes),
            ["variable"] = BuildCollectionVariables(collection.Variables),
        };

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult(Encoding.UTF8.GetBytes(json));
    }

    private static JsonArray BuildItems(List<ApiCollectionNode> nodes)
    {
        var arr = new JsonArray();
        foreach (var node in nodes)
        {
            if (node.Type == ApiCollectionNodeType.Folder)
            {
                arr.Add(new JsonObject
                {
                    ["name"] = node.Name,
                    ["item"] = BuildItems(node.Children),
                });
            }
            else if (node.Type == ApiCollectionNodeType.Request && node.Request is not null)
            {
                arr.Add(BuildRequestItem(node.Request));
            }
        }
        return arr;
    }

    private static JsonObject BuildRequestItem(HttpRequestEntry req)
    {
        var url = req.Url;
        var headers = new JsonArray();
        foreach (var h in req.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            headers.Add(new JsonObject { ["key"] = h.Key, ["value"] = h.Value ?? "" });
        }

        var queryParams = new JsonArray();
        foreach (var p in req.QueryParams.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key)))
        {
            queryParams.Add(new JsonObject { ["key"] = p.Key, ["value"] = p.Value ?? "" });
        }

        JsonNode? bodyNode = null;
        if (req.Method != ApiRequestMethod.GraphQl)
        {
            bodyNode = BuildBodyNode(req.Body);
        }
        else
        {
            bodyNode = new JsonObject
            {
                ["mode"] = "graphql",
                ["graphql"] = new JsonObject
                {
                    ["query"] = req.GraphQlQuery ?? "",
                    ["variables"] = req.GraphQlVariables ?? "",
                },
            };
        }

        var requestNode = new JsonObject
        {
            ["method"] = MapMethod(req.Method),
            ["header"] = headers,
            ["url"] = new JsonObject
            {
                ["raw"] = url,
                ["query"] = queryParams,
            },
        };

        if (bodyNode is not null)
            requestNode["body"] = bodyNode;

        return new JsonObject
        {
            ["name"] = req.Name,
            ["request"] = requestNode,
        };
    }

    private static JsonNode? BuildBodyNode(RequestBody body)
    {
        return body.Mode switch
        {
            RequestBodyMode.Json => new JsonObject
            {
                ["mode"] = "raw",
                ["raw"] = body.RawContent ?? "",
                ["options"] = new JsonObject { ["raw"] = new JsonObject { ["language"] = "json" } },
            },
            RequestBodyMode.Xml => new JsonObject
            {
                ["mode"] = "raw",
                ["raw"] = body.RawContent ?? "",
                ["options"] = new JsonObject { ["raw"] = new JsonObject { ["language"] = "xml" } },
            },
            RequestBodyMode.Text => new JsonObject
            {
                ["mode"] = "raw",
                ["raw"] = body.RawContent ?? "",
                ["options"] = new JsonObject { ["raw"] = new JsonObject { ["language"] = "text" } },
            },
            RequestBodyMode.FormData => BuildFormDataNode(body),
            RequestBodyMode.None => null,
            _ => null,
        };
    }

    private static JsonObject BuildFormDataNode(RequestBody body)
    {
        var formData = new JsonArray();
        foreach (var kv in body.FormData.Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.Key)))
        {
            formData.Add(new JsonObject { ["key"] = kv.Key, ["value"] = kv.Value ?? "", ["type"] = "text" });
        }
        return new JsonObject { ["mode"] = "formdata", ["formdata"] = formData };
    }

    private static JsonArray BuildCollectionVariables(List<CollectionVariable> vars)
    {
        var arr = new JsonArray();
        foreach (var v in vars.Where(v => v.IsEnabled && !string.IsNullOrWhiteSpace(v.Key)))
        {
            arr.Add(new JsonObject { ["key"] = v.Key, ["value"] = v.Value ?? "" });
        }
        return arr;
    }

    private static string MapMethod(ApiRequestMethod method) => method switch
    {
        ApiRequestMethod.Get => "GET",
        ApiRequestMethod.Post => "POST",
        ApiRequestMethod.Put => "PUT",
        ApiRequestMethod.Patch => "PATCH",
        ApiRequestMethod.Delete => "DELETE",
        ApiRequestMethod.Head => "HEAD",
        ApiRequestMethod.Options => "OPTIONS",
        ApiRequestMethod.GraphQl => "POST",
        _ => "GET",
    };
}

/// <summary>
/// Imports a Postman Collection v2 or v2.1 JSON file.
/// Collection variables are extracted as a new <see cref="ApiEnvironment"/>
/// named "&lt;CollectionName&gt; (imported)".
/// </summary>
public sealed class PostmanCollectionImporter : ICollectionImporter
{
    public bool CanImport(byte[] payload)
    {
        try
        {
            var json = Encoding.UTF8.GetString(payload);
            using var doc = JsonDocument.Parse(json);
            var schema = doc.RootElement
                .GetProperty("info").GetProperty("schema").GetString() ?? "";
            return schema.Contains("getpostman.com", StringComparison.OrdinalIgnoreCase) &&
                   schema.Contains("collection", StringComparison.OrdinalIgnoreCase);
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
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.TryGetProperty("info", out var info)
                ? info.TryGetProperty("name", out var n) ? n.GetString() ?? "Imported Collection" : "Imported Collection"
                : "Imported Collection";

            var collection = new ApiCollection
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            // Parse items
            if (root.TryGetProperty("item", out var items))
            {
                foreach (var itemEl in items.EnumerateArray())
                    collection.Nodes.Add(ParseItem(itemEl, warnings));
            }

            // Extract collection variables → new environment
            var environments = new List<ApiEnvironment>();
            int variablesExtracted = 0;
            if (root.TryGetProperty("variable", out var vars) && vars.ValueKind == JsonValueKind.Array)
            {
                var envVars = new List<EnvironmentVariable>();
                foreach (var v in vars.EnumerateArray())
                {
                    var key = GetStr(v, "key");
                    var value = GetStr(v, "value");
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        envVars.Add(new EnvironmentVariable { Key = key, Value = value, IsEnabled = true });
                        variablesExtracted++;
                    }
                }

                if (envVars.Count > 0)
                {
                    environments.Add(new ApiEnvironment
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = $"{name} (imported)",
                        Variables = envVars,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    });
                }
            }

            var requestCount = CountRequests(collection.Nodes);
            var captureCount = 0; // Postman has no equivalent capture rules
            var authCount = CountAuthConfigs(collection.Nodes);

            return Task.FromResult(new CollectionImportResult
            {
                Collections = [collection],
                Environments = environments,
                RequestCount = requestCount,
                CaptureRuleCount = captureCount,
                AuthConfigsRequiringReEntry = authCount,
                VariablesExtractedAsEnvironment = variablesExtracted,
                Warnings = warnings,
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CollectionImportResult
            {
                Warnings = [$"Import failed: {ex.Message}"],
            });
        }
    }

    private static ApiCollectionNode ParseItem(JsonElement el, List<string> warnings)
    {
        var name = GetStr(el, "name") ?? "Unnamed";

        // Folder — has an "item" array
        if (el.TryGetProperty("item", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            var folder = new ApiCollectionNode
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = ApiCollectionNodeType.Folder,
                Name = name,
                IsExpanded = true,
            };
            foreach (var child in children.EnumerateArray())
                folder.Children.Add(ParseItem(child, warnings));
            return folder;
        }

        // Request
        var request = new HttpRequestEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        if (el.TryGetProperty("request", out var reqEl) && reqEl.ValueKind == JsonValueKind.Object)
        {
            var method = GetStr(reqEl, "method")?.ToUpperInvariant() ?? "GET";
            request.Method = ParseMethod(method);
            request.Url = ExtractUrl(reqEl);

            // Headers
            if (reqEl.TryGetProperty("header", out var hdrs) && hdrs.ValueKind == JsonValueKind.Array)
            {
                foreach (var h in hdrs.EnumerateArray())
                {
                    var key = GetStr(h, "key") ?? "";
                    var val = GetStr(h, "value") ?? "";
                    if (!string.IsNullOrWhiteSpace(key))
                        request.Headers.Add(new KeyValuePair<string> { Key = key, Value = val });
                }
            }

            // Body
            if (reqEl.TryGetProperty("body", out var body))
                ParseBody(body, request, warnings);

            // Auth (flag as requiring re-entry — Postman stores auth in the request)
            if (reqEl.TryGetProperty("auth", out var auth) && auth.ValueKind == JsonValueKind.Object)
            {
                var authType = GetStr(auth, "type");
                if (!string.IsNullOrWhiteSpace(authType) && authType != "noauth")
                    request.Auth = new AuthConfig { Type = MapPostmanAuthType(authType) };
            }
        }
        else
        {
            warnings.Add($"Request '{name}' has no 'request' body — using defaults.");
        }

        return new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = ApiCollectionNodeType.Request,
            Name = name,
            Request = request,
        };
    }

    private static void ParseBody(JsonElement body, HttpRequestEntry req, List<string> warnings)
    {
        var mode = GetStr(body, "mode");
        switch (mode)
        {
            case "raw":
                {
                    var raw = GetStr(body, "raw") ?? "";
                    var language = body.TryGetProperty("options", out var opts) &&
                                   opts.TryGetProperty("raw", out var rawOpts) &&
                                   rawOpts.TryGetProperty("language", out var lang)
                        ? lang.GetString()
                        : null;

                    req.Body = new RequestBody
                    {
                        Mode = language switch
                        {
                            "json" => RequestBodyMode.Json,
                            "xml" => RequestBodyMode.Xml,
                            _ => RequestBodyMode.Text,
                        },
                        RawContent = raw,
                    };
                    break;
                }

            case "graphql":
                {
                    req.Method = ApiRequestMethod.GraphQl;
                    if (body.TryGetProperty("graphql", out var gql))
                    {
                        req.GraphQlQuery = GetStr(gql, "query");
                        req.GraphQlVariables = GetStr(gql, "variables");
                    }
                    break;
                }

            case "formdata":
                {
                    req.Body = new RequestBody { Mode = RequestBodyMode.FormData };
                    if (body.TryGetProperty("formdata", out var fd) && fd.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in fd.EnumerateArray())
                        {
                            var key = GetStr(item, "key") ?? "";
                            var value = GetStr(item, "value") ?? "";
                            if (!string.IsNullOrWhiteSpace(key))
                                req.Body.FormData.Add(new KeyValuePair<string> { Key = key, Value = value });
                        }
                    }
                    break;
                }
        }
    }

    private static string ExtractUrl(JsonElement reqEl)
    {
        if (!reqEl.TryGetProperty("url", out var url)) return "";
        if (url.ValueKind == JsonValueKind.String) return url.GetString() ?? "";
        if (url.ValueKind == JsonValueKind.Object)
        {
            var raw = GetStr(url, "raw");
            if (!string.IsNullOrWhiteSpace(raw)) return raw;
        }
        return "";
    }

    private static ApiRequestMethod ParseMethod(string method) => method switch
    {
        "POST" => ApiRequestMethod.Post,
        "PUT" => ApiRequestMethod.Put,
        "PATCH" => ApiRequestMethod.Patch,
        "DELETE" => ApiRequestMethod.Delete,
        "HEAD" => ApiRequestMethod.Head,
        "OPTIONS" => ApiRequestMethod.Options,
        _ => ApiRequestMethod.Get,
    };

    private static AuthType MapPostmanAuthType(string type) => type.ToLowerInvariant() switch
    {
        "bearer" => AuthType.BearerToken,
        "basic" => AuthType.Basic,
        "apikey" => AuthType.ApiKey,
        "oauth2" => AuthType.OAuth2,
        _ => AuthType.None,
    };

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

    private static int CountAuthConfigs(List<ApiCollectionNode> nodes)
    {
        var count = 0;
        foreach (var n in nodes)
        {
            if (n.Type == ApiCollectionNodeType.Request &&
                n.Request?.Auth is { Type: not AuthType.None and not AuthType.Inherited })
                count++;
            else if (n.Type == ApiCollectionNodeType.Folder)
                count += CountAuthConfigs(n.Children);
        }
        return count;
    }

    private static string? GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
