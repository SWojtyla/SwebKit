using System.Collections.Concurrent;
using KeySharp;
using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// OS-backed credential store for the sidecar, using the platform keychain/Secret Service where
/// available. Falls back to an in-memory dictionary when the OS store cannot be reached (for example,
/// in headless CI environments with no running Secret Service).
/// </summary>
public sealed class SidecarCredentialStore(ILogger<SidecarCredentialStore>? logger) : ICredentialStore
{
    private const string PackageName = "SwebKit";
    private const string ServiceName = "SwebKit";
    private readonly ConcurrentDictionary<string, string> _fallback = new();

    public void Save(string key, string secret)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        // Always keep a session fallback so auth keeps working even if the OS store is unavailable.
        _fallback[key] = secret;

        try
        {
            Keyring.SetPassword(PackageName, ServiceName, key, secret);
        }
        catch (KeyringException ex)
        {
            logger?.LogWarning(ex, "OS credential store unavailable; secret for key {Key} retained in session memory only.", key);
        }
    }

    public string? Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        try
        {
            return Keyring.GetPassword(PackageName, ServiceName, key);
        }
        catch (KeyringException ex) when (ex.Type == ErrorType.NotFound)
        {
            return null;
        }
        catch (KeyringException ex)
        {
            logger?.LogWarning(ex, "OS credential store unavailable; returning in-memory fallback for key {Key}.", key);
            return _fallback.TryGetValue(key, out var value) ? value : null;
        }
    }

    public void Delete(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        _fallback.TryRemove(key, out _);

        try
        {
            Keyring.DeletePassword(PackageName, ServiceName, key);
        }
        catch (KeyringException ex)
        {
            logger?.LogWarning(ex, "OS credential store delete failed for key {Key}; removed from session memory.", key);
        }
    }

    public IReadOnlyList<string> ListKeys(string prefix = "")
    {
        // keyring-dotnet does not expose enumeration; expose the in-memory fallback keys only.
        if (string.IsNullOrEmpty(prefix))
            return _fallback.Keys.ToList();

        return _fallback.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
