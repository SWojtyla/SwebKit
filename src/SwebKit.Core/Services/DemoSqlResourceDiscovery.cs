using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// Canned <see cref="ISqlResourceDiscovery"/> for demo mode — mirrors
/// <c>DemoObservabilityResourceDiscovery</c>'s role for App Insights. Yields the two demo
/// servers matching <c>DemoModeService</c>'s demo connections.
/// </summary>
public sealed class DemoSqlResourceDiscovery : ISqlResourceDiscovery
{
    public async IAsyncEnumerable<SqlDiscoveredServer> DiscoverServersAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();

        yield return new SqlDiscoveredServer
        {
            ServerFqdn = "orders-dev-sql.database.windows.net",
            Name = "orders-dev-sql",
            ResourceGroup = "rg-orders-dev",
            SubscriptionId = "00000000-0000-0000-0000-000000000001",
            SubscriptionName = "Demo Subscription",
            Location = "westeurope",
            Databases = ["orders"],
        };

        yield return new SqlDiscoveredServer
        {
            ServerFqdn = "payments-dev-sql.database.windows.net",
            Name = "payments-dev-sql",
            ResourceGroup = "rg-payments-dev",
            SubscriptionId = "00000000-0000-0000-0000-000000000001",
            SubscriptionName = "Demo Subscription",
            Location = "westeurope",
            Databases = ["payments", "orders-copy"],
        };
    }

    public void InvalidateCache() { }
}
