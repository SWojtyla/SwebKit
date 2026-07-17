using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Imports a Bruno collection from a folder on the local file system.
/// Walks the directory tree, parses <c>.bru</c> request files and
/// <c>environments/*.bru</c> environment files, and returns a
/// <see cref="CollectionImportResult"/>.
/// </summary>
public sealed class BrunoFolderImporter
{
    private static readonly HashSet<string> IgnoredDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase) { "node_modules", ".git", ".svn" };

    private static readonly HashSet<string> RawContentBlockNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "body:json", "body:text", "body:xml", "body:sparql",
            "body:graphql", "body:graphql:vars",
            "script:pre-request", "script:post-response",
        };

    /// <summary>
    /// Imports the Bruno collection rooted at <paramref name="folderPath"/>.
    /// <para>
    /// If the folder is itself a collection (contains a <c>bruno.json</c>) it is imported as a
    /// single collection. If the folder is <em>not</em> a collection but one or more of its
    /// immediate subdirectories are, each of those is imported as its own top-level collection —
    /// this flattens a "workspace" folder that merely groups several Bruno collections (and, as a
    /// side effect, lets each collection's own <c>environments/</c> folder be discovered).
    /// </para>
    /// </summary>
    public Task<CollectionImportResult> ImportFromFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
            return Task.FromResult(new CollectionImportResult
            {
                Warnings = [$"Folder not found: {folderPath}"],
            });

        // Multi-collection workspace: the picked folder is not a collection, but children are.
        var isCollectionRoot = File.Exists(Path.Combine(folderPath, "bruno.json"));
        if (!isCollectionRoot)
        {
            var childCollectionDirs = Directory.GetDirectories(folderPath)
                .Where(d => File.Exists(Path.Combine(d, "bruno.json")))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (childCollectionDirs.Count > 0)
            {
                var collections = new List<ApiCollection>();
                var environments = new List<ApiEnvironment>();
                var aggregateWarnings = new List<string>();
                var totalRequests = 0;
                var totalCaptures = 0;

                foreach (var childDir in childCollectionDirs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var childResult = ImportSingleCollection(childDir);
                    collections.AddRange(childResult.Collections);
                    environments.AddRange(childResult.Environments);
                    aggregateWarnings.AddRange(childResult.Warnings);
                    totalRequests += childResult.RequestCount;
                    totalCaptures += childResult.CaptureRuleCount;
                }

                return Task.FromResult(new CollectionImportResult
                {
                    Collections = collections,
                    Environments = environments,
                    RequestCount = totalRequests,
                    CaptureRuleCount = totalCaptures,
                    Warnings = aggregateWarnings,
                });
            }
        }

        return Task.FromResult(ImportSingleCollection(folderPath));
    }

    /// <summary>
    /// Imports a single Bruno collection rooted at <paramref name="folderPath"/>. The folder is
    /// treated as a collection whether or not it has a <c>bruno.json</c> (the manifest only supplies
    /// the display name; the folder name is used as a fallback).
    /// </summary>
    private static CollectionImportResult ImportSingleCollection(string folderPath)
    {
        var warnings = new List<string>();

        // Read collection name from bruno.json (fall back to folder name)
        var collectionName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var brunoJsonPath = Path.Combine(folderPath, "bruno.json");
        if (File.Exists(brunoJsonPath))
        {
            try
            {
                var manifestContent = File.ReadAllText(brunoJsonPath);
                // Extract "name" field — use simple string search to avoid a JSON dependency
                var extractedName = ExtractJsonStringValue(manifestContent, "name");
                if (!string.IsNullOrWhiteSpace(extractedName))
                    collectionName = extractedName;
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not read bruno.json: {ex.Message}");
            }
        }

        var collection = new ApiCollection
        {
            Id = Guid.NewGuid().ToString(),
            Name = collectionName,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Apply collection-level auth/variables from collection.bru
        var collectionBruPath = Path.Combine(folderPath, "collection.bru");
        if (File.Exists(collectionBruPath))
        {
            try
            {
                var content = File.ReadAllText(collectionBruPath);
                var blocks = ParseBlocks(content);
                ApplyAuthFromBlocks(blocks, out var auth);
                if (auth is not null) collection.DefaultAuth = auth;
                ApplyVariablesFromBlocks(blocks, out var variables);
                collection.Variables.AddRange(variables);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not parse collection.bru: {ex.Message}");
            }
        }

        // Walk directory for requests and folders (exclude special dirs)
        var requestCount = 0;
        var captureCount = 0;

        ProcessDirectory(folderPath, collection.Nodes, ref requestCount, ref captureCount, warnings);

        // Sort nodes by seq then name
        SortNodes(collection.Nodes);

        // Read environments
        var environments = new List<ApiEnvironment>();
        var envFolder = Path.Combine(folderPath, "environments");
        if (Directory.Exists(envFolder))
        {
            foreach (var envFile in Directory.GetFiles(envFolder, "*.bru").OrderBy(f => f))
            {
                try
                {
                    var envName = Path.GetFileNameWithoutExtension(envFile);
                    var content = File.ReadAllText(envFile);
                    var env = ParseEnvironmentFile(envName, content, warnings);
                    // Bruno environments/ files are collection environments — scope them to this collection.
                    env.CollectionId = collection.Id;
                    environments.Add(env);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not parse environment file {Path.GetFileName(envFile)}: {ex.Message}");
                }
            }
        }

        return new CollectionImportResult
        {
            Collections = [collection],
            Environments = environments,
            RequestCount = requestCount,
            CaptureRuleCount = captureCount,
            Warnings = warnings,
        };
    }

    // ── Directory walker ──────────────────────────────────────────────────────

    private static void ProcessDirectory(
        string dirPath,
        List<ApiCollectionNode> nodes,
        ref int requestCount,
        ref int captureCount,
        List<string> warnings)
    {
        // Track seq numbers from folder.bru or request files for ordering
        var seqMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Process .bru files as requests (skip meta files)
        foreach (var filePath in Directory.GetFiles(dirPath, "*.bru").OrderBy(f => f))
        {
            var fileName = Path.GetFileName(filePath);
            if (string.Equals(fileName, "folder.bru", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "collection.bru", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var content = File.ReadAllText(filePath);
                var (node, seq) = ParseRequestFile(content, warnings);
                if (node is null) continue;

                requestCount++;
                captureCount += node.Request?.CaptureRules.Count(r => r.IsEnabled) ?? 0;
                nodes.Add(node);
                if (seq > 0) seqMap[node.Name] = seq;
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not parse {fileName}: {ex.Message}");
            }
        }

        // Process subdirectories as folders (skip special names and environments)
        foreach (var subDir in Directory.GetDirectories(dirPath).OrderBy(d => d))
        {
            var dirName = Path.GetFileName(subDir);
            if (IgnoredDirectoryNames.Contains(dirName)) continue;
            if (string.Equals(dirName, "environments", StringComparison.OrdinalIgnoreCase)) continue;

            var folderNode = new ApiCollectionNode
            {
                Id = Guid.NewGuid().ToString(),
                Type = ApiCollectionNodeType.Folder,
                Name = dirName,
                IsExpanded = false,
            };

            // Read folder.bru for metadata
            var folderBruPath = Path.Combine(subDir, "folder.bru");
            var folderSeq = 0;
            if (File.Exists(folderBruPath))
            {
                try
                {
                    var content = File.ReadAllText(folderBruPath);
                    var blocks = ParseBlocks(content);

                    if (blocks.TryGetValue("meta", out var metaLines))
                    {
                        var meta = ParseKeyValues(metaLines);
                        if (meta.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
                            folderNode.Name = name;
                        if (meta.TryGetValue("seq", out var seqStr) && int.TryParse(seqStr, out var seq))
                            folderSeq = seq;
                    }

                    ApplyAuthFromBlocks(blocks, out var auth);
                    if (auth is not null) folderNode.DefaultAuth = auth;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not parse folder.bru in {dirName}: {ex.Message}");
                }
            }

            if (folderSeq > 0) seqMap[folderNode.Name] = folderSeq;

            ProcessDirectory(subDir, folderNode.Children, ref requestCount, ref captureCount, warnings);
            SortNodes(folderNode.Children);
            nodes.Add(folderNode);
        }

        // Apply seq ordering within this level
        if (seqMap.Count > 0)
        {
            nodes.Sort((a, b) =>
            {
                var aSeq = seqMap.GetValueOrDefault(a.Name, int.MaxValue);
                var bSeq = seqMap.GetValueOrDefault(b.Name, int.MaxValue);
                return aSeq.CompareTo(bSeq);
            });
        }
    }

    private static void SortNodes(List<ApiCollectionNode> nodes)
    {
        // Secondary stable sort: folders first, then requests, within the same seq group
        // Primary sort already done with seq; this is just a stable secondary
    }

    // ── .bru request file parser ──────────────────────────────────────────────

    private static (ApiCollectionNode? node, int seq) ParseRequestFile(
        string content,
        List<string> warnings)
    {
        var blocks = ParseBlocks(content);

        // Read meta block
        string? name = null;
        string? type = null;
        var seq = 0;

        if (blocks.TryGetValue("meta", out var metaLines))
        {
            var meta = ParseKeyValues(metaLines);
            meta.TryGetValue("name", out name);
            meta.TryGetValue("type", out type);
            if (meta.TryGetValue("seq", out var seqStr)) int.TryParse(seqStr, out seq);
        }

        if (string.IsNullOrWhiteSpace(name)) return (null, 0);

        // Determine HTTP method and URL
        ApiRequestMethod method = ApiRequestMethod.Get;
        string url = string.Empty;
        RequestBodyMode bodyMode = RequestBodyMode.None;

        var methodBlockKey = FindMethodBlock(blocks, out var methodStr);
        if (methodBlockKey is not null && blocks.TryGetValue(methodBlockKey, out var methodLines))
        {
            var methodKv = ParseKeyValues(methodLines);
            methodKv.TryGetValue("url", out url!);

            if (methodKv.TryGetValue("body", out var bodyStr))
                bodyMode = MapBodyMode(bodyStr);

            method = MapMethod(methodStr!);
        }

        var request = new HttpRequestEntry
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Method = method,
            Url = url ?? string.Empty,
            Body = new RequestBody { Mode = bodyMode },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Headers
        if (blocks.TryGetValue("headers", out var headerLines))
        {
            foreach (var (k, v) in ParseKeyValues(headerLines))
                request.Headers.Add(new KeyValuePair<string> { Key = k, Value = v, IsEnabled = true });
        }

        // Query params
        if (blocks.TryGetValue("query", out var queryLines))
        {
            foreach (var (k, v) in ParseKeyValues(queryLines))
                request.QueryParams.Add(new KeyValuePair<string> { Key = k, Value = v, IsEnabled = true });
        }

        // Body content
        if (blocks.TryGetValue("body:json", out var jsonLines) && jsonLines.Length > 0)
        {
            request.Body.Mode = RequestBodyMode.Json;
            request.Body.RawContent = string.Join("\n", jsonLines).Trim();
            request.Body.ContentType = "application/json";
        }
        else if (blocks.TryGetValue("body:text", out var textLines) && textLines.Length > 0)
        {
            request.Body.Mode = RequestBodyMode.Text;
            request.Body.RawContent = string.Join("\n", textLines).Trim();
        }
        else if (blocks.TryGetValue("body:xml", out var xmlLines) && xmlLines.Length > 0)
        {
            request.Body.Mode = RequestBodyMode.Xml;
            request.Body.RawContent = string.Join("\n", xmlLines).Trim();
            request.Body.ContentType = "application/xml";
        }
        else if (blocks.TryGetValue("body:form-urlencoded", out var formLines) && formLines.Length > 0)
        {
            request.Body.Mode = RequestBodyMode.FormData;
            foreach (var (k, v) in ParseKeyValues(formLines))
                request.Body.FormData.Add(new KeyValuePair<string> { Key = k, Value = v, IsEnabled = true });
        }

        // GraphQL
        if (blocks.TryGetValue("body:graphql", out var gqlLines) && gqlLines.Length > 0)
        {
            request.Method = ApiRequestMethod.GraphQl;
            request.GraphQlQuery = string.Join("\n", gqlLines).Trim();
        }
        if (blocks.TryGetValue("body:graphql:vars", out var gqlVarsLines) && gqlVarsLines.Length > 0)
        {
            request.GraphQlVariables = string.Join("\n", gqlVarsLines).Trim();
        }

        // Auth
        ApplyAuthFromBlocks(blocks, out var auth);
        request.Auth = auth;

        // Capture rules from vars:post-response
        if (blocks.TryGetValue("vars:post-response", out var captureLines))
        {
            foreach (var (k, v) in ParseKeyValues(captureLines))
            {
                if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(v)) continue;
                var rule = BuildCaptureRule(k, v);
                if (rule is not null) request.CaptureRules.Add(rule);
            }
        }

        var node = new ApiCollectionNode
        {
            Id = Guid.NewGuid().ToString(),
            Type = ApiCollectionNodeType.Request,
            Name = name,
            Request = request,
        };

        return (node, seq);
    }

    // ── Environment file parser ───────────────────────────────────────────────

    private static ApiEnvironment ParseEnvironmentFile(
        string name,
        string content,
        List<string> warnings)
    {
        var env = new ApiEnvironment
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var blocks = ParseBlocks(content);

        // Plain vars
        if (blocks.TryGetValue("vars", out var varLines))
        {
            foreach (var (k, v) in ParseKeyValues(varLines))
            {
                if (string.IsNullOrWhiteSpace(k)) continue;
                env.Variables.Add(new EnvironmentVariable
                {
                    Key = k,
                    Value = v,
                    SecretSource = EnvironmentVariableSecretSource.Plain,
                    IsEnabled = true,
                });
            }
        }

        // Secret var placeholders — add as empty plain variables (credentials require re-entry)
        if (blocks.TryGetValue("vars:secret[]", out var secretItems))
        {
            foreach (var key in secretItems)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                // Only add if not already present from vars block
                if (!env.Variables.Any(v => string.Equals(v.Key, key, StringComparison.Ordinal)))
                {
                    env.Variables.Add(new EnvironmentVariable
                    {
                        Key = key,
                        Value = string.Empty,
                        SecretSource = EnvironmentVariableSecretSource.Plain,
                        IsEnabled = true,
                    });
                }
            }
        }

        return env;
    }

    // ── Block-level helpers ───────────────────────────────────────────────────

    private static void ApplyAuthFromBlocks(
        Dictionary<string, string[]> blocks,
        out AuthConfig? auth)
    {
        auth = null;

        if (!blocks.TryGetValue("auth", out var authMetaLines)) return;
        var authMeta = ParseKeyValues(authMetaLines);
        if (!authMeta.TryGetValue("mode", out var mode) || string.IsNullOrWhiteSpace(mode)) return;

        switch (mode.ToLowerInvariant())
        {
            case "bearer":
                var bearerToken = string.Empty;
                if (blocks.TryGetValue("auth:bearer", out var bearerLines))
                {
                    var bearerKv = ParseKeyValues(bearerLines);
                    bearerKv.TryGetValue("token", out bearerToken!);
                }
                auth = new AuthConfig
                {
                    Type = AuthType.BearerToken,
                    // Bearer token is a credential — we leave CredentialKey null (requires re-entry in SwebKit)
                };
                break;

            case "basic":
                string? basicUser = null;
                if (blocks.TryGetValue("auth:basic", out var basicLines))
                {
                    var basicKv = ParseKeyValues(basicLines);
                    basicKv.TryGetValue("username", out basicUser);
                }
                auth = new AuthConfig
                {
                    Type = AuthType.Basic,
                    BasicUsername = basicUser,
                };
                break;

            case "apikey":
                string? apiKeyName = null;
                var apiKeyLocation = ApiKeyLocation.Header;
                if (blocks.TryGetValue("auth:apikey", out var apiKeyLines))
                {
                    var apiKeyKv = ParseKeyValues(apiKeyLines);
                    apiKeyKv.TryGetValue("key", out apiKeyName);
                    if (apiKeyKv.TryGetValue("placement", out var placement) &&
                        string.Equals(placement, "queryparams", StringComparison.OrdinalIgnoreCase))
                        apiKeyLocation = ApiKeyLocation.QueryParam;
                }
                auth = new AuthConfig
                {
                    Type = AuthType.ApiKey,
                    ApiKeyParamName = apiKeyName,
                    ApiKeyLocation = apiKeyLocation,
                };
                break;

            case "none":
                auth = new AuthConfig { Type = AuthType.None };
                break;

            case "inherit":
                auth = new AuthConfig { Type = AuthType.Inherited };
                break;
        }
    }

    private static void ApplyVariablesFromBlocks(
        Dictionary<string, string[]> blocks,
        out List<CollectionVariable> variables)
    {
        variables = [];
        if (!blocks.TryGetValue("vars:pre-request", out var varLines)) return;
        foreach (var (k, v) in ParseKeyValues(varLines))
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            variables.Add(new CollectionVariable { Key = k, Value = v, IsEnabled = true });
        }
    }

    private static CaptureRule? BuildCaptureRule(string targetVariable, string brunoExpr)
    {
        if (string.Equals(brunoExpr, "res.status", StringComparison.OrdinalIgnoreCase))
        {
            return new CaptureRule
            {
                Id = Guid.NewGuid().ToString(),
                TargetVariable = targetVariable,
                TargetScope = "collection",
                Source = CaptureSource.StatusCode,
                IsEnabled = true,
            };
        }

        if (brunoExpr.StartsWith("res.headers[", StringComparison.OrdinalIgnoreCase))
        {
            // res.headers['x-header-name'] or res.headers["x-header-name"]
            var headerName = brunoExpr
                .Replace("res.headers[", "", StringComparison.OrdinalIgnoreCase)
                .TrimEnd(']')
                .Trim('\'', '"');
            return new CaptureRule
            {
                Id = Guid.NewGuid().ToString(),
                TargetVariable = targetVariable,
                TargetScope = "collection",
                Source = CaptureSource.ResponseHeader,
                HeaderName = headerName,
                IsEnabled = true,
            };
        }

        if (brunoExpr.StartsWith("res.body", StringComparison.OrdinalIgnoreCase))
        {
            // Convert res.body.token → $.token
            // Convert res.body.data.user.id → $.data.user.id
            var path = brunoExpr.Replace("res.body", "$", StringComparison.OrdinalIgnoreCase);
            return new CaptureRule
            {
                Id = Guid.NewGuid().ToString(),
                TargetVariable = targetVariable,
                TargetScope = "collection",
                Source = CaptureSource.BodyJsonPath,
                JsonPath = path,
                IsEnabled = true,
            };
        }

        // Unknown expression — skip
        return null;
    }

    // ── .bru block parser ─────────────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>.bru</c> file into a dictionary of block-name → content lines.
    /// <para>
    /// List blocks (e.g. <c>vars:secret [item1, item2]</c>) are stored with a
    /// <c>"[]"</c> suffix on the key, and each element is a separate string entry.
    /// </para>
    /// </summary>
    private static Dictionary<string, string[]> ParseBlocks(string content)
    {
        var blocks = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            var trimmed = lines[i].Trim();

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith('#'))
            {
                i++;
                continue;
            }

            // List block: "block-name ["
            if (trimmed.EndsWith('['))
            {
                var blockName = trimmed[..^1].Trim() + "[]";
                var items = new List<string>();
                i++;

                while (i < lines.Length)
                {
                    var itemLine = lines[i].Trim();
                    if (itemLine.StartsWith(']')) break;
                    if (!string.IsNullOrWhiteSpace(itemLine))
                        items.Add(itemLine.TrimEnd(',').Trim());
                    i++;
                }
                i++; // skip ']'
                blocks[blockName] = [.. items];
                continue;
            }

            // Curly block: "block-name {"
            if (trimmed.EndsWith('{'))
            {
                var blockName = trimmed[..^1].Trim();
                var contentLines = new List<string>();
                i++;
                int depth = 1;

                // Raw content blocks preserve lines as-is; key-value blocks trim
                var isRaw = RawContentBlockNames.Contains(blockName);

                while (i < lines.Length && depth > 0)
                {
                    var line = lines[i];
                    var lineTrimmed = line.Trim();

                    // Track brace depth
                    foreach (var ch in line)
                    {
                        if (ch == '{') depth++;
                        else if (ch == '}') depth--;
                    }

                    if (depth > 0)
                        contentLines.Add(isRaw ? line : lineTrimmed);

                    i++;
                }

                blocks[blockName] = [.. contentLines];
                continue;
            }

            i++;
        }

        return blocks;
    }

    private static Dictionary<string, string> ParseKeyValues(string[] lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//")) continue;

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key = trimmed[..colonIdx].Trim();
            var value = trimmed[(colonIdx + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
                result[key] = value;
        }
        return result;
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static string? FindMethodBlock(
        Dictionary<string, string[]> blocks,
        out string? methodStr)
    {
        var httpMethods = new[] { "get", "post", "put", "patch", "delete", "head", "options" };
        foreach (var m in httpMethods)
        {
            if (blocks.ContainsKey(m))
            {
                methodStr = m;
                return m;
            }
        }
        methodStr = null;
        return null;
    }

    private static ApiRequestMethod MapMethod(string brunoMethod) =>
        brunoMethod.ToLowerInvariant() switch
        {
            "post" => ApiRequestMethod.Post,
            "put" => ApiRequestMethod.Put,
            "patch" => ApiRequestMethod.Patch,
            "delete" => ApiRequestMethod.Delete,
            "head" => ApiRequestMethod.Head,
            "options" => ApiRequestMethod.Options,
            _ => ApiRequestMethod.Get,
        };

    private static RequestBodyMode MapBodyMode(string? brunoBody) =>
        brunoBody?.ToLowerInvariant() switch
        {
            "json" => RequestBodyMode.Json,
            "xml" => RequestBodyMode.Xml,
            "text" => RequestBodyMode.Text,
            "form-urlencoded" or "formurlencoded" => RequestBodyMode.FormData,
            "multipart" or "multipartform" => RequestBodyMode.FormData,
            _ => RequestBodyMode.None,
        };

    private static string? ExtractJsonStringValue(string json, string key)
    {
        // Very simple extraction — avoids a JSON dependency for manifest reading
        var pattern = $"\"{key}\"";
        var keyIdx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (keyIdx < 0) return null;

        var colonIdx = json.IndexOf(':', keyIdx + pattern.Length);
        if (colonIdx < 0) return null;

        var remaining = json[(colonIdx + 1)..].TrimStart();
        if (!remaining.StartsWith('"')) return null;

        var valueStart = 1;
        var valueEnd = remaining.IndexOf('"', valueStart);
        if (valueEnd < 0) return null;

        return remaining[valueStart..valueEnd];
    }
}
