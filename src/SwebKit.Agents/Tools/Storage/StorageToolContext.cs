using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Agents.Tools.Storage;

/// <summary>
/// Shared storage-account resolution logic for the Storage agent tools — mirrors
/// <c>SwebKit.Agents.Tools.Redis.RedisToolContext</c>. No "active account" concept exists for
/// Storage (unlike Redis's <c>ActiveCacheId</c>), so the fallback is simply "the requested account,
/// or the first configured one."
/// </summary>
internal static class StorageToolContext
{
    public readonly record struct Resolution(IStorageClient? Client, StorageConfig? Account, string? Error);

    public static Resolution Resolve(
        AppStateService appState,
        ProfileRepository profiles,
        IStorageClientFactory factory,
        string? requestedAccountId)
    {
        if (appState.UseDemoData)
        {
            var demoClient = new DemoStorageClient();
            return new Resolution(demoClient, demoClient.Config, null);
        }

        var accounts = profiles.GetProfileData().Config.StorageAccounts;
        if (accounts.Count == 0)
            return new Resolution(null, null, "Storage is not configured. Add an account in settings.");

        // agent-correlation Module 5 widened the match beyond Id: workspace-topology nodes key on
        // the account *name* (e.g. "mystorageacct"), and the model is more likely to pass either
        // the name or the display label it saw in the system prompt than the opaque id.
        var account = requestedAccountId is not null
            ? accounts.FirstOrDefault(a =>
                a.Id == requestedAccountId ||
                string.Equals(a.AccountName, requestedAccountId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.DisplayName, requestedAccountId, StringComparison.OrdinalIgnoreCase))
            : accounts[0];

        if (account is null)
            return new Resolution(null, null, $"Storage account '{requestedAccountId}' not found.");

        return new Resolution(factory.Create(account), account, null);
    }
}
