using System.Collections.Concurrent;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Ambient collector for RBAC access-denied notices raised while fanning a request out
/// across multiple namespaces (see <see cref="IAksClient"/>'s multi-namespace default
/// methods). A single namespace lacking permission for a resource must not discard the
/// data successfully retrieved for sibling namespaces the caller *does* have access to —
/// so <c>FanOutNamespacesAsync</c> swallows per-namespace <see cref="AksAccessDeniedException"/>
/// and records a note here instead of letting it fail the whole batch. Callers (typically a
/// page load routine) wrap the load in a <see cref="AksAccessDeniedScope"/> and read
/// <see cref="Denials"/> afterwards to surface a "limited permissions" banner.
/// </summary>
/// <remarks>
/// Uses <see cref="AsyncLocal{T}"/> so it flows correctly through the <c>await</c> chain of
/// concurrently fanned-out per-namespace tasks without any explicit parameter threading
/// through every multi-namespace overload in <see cref="IAksClient"/>.
/// </remarks>
public sealed class AksAccessDeniedScope : IDisposable
{
    private static readonly AsyncLocal<ConcurrentBag<(string ResourceKind, string Namespace)>?> Current = new();

    private readonly ConcurrentBag<(string ResourceKind, string Namespace)>? _previous;
    private readonly ConcurrentBag<(string ResourceKind, string Namespace)> _bag = [];

    public AksAccessDeniedScope()
    {
        _previous = Current.Value;
        Current.Value = _bag;
    }

    /// <summary>Records that access to <paramref name="resourceKind"/> in <paramref name="ns"/> was denied. No-op outside an active scope.</summary>
    public static void Record(string resourceKind, string ns) => Current.Value?.Add((resourceKind, ns));

    /// <summary>Distinct (resource kind, namespace) denial pairs recorded since this scope began, sorted for stable display.</summary>
    public IReadOnlyList<(string ResourceKind, string Namespace)> Denials =>
        _bag.Distinct()
            .OrderBy(d => d.ResourceKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Namespace, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void Dispose() => Current.Value = _previous;
}
