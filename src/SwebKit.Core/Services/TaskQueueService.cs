using SwebKit.Core.Abstractions;
using SwebKit.Core.Constants;

namespace SwebKit.Core.Services;

public class TaskQueueService : ITaskQueue
{
    private readonly List<BackgroundTask> _tasks = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<BackgroundTask> Tasks
    {
        get { lock (_lock) return _tasks.ToList(); }
    }

    public event Action? TasksChanged;

    public BackgroundTask Enqueue(string title, string? detail = null, CancellationTokenSource? cts = null)
    {
        var task = new BackgroundTask { Title = title, Detail = detail, Cts = cts };
        lock (_lock) _tasks.Insert(0, task);
        TasksChanged?.Invoke();
        return task;
    }

    public void Update(Guid id, Action<BackgroundTask> mutate)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task is not null) mutate(task);
        }
        TasksChanged?.Invoke();
    }

    public void Complete(Guid id, bool success = true)
    {
        Update(id, t =>
        {
            t.Status = success ? BackgroundTaskStatus.Completed : BackgroundTaskStatus.Failed;
            t.FinishedAt = DateTimeOffset.UtcNow;
        });

        // Auto-remove completed tasks after 5 seconds
        _ = RemoveAfterDelayAsync(id);
    }

    public void Cancel(Guid id)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task is null) return;
            task.Cts?.Cancel();
            task.Status = BackgroundTaskStatus.Cancelled;
            task.FinishedAt = DateTimeOffset.UtcNow;
        }
        TasksChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_lock) _tasks.RemoveAll(t => t.Status != BackgroundTaskStatus.Running);
        TasksChanged?.Invoke();
    }

    private async Task RemoveAfterDelayAsync(Guid id)
    {
        await Task.Delay(Limits.TaskCompletionDelayMs);
        lock (_lock)
            _tasks.RemoveAll(t => t.Id == id && t.Status != BackgroundTaskStatus.Running);
        TasksChanged?.Invoke();
    }
}
