using System.Text;
using SwebKit.Core.Constants;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public partial class DemoAksClient
{
    private readonly Lock _jobLock = new();

    private readonly Dictionary<string, List<JobInfo>> _createdJobsByNamespace = new(StringComparer.Ordinal);

    private readonly Dictionary<string, bool> _cronJobSuspendOverrides = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _cronJobScheduleOverrides = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _jobParallelismOverrides = new(StringComparer.Ordinal);

    // Keys ("{ns}/{hpaName}") whose autoscaling the operator has disabled this session.
    private readonly HashSet<string> _disabledHpaKeys = new(StringComparer.Ordinal);

    // Keys ("{ns}/{hpaName}") -> (min, max) overrides applied this session.
    private readonly Dictionary<string, (int Min, int Max)> _hpaReplicaOverrides = new(StringComparer.Ordinal);

    private readonly HashSet<string> _deletedHpas = new(StringComparer.Ordinal);

    // KEDA ScaledJob session state — same keying as the HPA overrides.
    private readonly HashSet<string> _pausedScaledJobs = new(StringComparer.Ordinal);

    private readonly Dictionary<string, (int Min, int Max)> _scaledJobReplicaOverrides = new(StringComparer.Ordinal);

    private readonly HashSet<string> _deletedScaledJobs = new(StringComparer.Ordinal);

    private readonly Lock _scalingLock = new();

    private int _jobSequence;

    private static readonly (string Name, string Status, int Active, int Succeeded, int Failed, int? DesiredCompletions, int StartMinutesAgo, int? CompletionMinutesAgo, string? SourceKind, string? SourceName)[] DemoJobs =
    [
        ("inventory-sync-29100000", "Succeeded", 0, 1, 0, 1, 185, 183, "CronJob", "inventory-sync"),
        ("report-generator-29100001", "Failed", 0, 0, 1, 1, 620, 615, "CronJob", "report-generator"),
        ("cache-warmer-manual-001", "Active", 1, 0, 0, 1, 20, null, "CronJob", "cache-warmer"),
        ("ad-hoc-backfill-001", "Active", 1, 0, 0, 3, 12, null, null, null)
    ];

    public async Task<IReadOnlyList<HpaInfo>> GetHpasAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);
        var hpas = new List<HpaInfo>
        {
            new()
            {
                Name = "payment-gateway-hpa", Namespace = ns,
                TargetKind = "Deployment", TargetName = "payment-gateway",
                MinReplicas = 2, MaxReplicas = 5, CurrentReplicas = 3, DesiredReplicas = 3,
                CurrentCpuUtilizationPercent = 68, TargetCpuUtilizationPercent = 70,
                Metrics =
                [
                    new HpaMetricStatus { Name = "cpu", Type = "Resource", CurrentValue = 68, TargetValue = 70 }
                ],
                Conditions =
                [
                    new HpaCondition { Type = "ScalingActive", Status = "True", Reason = "ValidMetricFound" },
                    new HpaCondition { Type = "AbleToScale", Status = "True", Reason = "ReadyForNewScale" }
                ]
            },
            new()
            {
                Name = "order-api-hpa", Namespace = ns,
                TargetKind = "Deployment", TargetName = "order-api",
                MinReplicas = 2, MaxReplicas = 8, CurrentReplicas = 3, DesiredReplicas = 3,
                CurrentCpuUtilizationPercent = 42, TargetCpuUtilizationPercent = 75,
                Metrics =
                [
                    new HpaMetricStatus { Name = "cpu", Type = "Resource", CurrentValue = 42, TargetValue = 75 }
                ],
                Conditions =
                [
                    new HpaCondition { Type = "ScalingActive", Status = "True", Reason = "ValidMetricFound" },
                    new HpaCondition { Type = "AbleToScale", Status = "True", Reason = "ReadyForNewScale" }
                ]
            },
            new()
            {
                Name = "user-service-hpa", Namespace = ns,
                TargetKind = "Deployment", TargetName = "user-service",
                MinReplicas = 1, MaxReplicas = 4, CurrentReplicas = 2, DesiredReplicas = 2,
                CurrentCpuUtilizationPercent = 28, TargetCpuUtilizationPercent = 70,
                Metrics =
                [
                    new HpaMetricStatus { Name = "cpu", Type = "Resource", CurrentValue = 28, TargetValue = 70 }
                ],
                Conditions =
                [
                    new HpaCondition { Type = "ScalingActive", Status = "True", Reason = "ValidMetricFound" },
                    new HpaCondition { Type = "AbleToScale", Status = "True", Reason = "ReadyForNewScale" },
                    new HpaCondition { Type = "LimitedByMaxReplicas", Status = "False", Reason = "DesiredWithinRange" }
                ]
            },
            new()
            {
                // KEDA-managed HPA — the generated HPA carries the ScaledObject-name label, so it is
                // disabled through the ScaledObject's pause annotation rather than by freezing the HPA.
                Name = "keda-hpa-order-queue-scaler", Namespace = ns,
                TargetKind = "StatefulSet", TargetName = "order-queue",
                MinReplicas = 2, MaxReplicas = 6, CurrentReplicas = 3, DesiredReplicas = 3,
                CurrentCpuUtilizationPercent = 55, TargetCpuUtilizationPercent = 70,
                IsKedaManaged = true, ScaledObjectName = "order-queue-scaler",
                Metrics =
                [
                    new HpaMetricStatus { Name = "cpu", Type = "Resource", CurrentValue = 55, TargetValue = 70 }
                ],
                Conditions =
                [
                    new HpaCondition { Type = "ScalingActive", Status = "True", Reason = "ValidMetricFound" },
                    new HpaCondition { Type = "AbleToScale", Status = "True", Reason = "ReadyForNewScale" }
                ]
            }
        };

        lock (_scalingLock)
        {
            for (var i = hpas.Count - 1; i >= 0; i--)
            {
                var hpa = hpas[i];
                var key = $"{hpa.Namespace}/{hpa.Name}";
                if (_deletedHpas.Contains(key))
                {
                    hpas.RemoveAt(i);
                    continue;
                }
                if (_hpaReplicaOverrides.TryGetValue(key, out var bounds))
                {
                    hpa.MinReplicas = bounds.Min;
                    hpa.MaxReplicas = bounds.Max;
                }
                if (_disabledHpaKeys.Contains(key))
                {
                    hpa.IsScalingDisabled = true;
                }
            }
        }

        return hpas;
    }

    public Task SetHpaScalingEnabledAsync(string ns, string hpaName, bool enabled, CancellationToken ct = default)
    {
        var key = $"{ns}/{hpaName}";
        lock (_scalingLock)
        {
            if (enabled)
                _disabledHpaKeys.Remove(key);
            else
                _disabledHpaKeys.Add(key);
        }

        return Task.CompletedTask;
    }

    public Task ScaleHpaAsync(string ns, string hpaName, int minReplicas, int maxReplicas, CancellationToken ct = default)
    {
        var key = $"{ns}/{hpaName}";
        lock (_scalingLock)
        {
            _hpaReplicaOverrides[key] = (minReplicas, maxReplicas);
        }
        return Task.CompletedTask;
    }

    public Task DeleteHpaAsync(string ns, string hpaName, CancellationToken ct = default)
    {
        var key = $"{ns}/{hpaName}";
        lock (_scalingLock)
        {
            _deletedHpas.Add(key);
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<CronJobInfo>> GetCronJobsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(150, ct).ConfigureAwait(false);
        var cronJobs = BuildCronJobs(ns, DateTimeOffset.UtcNow).ToList();
        lock (_jobLock)
        {
            for (var i = 0; i < cronJobs.Count; i++)
            {
                var key = $"{ns}/{cronJobs[i].Name}";
                if (_cronJobSuspendOverrides.TryGetValue(key, out var suspended))
                    cronJobs[i].Suspend = suspended;
                if (_cronJobScheduleOverrides.TryGetValue(key, out var schedule))
                    cronJobs[i].Schedule = schedule;
            }
        }
        return cronJobs;
    }

    public async Task<IReadOnlyList<JobInfo>> GetJobsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(150, ct).ConfigureAwait(false);

        var allJobs = BuildBaseJobs(ns, DateTimeOffset.UtcNow)
            .Concat(GetCreatedJobsSnapshot(ns))
            .OrderByDescending(job => job.StartTime ?? DateTimeOffset.MinValue)
            .ThenBy(job => job.Name, StringComparer.Ordinal)
            .ToList();
        lock (_jobLock)
        {
            for (var i = 0; i < allJobs.Count; i++)
            {
                var key = $"{ns}/{allJobs[i].Name}";
                if (_jobParallelismOverrides.TryGetValue(key, out var p))
                    allJobs[i].Parallelism = p;
            }
        }
        return allJobs;
    }

    public async Task<string> TriggerCronJobAsync(string ns, string cronJobName, CancellationToken ct = default)
    {
        await Task.Delay(150, ct).ConfigureAwait(false);

        var cronJob = (await GetCronJobsAsync(ns, ct).ConfigureAwait(false)).FirstOrDefault(job =>
            string.Equals(job.Name, cronJobName, StringComparison.Ordinal));

        if (cronJob is null)
            throw new InvalidOperationException($"CronJob '{cronJobName}' was not found in namespace '{ns}'.");

        var createdJob = new JobInfo
        {
            Name = CreateTriggeredJobName(cronJob.Name, "manual", NextJobSequence()),
            Namespace = ns,
            Status = "Active",
            Active = 1,
            DesiredCompletions = 1,
            StartTime = DateTimeOffset.UtcNow,
            SourceKind = "CronJob",
            SourceName = cronJob.Name,
            Labels = CreateBatchLabels(cronJob.Labels, cronJob.Name)
        };

        StoreCreatedJob(createdJob);
        return createdJob.Name;
    }

    public async Task<string> RerunJobAsync(string ns, string jobName, CancellationToken ct = default)
    {
        await Task.Delay(150, ct).ConfigureAwait(false);

        var sourceJob = (await GetJobsAsync(ns, ct).ConfigureAwait(false)).FirstOrDefault(job =>
            string.Equals(job.Name, jobName, StringComparison.Ordinal));

        if (sourceJob is null)
            throw new InvalidOperationException($"Job '{jobName}' was not found in namespace '{ns}'.");

        var createdJob = new JobInfo
        {
            Name = CreateTriggeredJobName(sourceJob.Name, "rerun", NextJobSequence()),
            Namespace = ns,
            Status = "Active",
            Active = 1,
            DesiredCompletions = sourceJob.DesiredCompletions ?? 1,
            StartTime = DateTimeOffset.UtcNow,
            SourceKind = "Job",
            SourceName = sourceJob.Name,
            Labels = CreateBatchLabels(sourceJob.Labels, sourceJob.SourceName ?? sourceJob.Name)
        };

        StoreCreatedJob(createdJob);
        return createdJob.Name;
    }

    public async Task SuspendCronJobAsync(string ns, string cronJobName, bool suspend, CancellationToken ct = default)
    {
        await Task.Delay(100, ct).ConfigureAwait(false);
        lock (_jobLock)
            _cronJobSuspendOverrides[$"{ns}/{cronJobName}"] = suspend;
    }

    public async Task SetCronJobScheduleAsync(string ns, string cronJobName, string schedule, CancellationToken ct = default)
    {
        await Task.Delay(100, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(schedule))
            throw new InvalidOperationException("Schedule cannot be empty.");
        lock (_jobLock)
            _cronJobScheduleOverrides[$"{ns}/{cronJobName}"] = schedule.Trim();
    }

    // ── KEDA ScaledJobs ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ScaledJobInfo>> GetScaledJobsAsync(string ns, CancellationToken ct = default)
    {
        await Task.Delay(100, ct).ConfigureAwait(false);
        var jobs = new List<ScaledJobInfo>
        {
            new()
            {
                Name = "nightly-reindex", Namespace = ns,
                MinReplicas = 0, MaxReplicas = 3,
                Triggers = ["cron"]
            },
            new()
            {
                Name = "queue-drain-worker", Namespace = ns,
                MinReplicas = 0, MaxReplicas = 10,
                Triggers = ["azure-queue"]
            }
        };

        lock (_scalingLock)
        {
            for (var i = jobs.Count - 1; i >= 0; i--)
            {
                var key = $"{ns}/{jobs[i].Name}";
                if (_deletedScaledJobs.Contains(key))
                {
                    jobs.RemoveAt(i);
                    continue;
                }
                jobs[i].IsPaused = _pausedScaledJobs.Contains(key);
                if (_scaledJobReplicaOverrides.TryGetValue(key, out var bounds))
                {
                    jobs[i].MinReplicas = bounds.Min;
                    jobs[i].MaxReplicas = bounds.Max;
                }
            }
        }
        return jobs;
    }

    public Task SetScaledJobScalingEnabledAsync(string ns, string scaledJobName, bool enabled, CancellationToken ct = default)
    {
        var key = $"{ns}/{scaledJobName}";
        lock (_scalingLock)
        {
            if (enabled)
                _pausedScaledJobs.Remove(key);
            else
                _pausedScaledJobs.Add(key);
        }
        return Task.CompletedTask;
    }

    public Task ScaleScaledJobAsync(string ns, string scaledJobName, int minReplicas, int maxReplicas, CancellationToken ct = default)
    {
        lock (_scalingLock)
            _scaledJobReplicaOverrides[$"{ns}/{scaledJobName}"] = (minReplicas, maxReplicas);
        return Task.CompletedTask;
    }

    public Task DeleteScaledJobAsync(string ns, string scaledJobName, CancellationToken ct = default)
    {
        lock (_scalingLock)
            _deletedScaledJobs.Add($"{ns}/{scaledJobName}");
        return Task.CompletedTask;
    }

    // ── Envoy Gateway resources ───────────────────────────────────────────────

    public async Task SetJobParallelismAsync(string ns, string jobName, int parallelism, CancellationToken ct = default)
    {
        await Task.Delay(100, ct).ConfigureAwait(false);
        lock (_jobLock)
            _jobParallelismOverrides[$"{ns}/{jobName}"] = parallelism;
    }

    private static IReadOnlyList<CronJobInfo> BuildCronJobs(string ns, DateTimeOffset now)
    {
        return
        [
            new CronJobInfo
            {
                Name = "inventory-sync", Namespace = ns,
                Schedule = "*/15 * * * *", Suspend = false, ActiveCount = 0,
                LastScheduleTime = now.AddMinutes(-3),
                LastSuccessfulTime = now.AddMinutes(-3),
                Labels = CreateBatchLabels(null, "inventory-sync")
            },
            new CronJobInfo
            {
                Name = "report-generator", Namespace = ns,
                Schedule = "0 2 * * *", Suspend = false, ActiveCount = 0,
                LastScheduleTime = now.AddHours(-10),
                LastSuccessfulTime = now.AddHours(-10),
                Labels = CreateBatchLabels(null, "report-generator")
            },
            new CronJobInfo
            {
                Name = "cache-warmer", Namespace = ns,
                Schedule = "0 */6 * * *", TimeZone = "Europe/Brussels", Suspend = false, ActiveCount = 1,
                LastScheduleTime = now.AddMinutes(-20),
                LastSuccessfulTime = now.AddHours(-6),
                Labels = CreateBatchLabels(null, "cache-warmer")
            },
            new CronJobInfo
            {
                Name = "audit-log-archiver", Namespace = ns,
                Schedule = "0 0 * * 0", Suspend = true, ActiveCount = 0,
                LastScheduleTime = now.AddDays(-7),
                LastSuccessfulTime = now.AddDays(-7),
                Labels = CreateBatchLabels(null, "audit-log-archiver")
            },
            new CronJobInfo
            {
                Name = "order-cleanup", Namespace = ns,
                Schedule = "30 3 * * *", Suspend = false, ActiveCount = 0,
                LastScheduleTime = now.AddHours(-21),
                LastSuccessfulTime = now.AddHours(-21),
                Labels = CreateBatchLabels(null, "order-cleanup")
            }
        ];
    }

    private static IReadOnlyList<JobInfo> BuildBaseJobs(string ns, DateTimeOffset now)
    {
        return DemoJobs.Select(job => new JobInfo
        {
            Name = job.Name,
            Namespace = ns,
            Status = job.Status,
            Active = job.Active,
            Succeeded = job.Succeeded,
            Failed = job.Failed,
            DesiredCompletions = job.DesiredCompletions,
            StartTime = now.AddMinutes(-job.StartMinutesAgo),
            CompletionTime = job.CompletionMinutesAgo is int completionMinutesAgo
                ? now.AddMinutes(-completionMinutesAgo)
                : null,
            SourceKind = job.SourceKind,
            SourceName = job.SourceName,
            Labels = CreateBatchLabels(null, job.SourceName ?? job.Name)
        }).ToList();
    }

    private IReadOnlyList<JobInfo> GetCreatedJobsSnapshot(string ns)
    {
        lock (_jobLock)
        {
            if (!_createdJobsByNamespace.TryGetValue(ns, out var jobs))
                return [];

            return jobs.Select(CloneJob).ToList();
        }
    }

    private void StoreCreatedJob(JobInfo job)
    {
        lock (_jobLock)
        {
            if (!_createdJobsByNamespace.TryGetValue(job.Namespace, out var jobs))
            {
                jobs = [];
                _createdJobsByNamespace[job.Namespace] = jobs;
            }

            jobs.Add(CloneJob(job));
        }
    }

    private int NextJobSequence()
    {
        lock (_jobLock)
            return ++_jobSequence;
    }

    private static JobInfo CloneJob(JobInfo job)
    {
        return new JobInfo
        {
            Name = job.Name,
            Namespace = job.Namespace,
            Status = job.Status,
            Active = job.Active,
            Succeeded = job.Succeeded,
            Failed = job.Failed,
            DesiredCompletions = job.DesiredCompletions,
            Parallelism = job.Parallelism,
            StartTime = job.StartTime,
            CompletionTime = job.CompletionTime,
            SourceKind = job.SourceKind,
            SourceName = job.SourceName,
            Labels = new Dictionary<string, string>(job.Labels, StringComparer.Ordinal)
        };
    }

    private static Dictionary<string, string> CreateBatchLabels(
        IReadOnlyDictionary<string, string>? sourceLabels,
        string appName)
    {
        var labels = sourceLabels is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(sourceLabels, StringComparer.Ordinal);

        labels["app"] = appName;
        labels["tier"] = "batch";
        return labels;
    }

    private static string CreateTriggeredJobName(string sourceName, string operation, int sequence)
    {
        var sanitizedSourceName = SanitizeKubernetesName(sourceName);
        var suffix = $"-{operation}-{sequence:000}";
        var maxSourceLength = Math.Max(1, 63 - suffix.Length);

        if (sanitizedSourceName.Length > maxSourceLength)
            sanitizedSourceName = sanitizedSourceName[..maxSourceLength].TrimEnd('-');

        if (string.IsNullOrWhiteSpace(sanitizedSourceName))
            sanitizedSourceName = "job";

        return $"{sanitizedSourceName}{suffix}";
    }

    private static string SanitizeKubernetesName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "job";

        var builder = new StringBuilder(value.Length);
        var previousWasDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch <= sbyte.MaxValue)
            {
                builder.Append(ch);
                previousWasDash = false;
                continue;
            }

            if (previousWasDash)
                continue;

            builder.Append('-');
            previousWasDash = true;
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "job" : sanitized;
    }

    private string BuildJobYaml(string ns, string name)
    {
        var job = BuildBaseJobs(ns, DateTimeOffset.UtcNow)
            .Concat(GetCreatedJobsSnapshot(ns))
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? new JobInfo
            {
                Name = name,
                Namespace = ns,
                Status = "Pending",
                DesiredCompletions = 1,
                Labels = CreateBatchLabels(null, name)
            };

        var annotations = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(job.SourceKind))
            annotations[AksBatchAnnotations.SourceKind] = job.SourceKind;
        if (!string.IsNullOrWhiteSpace(job.SourceName))
            annotations[AksBatchAnnotations.SourceName] = job.SourceName;

        var workloadName = SanitizeKubernetesName(job.SourceName ?? job.Name);
        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: batch/v1");
        sb.AppendLine("kind: Job");
        sb.AppendLine("metadata:");
        sb.AppendLine($"  name: {job.Name}");
        sb.AppendLine($"  namespace: {job.Namespace}");
        AppendYamlMap(sb, 2, "labels", job.Labels);
        AppendYamlMap(sb, 2, "annotations", annotations);
        sb.AppendLine("spec:");
        sb.AppendLine($"  completions: {job.DesiredCompletions ?? 1}");
        sb.AppendLine("  backoffLimit: 2");
        sb.AppendLine("  template:");
        sb.AppendLine("    metadata:");
        AppendYamlMap(sb, 6, "labels", CreateBatchLabels(null, workloadName));
        sb.AppendLine("    spec:");
        sb.AppendLine("      restartPolicy: Never");
        sb.AppendLine("      containers:");
        sb.AppendLine($"      - name: {workloadName}");
        sb.AppendLine($"        image: acr.azurecr.io/{workloadName}:1.8.3");
        sb.AppendLine("        args:");
        sb.AppendLine("        - run");
        return sb.ToString().TrimEnd();
    }

    private string BuildCronJobYaml(string ns, string name)
    {
        var cronJob = BuildCronJobs(ns, DateTimeOffset.UtcNow)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? new CronJobInfo
            {
                Name = name,
                Namespace = ns,
                Schedule = "0 * * * *",
                Labels = CreateBatchLabels(null, name)
            };

        var workloadName = SanitizeKubernetesName(cronJob.Name);
        var sb = new StringBuilder();
        sb.AppendLine("apiVersion: batch/v1");
        sb.AppendLine("kind: CronJob");
        sb.AppendLine("metadata:");
        sb.AppendLine($"  name: {cronJob.Name}");
        sb.AppendLine($"  namespace: {cronJob.Namespace}");
        AppendYamlMap(sb, 2, "labels", cronJob.Labels);
        sb.AppendLine("spec:");
        sb.AppendLine($"  schedule: {EscapeYamlValue(cronJob.Schedule ?? "0 * * * *")}");
        sb.AppendLine($"  suspend: {cronJob.Suspend.ToString().ToLowerInvariant()}");
        sb.AppendLine("  jobTemplate:");
        sb.AppendLine("    spec:");
        sb.AppendLine("      template:");
        sb.AppendLine("        metadata:");
        AppendYamlMap(sb, 10, "labels", CreateBatchLabels(null, workloadName));
        sb.AppendLine("        spec:");
        sb.AppendLine("          restartPolicy: Never");
        sb.AppendLine("          containers:");
        sb.AppendLine($"          - name: {workloadName}");
        sb.AppendLine($"            image: acr.azurecr.io/{workloadName}:1.8.3");
        sb.AppendLine("            args:");
        sb.AppendLine("            - run");
        return sb.ToString().TrimEnd();
    }

    private static void AppendYamlMap(StringBuilder sb, int indent, string key, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0)
            return;

        sb.Append(' ', indent).Append(key).AppendLine(":");
        foreach (var item in values.OrderBy(item => item.Key, StringComparer.Ordinal))
            sb.Append(' ', indent + 2)
              .Append(item.Key)
              .Append(": ")
              .AppendLine(EscapeYamlValue(item.Value));
    }

    private static string EscapeYamlValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        var requiresQuotes = value.Any(ch => char.IsWhiteSpace(ch) || ch is ':' or '#' or '*' or '"' or '\'' or '[' or ']' or '{' or '}');
        return requiresQuotes
            ? $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    // ── Wave 1: namespace and workload constraint visibility ──────────────────
}
