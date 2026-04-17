using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class PipelineFailureClassifierTests
{
    private readonly PipelineFailureClassifier _classifier = new();

    // ── Succeeded / not-failed runs ──────────────────────────────────────────

    [Fact]
    public void Classify_SucceededRun_ReturnsUnknown()
    {
        var run = new AdoPipelineRun(1, 10, "Run", "completed", "succeeded",
            DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
            "main", null, null, []);

        var result = _classifier.Classify(run);

        Assert.Equal(PipelineFailureCategory.Unknown, result.Category);
        Assert.Null(result.FailedStageName);
    }

    [Fact]
    public void Classify_InProgressRun_ReturnsUnknown()
    {
        var run = new AdoPipelineRun(2, 10, "Run", "inProgress", "none",
            DateTimeOffset.UtcNow.AddMinutes(-5), null,
            "main", null, null, []);

        var result = _classifier.Classify(run);

        Assert.Equal(PipelineFailureCategory.Unknown, result.Category);
    }

    // ── Waiting stage → ApprovalGate ────────────────────────────────────────

    [Fact]
    public void Classify_WithWaitingStage_ReturnsApprovalGate()
    {
        var run = FailedRun(1, new AdoPipelineStage("deploy-prod", "failed", "failed", 2, null));
        var waiting = new[] { new WaitingStage("deploy-prod", "approval-42") };

        var result = _classifier.Classify(run, waiting);

        Assert.Equal(PipelineFailureCategory.ApprovalGate, result.Category);
        Assert.Equal("deploy-prod", result.FailedStageName);
    }

    // ── Stage name classification ────────────────────────────────────────────

    [Fact]
    public void Classify_StageBuildDotnet_ReturnsBuildOrTest()
    {
        var run = FailedRun(1, new AdoPipelineStage("build-dotnet", "failed", "failed", 1, null));

        var result = _classifier.Classify(run);

        Assert.Equal(PipelineFailureCategory.BuildOrTest, result.Category);
        Assert.Equal("build-dotnet", result.FailedStageName);
    }

    [Fact]
    public void Classify_StageDeployHelm_ReturnsDeploy()
    {
        var run = FailedRun(1, new AdoPipelineStage("deploy-helm", "failed", "failed", 1, null));

        var result = _classifier.Classify(run);

        Assert.Equal(PipelineFailureCategory.Deploy, result.Category);
        Assert.Equal("deploy-helm", result.FailedStageName);
    }

    [Fact]
    public void Classify_StagePostDeployHealthCheck_ReturnsPostDeployHealth()
    {
        var run = FailedRun(1, new AdoPipelineStage("post-deploy-health-check", "failed", "failed", 1, null));

        var result = _classifier.Classify(run);

        Assert.Equal(PipelineFailureCategory.PostDeployHealth, result.Category);
        Assert.Equal("post-deploy-health-check", result.FailedStageName);
    }

    [Fact]
    public void Classify_StageInitInfraCredentials_ReturnsInfraOrAuth()
    {
        var run = FailedRun(1, new AdoPipelineStage("init-infra-credentials", "failed", "failed", 1, null));

        var result = _classifier.Classify(run);

        Assert.Equal(PipelineFailureCategory.InfraOrAuth, result.Category);
        Assert.Equal("init-infra-credentials", result.FailedStageName);
    }

    [Fact]
    public void Classify_StageWithNoMatchingName_ReturnsUnknown()
    {
        var run = FailedRun(1, new AdoPipelineStage("custom-mystery-step", "failed", "failed", 1, null));

        var result = _classifier.Classify(run);

        Assert.Equal(PipelineFailureCategory.Unknown, result.Category);
        Assert.Equal("custom-mystery-step", result.FailedStageName);
    }

    // ── Canceled run with no stages ──────────────────────────────────────────

    [Fact]
    public void Classify_CanceledRunWithNoStages_ReturnsQueuedOrAgent()
    {
        var run = new AdoPipelineRun(5, 10, "Run", "completed", "canceled",
            DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
            "main", null, null, []);

        var result = _classifier.Classify(run);

        Assert.Equal(PipelineFailureCategory.QueuedOrAgent, result.Category);
        Assert.Null(result.FailedStageName);
    }

    // ── Multiple failed stages — first by Order wins ─────────────────────────

    [Fact]
    public void Classify_MultipleFailedStages_FirstByOrderWins()
    {
        var run = FailedRun(1,
            new AdoPipelineStage("deploy-helm", "failed", "failed", 3, null),
            new AdoPipelineStage("build-dotnet", "failed", "failed", 1, null),
            new AdoPipelineStage("post-deploy-health-check", "failed", "failed", 5, null));

        var result = _classifier.Classify(run);

        Assert.Equal(PipelineFailureCategory.BuildOrTest, result.Category);
        Assert.Equal("build-dotnet", result.FailedStageName);
    }

    [Fact]
    public void Classify_CanceledStageIsPickedAsFailedStage()
    {
        var run = FailedRun(1,
            new AdoPipelineStage("agent-pool-init", "canceled", "canceled", 1, null));

        var result = _classifier.Classify(run);

        Assert.Equal(PipelineFailureCategory.QueuedOrAgent, result.Category);
        Assert.Equal("agent-pool-init", result.FailedStageName);
    }

    // ── Result fields ────────────────────────────────────────────────────────

    [Fact]
    public void Classify_RunIdIsPreservedInResult()
    {
        var run = FailedRun(42, new AdoPipelineStage("build-dotnet", "failed", "failed", 1, null));

        var result = _classifier.Classify(run);

        Assert.Equal(42, result.RunId);
    }

    [Fact]
    public void Classify_ExplanationIsNonEmpty()
    {
        var run = FailedRun(1, new AdoPipelineStage("deploy-helm", "failed", "failed", 1, null));

        var result = _classifier.Classify(run);

        Assert.False(string.IsNullOrWhiteSpace(result.Explanation));
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static AdoPipelineRun FailedRun(int id = 1, params AdoPipelineStage[] stages) =>
        new(id, 10, "Run", "completed", "failed",
            DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
            "main", null, null, stages.ToList());
}
