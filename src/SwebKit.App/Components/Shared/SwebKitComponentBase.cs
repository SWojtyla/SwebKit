using Microsoft.AspNetCore.Components;
using SwebKit.Core.Configuration;

namespace SwebKit.App.Components.Shared;

/// <summary>
/// Base class for SwebKit Razor components. Provides:
/// - IsLoading / ErrorMessage state with BL-2-safe StateHasChanged dispatch
/// - RunAsync helper that enforces CS-2 (OperationCanceledException re-throw)
///   and BL-2 (InvokeAsync after await)
/// - Configurable render coalescing with debounce support
/// - Performance metrics tracking for render optimization
/// </summary>
public abstract class SwebKitComponentBase : ComponentBase, IDisposable
{
    private bool _needsRender = true;
    private bool _renderPending; // coalescing gate
    private CancellationTokenSource? _renderCts;
    private RenderCoalescingOptions? _coalescingOptions;

    protected bool IsLoading { get; set; }
    protected string? ErrorMessage { get; set; }

    /// <summary>
    /// Performance metrics for render coalescing effectiveness.
    /// </summary>
    protected record RenderMetrics(int RequestedCount, int ExecutedCount, int CoalescedCount)
    {
        public double CoalescingRatio => RequestedCount > 0 ? (double)CoalescedCount / RequestedCount : 0;
    }

    private int _renderRequestedCount;
    private int _renderExecutedCount;
    private int _renderCoalescedCount;

    /// <summary>
    /// Gets the current render metrics. Override to integrate with telemetry systems.
    /// </summary>
    protected virtual RenderMetrics GetRenderMetrics() => new(_renderRequestedCount, _renderExecutedCount, _renderCoalescedCount);

    /// <summary>
    /// Logs render metrics for telemetry and performance monitoring.
    /// Override to integrate with logging/telemetry systems.
    /// </summary>
    protected virtual void LogMetrics()
    {
        var metrics = GetRenderMetrics();
        if (metrics.RequestedCount > 0 && metrics.CoalescingRatio < 0.1)
        {
            // Warning: Low coalescing effectiveness may indicate suboptimal debounce tuning
            Console.WriteLine($"[RenderMetrics] {GetType().Name}: Coalescing ratio {metrics.CoalescingRatio:P2} ({metrics.CoalescedCount}/{metrics.RequestedCount} coalesced)");
        }
    }

    /// <summary>
    /// Sets the render coalescing configuration options.
    /// Call from component initialization or dependency injection.
    /// </summary>
    protected void SetCoalescingOptions(RenderCoalescingOptions options)
    {
        _coalescingOptions = options;
    }

    /// <summary>
    /// Gets the debounce window for render coalescing. Override for component-specific tuning.
    /// Uses configured value if available, otherwise returns default 75ms.
    /// </summary>
    protected virtual TimeSpan GetCoalescingDebounce()
    {
        var componentType = GetType().Name;
        if (_coalescingOptions?.ComponentOverrides.TryGetValue(componentType, out var overrideMs) == true)
        {
            return TimeSpan.FromMilliseconds(overrideMs);
        }
        return TimeSpan.FromMilliseconds(_coalescingOptions?.DefaultDebounceMs ?? 75);
    }

    protected override bool ShouldRender()
    {
        if (!_needsRender) return false;
        _needsRender = false;
        _renderExecutedCount++;
        return true;
    }

    /// <summary>
    /// Ensures parameter changes pushed down from a parent component always result in a render.
    /// Without this, the <see cref="ShouldRender"/> gate (which defaults to closed after the first
    /// render) would silently swallow parent-driven updates unless the component happened to call
    /// <see cref="RequestRender"/> itself. Subclasses overriding <c>OnParametersSet</c> or
    /// <c>OnParametersSetAsync</c> are unaffected since this runs independently in the lifecycle.
    /// </summary>
    protected override void OnParametersSet() => RequestRender();

    /// <summary>
    /// Shadows <see cref="ComponentBase.StateHasChanged"/> to also open the
    /// <see cref="ShouldRender"/> gate. Without this, direct StateHasChanged()
    /// calls in derived classes (e.g. event handlers) are silently suppressed
    /// because _needsRender remains false after the previous render.
    /// </summary>
    protected new void StateHasChanged()
    {
        _needsRender = true;
        base.StateHasChanged();
    }

    /// <summary>
    /// Marks the component as needing a render on the next cycle.
    /// Must be called before <see cref="ComponentBase.InvokeAsync(Action)"/> or StateHasChanged.
    /// </summary>
    protected void RequestRender() => _needsRender = true;

    /// <summary>
    /// Marks the component as needing a render and asynchronously requests a UI update.
    /// Use in async methods instead of <c>await InvokeAsync(StateHasChanged)</c>.
    /// </summary>
    protected async Task RequestRenderAsync()
    {
        _needsRender = true;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Coalesces rapid event-driven re-render requests into a single Blazor render cycle.
    /// Safe to call from any thread (uses InvokeAsync internally).
    /// Sets the ShouldRender gate so the queued render is not suppressed.
    /// Uses configurable debounce window via GetCoalescingDebounce().
    /// Use in event callback handlers instead of raw InvokeAsync(StateHasChanged).
    /// </summary>
    protected void RequestCoalescedRender()
    {
        _renderRequestedCount++;
        _needsRender = true;
        if (_renderPending)
        {
            _renderCoalescedCount++;
            return;
        }
        _renderPending = true;

        // Capture into a local so this continuation never re-reads the mutable _renderCts field:
        // Dispose() runs Cancel() -> Dispose() -> field = null on the UI thread, and touching the
        // field again here would race that sequence (ObjectDisposedException surfacing from inside
        // the exception filter, which then goes unobserved since this task is fire-and-forget).
        var cts = _renderCts ??= new CancellationTokenSource();
        _ = InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(GetCoalescingDebounce(), cts.Token);
                _renderPending = false;
                StateHasChanged();
            }
            catch (OperationCanceledException)
            {
                // Render was cancelled, expected during disposal
            }
        });
    }

    /// <summary>
    /// Executes an async operation with standard loading/error handling.
    /// - Sets IsLoading = true, clears ErrorMessage, dispatches StateHasChanged (BL-2)
    /// - Re-throws OperationCanceledException (CS-2)
    /// - Catches other exceptions, sets ErrorMessage
    /// - Sets IsLoading = false, dispatches StateHasChanged in finally
    /// </summary>
    protected async Task RunAsync(Func<Task> work, CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        _needsRender = true;
        await InvokeAsync(StateHasChanged); // BL-2
        try
        {
            await work();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } // CS-2
        catch (OperationCanceledException) { throw; } // CS-2: always re-throw cancellation
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            _needsRender = true;
            await InvokeAsync(StateHasChanged); // BL-2
        }
    }

    /// <summary>
    /// Overload that captures the result.
    /// </summary>
    protected async Task RunAsync<T>(Func<Task<T>> work, Action<T> onSuccess, CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        _needsRender = true;
        await InvokeAsync(StateHasChanged); // BL-2
        try
        {
            var result = await work();
            onSuccess(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } // CS-2
        catch (OperationCanceledException) { throw; } // CS-2
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            _needsRender = true;
            await InvokeAsync(StateHasChanged); // BL-2
        }
    }

    protected void SetError(string? message)
    {
        ErrorMessage = message;
        _needsRender = true;
    }

    protected void ClearError()
    {
        ErrorMessage = null;
        _needsRender = true;
    }

    /// <summary>
    /// Cancels pending renders and cleans up resources.
    /// </summary>
    public virtual void Dispose()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
        GC.SuppressFinalize(this);
    }
}
