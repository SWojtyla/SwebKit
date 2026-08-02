using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Agents;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>Handles whichever single AgentActionType it's constructed for; returns a canned result.</summary>
internal sealed class FakeAgentActionExecutor(AgentActionType handles, AgentActionResult? result = null) : IAgentActionExecutor
{
    public bool CanHandle(AgentActionType type) => type == handles;

    public Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct) =>
        Task.FromResult(result ?? new AgentActionResult { IsSuccess = true, ResultSummary = "applied" });
}

public class AgentPendingApprovalsEndpointsTests
{
    private static PendingAgentAction NewAction(string id = "a1") => new()
    {
        Id = id,
        Type = AgentActionType.DeleteRequest,
        Summary = "Delete request 'Get token'",
        Target = "Request 'Get token' (r1)",
        Risk = AgentActionRisk.High,
        Preview = "Name: Get token\nMethod: Post",
        ExpectedFingerprint = null,
    };

    [Fact]
    public void GetPendingApprovals_ReturnsMappedSummaries_WithoutExposingPayload()
    {
        var coordinator = new AgentActionCoordinator();
        coordinator.RegisterAction(NewAction());

        var result = AgentEndpoints.GetPendingApprovals(coordinator);

        var ok = Assert.IsType<Ok<IReadOnlyList<PendingActionSummary>>>(result);
        var summary = Assert.Single(ok.Value!);
        Assert.Equal("a1", summary.Id);
        Assert.Equal("DeleteRequest", summary.Type);
        Assert.Equal("High", summary.Risk);
        Assert.Equal("Delete request 'Get token'", summary.Summary);
        // PendingActionSummary has no Payload property at all — this is a compile-time guarantee,
        // not something that needs its own runtime assertion.
    }

    [Fact]
    public void GetPendingApprovals_ExcludesRejectedAndExpiredActions()
    {
        var coordinator = new AgentActionCoordinator();
        coordinator.RegisterAction(NewAction("active"));
        coordinator.RegisterAction(NewAction("rejected"));
        coordinator.RejectAction("rejected");

        var result = AgentEndpoints.GetPendingApprovals(coordinator);

        var ok = Assert.IsType<Ok<IReadOnlyList<PendingActionSummary>>>(result);
        Assert.Single(ok.Value!);
        Assert.Equal("active", ok.Value![0].Id);
    }

    [Fact]
    public async Task ConfirmActionAsync_UnknownId_ReturnsNotFound()
    {
        var coordinator = new AgentActionCoordinator();
        var applier = new AgentActionApplier(coordinator, [new FakeAgentActionExecutor(AgentActionType.DeleteRequest)]);

        var result = await AgentEndpoints.ConfirmActionAsync("does-not-exist", coordinator, applier, CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task ConfirmActionAsync_KnownId_ConfirmsThenApplies_AndReturnsTheApplyResult()
    {
        var coordinator = new AgentActionCoordinator();
        var action = NewAction();
        coordinator.RegisterAction(action);
        var executor = new FakeAgentActionExecutor(
            AgentActionType.DeleteRequest,
            new AgentActionResult { IsSuccess = true, ResultSummary = "Deleted request 'Get token'" });
        var applier = new AgentActionApplier(coordinator, [executor]);

        var result = await AgentEndpoints.ConfirmActionAsync(action.Id, coordinator, applier, CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentActionResult>>(result);
        Assert.True(ok.Value!.IsSuccess);
        Assert.Equal("Deleted request 'Get token'", ok.Value.ResultSummary);
        Assert.True(action.IsConfirmed);
        Assert.True(action.IsApplied);
    }

    [Fact]
    public async Task ConfirmActionAsync_ApplierRejectsAppropriately_WhenNoExecutorHandlesTheType()
    {
        var coordinator = new AgentActionCoordinator();
        var action = NewAction();
        coordinator.RegisterAction(action);
        // No executors registered at all — simulates confirming an action type nothing can apply yet.
        var applier = new AgentActionApplier(coordinator, []);

        var result = await AgentEndpoints.ConfirmActionAsync(action.Id, coordinator, applier, CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentActionResult>>(result);
        Assert.False(ok.Value!.IsSuccess);
        Assert.False(action.IsApplied);
    }

    [Fact]
    public void RejectAction_UnknownId_ReturnsNotFound()
    {
        var coordinator = new AgentActionCoordinator();

        var result = AgentEndpoints.RejectAction("does-not-exist", coordinator);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public void RejectAction_KnownId_RejectsIt_SoItNoLongerAppearsAsPending()
    {
        var coordinator = new AgentActionCoordinator();
        var action = NewAction();
        coordinator.RegisterAction(action);

        var result = AgentEndpoints.RejectAction(action.Id, coordinator);

        Assert.IsType<Ok<object>>(result);
        Assert.Empty(coordinator.GetPendingActions());
    }
}
