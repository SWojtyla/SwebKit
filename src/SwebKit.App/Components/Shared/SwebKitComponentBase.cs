using Microsoft.AspNetCore.Components;

namespace SwebKit.App.Components.Shared;

/// <summary>
/// Base class for SwebKit Razor components. Provides:
/// - IsLoading / ErrorMessage state with BL-2-safe StateHasChanged dispatch
/// - RunAsync helper that enforces CS-2 (OperationCanceledException re-throw)
///   and BL-2 (InvokeAsync after await)
/// </summary>
public abstract class SwebKitComponentBase : ComponentBase
{
    private bool _needsRender = true;

    protected bool IsLoading { get; private set; }
    protected string? ErrorMessage { get; private set; }

    protected override bool ShouldRender()
    {
        if (!_needsRender) return false;
        _needsRender = false;
        return true;
    }

    /// <summary>
    /// Marks the component as needing a render on the next cycle.
    /// Must be called before <see cref="ComponentBase.InvokeAsync(Action)"/> or StateHasChanged.
    /// </summary>
    protected void RequestRender() => _needsRender = true;

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
}
