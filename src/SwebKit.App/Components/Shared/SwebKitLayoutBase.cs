using Microsoft.AspNetCore.Components;

namespace SwebKit.App.Components.Shared;

/// <summary>
/// Base class for SwebKit layout components (LayoutComponentBase derivatives).
/// Mirrors SwebKitComponentBase: provides a ShouldRender gate and RequestCoalescedRender.
///
/// MainLayout uses this so that the full shell chrome is not re-diffed on every
/// background event callback — only when state that affects the layout has actually changed.
/// </summary>
public abstract class SwebKitLayoutBase : LayoutComponentBase
{
    private bool _needsRender = true;
    private bool _renderPending; // coalescing gate

    protected override bool ShouldRender()
    {
        if (!_needsRender) return false;
        _needsRender = false;
        return true;
    }

    /// <summary>
    /// Marks the layout as needing a render on the next cycle.
    /// Must precede an explicit InvokeAsync(StateHasChanged) call when ordering matters.
    /// </summary>
    protected void RequestRender() => _needsRender = true;

    /// <summary>
    /// Coalesces rapid event-driven re-render requests into a single Blazor render cycle.
    /// Safe to call from any thread (uses InvokeAsync internally).
    /// Sets the ShouldRender gate so the queued render is not suppressed.
    /// Use in event callback handlers instead of raw InvokeAsync(StateHasChanged).
    /// </summary>
    protected void RequestCoalescedRender()
    {
        _needsRender = true;
        if (_renderPending) return;
        _renderPending = true;
        _ = InvokeAsync(() =>
        {
            _renderPending = false;
            StateHasChanged();
        });
    }
}
