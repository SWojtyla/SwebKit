using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services.AccessProbes;

/// <summary>
/// Storage access probe: list containers on each configured account. The storage pool is not
/// demo-aware, so the adapter resolves the demo client itself in demo mode — matching the
/// <c>CreateClient</c> pattern in <c>StorageEndpoints</c>.
/// </summary>
internal static class StorageAccessProbes
{
    public const string Area = "Storage";
    public const string CapabilityBlobs = "storage.blobs";

    public static IEnumerable<AccessProbeSpec> Build(
        IReadOnlyList<StorageConfig> accounts,
        IStorageConnectionPool pool,
        DemoModeService demo)
    {
        foreach (var account in accounts)
        {
            var label = string.IsNullOrWhiteSpace(account.DisplayName) ? account.AccountName : account.DisplayName;
            var authMode = account.UseAad ? null : "connectionString";

            yield return new AccessProbeSpec(Area, account.Id, CapabilityBlobs, label, account.ResourceId, authMode,
                async ct =>
                {
                    var client = demo.IsDemoMode ? demo.GetStorageClient() : pool.GetOrCreate(account);
                    await client.ListContainersAsync(ct).ConfigureAwait(false);
                    return ProbeOutcome.Ok;
                });
        }
    }
}
