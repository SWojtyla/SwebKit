namespace SwebKit.App.Components.Shared;

/// <summary>
/// Manages a periodic refresh timer for a Blazor component.
/// Instantiate in OnInitialized, dispose in DisposeAsync.
/// </summary>
public sealed class AutoRefreshController : IAsyncDisposable
{
    private System.Timers.Timer? _timer;
    private Func<Task>? _callback;

    /// <summary>
    /// Sets the refresh interval in seconds. Pass 0 to stop.
    /// </summary>
    public void SetInterval(int seconds, Func<Task> callback)
    {
        Stop();
        _callback = callback;

        if (seconds <= 0) return;

        _timer = new System.Timers.Timer(seconds * 1000) { AutoReset = true };
        _timer.Elapsed += OnElapsed;
        _timer.Start();
    }

    public void Stop()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Elapsed -= OnElapsed;
        _timer.Dispose();
        _timer = null;
    }

    private void OnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        _ = _callback?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }
}
