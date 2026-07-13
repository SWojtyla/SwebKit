using SwebKit.App.Services;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class TrayLifecycleStateTests
{
    [Fact]
    public void TryIncrementUnreadForAlert_DoesNotIncrement_WhenWindowIsVisible()
    {
        var state = new TrayLifecycleState();

        var incremented = state.TryIncrementUnreadForAlert(CreateEvent());

        Assert.False(incremented);
        Assert.Equal(0, state.UnreadAlerts);
    }

    [Fact]
    public void TryIncrementUnreadForAlert_Increments_WhenWindowIsHiddenToTray()
    {
        var state = new TrayLifecycleState();
        state.MarkHiddenToTray();

        var first = state.TryIncrementUnreadForAlert(CreateEvent("pod-a"));
        var second = state.TryIncrementUnreadForAlert(CreateEvent("pod-b"));

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(2, state.UnreadAlerts);
    }

    [Fact]
    public void MarkRestoredFromTray_ClearsUnreadAndStopsHiddenMode()
    {
        var state = new TrayLifecycleState();
        state.MarkHiddenToTray();
        state.TryIncrementUnreadForAlert(CreateEvent());

        state.MarkRestoredFromTray();
        var incrementedAfterRestore = state.TryIncrementUnreadForAlert(CreateEvent("pod-after"));

        Assert.False(state.IsHiddenToTray);
        Assert.False(incrementedAfterRestore);
        Assert.Equal(0, state.UnreadAlerts);
    }

    [Fact]
    public void MarkExplicitExitRequested_DisablesMinimizeToTrayRouting()
    {
        var state = new TrayLifecycleState();

        state.MarkExplicitExitRequested();

        Assert.False(state.ShouldRouteMinimizeToTray);
    }

    [Fact]
    public void TryIncrementUnreadForAlertFired_Increments_WhenWindowIsHiddenToTray()
    {
        var state = new TrayLifecycleState();
        state.MarkHiddenToTray();

        var evt = new SwebKit.Core.Models.AlertFiredEvent(
            "r1", "Test", SwebKit.Core.Models.AlertRuleSource.AksPodHealth,
            SwebKit.Core.Models.AlertSeverity.Warning,
            "fired", string.Empty, DateTimeOffset.UtcNow, "Default");

        var incremented = state.TryIncrementUnreadForAlertFired(evt);

        Assert.True(incremented);
        Assert.Equal(1, state.UnreadAlerts);
    }

    [Fact]
    public void TryIncrementUnreadForAlertFired_DoesNotIncrement_WhenWindowIsVisible()
    {
        var state = new TrayLifecycleState();

        var evt = new SwebKit.Core.Models.AlertFiredEvent(
            "r1", "Test", SwebKit.Core.Models.AlertRuleSource.AksPodHealth,
            SwebKit.Core.Models.AlertSeverity.Warning,
            "fired", string.Empty, DateTimeOffset.UtcNow, "Default");

        var incremented = state.TryIncrementUnreadForAlertFired(evt);

        Assert.False(incremented);
        Assert.Equal(0, state.UnreadAlerts);
    }

    private static PodHealthEvent CreateEvent(string podName = "api-pod")
        => new(
            podName,
            "default",
            "cluster-a",
            PodHealthEventType.PodCrashLoop,
            "Running",
            "CrashLoopBackOff",
            3,
            DateTimeOffset.UtcNow,
            "Container restarted repeatedly");
}
