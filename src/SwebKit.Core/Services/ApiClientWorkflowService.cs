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
        var resolved = await substitution.BuildScopeAsync(collection.Variables, [activeEnvironment], cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Parses a pasted cURL command — or a paste holding several commands separated by newlines,
    /// <c>&&</c>, or <c>;</c> — into request entries. Shell continuations (<c>\</c> and cmd.exe's
    /// <c>^</c>) are joined first; each <c>curl</c> invocation and each <c>--next</c> section
    /// produces one request. Flags the importer doesn't honor land in warnings rather than
    /// failing the parse or being dropped silently.
    /// </summary>
    public CurlImportResult ImportCurl(string command)
    {
        var warnings = new List<string>();
        var groups = SplitCommandGroups(command, warnings);
        if (groups.Count == 0)
        {
            return CurlImportResult.Failure("Paste a cURL command first.");
        }

        var requests = new List<HttpRequestEntry>();
        foreach (var group in groups)
        {
            var tokens = Tokenize(group);
            if (tokens.Count > 0 && IsCurlToken(tokens[0]))
            {
                tokens.RemoveAt(0);
            }

            foreach (var invocation in SplitOnNextSeparator(tokens))
            {
                var (request, error) = ParseSingleCurlCommand(invocation, warnings);
                if (error is not null)
                {
                    return CurlImportResult.Failure(error);
                }

                requests.Add(request!);
            }
        }

        if (requests.Count == 0)
        {
            return CurlImportResult.Failure("Could not find a URL in the cURL command.");
        }

        DeduplicateRequestNames(requests);
        return CurlImportResult.Success(requests, warnings);
    }

    /// <summary>
    /// Parses the tokens of one curl invocation (after the <c>curl</c> prefix was stripped).
    /// Returns the request, or an error message for the same hard failures as before
    /// (missing values, no URL).
    /// </summary>
    private static (HttpRequestEntry? Request, string? Error) ParseSingleCurlCommand(
        IReadOnlyList<string> tokens, List<string> warnings)
    {
        var method = ApiRequestMethod.Get;
        var url = string.Empty;
        var headers = new List<KeyValuePair<string>>();
        var bodyParts = new List<string>();
        var formFields = new List<FormDataField>();
        var endOfOptions = false;
        string? basicUser = null;
        string? error = null;
        var index = 0;

        string? TakeValue(string? inlineValue) =>
            inlineValue ?? (index + 1 < tokens.Count ? tokens[++index] : null);

        // Applies a flag the importer understands: mapped flags, warnings, and the ignorable
        // tables. Returns false for anything unknown so the caller can surface it in warnings.
        bool TryApplyFlag(string flag, string? inlineValue)
        {
            switch (flag)
            {
                case "-X":
                case "--request":
                    var requestMethod = TakeValue(inlineValue);
                    if (requestMethod is null) { error = "cURL request method is missing."; return true; }
                    method = ParseMethod(requestMethod);
                    return true;

                case "-H":
                case "--header":
                    var header = TakeValue(inlineValue);
                    if (header is null) { error = "cURL header value is missing."; return true; }
                    AddHeader(headers, header);
                    return true;

                case "-d":
                case "--data":
                case "--data-raw":
                case "--data-binary":
                case "--data-ascii":
                case "--data-urlencode":
                    var data = TakeValue(inlineValue);
                    if (data is null) { error = "cURL body value is missing."; return true; }
                    bodyParts.Add(data);
                    if (method == ApiRequestMethod.Get) method = ApiRequestMethod.Post;
                    return true;

                case "--json":
                    // curl ≥7.82: --json is --data plus JSON content headers.
                    var json = TakeValue(inlineValue);
                    if (json is null) { error = "cURL body value is missing."; return true; }
                    bodyParts.Add(json);
                    if (method == ApiRequestMethod.Get) method = ApiRequestMethod.Post;
                    SetHeader(headers, "Content-Type", "application/json");
                    SetHeader(headers, "Accept", "application/json");
                    return true;

                case "-F":
                case "--form":
                case "--form-string":
                    var field = TakeValue(inlineValue);
                    if (field is null) { error = "cURL form field value is missing."; return true; }
                    AddFormField(formFields, field, literal: flag == "--form-string", warnings);
                    if (method == ApiRequestMethod.Get) method = ApiRequestMethod.Post;
                    return true;

                case "-b":
                case "--cookie":
                    var cookie = TakeValue(inlineValue);
                    if (cookie is null) { error = "cURL cookie value is missing."; return true; }
                    // Multiple -b flags merge into one Cookie header, matching curl's "; " join.
                    var existingCookie = headers.LastOrDefault(h => h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
                    if (existingCookie is not null) existingCookie.Value = $"{existingCookie.Value}; {cookie}";
                    else headers.Add(new KeyValuePair<string> { Key = "Cookie", Value = cookie, IsEnabled = true });
                    return true;

                case "-A":
                case "--user-agent":
                    var agent = TakeValue(inlineValue);
                    if (agent is null) { error = "cURL user-agent value is missing."; return true; }
                    SetHeader(headers, "User-Agent", agent);
                    return true;

                case "-e":
                case "--referer":
                    var referer = TakeValue(inlineValue);
                    if (referer is null) { error = "cURL referer value is missing."; return true; }
                    SetHeader(headers, "Referer", referer);
                    return true;

                case "--url":
                    var target = TakeValue(inlineValue);
                    if (target is null) { error = "cURL URL is missing."; return true; }
                    url = target;
                    return true;

                case "-I":
                case "--head":
                    method = ApiRequestMethod.Head;
                    return true;

                case "-u":
                case "--user":
                    // `curl -u alice:secret` is Basic auth — common in API docs, and silently
                    // dropping it produced an imported request that 401s for no visible reason.
                    var user = TakeValue(inlineValue);
                    if (user is null) { error = "cURL user value is missing."; return true; }
                    basicUser = user;
                    return true;

                case "-k":
                case "--insecure":
                    const string insecureWarning =
                        "Command uses -k/--insecure; the app's global SSL verification setting still applies.";
                    if (!warnings.Contains(insecureWarning)) warnings.Add(insecureWarning);
                    return true;

                default:
                    if (IgnorableValueFlags.Contains(flag))
                    {
                        TakeValue(inlineValue);
                        return true;
                    }

                    if (IgnorableFlags.Contains(flag))
                    {
                        return true;
                    }

                    return false;
            }
        }

        for (index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];

            if (endOfOptions)
            {
                if (LooksLikeUrl(token)) url = token;
                continue;
            }

            if (token == "--")
            {
                endOfOptions = true;
                continue;
            }

            if (!token.StartsWith('-'))
            {
                if (LooksLikeUrl(token)) url = token;
                continue;
            }

            // Combined short flags (-sL, -fsSLk) and attached values (-XPOST, -d{..}): walk the
            // chars; a value-taking short swallows the remainder of the token as its value.
            if (token.Length > 2 && token[1] != '-')
            {
                for (var pos = 1; pos < token.Length; pos++)
                {
                    var shortFlag = string.Concat('-', token[pos]);
                    if (ShortFlagTakesValue(token[pos]))
                    {
                        var rest = pos + 1 < token.Length ? token[(pos + 1)..] : null;
                        if (!TryApplyFlag(shortFlag, rest ?? TakeValue(null)))
                        {
                            warnings.Add($"Ignored cURL flag: {shortFlag}");
                        }
                        break;
                    }

                    if (!TryApplyFlag(shortFlag, null))
                    {
                        warnings.Add($"Ignored cURL flag: {shortFlag}");
                    }
                }

                if (error is not null) return (null, error);
                continue;
            }

            var flag = token;
            string? inlineValue = null;
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var separator = token.IndexOf('=', StringComparison.Ordinal);
                if (separator >= 2)
                {
                    flag = token[..separator];
                    inlineValue = token[(separator + 1)..];
                }
            }

            if (TryApplyFlag(flag, inlineValue))
            {
                if (error is not null) return (null, error);
                continue;
            }

            warnings.Add($"Ignored cURL flag: {flag}");
            // An unrecognized flag may take a value — swallow the next token only when it can't
            // be anything else (never another flag, never the request URL).
            if (inlineValue is null
                && index + 1 < tokens.Count
                && !tokens[index + 1].StartsWith('-')
                && !LooksLikeUrl(tokens[index + 1]))
            {
                index++;
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return (null, "Could not find a URL in the cURL command.");
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

        if (formFields.Count > 0)
        {
            request.Body.Mode = RequestBodyMode.FormData;
            request.Body.FormData = formFields;
        }
        else if (bodyParts.Count > 0)
        {
            request.Body.RawContent = string.Join("&", bodyParts);
            request.Body.Mode = LooksLikeJson(request.Body.RawContent) ? RequestBodyMode.Json : RequestBodyMode.Text;
        }

        if (basicUser is not null)
        {
            // Split on the first ':' — curl's `-u user:pass` contract. The password lands in
            // CredentialKey, which the auth builder resolves verbatim (the legacy literal fallback)
            // when it isn't a credential-store key.
            var separator = basicUser.IndexOf(':', StringComparison.Ordinal);
            request.Auth = new AuthConfig
            {
                Type = AuthType.Basic,
                BasicUsername = separator >= 0 ? basicUser[..separator] : basicUser,
                CredentialKey = separator >= 0 ? basicUser[(separator + 1)..] : string.Empty,
            };
        }

        return (request, null);
    }

    // Flags the importer understands but cannot honor consume their value silently so the
    // value never masquerades as a URL; anything not listed warns instead.
    private static readonly HashSet<string> IgnorableValueFlags = new(StringComparer.Ordinal)
    {
        "-x", "--proxy", "-U", "--proxy-user", "-o", "--output",
        "-m", "--max-time", "--connect-timeout", "--expect100-timeout", "--keepalive-time",
        "--retry", "--retry-delay", "--retry-max-time", "--max-redirs",
        "--resolve", "--connect-to", "--cacert", "--capath", "--proxy-cacert",
        "--limit-rate", "-y", "--speed-time", "-Y", "--speed-limit", "--max-filesize",
        "--noproxy", "--unix-socket", "-w", "--write-out", "-D", "--dump-header",
        "-c", "--cookie-jar", "-C", "--continue-at", "-z", "--time-cond",
        "-r", "--range", "--ciphers", "--curves", "--tls-max", "--tls13-ciphers",
        "--interface", "--local-port", "-t", "--telnet-option", "-P", "--ftp-port", "-Q", "--quote",
    };

    private static readonly HashSet<string> IgnorableFlags = new(StringComparer.Ordinal)
    {
        "-s", "--silent", "-S", "--show-error", "-v", "--verbose", "-i", "--include",
        "-L", "--location", "-g", "--globoff", "-N", "--no-buffer", "--no-keepalive",
        "--compressed", "-4", "--ipv4", "-6", "--ipv6",
        "-0", "--http1.0", "--http1.1", "--http2", "--http2-prior-knowledge", "--http3",
        "-f", "--fail", "--fail-with-body", "-q", "--disable", "--raw", "--path-as-is",
        "--ssl", "--ssl-reqd", "--no-alpn", "--no-npn",
        "--tlsv1", "--tlsv1.0", "--tlsv1.1", "--tlsv1.2", "--tlsv1.3",
        "-p", "--proxytunnel", "--basic", "--anyauth", "-j", "--junk-session-cookies",
        "-O", "--remote-name", "-J", "--remote-header-name", "-R", "--remote-time",
        "-#", "--progress-bar", "--no-progress-meter", "-M", "--manual",
        "-l", "--list-only", "-B", "--use-ascii", "--disable-eprt", "--disable-epsv",
    };

    // Short flags that consume a value — when unpacking combined flags (-sLk) or attached
    // values (-XPOST), the token remainder after one of these is that flag's value. Flags
    // the table doesn't know (-T, -K, -E) still warn but get their value consumed correctly.
    private static bool ShortFlagTakesValue(char c) => c is
        'X' or 'H' or 'd' or 'F' or 'u' or 'b' or 'A' or 'e' or
        'x' or 'o' or 'm' or 'w' or 'c' or 'D' or 'C' or 'z' or 'r' or
        'E' or 'K' or 'T' or 'U' or 'y' or 'Y' or 't' or 'P' or 'Q';

    /// <summary>
    /// Splits the raw paste into one group per command. A segment starts a new command only
    /// when its first token is a standalone <c>curl</c>; every other segment continues the
    /// previous one (forgiving multi-line pastes without continuation characters). When no
    /// <c>curl</c> token appears at all, the whole paste is a single command.
    /// </summary>
    private static List<string> SplitCommandGroups(string command, List<string> warnings)
    {
        var segments = SplitSegments(StripLineContinuations(command))
            .Select(static segment => segment.Trim())
            .Where(static segment => segment.Length > 0)
            .ToList();
        if (segments.Count == 0)
        {
            return [];
        }

        var anyCurl = segments.Any(StartsWithCurlToken);
        var groups = new List<string>();
        foreach (var segment in segments)
        {
            if (anyCurl && StartsWithCurlToken(segment))
            {
                groups.Add(segment);
            }
            else if (groups.Count > 0)
            {
                groups[^1] = $"{groups[^1]} {segment}";
            }
            else if (anyCurl)
            {
                // Shell noise ahead of the first curl (echo, comments) — dropped, but told.
                warnings.Add($"Ignored input that is not a cURL command: {TruncateForWarning(segment)}");
            }
            else
            {
                groups.Add(segment);
            }
        }

        return groups;
    }

    private static bool StartsWithCurlToken(string segment)
    {
        var firstSpace = segment.IndexOf(' ', StringComparison.Ordinal);
        var firstTab = segment.IndexOf('\t', StringComparison.Ordinal);
        var end = (firstSpace, firstTab) switch
        {
            (< 0, < 0) => segment.Length,
            (< 0, var tab) => tab,
            (var space, < 0) => space,
            (var space, var tab) => Math.Min(space, tab),
        };
        return IsCurlToken(segment[..end]);
    }

    private static bool IsCurlToken(string token) =>
        token.Equals("curl", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("curl.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Quote-aware split on command separators: newlines, <c>;</c>, and <c>&&</c>. A lone
    /// <c>&amp;</c> stays (URLs carry it), and nothing inside quotes splits.
    /// </summary>
    private static List<string> SplitSegments(string command)
    {
        var segments = new List<string>();
        var builder = new StringBuilder();
        var quote = '\0';
        var escape = false;

        for (var i = 0; i < command.Length; i++)
        {
            var character = command[i];
            if (escape)
            {
                builder.Append(character);
                escape = false;
                continue;
            }

            if (character == '\\' && quote != '\'')
            {
                builder.Append(character);
                escape = true;
                continue;
            }

            if ((character == '\'' || character == '"') && quote == '\0')
            {
                quote = character;
                builder.Append(character);
                continue;
            }

            if (character == quote)
            {
                quote = '\0';
                builder.Append(character);
                continue;
            }

            if (quote == '\0')
            {
                if (character == '\n' || character == '\r' || character == ';')
                {
                    FlushSegment(segments, builder);
                    continue;
                }

                if (character == '&' && i + 1 < command.Length && command[i + 1] == '&')
                {
                    FlushSegment(segments, builder);
                    i++; // consume the second '&'
                    continue;
                }
            }

            builder.Append(character);
        }

        FlushSegment(segments, builder);
        return segments;
    }

    private static void FlushSegment(List<string> segments, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        segments.Add(builder.ToString());
        builder.Clear();
    }

    /// <summary>
    /// Joins shell line continuations before tokenizing: <c>\</c>-newline (bash, also inside
    /// double quotes) and <c>^</c>-newline (cmd.exe, never inside quotes — <c>^</c> is not a
    /// cmd escape inside a quoted string, so stripping it there would corrupt the value).
    /// </summary>
    private static string StripLineContinuations(string command)
    {
        var builder = new StringBuilder(command.Length);
        var quote = '\0';

        for (var i = 0; i < command.Length; i++)
        {
            var character = command[i];

            if (character == '\\' && quote != '\'')
            {
                if (i + 1 < command.Length && IsNewline(command[i + 1]))
                {
                    i = SkipNewline(command, i + 1);
                    continue;
                }

                // Keep the escape verbatim for the tokenizer (it consumes '\X' as literal X);
                // copying both chars also keeps quote tracking honest for sequences like \".
                builder.Append(character);
                if (i + 1 < command.Length)
                {
                    builder.Append(command[++i]);
                }
                continue;
            }

            if (character == '^' && quote == '\0'
                && i + 1 < command.Length && IsNewline(command[i + 1]))
            {
                i = SkipNewline(command, i + 1);
                continue;
            }

            if ((character == '\'' || character == '"') && quote == '\0')
            {
                quote = character;
                builder.Append(character);
                continue;
            }

            if (character == quote)
            {
                quote = '\0';
                builder.Append(character);
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool IsNewline(char character) => character is '\n' or '\r';

    /// <summary>Returns the index of the last character of the newline starting at <paramref name="index"/>.</summary>
    private static int SkipNewline(string value, int index) =>
        value[index] == '\r' && index + 1 < value.Length && value[index + 1] == '\n'
            ? index + 1
            : index;

    /// <summary><c>--next</c> splits one curl invocation into independent request sections.</summary>
    private static IEnumerable<List<string>> SplitOnNextSeparator(List<string> tokens)
    {
        var current = new List<string>();
        foreach (var token in tokens)
        {
            if (token == "--next")
            {
                if (current.Count > 0)
                {
                    yield return current;
                    current = [];
                }
                continue;
            }

            current.Add(token);
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    private static void AddFormField(List<FormDataField> fields, string field, bool literal, List<string> warnings)
    {
        var separator = field.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            warnings.Add($"Ignored -F/--form field without a 'name=value' shape: {TruncateForWarning(field)}");
            return;
        }

        var value = field[(separator + 1)..];
        // curl: @path sends a real file part, <path sends file contents; --form-string never interprets either.
        var isFile = !literal
            && (value.StartsWith('@') || value.StartsWith('<'));
        fields.Add(new FormDataField
        {
            Key = field[..separator],
            Value = isFile ? value[1..] : value,
            IsEnabled = true,
            IsFile = isFile,
        });
    }

    /// <summary>Sets a header value, replacing an existing header of the same name (last wins).</summary>
    private static void SetHeader(List<KeyValuePair<string>> headers, string key, string value)
    {
        var existing = headers.LastOrDefault(h => h.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Value = value;
            return;
        }

        headers.Add(new KeyValuePair<string> { Key = key, Value = value, IsEnabled = true });
    }

    /// <summary>Appends " (2)", " (3)", … to names that repeat within one imported batch.</summary>
    private static void DeduplicateRequestNames(List<HttpRequestEntry> requests)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var request in requests)
        {
            var baseName = request.Name;
            var name = baseName;
            var suffix = 2;
            while (!seen.Add(name))
            {
                name = $"{baseName} ({suffix++})";
            }

            request.Name = name;
        }
    }

    private static string TruncateForWarning(string value) =>
        value.Length <= 80 ? value : string.Concat(value.AsSpan(0, 80), "…");

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

        var resolved = await substitution.BuildScopeAsync(collection.Variables, [activeEnvironment], cancellationToken).ConfigureAwait(false);
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
