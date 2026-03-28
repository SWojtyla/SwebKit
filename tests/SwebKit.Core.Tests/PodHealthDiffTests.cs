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
        string? status = null) => new()
        {
            Name = name,
            Namespace = TestNs,
            Phase = phase,
            Status = status ?? phase,
            ReadyContainers = ready,
            TotalContainers = total,
            RestartCount = restarts,
        };

    private static PodSnapshot Snap(
        string phase,
        int ready = 1,
        int total = 1,
        int restarts = 0) => new(phase, ready, total, restarts);

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
}
