using Xunit;

namespace SwebKit.Agents.Tests;

public class AgentActionCoordinatorTests
{
    [Fact]
    public void RegisterAction_StoresAction()
    {
        var coordinator = new AgentActionCoordinator();
        var action = new PendingAgentAction
        {
            Id = "test-1",
            Type = AgentActionType.CreateRequest,
            Summary = "Test action",
            Target = "Test target",
            Risk = AgentActionRisk.Low,
            Preview = "Test preview",
            ExpectedFingerprint = null,
        };

        var id = coordinator.RegisterAction(action);

        Assert.Equal("test-1", id);
        var retrieved = coordinator.GetAction("test-1");
        Assert.NotNull(retrieved);
        Assert.Equal("Test action", retrieved.Summary);
    }

    [Fact]
    public void GetAction_Nonexistent_ReturnsNull()
    {
        var coordinator = new AgentActionCoordinator();
        Assert.Null(coordinator.GetAction("nonexistent"));
    }

    [Fact]
    public void GetAction_Expired_ReturnsNull()
    {
        var coordinator = new AgentActionCoordinator();
        var action = new PendingAgentAction
        {
            Id = "expired-1",
            Type = AgentActionType.DeleteRequest,
            Summary = "Expired",
            Target = "Target",
            Risk = AgentActionRisk.High,
            Preview = "Preview",
            ExpectedFingerprint = null,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        coordinator.RegisterAction(action);
        Assert.Null(coordinator.GetAction("expired-1"));
    }

    [Fact]
    public void GetPendingActions_ReturnsOnlyActive()
    {
        var coordinator = new AgentActionCoordinator();

        coordinator.RegisterAction(new PendingAgentAction
        {
            Id = "active-1",
            Type = AgentActionType.CreateRequest,
            Summary = "Active",
            Target = "T",
            Risk = AgentActionRisk.Low,
            Preview = "P",
            ExpectedFingerprint = null,
        });

        coordinator.RegisterAction(new PendingAgentAction
        {
            Id = "expired-1",
            Type = AgentActionType.CreateRequest,
            Summary = "Expired",
            Target = "T",
            Risk = AgentActionRisk.Low,
            Preview = "P",
            ExpectedFingerprint = null,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        coordinator.RegisterAction(new PendingAgentAction
        {
            Id = "rejected-1",
            Type = AgentActionType.CreateRequest,
            Summary = "Rejected",
            Target = "T",
            Risk = AgentActionRisk.Low,
            Preview = "P",
            ExpectedFingerprint = null,
        });
        coordinator.RejectAction("rejected-1");

        var pending = coordinator.GetPendingActions();
        Assert.Single(pending);
        Assert.Equal("active-1", pending[0].Id);
    }

    [Fact]
    public void RejectAction_RemovesFromStore()
    {
        var coordinator = new AgentActionCoordinator();
        coordinator.RegisterAction(new PendingAgentAction
        {
            Id = "reject-me",
            Type = AgentActionType.DeleteRequest,
            Summary = "S",
            Target = "T",
            Risk = AgentActionRisk.High,
            Preview = "P",
            ExpectedFingerprint = null,
        });

        coordinator.RejectAction("reject-me");
        Assert.Null(coordinator.GetAction("reject-me"));
    }

    [Fact]
    public void CleanupExpired_RemovesExpiredActions()
    {
        var coordinator = new AgentActionCoordinator();

        coordinator.RegisterAction(new PendingAgentAction
        {
            Id = "active",
            Type = AgentActionType.CreateRequest,
            Summary = "S",
            Target = "T",
            Risk = AgentActionRisk.Low,
            Preview = "P",
            ExpectedFingerprint = null,
        });

        coordinator.RegisterAction(new PendingAgentAction
        {
            Id = "expired",
            Type = AgentActionType.CreateRequest,
            Summary = "S",
            Target = "T",
            Risk = AgentActionRisk.Low,
            Preview = "P",
            ExpectedFingerprint = null,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        coordinator.CleanupExpired();

        Assert.NotNull(coordinator.GetAction("active"));
        Assert.Null(coordinator.GetAction("expired"));
    }

    [Fact]
    public void RegisterAction_EnforcesBoundedStore()
    {
        var coordinator = new AgentActionCoordinator();

        // Fill to max
        for (var i = 0; i < coordinator.MaxPendingActions; i++)
        {
            coordinator.RegisterAction(new PendingAgentAction
            {
                Id = $"action-{i}",
                Type = AgentActionType.CreateRequest,
                Summary = $"Action {i}",
                Target = "T",
                Risk = AgentActionRisk.Low,
                Preview = "P",
                ExpectedFingerprint = null,
            });
        }

        // Adding one more should evict the oldest
        coordinator.RegisterAction(new PendingAgentAction
        {
            Id = "new-action",
            Type = AgentActionType.CreateRequest,
            Summary = "New",
            Target = "T",
            Risk = AgentActionRisk.Low,
            Preview = "P",
            ExpectedFingerprint = null,
        });

        Assert.Null(coordinator.GetAction("action-0")); // oldest evicted
        Assert.NotNull(coordinator.GetAction("new-action"));
    }

    [Fact]
    public void PendingAgentAction_IsExpired_True_WhenPastExpiry()
    {
        var action = new PendingAgentAction
        {
            Id = "test",
            Type = AgentActionType.CreateRequest,
            Summary = "S",
            Target = "T",
            Risk = AgentActionRisk.Low,
            Preview = "P",
            ExpectedFingerprint = null,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };

        Assert.True(action.IsExpired);
    }

    [Fact]
    public void PendingAgentAction_Confirm_SetsIsConfirmed()
    {
        var action = new PendingAgentAction
        {
            Id = "test",
            Type = AgentActionType.CreateRequest,
            Summary = "S",
            Target = "T",
            Risk = AgentActionRisk.Low,
            Preview = "P",
            ExpectedFingerprint = null,
        };

        Assert.False(action.IsConfirmed);
        action.Confirm();
        Assert.True(action.IsConfirmed);
    }

    [Fact]
    public void PendingAgentAction_MarkApplied_SetsIsApplied()
    {
        var action = new PendingAgentAction
        {
            Id = "test",
            Type = AgentActionType.CreateRequest,
            Summary = "S",
            Target = "T",
            Risk = AgentActionRisk.Low,
            Preview = "P",
            ExpectedFingerprint = null,
        };

        Assert.False(action.IsApplied);
        action.MarkApplied();
        Assert.True(action.IsApplied);
    }
}
