using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

public sealed partial class ApiClientWorkflowService(IVariableSubstitutionService substitution)
{
    private const string SecretMask = "********";

    public async Task<string> BuildCurlAsync(
        HttpRequestEntry request,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default)
    {
        var resolved = await substitution.BuildScopeAsync(collection.Variables, activeEnvironment, cancellationToken).ConfigureAwait(false);
        var scope = BuildSafeScope(collection.Variables, activeEnvironment, resolved);
        var method = request.Method == ApiRequestMethod.GraphQl ? ApiRequestMethod.Post : request.Method;
        var url = UrlBuilder.Build(request, scope, substitution);
        var lines = new List<string> { $"curl {Quote(url)}" };

        if (method != ApiRequestMethod.Get)
        {
            lines.Add($"  -X {MethodName(method)}");
        }

        foreach (var header in request.Headers.Where(static header => header.IsEnabled && !string.IsNullOrWhiteSpace(header.Key)))
        {
            var value = substitution.Substitute(header.Value ?? string.Empty, scope);
            lines.Add($"  -H {Quote($"{header.Key}: {value}")}");
        }

        var body = request.Method == ApiRequestMethod.GraphQl
            ? BuildGraphQlBody(request, scope)
            : BuildRequestBody(request.Body, scope);
        if (!string.IsNullOrWhiteSpace(body))
        {
            lines.Add($"  --data-raw {Quote(body)}");
        }

        return string.Join(" \\\n", lines);
    }

    public CurlImportResult ImportCurl(string command)
    {
        var tokens = Tokenize(command);
        if (tokens.Count == 0)
        {
            return CurlImportResult.Failure("Paste a cURL command first.");
        }

        if (tokens[0].Equals("curl", StringComparison.OrdinalIgnoreCase))
        {
            tokens.RemoveAt(0);
        }

        var method = ApiRequestMethod.Get;
        var url = string.Empty;
        var headers = new List<KeyValuePair<string>>();
        var bodyParts = new List<string>();

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var value = index + 1 < tokens.Count ? tokens[index + 1] : null;

            switch (token)
            {
                case "-X":
                case "--request":
                    if (value is null) return CurlImportResult.Failure("cURL request method is missing.");
                    method = ParseMethod(value);
                    index++;
                    break;

                case "-H":
                case "--header":
                    if (value is null) return CurlImportResult.Failure("cURL header value is missing.");
                    AddHeader(headers, value);
                    index++;
                    break;

                case "-d":
                case "--data":
                case "--data-raw":
                case "--data-binary":
                case "--data-urlencode":
                    if (value is null) return CurlImportResult.Failure("cURL body value is missing.");
                    bodyParts.Add(value);
                    if (method == ApiRequestMethod.Get)
                    {
                        method = ApiRequestMethod.Post;
                    }
                    index++;
                    break;

                case "--url":
                    if (value is null) return CurlImportResult.Failure("cURL URL is missing.");
                    url = value;
                    index++;
                    break;

                case "-I":
                case "--head":
                    method = ApiRequestMethod.Head;
                    break;

                default:
                    if (!token.StartsWith('-') && LooksLikeUrl(token))
                    {
                        url = token;
                    }
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return CurlImportResult.Failure("Could not find a URL in the cURL command.");
        }

        var request = new HttpRequestEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = BuildNameFromUrl(url),
            Method = method,
            Url = url,
            Headers = headers,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        if (bodyParts.Count > 0)
        {
            request.Body.RawContent = string.Join("&", bodyParts);
            request.Body.Mode = LooksLikeJson(request.Body.RawContent) ? RequestBodyMode.Json : RequestBodyMode.Text;
        }

        return CurlImportResult.Success(request);
    }

    public async Task<IReadOnlyList<VariableInspectionItem>> InspectVariablesAsync(
        HttpRequestEntry request,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default)
    {
        var tokens = ExtractTokens(request);
        if (tokens.Count == 0)
        {
            return [];
        }

        var resolved = await substitution.BuildScopeAsync(collection.Variables, activeEnvironment, cancellationToken).ConfigureAwait(false);
        return tokens.Select(token => InspectToken(token, collection, activeEnvironment, resolved)).ToList();
    }

