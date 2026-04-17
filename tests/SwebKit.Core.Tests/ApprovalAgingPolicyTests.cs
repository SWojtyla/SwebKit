using SwebKit.Core.Models;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class ApprovalAgingPolicyTests
{
    private readonly ApprovalAgingPolicy _policy = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    // ── Prod thresholds ──────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_ProdEnv_AgeBelowWarning_ReturnsOnTime()
    {
        var approval = Approval("prod-eastus", Now - TimeSpan.FromMinutes(10));
        var result = _policy.Evaluate(approval, Now);
        Assert.Equal(ApprovalAgeState.OnTime, result.State);
    }

    [Fact]
    public void Evaluate_ProdEnv_AgeAtWarningBoundary_ReturnsWarning()
    {
        var approval = Approval("Production", Now - TimeSpan.FromMinutes(15));
        var result = _policy.Evaluate(approval, Now);
        Assert.Equal(ApprovalAgeState.Warning, result.State);
    }

    [Fact]
    public void Evaluate_ProdEnv_AgeAtBreachedBoundary_ReturnsBreached()
    {
        var approval = Approval("prd", Now - TimeSpan.FromMinutes(45));
        var result = _policy.Evaluate(approval, Now);
        Assert.Equal(ApprovalAgeState.Breached, result.State);
    }

    [Fact]
    public void Evaluate_ProdEnv_AgeBeyondBreached_ReturnsBreached()
    {
        var approval = Approval("prod", Now - TimeSpan.FromHours(2));
        var result = _policy.Evaluate(approval, Now);
        Assert.Equal(ApprovalAgeState.Breached, result.State);
    }

    // ── Non-prod thresholds ──────────────────────────────────────────────────

    [Fact]
    public void Evaluate_NonProdEnv_AgeBelowWarning_ReturnsOnTime()
    {
        var approval = Approval("staging", Now - TimeSpan.FromMinutes(30));
        var result = _policy.Evaluate(approval, Now);
        Assert.Equal(ApprovalAgeState.OnTime, result.State);
    }

    [Fact]
    public void Evaluate_NonProdEnv_AgeAtWarningBoundary_ReturnsWarning()
    {
        var approval = Approval("dev", Now - TimeSpan.FromMinutes(60));
        var result = _policy.Evaluate(approval, Now);
        Assert.Equal(ApprovalAgeState.Warning, result.State);
    }

    [Fact]
    public void Evaluate_NonProdEnv_AgeAtBreachedBoundary_ReturnsBreached()
    {
        var approval = Approval("staging", Now - TimeSpan.FromHours(4));
        var result = _policy.Evaluate(approval, Now);
        Assert.Equal(ApprovalAgeState.Breached, result.State);
    }

    // ── Production name detection ────────────────────────────────────────────

    [Theory]
    [InlineData("Production")]
    [InlineData("prod-eastus")]
    [InlineData("prd")]
    [InlineData("PROD")]
    [InlineData("my-production-env")]
    public void Evaluate_ProductionNameVariants_UsesProdThresholds(string envName)
    {
        // Age of 20 min: Warning for prod (≥15m), OnTime for non-prod (<60m)
        var approval = Approval(envName, Now - TimeSpan.FromMinutes(20));
        var result = _policy.Evaluate(approval, Now);
        Assert.Equal(ApprovalAgeState.Warning, result.State);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("dev")]
    [InlineData(null)]
    [InlineData("qa")]
    [InlineData("uat")]
    public void Evaluate_NonProductionNameVariants_UsesNonProdThresholds(string? envName)
    {
        // Age of 20 min: OnTime for non-prod (<60m), Warning for prod (≥15m)
        var approval = Approval(envName, Now - TimeSpan.FromMinutes(20));
        var result = _policy.Evaluate(approval, Now);
        Assert.Equal(ApprovalAgeState.OnTime, result.State);
    }

    // ── Result fields ────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_SetsCorrectApprovalIdAndEnvironmentName()
    {
        var approval = Approval("prod", Now - TimeSpan.FromMinutes(5));
        var result = _policy.Evaluate(approval, Now);
        Assert.Equal("id1", result.ApprovalId);
        Assert.Equal("prod", result.EnvironmentName);
    }

    [Fact]
    public void Evaluate_SetsAgeApproximatelyCorrect()
    {
        var createdOn = Now - TimeSpan.FromMinutes(30);
        var approval = Approval("staging", createdOn);
        var result = _policy.Evaluate(approval, Now);
        Assert.Equal(TimeSpan.FromMinutes(30), result.Age);
    }

    // ── EvaluateAll ──────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateAll_ReturnsCorrectCountAndStates()
    {
        var approvals = new[]
        {
            Approval("prod", Now - TimeSpan.FromMinutes(5)),   // OnTime (prod)
            Approval("prod", Now - TimeSpan.FromMinutes(20)),  // Warning (prod)
            Approval("dev",  Now - TimeSpan.FromMinutes(90)),  // Warning (non-prod)
            Approval("prd",  Now - TimeSpan.FromMinutes(50)),  // Breached (prod)
        };

        var results = _policy.EvaluateAll(approvals, Now);

        Assert.Equal(4, results.Count);
        Assert.Equal(ApprovalAgeState.OnTime, results[0].State);
        Assert.Equal(ApprovalAgeState.Warning, results[1].State);
        Assert.Equal(ApprovalAgeState.Warning, results[2].State);
        Assert.Equal(ApprovalAgeState.Breached, results[3].State);
    }

    [Fact]
    public void EvaluateAll_EmptyInput_ReturnsEmptyList()
    {
        var results = _policy.EvaluateAll([], Now);
        Assert.Empty(results);
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static AdoApproval Approval(string? envName, DateTimeOffset createdOn) =>
        new("id1", "pending", 1, "Pipeline", 100, "Deploy", envName, "me", null, createdOn);
}
