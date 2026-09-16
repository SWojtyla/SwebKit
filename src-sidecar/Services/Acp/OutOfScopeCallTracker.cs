using System.Collections.Concurrent;

namespace SwebKit.Sidecar.Services.Acp;

/// <summary>
/// Counts ACP agents' calls to known-but-out-of-scope MCP tools, keyed by the turn's allowlist
/// (the decoded <c>?tools=</c> value — the same string the bridge reads back off the request
/// query). agent-correlation Module 3: <see cref="AcpAgentModelClient"/> snapshots the count
/// before prompting and compares it after the turn — growth means the agent reached for a tool
/// the scope fence hid, so the result carries <c>SuggestedScope = "workspace"</c> and the UI
/// offers a one-click scope-widened retry.
///
/// In-memory only, keyed on the allowlist rather than the session on purpose: a stale count can
/// at worst re-suggest widening scope on an unrelated later turn, never widen it silently.
/// </summary>
public sealed class OutOfScopeCallTracker
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>Null/empty key means an unfiltered bridge request (no <c>?tools=</c> allowlist →
    /// nothing can be out of scope) — recorded as a no-op so callers needn't branch.</summary>
    public void RecordOutOfScopeCall(string? allowlistKey)
    {
        if (string.IsNullOrEmpty(allowlistKey))
            return;

        _counts.AddOrUpdate(allowlistKey, 1, static (_, count) => count + 1);
    }

    public int CountFor(string? allowlistKey) =>
        !string.IsNullOrEmpty(allowlistKey) && _counts.TryGetValue(allowlistKey, out var count) ? count : 0;
}
