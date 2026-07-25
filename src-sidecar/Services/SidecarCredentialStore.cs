using System.Collections.Concurrent;
using SwebKit.Core.Abstractions;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Simple in-memory credential store for the sidecar. Secrets are kept in process memory and are
/// lost when the sidecar restarts. This is acceptable for the local dev MVP; a secure-backed
/// implementation can replace it later.
/// </summary>
public sealed class SidecarCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new();

    public void Save(string key, string secret)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _secrets[key] = secret;
    }

    public string? Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        _secrets.TryGetValue(key, out var value);
        return value;
    }

    public void Delete(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _secrets.TryRemove(key, out _);
    }

    public IReadOnlyList<string> ListKeys(string prefix = "")
    {
        if (string.IsNullOrEmpty(prefix))
            return _secrets.Keys.ToList();

        return _secrets.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
