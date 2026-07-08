using System.Text.RegularExpressions;

namespace SwebKit.Core.Diagnostics;

/// <summary>
/// Best-effort, pattern-based secret redaction applied unconditionally to every log message,
/// exception, and scope value before it is persisted. See <c>docs/features/active/structured-file-logging/decisions.md</c> D3.
/// </summary>
public static class LogRedactor
{
    private const string RedactedPlaceholder = "***REDACTED***";

    private static readonly string[] DenylistKeyTerms =
    [
        "password", "secret", "token", "key", "connectionstring", "pat", "sas"
    ];

    // Azure Storage / Service Bus connection-string style secrets: AccountKey=..., SharedAccessKey=..., SharedAccessSignature=...
    private static readonly Regex ConnectionStringSecretRegex = new(
        @"(?<name>AccountKey|SharedAccessKey|SharedAccessSignature)\s*=\s*(?<value>[^;\s""']+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Authorization: Bearer <token>" - keep the "Bearer " prefix, redact the token itself.
    private static readonly Regex BearerTokenRegex = new(
        @"(?<prefix>Bearer\s+)(?<value>[A-Za-z0-9\-_\.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Generic "pat=", "token=", "key=", "secret=" assignments followed by a long token-shaped value.
    private static readonly Regex GenericSecretAssignmentRegex = new(
        @"\b(?<name>pat|token|key|secret)\s*=\s*(?<value>[A-Za-z0-9\-_]{20,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Masks secret-shaped substrings in free-form text (message, exception text). Safe for null/empty input.</summary>
    public static string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var redacted = ConnectionStringSecretRegex.Replace(text, m => $"{m.Groups["name"].Value}={RedactedPlaceholder}");
        redacted = BearerTokenRegex.Replace(redacted, m => $"{m.Groups["prefix"].Value}{RedactedPlaceholder}");
        redacted = GenericSecretAssignmentRegex.Replace(redacted, m => $"{m.Groups["name"].Value}={RedactedPlaceholder}");

        return redacted;
    }

    /// <summary>
    /// Redacts a flattened <c>BeginScope</c> value. If <paramref name="key"/> matches the secret-shaped
    /// key denylist (case-insensitive), the value is always fully replaced regardless of its shape.
    /// Otherwise the value still passes through the same pattern-based <see cref="Redact"/> logic.
    /// </summary>
    public static string? RedactScopeValue(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (!string.IsNullOrEmpty(key))
        {
            var normalizedKey = key.ToLowerInvariant();
            foreach (var term in DenylistKeyTerms)
            {
                if (normalizedKey.Contains(term, StringComparison.Ordinal))
                    return RedactedPlaceholder;
            }
        }

        return Redact(value);
    }
}
