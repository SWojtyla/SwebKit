using SwebKit.App.Services;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class NotificationServiceTests
{
    [Fact]
    public void ShowSuccess_AddsSuccessNotification()
    {
        var svc = new NotificationService();

        svc.ShowSuccess("Done");

        var n = Assert.Single(svc.All);
        Assert.Equal(NotificationSeverity.Success, n.Severity);
        Assert.Equal("Done", n.Message);
        Assert.Null(n.Detail);
    }

    [Fact]
    public void ShowInfo_AddsInfoNotification()
    {
        var svc = new NotificationService();

        svc.ShowInfo("FYI", "some detail");

        var n = Assert.Single(svc.All);
        Assert.Equal(NotificationSeverity.Info, n.Severity);
        Assert.Equal("some detail", n.Detail);
    }

    [Fact]
    public void ShowWarning_AddsWarningNotification()
    {
        var svc = new NotificationService();

        svc.ShowWarning("Careful");

        Assert.Equal(NotificationSeverity.Warning, Assert.Single(svc.All).Severity);
    }

    [Fact]
    public void ShowError_AddsErrorNotification()
    {
        var svc = new NotificationService();

        svc.ShowError("Failed");

        Assert.Equal(NotificationSeverity.Error, Assert.Single(svc.All).Severity);
    }

    [Fact]
    public void ShowError_WithException_UsesExceptionMessageAsDetail()
    {
        var svc = new NotificationService();

        svc.ShowError("Failed", ex: new InvalidOperationException("bad state"));

        Assert.Equal("bad state", Assert.Single(svc.All).Detail);
    }

    [Fact]
    public void ShowError_WithDetailAndException_AppendExceptionMessageToDetail()
    {
        var svc = new NotificationService();

        svc.ShowError("Failed", "context info", new InvalidOperationException("root cause"));

        Assert.Equal("context info: root cause", Assert.Single(svc.All).Detail);
    }

    [Fact]
    public void ShowSuccess_FiresNotificationsChanged()
    {
        var svc = new NotificationService();
        var fired = 0;
        svc.NotificationsChanged += () => fired++;

        svc.ShowSuccess("ok");

        Assert.Equal(1, fired);
    }

    [Fact]
    public void ShowError_FiresNotificationsChanged()
    {
        var svc = new NotificationService();
        var fired = 0;
        svc.NotificationsChanged += () => fired++;

        svc.ShowError("boom");

        Assert.Equal(1, fired);
    }

    [Fact]
    public void MultipleCalls_EachFiresOneEvent()
    {
        var svc = new NotificationService();
        var fired = 0;
        svc.NotificationsChanged += () => fired++;

        svc.ShowSuccess("a");
        svc.ShowInfo("b");
        svc.ShowWarning("c");

        Assert.Equal(3, fired);
        Assert.Equal(3, svc.All.Count);
    }

    [Fact]
    public void Dismiss_RemovesNotificationById()
    {
        var svc = new NotificationService();
        svc.ShowSuccess("a");
        svc.ShowSuccess("b");
        var id = svc.All[0].Id;

        svc.Dismiss(id);

        Assert.Single(svc.All);
        Assert.DoesNotContain(svc.All, n => n.Id == id);
    }

    [Fact]
    public void Dismiss_FiresNotificationsChanged()
    {
        var svc = new NotificationService();
        svc.ShowSuccess("a");
        var fired = 0;
        svc.NotificationsChanged += () => fired++;

        svc.Dismiss(svc.All[0].Id);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void ClearAll_RemovesAllNotifications()
    {
        var svc = new NotificationService();
        svc.ShowSuccess("a");
        svc.ShowError("b");

        svc.ClearAll();

        Assert.Empty(svc.All);
    }

    [Fact]
    public void ClearAll_FiresNotificationsChanged()
    {
        var svc = new NotificationService();
        svc.ShowSuccess("a");
        var fired = 0;
        svc.NotificationsChanged += () => fired++;

        svc.ClearAll();

        Assert.Equal(1, fired);
    }

    [Fact]
    public void All_ReturnsSnapshot_NotLiveReference()
    {
        var svc = new NotificationService();
        svc.ShowSuccess("a");
        var snapshot = svc.All;

        svc.ShowSuccess("b");

        Assert.Single(snapshot);
        Assert.Equal(2, svc.All.Count);
    }

    [Fact]
    public void ConcurrentAdd_DoesNotThrow()
    {
        var svc = new NotificationService();

        Parallel.For(0, 100, i => svc.ShowSuccess($"msg-{i}"));

        Assert.Equal(100, svc.All.Count);
    }

    [Fact]
    public void EachNotification_HasUniqueId()
    {
        var svc = new NotificationService();

        svc.ShowSuccess("a");
        svc.ShowSuccess("b");
        svc.ShowSuccess("c");

        var ids = svc.All.Select(n => n.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
