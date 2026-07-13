using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

/// <summary>
/// In-memory DevOps client that returns realistic dummy data for demo purposes.
/// Simulates 2 ADO projects, each with pipelines, environments, repos, approvals, and tags.
/// </summary>
public class DemoDevOpsClient : IDevOpsClient
{
    private static readonly Random Rng = new(42);

    private static readonly AdoProject[] Projects =
    [
        new("proj-1", "ecommerce-platform", "E-commerce platform services", "wellFormed"),
        new("proj-2", "internal-tools", "Internal tooling and admin apps", "wellFormed")
    ];

    private static readonly Dictionary<string, AdoPipeline[]> PipelinesByProject = new()
    {
        ["ecommerce-platform"] =
        [
            new(101, "order-api-ci-cd", "\\ecommerce", ""),
            new(102, "product-catalog-ci-cd", "\\ecommerce", ""),
            new(103, "payment-gateway-ci-cd", "\\ecommerce", ""),
            new(104, "cart-api-ci-cd", "\\ecommerce", ""),
            new(105, "user-service-ci-cd", "\\ecommerce", "")
        ],
        ["internal-tools"] =
        [
            new(201, "admin-dashboard-ci-cd", "\\tools", ""),
            new(202, "notification-service-ci-cd", "\\tools", ""),
            new(203, "report-generator-ci-cd", "\\tools", "")
        ]
    };

    private static readonly Dictionary<string, AdoEnvironment[]> EnvironmentsByProject = new()
    {
        ["ecommerce-platform"] =
        [
            new(1, "DEV"),
            new(2, "TST"),
            new(3, "STG"),
            new(4, "PRD")
        ],
        ["internal-tools"] =
        [
            new(10, "DEV"),
            new(11, "ACC"),
            new(12, "PRD")
        ]
    };

    private static readonly Dictionary<string, AdoRepository[]> ReposByProject = new()
    {
        ["ecommerce-platform"] =
        [
            new("repo-1", "order-api", "refs/heads/main", "https://dev.azure.com/demo/ecommerce-platform/_git/order-api"),
            new("repo-2", "product-catalog", "refs/heads/main", "https://dev.azure.com/demo/ecommerce-platform/_git/product-catalog"),
            new("repo-3", "payment-gateway", "refs/heads/main", "https://dev.azure.com/demo/ecommerce-platform/_git/payment-gateway"),
            new("repo-4", "cart-api", "refs/heads/main", "https://dev.azure.com/demo/ecommerce-platform/_git/cart-api"),
            new("repo-5", "user-service", "refs/heads/main", "https://dev.azure.com/demo/ecommerce-platform/_git/user-service")
        ],
        ["internal-tools"] =
        [
            new("repo-10", "admin-dashboard", "refs/heads/main", "https://dev.azure.com/demo/internal-tools/_git/admin-dashboard"),
            new("repo-11", "notification-service", "refs/heads/main", "https://dev.azure.com/demo/internal-tools/_git/notification-service"),
            new("repo-12", "report-generator", "refs/heads/main", "https://dev.azure.com/demo/internal-tools/_git/report-generator")
        ]
    };

    // Mutable state for demo interactions
    private readonly List<AdoApproval> _pendingApprovals = [];
    private readonly List<AdoPipelineRun> _triggeredRuns = [];
    private readonly Dictionary<string, List<AdoTag>> _createdTags = new();
    private int _nextRunId = 5000;

    // ── Pre-built demo releases ————————————————————————————————————————————
    // These are returned by ReleasesPage when demo mode is on and no user-created
    // releases exist, so the board, approval center, and tag manager all have
    // realistic content without any ADO configuration.

