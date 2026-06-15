using System.Text.RegularExpressions;
using SwebKit.Core.Abstractions;

namespace SwebKit.Core.Services;

/// <summary>
/// Returns a per-token preview of resolved values for display in the UI.
/// Secrets are masked as <c>••••••••</c> so they are never shown in plain text.
/// </summary>
public sealed class VariablePreviewService : IVariablePreviewService
{
    private const string SecretMask = "••••••••";

    private static readonly Regex TokenPattern = new(
        @"\{\{([^{}]+?)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string?> Preview(
        string text,
        IReadOnlyDictionary<string, string?> scope)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{"))
            return new Dictionary<string, string?>();

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (Match match in TokenPattern.Matches(text))
        {
            var key = match.Groups[1].Value.Trim();
            if (result.ContainsKey(key)) continue;

            if (scope.TryGetValue(key, out var value))
            {
                // Mask values that look like secrets
                result[key] = IsLikelySecret(key) ? SecretMask : value;
            }
            else
            {
                result[key] = null; // unresolved
            }
        }

        return result;
    }

    private static bool IsLikelySecret(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower.Contains("secret") ||
               lower.Contains("password") ||
               lower.Contains("passwd") ||
               lower.Contains("token") ||
               lower.Contains("apikey") ||
               lower.Contains("api_key") ||
               lower.Contains("credential") ||
               lower.Contains("private");
    }
}
