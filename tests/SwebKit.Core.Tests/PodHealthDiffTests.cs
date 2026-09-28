using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public class PodHealthDiffTests
{
    private const string TestNs = "default";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCooldowns =
        new Dictionary<string, DateTimeOffset>();

    private static PodInfo MakePod(
        string name,
        string phase,
        int ready = 1,
        int total = 1,
        int restarts = 0,
        string? status = null,
        string? ownerKind = null) => new()
        {
            Name = name,
            Namespace = TestNs,
            Phase = phase,
            Status = status ?? phase,
            ReadyContainers = ready,
            TotalContainers = total,
            RestartCount = restarts,
            OwnerKind = ownerKind,
        };

    private static PodSnapshot Snap(
        string phase,
        int ready = 1,
        int total = 1,
        int restarts = 0,
        string? ownerKind = null) => new(phase, ready, total, restarts, ownerKind);

    // ── Test 1 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_NullExisting_ReturnsNoEvents_BaselineRule()
    {
        // A pod already in "Failed" state at first observation must NOT emit an event.
        var current = new List<PodInfo> { MakePod("pod-a", "Failed") };

        var result = PodHealthDiffer.Diff(TestNs, existing: null, current, NoCooldowns, Now);

        Assert.Empty(result);
    }

    // ── Test 2 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_RunningToFailed_EmitsPodFailed()
    {
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["pod-b"] = Snap("Running")
        };
        var current = new List<PodInfo> { MakePod("pod-b", "Failed") };

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Single(result);
        Assert.Equal(PodHealthEventType.PodFailed, result[0].EventType);
        Assert.Equal("pod-b", result[0].PodName);
        Assert.Equal("Running", result[0].PreviousPhase);
        Assert.Equal("Failed", result[0].CurrentPhase);
    }

    // ── Test 3 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_RunningToUnknown_EmitsPodUnknown()
    {
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["pod-c"] = Snap("Running")
        };
        var current = new List<PodInfo> { MakePod("pod-c", "Unknown") };

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Single(result);
        Assert.Equal(PodHealthEventType.PodUnknown, result[0].EventType);
    }

    // ── Test 4 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_RestartCountIncrease_EmitsPodCrashLoop()
    {
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["pod-d"] = Snap("Running", restarts: 2)
        };
        // Same phase, same status — only restart count jumped.
        var current = new List<PodInfo> { MakePod("pod-d", "Running", restarts: 5, status: "Running") };

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Single(result);
        Assert.Equal(PodHealthEventType.PodCrashLoop, result[0].EventType);
        Assert.Equal(5, result[0].RestartCount);
    }

    // ── Test 5 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_FullyReadyToPartiallyReady_EmitsContainerNotReady()
    {
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["pod-e"] = Snap("Running", ready: 2, total: 2)
        };
        // Same restarts, same phase, NOT CrashLoop — only container readiness drops.
        var current = new List<PodInfo> { MakePod("pod-e", "Running", ready: 1, total: 2, status: "Running") };

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Single(result);
        Assert.Equal(PodHealthEventType.ContainerNotReady, result[0].EventType);
        Assert.Contains("1/2", result[0].Message);
    }

    // ── Test 6 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_PodDisappeared_EmitsPodTerminated()
    {
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["pod-f"] = Snap("Running")
        };
        // Return no pods — pod-f has been deleted.
        var current = new List<PodInfo>();

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Single(result);
        Assert.Equal(PodHealthEventType.PodTerminated, result[0].EventType);
        Assert.Equal("pod-f", result[0].PodName);
        Assert.Equal("Running", result[0].PreviousPhase);
    }

    // ── Test 6b ──────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_PendingToFailed_EmitsPodFailed()
    {
        // A pod that never reached Running (init crash, bad image) is just as much a
        // failure as Running → Failed — this is the demo client's search-indexer scenario.
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["pod-f0"] = Snap("Pending", ready: 0)
        };
        var current = new List<PodInfo> { MakePod("pod-f0", "Failed", ready: 0, status: "Error") };

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Single(result);
        Assert.Equal(PodHealthEventType.PodFailed, result[0].EventType);
        Assert.Equal("Pending", result[0].PreviousPhase);
        Assert.Equal("Failed", result[0].CurrentPhase);
    }

    // ── Test 6c ──────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_FailedStaysFailed_NoEvent()
    {
        // A pod still Failed on the next tick must not re-fire — cooldown is rule-level
        // and shouldn't be the only thing standing between the user and per-tick alerts.
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["pod-f1"] = Snap("Failed", ready: 0)
        };
        var current = new List<PodInfo> { MakePod("pod-f1", "Failed", ready: 0, status: "Error") };

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Empty(result);
    }

    // ── Test 7 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_WithActiveCooldown_SuppressesDuplicateEvent()
    {
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["pod-g"] = Snap("Running")
        };
        var current = new List<PodInfo> { MakePod("pod-g", "Failed") };

        // Cooldown for this exact (ns, pod, eventType) triple is still active.
        var cooldownKey = PodHealthDiffer.CooldownKey(TestNs, "pod-g", PodHealthEventType.PodFailed);
        var cooldowns = new Dictionary<string, DateTimeOffset>
        {
            [cooldownKey] = Now.AddMinutes(10) // expires in the future
        };

        var result = PodHealthDiffer.Diff(TestNs, existing, current, cooldowns, Now);

        Assert.Empty(result);
    }

    // ── Test 8 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_ExpiredCooldown_AllowsEvent()
    {
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["pod-h"] = Snap("Running")
        };
        var current = new List<PodInfo> { MakePod("pod-h", "Failed") };

        // Cooldown entry exists but is expired.
        var cooldownKey = PodHealthDiffer.CooldownKey(TestNs, "pod-h", PodHealthEventType.PodFailed);
        var cooldowns = new Dictionary<string, DateTimeOffset>
        {
            [cooldownKey] = Now.AddMinutes(-1) // expired one minute ago
        };

        var result = PodHealthDiffer.Diff(TestNs, existing, current, cooldowns, Now);

        Assert.Single(result);
        Assert.Equal(PodHealthEventType.PodFailed, result[0].EventType);
    }

    // ── Test 9 ───────────────────────────────────────────────────────────────
    // False-positive class: normal Job/CronJob lifecycle cleanup must not emit
    // PodTerminated (the "Pods down" false positive on sign-schedule's CronJob).

    [Fact]
    public void Diff_JobOwnedPodDisappeared_NoEvent()
    {
        // The pod was Running at the last tick — the Job completed and the CronJob
        // controller deleted the pod before the next tick observed Succeeded.
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["batch-123-abcd"] = Snap("Running", ownerKind: "Job")
        };
        var current = new List<PodInfo>();

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Empty(result);
    }

    // ── Test 10 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_SucceededPodDisappeared_NoEvent()
    {
        // A finished pod being garbage-collected (TTL/history limit) is expected.
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["job-pod"] = Snap("Succeeded", ready: 0, ownerKind: "Job")
        };
        var current = new List<PodInfo>();

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Empty(result);
    }

    // ── Test 11 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_FailedPodDisappeared_NoEvent()
    {
        // Entry into Failed already emitted PodFailed — its later removal is cleanup.
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["pod-dead"] = Snap("Failed", ready: 0)
        };
        var current = new List<PodInfo>();

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Empty(result);
    }

    // ── Test 12 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_ReplicaSetOwnedRunningPodDisappeared_EmitsPodTerminated()
    {
        // Scope pin: only Job-owned / terminal pods are excused — a healthy
        // ReplicaSet pod vanishing mid-life still alerts.
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["api-7f8-xyz"] = Snap("Running", ownerKind: "ReplicaSet")
        };
        var current = new List<PodInfo>();

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Single(result);
        Assert.Equal(PodHealthEventType.PodTerminated, result[0].EventType);
    }

    // ── Test 13 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_RunningToSucceeded_NoEvent()
    {
        // Completing containers flip Ready to 0/N — without the Succeeded guard this
        // read as ContainerNotReady, firing "Pods down" on every successful Job run.
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["batch-124-abcd"] = Snap("Running", ownerKind: "Job")
        };
        var current = new List<PodInfo>
        {
            MakePod("batch-124-abcd", "Succeeded", ready: 0, status: "Completed", ownerKind: "Job")
        };

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Empty(result);
    }

    // ── Test 14 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Diff_JobOwnedRunningToFailed_StillEmitsPodFailed()
    {
        // The suppression is only for termination noise — a Job pod that actually
        // fails while alive must still alert.
        var existing = new Dictionary<string, PodSnapshot>
        {
            ["batch-125-abcd"] = Snap("Running", ownerKind: "Job")
        };
        var current = new List<PodInfo>
        {
            MakePod("batch-125-abcd", "Failed", ready: 0, status: "Error", ownerKind: "Job")
        };

        var result = PodHealthDiffer.Diff(TestNs, existing, current, NoCooldowns, Now);

        Assert.Single(result);
        Assert.Equal(PodHealthEventType.PodFailed, result[0].EventType);
    }
}
