# Frontend Plan — Redis TTL Visualisation

## Affected files

- `src/SwebKit.App/Components/Redis/RedisKeyDetail.razor` — update TTL section
- `src/SwebKit.App/Components/Redis/RedisKeyDetail.razor.css` — add progress bar styles
- `src/SwebKit.App/Helpers/TtlFormatter.cs` — new static utility class

## `TtlFormatter` utility

Static class in `SwebKit.App/Helpers/` (or `SwebKit.Core/Utilities/`).

```csharp
public static class TtlFormatter
{
    public static string Format(long ttlSeconds) => ttlSeconds switch
    {
        -1 => "No expiry",
        -2 => "Key has no TTL / already expired",
        0 => "Expired",
        < 60 => $"{ttlSeconds}s remaining",
        < 3600 => $"{ttlSeconds / 60}m {ttlSeconds % 60}s remaining",
        _ => $"{ttlSeconds / 3600}h {ttlSeconds % 3600 / 60}m remaining"
    };

    public static string SeverityClass(long ttlSeconds, long? capturedTtl) =>
        ttlSeconds < 60 ? "ttl-critical" :
        ttlSeconds < 300 ? "ttl-warning" :
        capturedTtl is null ? "ttl-ok" :
        (double)ttlSeconds / capturedTtl.Value < 0.05 ? "ttl-critical" :
        (double)ttlSeconds / capturedTtl.Value < 0.20 ? "ttl-warning" :
        "ttl-ok";

    public static double BarFillPercent(long ttlSeconds, long? capturedTtl)
    {
        if (capturedTtl is null or 0) return Math.Min(1.0, ttlSeconds / 3600.0);
        return Math.Clamp((double)ttlSeconds / capturedTtl.Value, 0.0, 1.0);
    }
}
```

## `RedisKeyDetail.razor` TTL section

Current state: TTL shown as raw number in a property row.

New TTL section:

```html
<div class="ttl-section">
    <div class="ttl-label @TtlFormatter.SeverityClass(_currentTtl, _capturedTtl)">
        @TtlFormatter.Format(_currentTtl)
    </div>
    @if (_currentTtl > 0)
    {
        <div class="ttl-bar-track">
            <div class="ttl-bar-fill @TtlFormatter.SeverityClass(_currentTtl, _capturedTtl)"
                 style="width: @(TtlFormatter.BarFillPercent(_currentTtl, _capturedTtl) * 100)%">
            </div>
        </div>
    }
    <button class="ttl-edit-btn" @onclick="OpenSetTtlPopover">Set TTL</button>
</div>
```

### Component state

- `long _currentTtl` — initialised from `KeyDetail.Ttl` when panel opens; decremented client-side
- `long? _capturedTtl` — set once on panel open (same as initial `_currentTtl` if > 0; null if -1/-2)
- `PeriodicTimer? _countdown` — 1-second tick; decrements `_currentTtl` and calls `StateHasChanged()`
- `PeriodicTimer? _refresh` — 30-second tick; calls `IRedisClient.GetKeyDetailAsync` to re-sync TTL

Lifecycle:
- `OnParametersSetAsync`: initialise `_currentTtl`, `_capturedTtl`, start timers
- `IAsyncDisposable.DisposeAsync`: dispose both timers

### Set TTL popover

Inline popover (or `FluentDialog` minimised as a popover) with:
- Numeric input (seconds) or a MM:SS helper input
- "No expiry" checkbox
- Confirm / Cancel buttons
- On confirm: call `IRedisClient.SetTtlAsync(key, ttlSeconds)` → refresh TTL display → show notification (once notification system exists)

## CSS additions to `RedisKeyDetail.razor.css`

```css
.ttl-bar-track {
    height: 4px;
    background: var(--color-border);
    border-radius: 2px;
    margin: 4px 0;
    overflow: hidden;
}
.ttl-bar-fill {
    height: 100%;
    border-radius: 2px;
    transition: width 1s linear;
}
.ttl-bar-fill.ttl-ok    { background: var(--color-success); }
.ttl-bar-fill.ttl-warning { background: var(--color-warning); }
.ttl-bar-fill.ttl-critical { background: var(--color-error); }

.ttl-label.ttl-warning { color: var(--color-warning); }
.ttl-label.ttl-critical { color: var(--color-error); }
```

## Tasks

- [ ] Create `TtlFormatter` utility with Format, SeverityClass, BarFillPercent
- [ ] Update `RedisKeyDetail.razor` TTL section (label + bar + set button)
- [ ] Implement 1-second countdown timer with proper disposal
- [ ] Implement 30-second refresh to re-sync with server TTL
- [ ] Implement Set TTL popover
- [ ] Wire Set TTL call to `IRedisClient`
- [ ] Write CSS for bar, colours, label states
- [ ] Unit tests for `TtlFormatter`