    public static readonly IReadOnlyList<ReleaseRecord> DemoReleases =
    [
        new()
        {
            Id = new Guid("d3e0beef-0000-0000-0001-000000000001"),
            Name = "E-Commerce Platform v2.4",
            SprintNumber = 42,
            Label = "release/2.4",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            CreatedBy = "Alice Johnson",
            Status = ReleaseStatus.InProgress,
            Notes = "Sprint 42 release — payment retry logic, cart fixes, user-service hardening.",
            Components =
            [
                new() { ComponentName = "order-api",       ProjectName = "ecommerce-platform", RepositoryId = "repo-1",  PipelineId = 101, InScope = true, TargetTag = "v2.4.0", TagConfirmed = true  },
                new() { ComponentName = "payment-gateway", ProjectName = "ecommerce-platform", RepositoryId = "repo-3",  PipelineId = 103, InScope = true, TargetTag = "v2.4.0", TagConfirmed = true  },
                new() { ComponentName = "cart-api",        ProjectName = "ecommerce-platform", RepositoryId = "repo-4",  PipelineId = 104, InScope = true, TargetTag = "v2.4.0", TagConfirmed = false },
                new() { ComponentName = "user-service",    ProjectName = "ecommerce-platform", RepositoryId = "repo-5",  PipelineId = 105, InScope = true, TargetTag = "v2.4.0", TagConfirmed = true  },
            ]
        },
        new()
        {
            Id = new Guid("d3e0beef-0000-0000-0001-000000000002"),
            Name = "Internal Tools Q1 2026",
            SprintNumber = 12,
            Label = "q1-release",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedBy = "Charlie Davis",
            Status = ReleaseStatus.Draft,
            Notes = "Q1 admin-dashboard update and notification service refresh.",
            Components =
            [
                new() { ComponentName = "admin-dashboard",        ProjectName = "internal-tools", RepositoryId = "repo-10", PipelineId = 201, InScope = true, TargetTag = "v1.2.0", TagConfirmed = true  },
                new() { ComponentName = "notification-service",   ProjectName = "internal-tools", RepositoryId = "repo-11", PipelineId = 202, InScope = true, TargetTag = "v1.2.0", TagConfirmed = false },
                new() { ComponentName = "report-generator",       ProjectName = "internal-tools", RepositoryId = "repo-12", PipelineId = 203, InScope = false },
            ]
        }
    ];

