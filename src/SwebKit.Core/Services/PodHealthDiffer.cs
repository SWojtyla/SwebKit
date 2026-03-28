using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// A point-in-time snapshot of a single pod's observed health state.
/// </summary>
public sealed record PodSnapshot(
    string Phase,
    int ReadyContainers,
    int TotalContainers,
    int RestartCount);

/// <summary>
/// A single detected health transition for one pod within a namespace.
/// </summary>
public sealed record PodDiffResult(
    string PodName,
    PodHealthEventType EventType,
    string PreviousPhase,
    string CurrentPhase,
    int RestartCount,
    string? Message);

/// <summary>
/// Stateless, pure diff function — compares a previous snapshot against a current pod list
/// and returns the set of health events that should be emitted, filtered by active cooldowns.
/// </summary>
public static class PodHealthDiffer
{
    /// <summary>
    /// Produces the cooldown dictionary key for a given (namespace, pod, event type) triple.
    /// </summary>
    public static string CooldownKey(string ns, string podName, PodHealthEventType eventType)
        => $"{ns}/{podName}/{eventType}";

    /// <summary>
    /// Computes health transitions for <paramref name="ns"/>.
    /// </summary>
    /// <param name="existing">
    /// Previous snapshot keyed by pod name.
    /// Pass <see langword="null"/> on first observation — returns an empty list (baseline tick).
    /// </param>
    /// <param name="activeCooldowns">
    /// Keys are <see cref="CooldownKey"/> values; values are expiry times.
    /// Transitions whose key is still within the cooldown window are suppressed.
    /// </param>
    /// <param name="now">Reference time used to evaluate cooldown expiry.</param>
    public static IReadOnlyList<PodDiffResult> Diff(
        string ns,
        IReadOnlyDictionary<string, PodSnapshot>? existing,
        IReadOnlyList<PodInfo> current,
        IReadOnlyDictionary<string, DateTimeOffset> activeCooldowns,
        DateTimeOffset now)
    {
        // First observation — record as baseline, emit nothing.
        if (existing is null)
            return [];

        var results = new List<PodDiffResult>();
        var currentByName = current.ToDictionary(p => p.Name);

        // Detect pods that disappeared entirely.
        foreach (var (podName, prev) in existing)
        {
            if (currentByName.ContainsKey(podName))
                continue;

            var key = CooldownKey(ns, podName, PodHealthEventType.PodTerminated);
            if (!IsInCooldown(activeCooldowns, key, now))
            {
                results.Add(new PodDiffResult(
                    podName, PodHealthEventType.PodTerminated,
                    prev.Phase, string.Empty, prev.RestartCount, null));
            }
        }

        // Detect transitions on pods that exist in both snapshots.
        foreach (var pod in current)
        {
            if (!existing.TryGetValue(pod.Name, out var prev))
                continue; // New pod this tick — will be baselined in the snapshot update.

            PodDiffResult? result = DetectTransition(pod, prev);

            if (result is not null)
            {
                var key = CooldownKey(ns, pod.Name, result.EventType);
                if (!IsInCooldown(activeCooldowns, key, now))
                    results.Add(result);
            }
        }

        return results;
    }

    private static PodDiffResult? DetectTransition(PodInfo pod, PodSnapshot prev)
    {
        // CrashLoopBackOff takes priority — covers Status field and restart increase.
        bool isCrashLoop =
            pod.Status.Contains("CrashLoopBackOff", StringComparison.OrdinalIgnoreCase) ||
            pod.RestartCount > prev.RestartCount;

        if (isCrashLoop)
        {
            var message = pod.Status.Contains("CrashLoopBackOff", StringComparison.OrdinalIgnoreCase)
                ? "CrashLoopBackOff"
                : $"Restarts: {prev.RestartCount} \u2192 {pod.RestartCount}";

            return new PodDiffResult(
                pod.Name, PodHealthEventType.PodCrashLoop,
                prev.Phase, pod.Phase, pod.RestartCount, message);
        }

        if (prev.Phase == "Running" && pod.Phase == "Failed")
        {
            return new PodDiffResult(
                pod.Name, PodHealthEventType.PodFailed,
                prev.Phase, pod.Phase, pod.RestartCount, null);
        }

        if (prev.Phase == "Running" && pod.Phase == "Unknown")
        {
            return new PodDiffResult(
                pod.Name, PodHealthEventType.PodUnknown,
                prev.Phase, pod.Phase, pod.RestartCount, null);
        }

        // ContainerNotReady — only checked when no more-specific event applies.
        bool wasFullyReady = prev.TotalContainers > 0 && prev.ReadyContainers == prev.TotalContainers;
        bool isNowPartiallyReady = pod.ReadyContainers < pod.TotalContainers;

        if (wasFullyReady && isNowPartiallyReady)
        {
            return new PodDiffResult(
                pod.Name, PodHealthEventType.ContainerNotReady,
                prev.Phase, pod.Phase, pod.RestartCount,
                $"Ready: {pod.ReadyContainers}/{pod.TotalContainers}");
        }

        return null;
    }

    private static bool IsInCooldown(
        IReadOnlyDictionary<string, DateTimeOffset> cooldowns,
        string key,
        DateTimeOffset now)
        => cooldowns.TryGetValue(key, out var exp) && now < exp;
}
