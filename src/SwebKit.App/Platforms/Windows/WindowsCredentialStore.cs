using SwebKit.Core.Abstractions;
using Windows.Security.Credentials;

namespace SwebKit.App.Platforms.Windows;

public class WindowsCredentialStore : ICredentialStore
{
    private const string ResourcePrefix = "SwebKit:";

    public void Save(string key, string secret)
    {
        var vault = new PasswordVault();
        try { vault.Remove(vault.Retrieve(ResourcePrefix + key, key)); } catch { }
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
        catch
        {
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
        catch { }
    }

    public IReadOnlyList<string> ListKeys(string prefix = "")
    {
        try
        {
            var vault = new PasswordVault();
            var all = vault.RetrieveAll();
            return all
                .Where(c => c.Resource.StartsWith(ResourcePrefix) && c.UserName.StartsWith(prefix))
                .Select(c => c.UserName)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