    public DemoDevOpsClient()
    {
        // Seed pending approvals
        _pendingApprovals.AddRange(
        [
            new("appr-1", "pending", 101, "order-api-ci-cd", 1001, "Deploy to STG", "STG", "Alice Johnson",
                "https://dev.azure.com/demo/ecommerce-platform/_build/results?buildId=1001", DateTimeOffset.UtcNow.AddMinutes(-45)),
            new("appr-2", "pending", 103, "payment-gateway-ci-cd", 1003, "Deploy to PRD", "PRD", "Bob Smith",
                "https://dev.azure.com/demo/ecommerce-platform/_build/results?buildId=1003", DateTimeOffset.UtcNow.AddMinutes(-12)),
            new("appr-3", "pending", 201, "admin-dashboard-ci-cd", 2001, "Deploy to ACC", "ACC", "Charlie Davis",
                "https://dev.azure.com/demo/internal-tools/_build/results?buildId=2001", DateTimeOffset.UtcNow.AddMinutes(-5))
        ]);
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    public async Task<List<AdoProject>> GetProjectsAsync(CancellationToken ct = default)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);
        return [.. Projects];
    }

    public async Task<List<AdoPipeline>> GetPipelinesAsync(string project, CancellationToken ct = default)
    {
        await Task.Delay(150, ct).ConfigureAwait(false);
        return PipelinesByProject.TryGetValue(project, out var pipelines)
            ? [.. pipelines]
            : [];
    }

    public async Task<List<AdoPipelineRun>> GetPipelineRunsAsync(
        string project, int pipelineId, int? top = null, CancellationToken ct = default)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var envs = EnvironmentsByProject.GetValueOrDefault(project) ?? [];
        var pipeline = PipelinesByProject.GetValueOrDefault(project)?
            .FirstOrDefault(p => p.Id == pipelineId);
        if (pipeline is null) return [];

        var runs = new List<AdoPipelineRun>();

        // Generate 3 historical runs.
        // Pipelines 101 (order-api), 103 (payment-gateway), 201 (admin-dashboard):
        // latest run (i=0) is "inProgress" with one stage awaiting approval so that
        // ApprovalCenter and the release board display meaningful demo content.
        for (var i = 0; i < 3; i++)
        {
            var runId = pipelineId * 10 + i;
            var age = TimeSpan.FromHours(6 * (i + 1) + Rng.Next(12));

            // Stage index (0-based) that should show as "inProgress / awaiting approval"
            var waitingIdx = pipelineId switch
            {
                101 => 2, // Deploy to STG  (envs: DEV TST STG PRD)
                103 => 3, // Deploy to PRD  (envs: DEV TST STG PRD)
                201 => 1, // Deploy to ACC  (envs: DEV ACC PRD)
                _ => -1
            };
            var isInProgress = i == 0 && waitingIdx >= 0;

            List<AdoPipelineStage> stages;
            if (isInProgress)
            {
                stages = envs.Select((env, idx) =>
                    idx < waitingIdx
                        ? new AdoPipelineStage($"Deploy to {env.Name}", "completed", "succeeded", idx + 1, env.Name)
                        : idx == waitingIdx
                            ? new AdoPipelineStage($"Deploy to {env.Name}", "inProgress", "", idx + 1, env.Name)
                            : new AdoPipelineStage($"Deploy to {env.Name}", "pending", "", idx + 1, env.Name))
                    .ToList();
            }
            else
            {
                stages = envs.Select((env, idx) => new AdoPipelineStage(
                    $"Deploy to {env.Name}", "completed",
                    i == 2 && idx == envs.Length - 1 ? "failed" : "succeeded",
                    idx + 1, env.Name)).ToList();
            }

            runs.Add(new AdoPipelineRun(
                runId, pipelineId, pipeline.Name,
                isInProgress ? "inProgress" : "completed",
                isInProgress ? "" : (i == 2 ? "failed" : "succeeded"),
                now - age, isInProgress ? null : now - age + TimeSpan.FromMinutes(8),
                "main", "CI Trigger",
                $"https://dev.azure.com/demo/{project}/_build/results?buildId={runId}",
                stages));
        }

        // Add any triggered runs
        runs.AddRange(_triggeredRuns.Where(r => r.PipelineId == pipelineId));

        return (top.HasValue ? runs.Take(top.Value).ToList() : runs)
            .OrderByDescending(r => r.CreatedDate).ToList();
    }

    public async Task<AdoPipelineRun> GetPipelineRunAsync(
        string project, int pipelineId, int runId, CancellationToken ct = default)
    {
        var runs = await GetPipelineRunsAsync(project, pipelineId, ct: ct).ConfigureAwait(false);
        return runs.FirstOrDefault(r => r.Id == runId)
            ?? throw new InvalidOperationException($"Run {runId} not found.");
    }

    public async Task<AdoPipelineRun> TriggerPipelineRunAsync(
        string project, int pipelineId, string branch,
        Dictionary<string, string>? templateParameters = null,
        CancellationToken ct = default)
    {
        await Task.Delay(500, ct).ConfigureAwait(false);
        var pipeline = PipelinesByProject.GetValueOrDefault(project)?
            .FirstOrDefault(p => p.Id == pipelineId);
        var envs = EnvironmentsByProject.GetValueOrDefault(project) ?? [];

        var run = new AdoPipelineRun(
            _nextRunId++, pipelineId, pipeline?.Name ?? "Unknown",
            "inProgress", "",
            DateTimeOffset.UtcNow, null,
            branch, "You (demo)",
            $"https://dev.azure.com/demo/{project}/_build/results?buildId={_nextRunId - 1}",
            envs.Select((env, idx) => new AdoPipelineStage(
                $"Deploy to {env.Name}",
                idx == 0 ? "inProgress" : "pending",
                "", idx + 1, env.Name)).ToList());

        _triggeredRuns.Add(run);
        return run;
    }

    public async Task<List<AdoApproval>> GetPendingApprovalsAsync(string project, CancellationToken ct = default)
    {
        await Task.Delay(150, ct).ConfigureAwait(false);
        var pipelineIds = PipelinesByProject.GetValueOrDefault(project)?
            .Select(p => p.Id).ToHashSet() ?? [];
        return _pendingApprovals.Where(a => pipelineIds.Contains(a.PipelineId)).ToList();
    }

    public Task<List<WaitingStage>> GetWaitingStagesAsync(string project, int runId, CancellationToken ct = default)
    {
        var waitingStages = new List<WaitingStage>();
        // runId formula: pipelineId * 10 + i; latest run is i=0.
        if (runId == 1010) waitingStages.Add(new WaitingStage("Deploy to STG", "appr-1")); // order-api
        if (runId == 1030) waitingStages.Add(new WaitingStage("Deploy to PRD", "appr-2")); // payment-gateway
        if (runId == 2010) waitingStages.Add(new WaitingStage("Deploy to ACC", "appr-3")); // admin-dashboard
        return Task.FromResult(waitingStages);
    }

    public async Task ApproveAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default)
    {
        await Task.Delay(300, ct).ConfigureAwait(false);
        _pendingApprovals.RemoveAll(a => a.Id == approvalId);
    }

    public async Task RejectAsync(string project, string approvalId, string? comment = null, CancellationToken ct = default)
    {
        await Task.Delay(300, ct).ConfigureAwait(false);
        _pendingApprovals.RemoveAll(a => a.Id == approvalId);
    }

    public async Task<List<AdoRepository>> GetRepositoriesAsync(string project, CancellationToken ct = default)
    {
        await Task.Delay(150, ct).ConfigureAwait(false);
        return ReposByProject.TryGetValue(project, out var repos)
            ? [.. repos]
            : [];
    }

    public async Task<List<string>> GetBranchesAsync(string project, string repositoryId, CancellationToken ct = default)
    {
        await Task.Delay(150, ct).ConfigureAwait(false);
        return ["develop", "feature/cart-improvements", "feature/new-checkout", "hotfix/payment-fix", "main", "release/1.5.0"];
    }

    public async Task<List<AdoTag>> GetTagsAsync(string project, string repositoryId, CancellationToken ct = default)
    {
        await Task.Delay(150, ct).ConfigureAwait(false);
        var repo = ReposByProject.Values.SelectMany(r => r).FirstOrDefault(r => r.Id == repositoryId);
        var name = repo?.Name ?? "unknown";
        var now = DateTimeOffset.UtcNow;

        var tags = new List<AdoTag>
        {
            new($"v1.4.2", "abc1234", $"Release v1.4.2 of {name}", "Alice Johnson", now.AddDays(-5)),
            new($"v1.4.1", "def5678", $"Release v1.4.1 of {name}", "Bob Smith", now.AddDays(-12)),
            new($"v1.4.0", "ghi9012", $"Release v1.4.0 of {name}", "Charlie Davis", now.AddDays(-20)),
            new($"v1.3.0", "jkl3456", $"Sprint 40 release", "Alice Johnson", now.AddDays(-35))
        };

        // Append any user-created tags
        if (_createdTags.TryGetValue(repositoryId, out var userTags))
            tags.InsertRange(0, userTags);

        return tags;
    }

    public async Task<AdoTag> CreateAnnotatedTagAsync(
        string project, string repositoryId, string name, string commitSha, string message,
        CancellationToken ct = default)
    {
        await Task.Delay(400, ct).ConfigureAwait(false);
        var tag = new AdoTag(name, commitSha, message, "You (demo)", DateTimeOffset.UtcNow);

        if (!_createdTags.ContainsKey(repositoryId))
            _createdTags[repositoryId] = [];
        _createdTags[repositoryId].Add(tag);

        return tag;
    }

    public async Task<List<AdoCommit>> GetCommitsAsync(
        string project, string repositoryId, string branch, int top = 20, CancellationToken ct = default)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var messages = new[]
        {
            "feat: add retry logic to payment processing",
            "fix: resolve null reference in order validation",
            "chore: update NuGet dependencies",
            "feat: implement circuit breaker for external calls",
            "fix: correct timezone handling in scheduled jobs",
            "refactor: extract common auth middleware",
            "docs: update API documentation",
            "feat: add health check endpoint",
            "fix: memory leak in connection pool",
            "test: add integration tests for cart service"
        };

        return Enumerable.Range(0, Math.Min(top, messages.Length))
            .Select(i =>
            {
                var sha = $"{Rng.Next(0x1000000):x6}{Rng.Next(0x10000000):x7}{Rng.Next(0x10000000):x7}";
                return new AdoCommit(
                    sha, sha[..7],
                    messages[i],
                    i % 3 == 0 ? "Alice Johnson" : i % 3 == 1 ? "Bob Smith" : "Charlie Davis",
                    now.AddHours(-(i * 4 + Rng.Next(3))));
            })
            .ToList();
    }

    public async Task<List<AdoEnvironment>> GetEnvironmentsAsync(string project, CancellationToken ct = default)
    {
        await Task.Delay(100, ct).ConfigureAwait(false);
        return EnvironmentsByProject.TryGetValue(project, out var envs)
            ? [.. envs]
            : [];
    }

    public async Task<List<PipelineEnvironmentStatus>> GetEnvironmentStatusAsync(
        string project, int pipelineId, int scanDepth = 5, CancellationToken ct = default)
    {
        await Task.Delay(200, ct).ConfigureAwait(false);
        var envs = EnvironmentsByProject.GetValueOrDefault(project) ?? [];
        var pipeline = PipelinesByProject.GetValueOrDefault(project)?
            .FirstOrDefault(p => p.Id == pipelineId);
        if (pipeline is null || envs.Length == 0) return [];

        var now = DateTimeOffset.UtcNow;
        var runId = pipelineId * 10; // latest run id formula

        // Determine waiting stage index for known pipelines
        var waitingIdx = pipelineId switch
        {
            101 => 2,
            103 => 3,
            201 => 1,
            _ => -1
        };

        var statuses = new List<PipelineEnvironmentStatus>();
        for (var i = 0; i < envs.Length; i++)
        {
            var env = envs[i];
            var isWaiting = i == waitingIdx;
            var isCompleted = i < waitingIdx || waitingIdx < 0;

            statuses.Add(new PipelineEnvironmentStatus(
                EnvironmentName: env.Name,
                StageName: $"Deploy to {env.Name}",
                LatestRunId: runId,
                RunName: pipeline.Name,
                State: isWaiting ? "inProgress" : (isCompleted ? "completed" : "pending"),
                Result: isCompleted ? "succeeded" : "",
                FinishedAt: isCompleted ? now.AddHours(-(envs.Length - i) * 2) : null,
                TriggeredBy: "CI Trigger",
                WaitingForApproval: isWaiting));
        }

        return statuses;
    }
}
