# Frontend Plan — Demo Mode Overhaul

## Affected files

- `src/SwebKit.App/Components/Layout/TopBar.razor` — replace checkbox with toggle button
- `src/SwebKit.App/Components/Layout/MainLayout.razor` — add demo banner
- `src/SwebKit.App/Components/Layout/MainLayout.razor.css` — banner styles (or `app.css`)

## `TopBar.razor` changes

Remove:
```html
<label ...>
    <input type="checkbox" checked="@AppState.UseDemoData" @onchange="ToggleDemo" />
    Demo
</label>
```

Replace with a deliberate toggle button:
```html
@if (!AppState.UseDemoData)
{
    <button class="demo-toggle-btn" @onclick="ShowDemoConfirm" title="Enable demo mode">
        Demo
    </button>
}
```

When clicked, show an inline confirmation popover (`FluentPopover` or a small custom `<div>` overlay anchored to the button):

```
┌─────────────────────────────────────┐
│ Enable demo mode?                   │
│ Live connections will be replaced   │
│ with synthetic data.                │
│                                     │
│  [Enable]  [Cancel]                 │
└─────────────────────────────────────┘
```

On "Enable": calls `AppState.SetDemoModeAsync(true)`, closes popover.

When demo is active, the button is hidden (the banner provides the disable action).

## Demo banner (`MainLayout.razor`)

Rendered conditionally inside the `.app-shell` div, below `<TopBar>` and above `<LeftNav>` / `<main>`:

```html
@if (AppState.UseDemoData)
{
    <div class="demo-banner">
        <span>⚠ DEMO MODE — data is synthetic. No live connections are used.</span>
        <button class="demo-banner-disable" @onclick="DisableDemo">Disable</button>
    </div>
}
```

CSS:
```css
.demo-banner {
    grid-area: demo-banner; /* or span full width above main */
    background: var(--color-warning);
    color: #1a1a1a;
    display: flex;
    align-items: center;
    justify-content: center;
    gap: var(--spacing-md);
    padding: var(--spacing-xs) var(--spacing-md);
    font-size: var(--font-size-sm);
    font-weight: 600;
    z-index: 10;
}
.demo-banner-disable {
    background: rgba(0,0,0,0.15);
    border: none;
    border-radius: 3px;
    padding: 2px 10px;
    cursor: pointer;
    font-weight: 600;
}
```

The CSS Grid area for the app shell must be updated to accommodate the banner row (conditionally):
```css
.app-shell {
    grid-template-rows: auto auto 1fr auto; /* topbar, demobanner, main, statusbar */
    grid-template-areas:
        "topbar topbar"
        "demobanner demobanner"
        "leftnav main"
        "statusbar statusbar";
}
/* When no demo banner, the demobanner row collapses to 0 */
```

Alternatively, use a simpler approach: insert the banner as a flex child between topbar and the main content row, adjusting heights via flex layout rather than grid.

## `MainLayout.razor` code changes

```csharp
private async Task DisableDemo()
{
    await AppState.SetDemoModeAsync(false);
    // Optionally: navigate to dashboard to reset page state
    Nav.NavigateTo("/");
}
```

Subscribe to `AppState` change events (or implement `INotifyPropertyChanged`) to re-render the banner when `UseDemoData` changes from another component.

## Handle page reload on toggle

When demo mode is toggled while a feature page is open, the safest approach is to navigate back to `/` (dashboard). This avoids stale client state and forces all pages to re-initialise with the new client implementations. Show a notification: "Demo mode enabled — navigated to dashboard."

## Tasks

- [ ] Remove checkbox from `TopBar.razor`
- [ ] Add demo toggle button with confirmation popover
- [ ] Add demo banner to `MainLayout.razor`
- [ ] Update CSS Grid template to accommodate banner row
- [ ] Implement `DisableDemo` with navigation reset
- [ ] Subscribe to `AppState.UseDemoData` changes for reactive banner render
- [ ] Visual check: banner visible on all pages in demo mode
- [ ] Visual check: banner absent in normal mode
