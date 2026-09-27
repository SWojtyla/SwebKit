using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Security;

namespace SwebKit.Sidecar.Services.AccessProbes;

/// <summary>
/// Service Bus access probes. Entity listing needs the Manage claim while peeking needs
/// Data Receiver — <c>servicebus.manage</c> and <c>servicebus.peek</c> are deliberately separate
/// rows so a data-plane-only grant doesn't read as a false deny on the management plane.
/// <c>servicebus.send</c> can't be probed without mutating the entity, so its row reports only
/// denials observed by the agent/endpoint path.
/// </summary>
internal static class ServiceBusAccessProbes
{
    public const string Area = "ServiceBus";
    public const string CapabilityManage = "servicebus.manage";
    public const string CapabilityPeek = "servicebus.peek";
    public const string CapabilitySend = "servicebus.send";

    /// <param name="knownDenial">Looks up a denial already recorded/probed for
    /// (area, connectionKey, capability) — used by the un-probeable send row.</param>
    public static IEnumerable<AccessProbeSpec> Build(
        IReadOnlyList<ServiceBusNamespace> namespaces,
        IReadOnlyList<SbEntityLink> entityLinks,
        IServiceBusConnectionPool pool,
        Func<string, string, string, AccessDenial?> knownDenial)
    {
        foreach (var ns in namespaces)
        {
            var connectionKey = ns.Id.ToString();
            var label = string.IsNullOrWhiteSpace(ns.Alias) ? ns.FullyQualifiedNamespace : ns.Alias;
            // Connection-string namespaces never authenticate as the signed-in identity — the row
            // is marked so no ARM-scope/az artifacts are offered for it.
            var authMode = ns.AuthMode == SbAuthMode.ConnectionString ? "connectionString" : null;

            yield return new AccessProbeSpec(Area, connectionKey, CapabilityManage, label, ns.ResourceId, authMode,
                async ct =>
                {
                    await pool.GetOrCreate(ns).GetNamespaceInfoAsync(ct).ConfigureAwait(false);
                    return ProbeOutcome.Ok;
                });

            yield return new AccessProbeSpec(Area, connectionKey, CapabilityPeek, label, ns.ResourceId, authMode,
                async ct =>
                {
                    var client = pool.GetOrCreate(ns);
                    // Peek needs an entity path — a linked entity first, else the first queue.
                    // Topics aren't usable paths (receivers bind to subscriptions); a dead-end
                    // link surfaces as Unknown rather than a false deny.
                    var path = entityLinks
                        .FirstOrDefault(l => l.NamespaceId == ns.Id && !string.IsNullOrWhiteSpace(l.EntityPath))
                        ?.EntityPath;
                    if (path is null)
                    {
                        var queues = await client.ListQueuesAsync(ct).ConfigureAwait(false);
                        path = queues.Count > 0 ? queues[0].EntityPath : null;
                    }
                    if (path is null)
                        return ProbeOutcome.Unknown("No queue, topic or subscription exists to peek.");
                    await client.PeekMessagesAsync(path, 1, ct).ConfigureAwait(false);
                    return ProbeOutcome.Ok;
                });

            yield return new AccessProbeSpec(Area, connectionKey, CapabilitySend, label, ns.ResourceId, authMode,
                ct =>
                {
                    var observed = knownDenial(Area, connectionKey, CapabilitySend);
                    return Task.FromResult(observed is not null
                        ? ProbeOutcome.Denied(observed)
                        : ProbeOutcome.Unknown(
                            "Not probed — verifying send rights would write a real message. " +
                            "This row reports a denial if one is observed."));
                });
        }
    }
}
