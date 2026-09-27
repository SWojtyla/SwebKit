using System.Text.Json;

namespace SwebKit.Agents.Tools;

/// <summary>
/// The dotted capability names the per-environment access report uses (see
/// docs/features/active/access-awareness-pipeline.md, Phase 2). These must match the strings the
/// sidecar's probe adapters emit — they are the join key between a probe/observed denial and the
/// registry's known-denial short-circuit.
/// </summary>
public static class AccessCapabilities
{
    public const string ServiceBusManage = "servicebus.manage";
    public const string ServiceBusPeek = "servicebus.peek";
    public const string ServiceBusSend = "servicebus.send";
    public const string SqlQuery = "sql.query";
    public const string SqlMetadata = "sql.metadata";
    public const string RedisData = "redis.data";
    public const string StorageBlobs = "storage.blobs";
    public const string KubernetesRead = "kubernetes.read";
    /// <summary>The report's single Observability row — queries and metrics both resolve to it
    /// (the area has exactly one probed capability).</summary>
    public const string ObservabilityLogs = "observability.logs";
}

/// <summary>
/// Opt-in contract for tools whose call targets a single configured connection and exercises one
/// capability on it (access-awareness Phase 2). When a tool implements this,
/// <see cref="AgentToolRegistry"/> can:
/// <list type="bullet">
///   <item>short-circuit the call before execution when
///   <c>IAccessReportService.TryGetKnownDenial</c> has a fresh denial for
///   (feature area, connection, capability) — the "don't retry 403s" behavior;</item>
///   <item>feed an exception classified as a denial back via <c>RecordObservedDenial</c> so the
///   access report flips red immediately for capabilities that can't be probed
///   (e.g. <c>servicebus.send</c>, <c>sql.query</c>).</item>
/// </list>
/// </summary>
public interface IAccessAwareTool : IAgentTool
{
    /// <summary>
    /// The capability this tool exercises — one of <see cref="AccessCapabilities"/>. A tool that
    /// mixes capabilities should NOT implement this interface: a denial on one leg would be
    /// recorded against the wrong report row.
    /// </summary>
    string Capability { get; }

    /// <summary>
    /// Resolves which configured connection this call targets, from the tool-call arguments —
    /// must return the same key the access report's probes use for that connection (e.g. the
    /// Service Bus namespace id, SQL connection id, cache id, storage account id). Resolution
    /// must be cheap (no client creation); return <see langword="null"/> when the target can't
    /// be determined — the call then runs normally.
    /// </summary>
    string? GetConnectionKey(JsonElement arguments);
}
