namespace SwebKit.Core.Abstractions;

public enum BackgroundTaskStatus { Running, Completed, Failed, Cancelled }

public class BackgroundTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? Detail { get; set; }
    public BackgroundTaskStatus Status { get; set; } = BackgroundTaskStatus.Running;
    public int? Progress { get; set; }
    public int? Total { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public CancellationTokenSource? Cts { get; set; }

    public string ProgressText => Total is > 0 ? $"{Progress}/{Total}" : string.Empty;
}

public interface ITaskQueue
{
    IReadOnlyList<BackgroundTask> Tasks { get; }
    event Action TasksChanged;

    BackgroundTask Enqueue(string title, string? detail = null, CancellationTokenSource? cts = null);
    void Update(Guid id, Action<BackgroundTask> mutate);
    void Complete(Guid id, bool success = true);
    void Cancel(Guid id);
    void Clear();
}
