using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Handles detection and masking of secret-looking variable keys.
/// </summary>
public sealed class FlowSecretsMasker
{
    private static readonly string[] SecretKeyPatterns = new[]
    {
        "secret", "token", "password", "passwd", "pwd", "key", "auth",
        "credential", "api", "bearer", "access", "refresh", "private"
    };

    /// <summary>
    /// Checks if a variable key looks like a secret (for masking in UI).
    /// </summary>
    public bool IsSecretLookingKey(string key)
    {
        var lowerKey = key.ToLowerInvariant();
        return SecretKeyPatterns.Any(pattern => lowerKey.Contains(pattern));
    }

    /// <summary>
    /// Masks secret-looking values in a dictionary.
    /// </summary>
    public Dictionary<string, string> MaskSecrets(Dictionary<string, string> values)
    {
        var masked = new Dictionary<string, string>();
        foreach (var kvp in values)
        {
            if (IsSecretLookingKey(kvp.Key))
            {
                masked[kvp.Key] = "***MASKED***";
            }
            else
            {
                masked[kvp.Key] = kvp.Value;
            }
        }
        return masked;
    }
}
