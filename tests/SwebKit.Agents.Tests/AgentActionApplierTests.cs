using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>Records whether/how it was called; returns a canned result.</summary>
internal sealed class FakeActionExecutor : IAgentActionExecutor
{
    private readonly AgentActionType _handles;
    public PendingAgentAction? LastApplied { get; private set; }
    public AgentActionResult NextResult { get; set; } = new() { IsSuccess = true, ResultSummary = "done" };

    public FakeActionExecutor(AgentActionType handles) => _handles = handles;

    public bool CanHandle(AgentActionType type) => type == _handles;

    public Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct)
    {
        LastApplied = action;
        return Task.FromResult(NextResult);
    }
}

public class AgentActionApplierTests
{
    private static PendingAgentAction NewAction(AgentActionType type = AgentActionType.DeleteRequest) => new()
    {
        Id = "a1",
        Type = type,
        Summary = "S",
        Target = "T",
        Risk = AgentActionRisk.High,
        Preview = "P",
        ExpectedFingerprint = null,
    };

    [Fact]
    public async Task ApplyAsync_UnknownActionId_FailsWithoutCallingAnyExecutor()
    {
        var coordinator = new AgentActionCoordinator();
        var executor = new FakeActionExecutor(AgentActionType.DeleteRequest);
        var applier = new AgentActionApplier(coordinator, [executor]);

        var result = await applier.ApplyAsync("does-not-exist", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(executor.LastApplied);
    }

    [Fact]
    public async Task ApplyAsync_NotYetConfirmed_FailsWithoutCallingAnyExecutor()
    {
        var coordinator = new AgentActionCoordinator();
        var executor = new FakeActionExecutor(AgentActionType.DeleteRequest);
        var applier = new AgentActionApplier(coordinator, [executor]);
        var action = NewAction();
        coordinator.RegisterAction(action);
        // Deliberately not calling action.Confirm().

        var result = await applier.ApplyAsync(action.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not been confirmed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(executor.LastApplied);
    }

    [Fact]
    public async Task ApplyAsync_Rejected_FailsWithoutCallingAnyExecutor()
    {
        var coordinator = new AgentActionCoordinator();
        var executor = new FakeActionExecutor(AgentActionType.DeleteRequest);
        var applier = new AgentActionApplier(coordinator, [executor]);
        var action = NewAction();
        coordinator.RegisterAction(action);
        action.Confirm();
        coordinator.RejectAction(action.Id);

        var result = await applier.ApplyAsync(action.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(executor.LastApplied);
    }

    [Fact]
    public async Task ApplyAsync_Expired_FailsWithoutCallingAnyExecutor()
    {
        var coordinator = new AgentActionCoordinator();
        var executor = new FakeActionExecutor(AgentActionType.DeleteRequest);
        var applier = new AgentActionApplier(coordinator, [executor]);
        var action = new PendingAgentAction
        {
            Id = "a1",
            Type = AgentActionType.DeleteRequest,
            Summary = "S",
            Target = "T",
            Risk = AgentActionRisk.High,
            Preview = "P",
            ExpectedFingerprint = null,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        coordinator.RegisterAction(action);
        action.Confirm();

        var result = await applier.ApplyAsync(action.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(executor.LastApplied);
    }

    [Fact]
    public async Task ApplyAsync_ConfirmedAndFresh_DispatchesToTheExecutorThatCanHandleTheType_AndMarksApplied()
    {
        var coordinator = new AgentActionCoordinator();
        var deleteExecutor = new FakeActionExecutor(AgentActionType.DeleteRequest);
        var createExecutor = new FakeActionExecutor(AgentActionType.CreateRequest);
        var applier = new AgentActionApplier(coordinator, [deleteExecutor, createExecutor]);
        var action = NewAction(AgentActionType.DeleteRequest);
        coordinator.RegisterAction(action);
        action.Confirm();

        var result = await applier.ApplyAsync(action.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(action, deleteExecutor.LastApplied);
        Assert.Null(createExecutor.LastApplied);
        Assert.True(action.IsApplied);
    }

    [Fact]
    public async Task ApplyAsync_AlreadyApplied_FailsAndDoesNotCallTheExecutorAgain()
    {
        var coordinator = new AgentActionCoordinator();
        var executor = new FakeActionExecutor(AgentActionType.DeleteRequest);
        var applier = new AgentActionApplier(coordinator, [executor]);
        var action = NewAction();
        coordinator.RegisterAction(action);
        action.Confirm();
        await applier.ApplyAsync(action.Id, CancellationToken.None);

        var second = await applier.ApplyAsync(action.Id, CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Contains("already applied", second.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_NoExecutorHandlesTheType_FailsWithAClearMessage()
    {
        var coordinator = new AgentActionCoordinator();
        var applier = new AgentActionApplier(coordinator, []); // no executors registered at all
        var action = NewAction();
        coordinator.RegisterAction(action);
        action.Confirm();

        var result = await applier.ApplyAsync(action.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("No executor registered", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_ExecutorThrows_IsCaughtAndReturnedAsAFailureResult_NotMarkedApplied()
    {
        var coordinator = new AgentActionCoordinator();
        var executor = new ThrowingExecutor(AgentActionType.DeleteRequest);
        var applier = new AgentActionApplier(coordinator, [executor]);
        var action = NewAction();
        coordinator.RegisterAction(action);
        action.Confirm();

        var result = await applier.ApplyAsync(action.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(action.IsApplied);
    }

    private sealed class ThrowingExecutor : IAgentActionExecutor
    {
        private readonly AgentActionType _handles;
        public ThrowingExecutor(AgentActionType handles) => _handles = handles;
        public bool CanHandle(AgentActionType type) => type == _handles;
        public Task<AgentActionResult> ApplyAsync(PendingAgentAction action, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }
}
