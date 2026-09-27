using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services.AccessProbes;

/// <summary>
/// Redis access probe: connect via the pooled client and run a single-key SCAN. The probe is
/// time-boxed by the caller — clients are created with <c>AbortOnConnectFail=false</c>, so an
/// unreachable cache would otherwise block the report.
/// </summary>
internal static class RedisAccessProbes
{
    public const string Area = "Redis";
    public const string CapabilityData = "redis.data";

    public static IEnumerable<AccessProbeSpec> Build(
        IReadOnlyList<RedisCacheEntry> caches,
        IRedisConnectionPool pool)
    {
        foreach (var cache in caches)
        {
            var label = string.IsNullOrWhiteSpace(cache.DisplayName) ? cache.Id : cache.DisplayName;
            // Entra caches authenticate as the signed-in identity; access-key/connection-string
            // caches get no ARM scope or az artifacts.
            var authMode = cache.UseAad ? null : "connectionString";

            yield return new AccessProbeSpec(Area, cache.Id, CapabilityData, label, null, authMode,
                async ct =>
                {
                    var client = await pool.GetOrCreateAsync(cache, ct).ConfigureAwait(false);
                    await client.ScanKeysAsync("*", cursor: 0, pageSize: 1, ct).ConfigureAwait(false);
                    return ProbeOutcome.Ok;
                });
        }
    }
}