    public ResponseExample CreateResponseExample(HttpRequestResult result, string name, string? environmentName)
    {
        return new ResponseExample
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? BuildExampleName(result) : name.Trim(),
            StatusCode = result.StatusCode,
            StatusText = result.StatusText,
            ContentType = result.ContentType,
            Body = ScrubBody(result.ResponseBody),
            Headers = result.ResponseHeaders
                .Select(header => new KeyValuePair<string>
                {
                    Key = header.Name,
                    Value = IsLikelySecret(header.Name) ? SecretMask : header.Value,
                    IsEnabled = true,
                })
                .ToList(),
            CapturedAt = DateTimeOffset.UtcNow,
            EnvironmentName = environmentName,
        };
    }

    private IReadOnlyDictionary<string, string?> BuildSafeScope(
        IEnumerable<CollectionVariable> collectionVariables,
        ApiEnvironment? activeEnvironment,
        IReadOnlyDictionary<string, string?> resolved)
    {
        var safe = new Dictionary<string, string?>(resolved, StringComparer.Ordinal);

        foreach (var variable in collectionVariables.Where(static variable => IsLikelySecret(variable.Key)))
        {
            safe[variable.Key] = SecretMask;
        }

        if (activeEnvironment is not null)
        {
            foreach (var variable in activeEnvironment.Variables.Where(static variable => IsSecretVariable(variable) || IsLikelySecret(variable.Key)))
            {
                safe[variable.Key] = SecretMask;
            }
        }

        return safe;
    }

    private VariableInspectionItem InspectToken(
        string token,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        IReadOnlyDictionary<string, string?> resolved)
    {
        var environmentVariable = activeEnvironment?.Variables.FirstOrDefault(variable => variable.IsEnabled && variable.Key.Equals(token, StringComparison.Ordinal));
        if (environmentVariable is not null)
        {
            var isSecret = IsSecretVariable(environmentVariable) || IsLikelySecret(token);
            return new VariableInspectionItem
            {
                Key = token,
                Source = environmentVariable.SecretSource switch
                {
                    EnvironmentVariableSecretSource.Generated => VariableInspectionSource.Generated,
                    EnvironmentVariableSecretSource.WindowsCredentialStore => VariableInspectionSource.CredentialStore,
                    EnvironmentVariableSecretSource.AzureKeyVault => VariableInspectionSource.KeyVault,
                    _ => VariableInspectionSource.Environment,
                },
                DisplayValue = isSecret ? SecretMask : resolved.GetValueOrDefault(token),
                IsSecret = isSecret,
            };
        }

        var collectionVariable = collection.Variables.FirstOrDefault(variable => variable.IsEnabled && variable.Key.Equals(token, StringComparison.Ordinal));
        if (collectionVariable is not null)
        {
            var isSecret = IsLikelySecret(token);
            return new VariableInspectionItem
            {
                Key = token,
                Source = collectionVariable.Generator is not null ? VariableInspectionSource.Generated : VariableInspectionSource.Collection,
                DisplayValue = isSecret ? SecretMask : resolved.GetValueOrDefault(token),
                IsSecret = isSecret,
            };
        }

        return new VariableInspectionItem
        {
            Key = token,
            Source = VariableInspectionSource.Unresolved,
            DisplayValue = null,
            IsSecret = IsLikelySecret(token),
        };
    }

    private string? BuildRequestBody(RequestBody body, IReadOnlyDictionary<string, string?> scope)
    {
        return body.Mode switch
        {
            RequestBodyMode.Json or RequestBodyMode.Xml or RequestBodyMode.Text => substitution.Substitute(body.RawContent ?? string.Empty, scope),
            RequestBodyMode.FormData => string.Join('&', body.FormData
                .Where(static item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.Key))
                .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(substitution.Substitute(item.Value ?? string.Empty, scope))}")),
            _ => null,
        };
    }

    private string BuildGraphQlBody(HttpRequestEntry request, IReadOnlyDictionary<string, string?> scope)
    {
        var payload = new Dictionary<string, object?>
        {
            ["query"] = substitution.Substitute(request.GraphQlQuery ?? string.Empty, scope),
        };

        if (!string.IsNullOrWhiteSpace(request.GraphQlVariables))
        {
            var variablesRaw = substitution.Substitute(request.GraphQlVariables, scope);
            try { payload["variables"] = JsonNode.Parse(variablesRaw); }
            catch { payload["variables"] = variablesRaw; }
        }

        if (!string.IsNullOrWhiteSpace(request.GraphQlSelectedOperation))
        {
            payload["operationName"] = request.GraphQlSelectedOperation;
        }

        return JsonSerializer.Serialize(payload);
    }

    private static IReadOnlyList<string> ExtractTokens(HttpRequestEntry request)
    {
        var values = new List<string?>
        {
            request.Url,
            request.Body.RawContent,
            request.GraphQlQuery,
            request.GraphQlVariables,
        };
        values.AddRange(request.Headers.Select(static header => header.Value));
        values.AddRange(request.QueryParams.Select(static param => param.Value));
        values.AddRange(request.Body.FormData.Select(static form => form.Value));

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(static value => TokenPattern().Matches(value!).Select(match => match.Groups[1].Value.Trim()))
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static token => token, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        var quote = '\0';
        var escape = false;

        foreach (var character in command)
        {
            if (escape)
            {
                builder.Append(character);
                escape = false;
                continue;
            }

            if (character == '\\' && quote != '\'')
            {
                escape = true;
                continue;
            }

            if ((character == '\'' || character == '"') && quote == '\0')
            {
                quote = character;
                continue;
            }

            if (character == quote)
            {
                quote = '\0';
                continue;
            }

            if (char.IsWhiteSpace(character) && quote == '\0')
            {
                FlushToken(tokens, builder);
                continue;
            }

            builder.Append(character);
        }

        FlushToken(tokens, builder);
        return tokens;
    }

    private static void FlushToken(List<string> tokens, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        tokens.Add(builder.ToString());
        builder.Clear();
    }

    private static void AddHeader(List<KeyValuePair<string>> headers, string header)
    {
        var separator = header.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return;
        }

        headers.Add(new KeyValuePair<string>
        {
            Key = header[..separator].Trim(),
            Value = header[(separator + 1)..].Trim(),
            IsEnabled = true,
        });
    }

    private static ApiRequestMethod ParseMethod(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "POST" => ApiRequestMethod.Post,
            "PUT" => ApiRequestMethod.Put,
            "PATCH" => ApiRequestMethod.Patch,
            "DELETE" => ApiRequestMethod.Delete,
            "HEAD" => ApiRequestMethod.Head,
            "OPTIONS" => ApiRequestMethod.Options,
            _ => ApiRequestMethod.Get,
        };
    }

    private static string MethodName(ApiRequestMethod method) => method switch
    {
        ApiRequestMethod.Post => "POST",
        ApiRequestMethod.Put => "PUT",
        ApiRequestMethod.Patch => "PATCH",
        ApiRequestMethod.Delete => "DELETE",
        ApiRequestMethod.Head => "HEAD",
        ApiRequestMethod.Options => "OPTIONS",
        ApiRequestMethod.GraphQl => "POST",
        _ => "GET",
    };

    private static string Quote(string value)
    {
        return $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }

    private static bool LooksLikeUrl(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static string BuildNameFromUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var segment = uri.Segments.LastOrDefault()?.Trim('/') ?? uri.Host;
            return string.IsNullOrWhiteSpace(segment) ? uri.Host : segment;
        }

        return "Imported cURL request";
    }

    private static string BuildExampleName(HttpRequestResult result) =>
        $"{result.StatusCode} {DateTimeOffset.Now:yyyy-MM-dd HH-mm-ss}";

    private static string? ScrubBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        try
        {
            var node = JsonNode.Parse(body);
            ScrubNode(node);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static void ScrubNode(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (IsLikelySecret(property.Key))
                {
                    obj[property.Key] = SecretMask;
                }
                else
                {
                    ScrubNode(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                ScrubNode(item);
            }
        }
    }

    private static bool IsSecretVariable(EnvironmentVariable variable) =>
        variable.SecretSource is EnvironmentVariableSecretSource.WindowsCredentialStore or EnvironmentVariableSecretSource.AzureKeyVault;

    private static bool IsLikelySecret(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower.Contains("secret", StringComparison.Ordinal) ||
               lower.Contains("password", StringComparison.Ordinal) ||
               lower.Contains("passwd", StringComparison.Ordinal) ||
               lower.Contains("token", StringComparison.Ordinal) ||
               lower.Contains("apikey", StringComparison.Ordinal) ||
               lower.Contains("api_key", StringComparison.Ordinal) ||
               lower.Contains("authorization", StringComparison.Ordinal) ||
               lower.Contains("credential", StringComparison.Ordinal) ||
               lower.Contains("private", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\{\{([^{}]+?)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
