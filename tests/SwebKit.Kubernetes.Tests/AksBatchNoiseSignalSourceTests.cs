using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Models;
using SwebKit.Kubernetes.AksClient;

namespace SwebKit.Kubernetes.Tests;

/// <summary>Regression coverage for the batch-workload false-positive class shared by
/// the two threshold-based pod sources: finished (Succeeded) Job pods must not count as
/// unhealthy workload or as a live restart loop. Failing pods still fire.</summary>
public class AksBatchNoiseSignalSourceTests
{
    private static MonitoringAlertRule Rule(string ns, AlertRuleSource source) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = "rule",
        Source = source,
        Enabled = true,
        IntervalSeconds = 60,
        AksPodParams = new AksPodAlertParams { Namespace = ns },
    };

    private static PodInfo Pod(string name, string phase, int restarts = 0, int ready = 1, int total = 1) => new()
    {
        Name = name,
        Namespace = "dev-briocomp",
        Phase = phase,
        Status = phase,
        ReadyContainers = ready,
        TotalContainers = total,
        RestartCount = restarts,
    };

    [Fact]
    public async Task HealthScore_SucceededJobPods_DoNotLowerScore()
    {
        // 3 healthy service pods + 5 finished Job leftovers → used to report 62%
        // not ready; with Succeeded excluded the namespace is 0% not ready.
        var client = new RecordingAksClient();
        var pods = new List<PodInfo>
        {
            Pod("api-0", "Running"), Pod("api-1", "Running"), Pod("api-2", "Running"),
        };
        for (var i = 0; i < 5; i++)
            pods.Add(Pod($"batch-{i}", "Succeeded", ready: 0));
        client.EnqueuePods(pods.ToArray());

        var source = new AksNamespaceHealthScoreSignalSource(
            new FakePool(client), NullLogger<AksNamespaceHealthScoreSignalSource>.Instance);

        var result = await source.EvaluateAsync(
            Rule("dev-briocomp", AlertRuleSource.AksNamespaceHealthScore), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Ok, result.Status);
    }

    [Fact]
    public async Task HealthScore_FailedPod_StillCounts()
    {
        var client = new RecordingAksClient();
        client.EnqueuePods(Pod("api-0", "Running"), Pod("job-0", "Failed", ready: 0), Pod("done-0", "Succeeded", ready: 0));

        var source = new AksNamespaceHealthScoreSignalSource(
            new FakePool(client), NullLogger<AksNamespaceHealthScoreSignalSource>.Instance);

        var result = await source.EvaluateAsync(
            Rule("dev-briocomp", AlertRuleSource.AksNamespaceHealthScore), CancellationToken.None);

        // 1 of 2 live pods not ready = 50% > default 25% threshold.
        Assert.Equal(AlertSignalStatus.Firing, result.Status);
        Assert.Contains("1/2", result.Detail);
    }

    [Fact]
    public async Task RestartRate_SucceededPodOverThreshold_DoesNotFire()
    {
        // A Job pod that retried past the threshold and then succeeded is history,
        // not a restart loop — it must not keep the rule firing forever.
        var client = new RecordingAksClient();
        client.EnqueuePods(Pod("batch-0", "Succeeded", restarts: 8, ready: 0));

        var source = new AksPodRestartRateSignalSource(
            new FakePool(client), NullLogger<AksPodRestartRateSignalSource>.Instance);

        var result = await source.EvaluateAsync(
            Rule("dev-briocomp", AlertRuleSource.AksPodRestartRate), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Ok, result.Status);
    }

    [Fact]
    public async Task RestartRate_RunningPodOverThreshold_StillFires()
    {
        var client = new RecordingAksClient();
        client.EnqueuePods(Pod("api-0", "Running", restarts: 8));

        var source = new AksPodRestartRateSignalSource(
            new FakePool(client), NullLogger<AksPodRestartRateSignalSource>.Instance);

        var result = await source.EvaluateAsync(
            Rule("dev-briocomp", AlertRuleSource.AksPodRestartRate), CancellationToken.None);

        Assert.Equal(AlertSignalStatus.Firing, result.Status);
        Assert.Contains("api-0", result.Message);
    }
}
