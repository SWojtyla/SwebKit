using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests.Services;

public sealed class TaskQueueServiceTests
{
    [Fact]
    public void Enqueue_AddsTaskWithRunningStatus()
    {
        var svc = new TaskQueueService();

        svc.Enqueue("Test task");

        Assert.Single(svc.Tasks);
        Assert.Equal(BackgroundTaskStatus.Running, svc.Tasks[0].Status);
        Assert.Equal("Test task", svc.Tasks[0].Title);
    }

    [Fact]
    public void Enqueue_WithDetail_SetsDetail()
    {
        var svc = new TaskQueueService();

        svc.Enqueue("Task", detail: "some detail");

        Assert.Equal("some detail", svc.Tasks[0].Detail);
    }

    [Fact]
    public void Enqueue_ReturnsTaskWithNewId()
    {
        var svc = new TaskQueueService();

        var task = svc.Enqueue("Task");

        Assert.NotEqual(Guid.Empty, task.Id);
    }

    [Fact]
    public void Enqueue_MultipleTasksAreAddedMostRecentFirst()
    {
        var svc = new TaskQueueService();

        svc.Enqueue("First");
        svc.Enqueue("Second");

        // Enqueue inserts at index 0, so newest is first
        Assert.Equal("Second", svc.Tasks[0].Title);
        Assert.Equal("First", svc.Tasks[1].Title);
    }

    [Fact]
    public void TasksChanged_FiredWhenTaskAdded()
    {
        var svc = new TaskQueueService();
        var changed = false;
        svc.TasksChanged += () => changed = true;

        svc.Enqueue("Test");

        Assert.True(changed);
    }

    [Fact]
    public void Complete_Success_SetsCompletedStatus()
    {
        var svc = new TaskQueueService();
        var task = svc.Enqueue("Task");

        svc.Complete(task.Id, success: true);

        Assert.Equal(BackgroundTaskStatus.Completed, svc.Tasks[0].Status);
    }

    [Fact]
    public void Complete_Failure_SetsFailedStatus()
    {
        var svc = new TaskQueueService();
        var task = svc.Enqueue("Task");

        svc.Complete(task.Id, success: false);

        Assert.Equal(BackgroundTaskStatus.Failed, svc.Tasks[0].Status);
    }

    [Fact]
    public void Complete_SetsFinishedAt()
    {
        var svc = new TaskQueueService();
        var before = DateTimeOffset.UtcNow;
        var task = svc.Enqueue("Task");

        svc.Complete(task.Id);

        Assert.NotNull(task.FinishedAt);
        Assert.True(task.FinishedAt >= before);
    }

    [Fact]
    public void Complete_TasksChanged_FiredAfterCompletion()
    {
        var svc = new TaskQueueService();
        var task = svc.Enqueue("Task");
        var changeCount = 0;
        svc.TasksChanged += () => changeCount++;

        svc.Complete(task.Id);

        // At least one more TasksChanged event was fired after the initial Enqueue
        Assert.True(changeCount >= 1);
    }

    [Fact]
    public void Cancel_SetsCancelledStatusAndFinishedAt()
    {
        var svc = new TaskQueueService();
        var task = svc.Enqueue("Task");

        svc.Cancel(task.Id);

        Assert.Equal(BackgroundTaskStatus.Cancelled, task.Status);
        Assert.NotNull(task.FinishedAt);
    }

    [Fact]
    public void Cancel_WithCancellationTokenSource_CancelsToken()
    {
        var svc = new TaskQueueService();
        var cts = new CancellationTokenSource();
        var task = svc.Enqueue("Task", cts: cts);

        svc.Cancel(task.Id);

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_UnknownId_DoesNotThrow()
    {
        var svc = new TaskQueueService();

        var ex = Record.Exception(() => svc.Cancel(Guid.NewGuid()));

        Assert.Null(ex);
    }

    [Fact]
    public void Update_MutatesTask()
    {
        var svc = new TaskQueueService();
        var task = svc.Enqueue("Task");

        svc.Update(task.Id, t => t.Detail = "updated detail");

        Assert.Equal("updated detail", svc.Tasks[0].Detail);
    }

    [Fact]
    public void Update_TasksChanged_Fired()
    {
        var svc = new TaskQueueService();
        var task = svc.Enqueue("Task");
        var changeCount = 0;
        svc.TasksChanged += () => changeCount++;

        svc.Update(task.Id, _ => { });

        Assert.True(changeCount >= 1);
    }

    [Fact]
    public void Update_UnknownId_DoesNotThrow()
    {
        var svc = new TaskQueueService();

        var ex = Record.Exception(() => svc.Update(Guid.NewGuid(), t => t.Detail = "x"));

        Assert.Null(ex);
    }

    [Fact]
    public void Clear_RemovesCompletedTasks_KeepsRunningTasks()
    {
        var svc = new TaskQueueService();
        var running = svc.Enqueue("Running");
        var done = svc.Enqueue("Done");
        svc.Complete(done.Id);

        svc.Clear();

        var titles = svc.Tasks.Select(t => t.Title).ToList();
        Assert.Contains("Running", titles);
        Assert.DoesNotContain("Done", titles);
    }

    [Fact]
    public void Clear_TasksChanged_Fired()
    {
        var svc = new TaskQueueService();
        var task = svc.Enqueue("Task");
        svc.Complete(task.Id);
        var changeCount = 0;
        svc.TasksChanged += () => changeCount++;

        svc.Clear();

        Assert.True(changeCount >= 1);
    }
}
