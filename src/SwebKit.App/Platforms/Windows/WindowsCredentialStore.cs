using Microsoft.Extensions.Logging;
using SwebKit.Core.Abstractions;
using Windows.Security.Credentials;

namespace SwebKit.App.Platforms.Windows;

public class WindowsCredentialStore(ILogger<WindowsCredentialStore>? logger = null) : ICredentialStore
{
    private const string ResourcePrefix = "SwebKit:";

    public void Save(string key, string secret)
    {
        var vault = new PasswordVault();
        try { vault.Remove(vault.Retrieve(ResourcePrefix + key, key)); }
        catch (Exception ex) { logger?.LogDebug(ex, "Credential store: no existing entry to remove for {Key}", key); }
        vault.Add(new PasswordCredential(ResourcePrefix + key, key, secret));
    }

    public string? Get(string key)
    {
        try
        {
            var vault = new PasswordVault();
            var cred = vault.Retrieve(ResourcePrefix + key, key);
            cred.RetrievePassword();
            return cred.Password;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Credential store: Get failed for {Key}", key);
            return null;
        }
    }

    public void Delete(string key)
    {
        try
        {
            var vault = new PasswordVault();
            vault.Remove(vault.Retrieve(ResourcePrefix + key, key));
        }
        catch (Exception ex) { logger?.LogDebug(ex, "Credential store: Delete failed for {Key}", key); }
    }

    public IReadOnlyList<string> ListKeys(string prefix = "")
    {
        try
        {
            var vault = new PasswordVault();
            var all = vault.RetrieveAll();
            return all
                .Where(c => c.Resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) && c.UserName.StartsWith(prefix, StringComparison.Ordinal))
                .Select(c => c.UserName)
                .ToList();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Credential store: ListKeys failed");
            return [];
        }
    }
}
