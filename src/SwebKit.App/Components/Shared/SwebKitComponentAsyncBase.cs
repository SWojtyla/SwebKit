using Microsoft.AspNetCore.Components;

namespace SwebKit.App.Components.Shared;

/// <summary>
/// Async-disposable variant of <see cref="SwebKitComponentBase"/>.
/// Use for components that need <see cref="IAsyncDisposable"/> support while still gaining
/// coalesced renders, performance metrics, and lifecycle helpers.
/// </summary>
public abstract class SwebKitComponentAsyncBase : SwebKitComponentBase, IAsyncDisposable
{
    /// <summary>
    /// Performs asynchronous cleanup. Override for async disposal, but always call <c>base.DisposeAsync()</c>.
    /// The base implementation calls <see cref="SwebKitComponentBase.Dispose"/> synchronously.
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        Dispose();
        await ValueTask.CompletedTask;
    }
}
