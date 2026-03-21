# Frontend Plan — Command Palette & Keyboard-First Navigation

## Affected files

- `src/SwebKit.App/Components/Shared/CommandPalette.razor` — significant rewrite
- `src/SwebKit.App/Components/Shared/CommandPalette.razor.css`
- `src/SwebKit.App/Components/Shared/KeyboardShortcutsPanel.razor` — new
- `src/SwebKit.App/Components/Layout/MainLayout.razor` — skip-to-content link, shortcuts panel toggle
- `src/SwebKit.App/wwwroot/js/keyboardShortcuts.js` — new `?` shortcut + grid key handlers
- All feature pages — grid keyboard handlers
- All modals and panels — focus trap JSInterop

## `CommandPalette.razor` rewrite

### Layout

```
┌──────────────────────────────────────────┐
│ > Search commands...                     │  ← input, auto-focused
├──────────────────────────────────────────┤
│ RECENT                                   │
│  ↺  Restart deployment        Alt+R      │
│  ↺  Peek messages             Ctrl+P     │
├──────────────────────────────────────────┤
│ AKS                                      │
│     Restart deployment        Alt+R      │
│  ▶  Scale deployment                     │
│     Open pod logs                        │
├──────────────────────────────────────────┤
│ GLOBAL                                   │
│     Refresh                   F5         │
│     Open Settings             Alt+5      │
└──────────────────────────────────────────┘
```

### Fuzzy search

Simple fuzzy: iterate query characters in order through label string. Score by consecutive matches and match position (earlier = higher score). No external library needed.

```csharp
private static bool FuzzyMatch(string query, string label, out int score)
{
    query = query.ToLowerInvariant();
    label = label.ToLowerInvariant();
    int qi = 0; score = 0; int lastMatch = -1;
    for (int i = 0; i < label.Length && qi < query.Length; i++)
    {
        if (label[i] == query[qi])
        {
            score += (i == lastMatch + 1) ? 2 : 1; // consecutive bonus
            lastMatch = i; qi++;
        }
    }
    return qi == query.Length;
}
```

### Recent section

- On palette open: read `CommandRegistry.RecentCommandIds`, resolve to commands (skip any no longer registered)
- Shown only when input is empty
- Label: "RECENT" section header

### Available/unavailable

- Only `CommandRegistry.GetAvailable(CurrentArea)` commands shown
- Context-dependent commands greyed out if `IsAvailable()` returns false but `AreaScope` matches — or hidden entirely (simpler, less confusing)

### Keyboard handling

- `↑`/`↓` — move highlighted row
- `Enter` — execute highlighted command; call `CommandRegistry.RecordUsed(id)`; close palette
- `Escape` — close; restore focus
- Type any character — filter input receives it (input is always focused)

### Shortcut display

Right-aligned `<kbd>` tag in each row for commands with a shortcut.

## `KeyboardShortcutsPanel.razor`

Full-page overlay (or slide-in from right) listing all registered commands grouped by category. Opened via the `?` shortcut or a "Keyboard shortcuts" command in the palette.

Close via `Escape` or a × button.

## Grid keyboard navigation (per feature page)

Pattern to apply in each grid component using `@onkeydown` on the grid container:

```csharp
private void OnGridKeyDown(KeyboardEventArgs e)
{
    switch (e.Key)
    {
        case "ArrowDown": SelectNext(); break;
        case "ArrowUp":   SelectPrev(); break;
        case "Enter":     OpenDetailPanel(SelectedItem); break;
        case "Escape":    CloseDetailPanel(); break;
        case "Delete":
            if (!e.Target.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
                PromptDelete(SelectedItem);
            break;
    }
}
```

Apply to: `MessageListView`, `PodGrid`, `DeploymentGrid`, `StatefulSetGrid`, `ConfigMapGrid`, `SecretGrid`, `IngressGrid`, `CronJobGrid`, `HelmGrid`, `RedisKeyList`, `StorageBlobList`, `ReleaseBoard`.

## Focus trap (JSInterop)

New JS function in `keyboardShortcuts.js`:

```js
SwebKit.trapFocus = (element) => {
    const focusables = element.querySelectorAll(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])');
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    element._trapHandler = (e) => {
        if (e.key !== 'Tab') return;
        if (e.shiftKey ? document.activeElement === first : document.activeElement === last) {
            e.preventDefault();
            (e.shiftKey ? last : first).focus();
        }
    };
    element.addEventListener('keydown', element._trapHandler);
    first?.focus();
};

SwebKit.releaseTrap = (element) => {
    element.removeEventListener('keydown', element._trapHandler);
};
```

Apply in `ConfirmDialog.razor`, `Modal.razor`, `CommandPalette.razor`, `PortForwardStartDialog.razor`, and all detail side panels:
```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender) await JS.InvokeVoidAsync("SwebKit.trapFocus", _panelRef);
}
public async ValueTask DisposeAsync()
{
    await JS.InvokeVoidAsync("SwebKit.releaseTrap", _panelRef);
    // restore focus to trigger element via stored ElementReference
}
```

## Skip-to-content link (`MainLayout.razor`)

```html
<a href="#main-content" class="skip-link">Skip to main content</a>
...
<main id="main-content" class="main-content" tabindex="-1">
    @Body
</main>
```

CSS:
```css
.skip-link {
    position: absolute;
    top: -100px;
    left: var(--spacing-md);
    background: var(--color-accent);
    color: white;
    padding: var(--spacing-xs) var(--spacing-md);
    border-radius: 0 0 4px 4px;
    transition: top 0.1s;
    z-index: var(--z-overlay);
}
.skip-link:focus { top: 0; }
```

## `?` shortcut (`keyboardShortcuts.js`)

Add to the shortcut handler:
```js
case '?':
    if (!isTypingContext(e)) dotNetRef.invokeMethodAsync('OnShortcut', 'KeyboardShortcuts');
    break;
```

`MainLayout.OnShortcut` handles `"KeyboardShortcuts"` by toggling the shortcuts panel.

## Tasks

- [ ] Rewrite `CommandPalette.razor` (fuzzy search, recent, categories, shortcut display)
- [ ] Implement `FuzzyMatch` utility method
- [ ] Create `KeyboardShortcutsPanel.razor`
- [ ] Add `?` shortcut to `keyboardShortcuts.js` + `MainLayout.OnShortcut`
- [ ] Add skip-to-content link to `MainLayout.razor`
- [ ] Apply grid keyboard nav (`↑`/`↓`/`Enter`/`Escape`/`Delete`) to all grids
- [ ] Implement `SwebKit.trapFocus` / `releaseTrap` in JS
- [ ] Apply focus trap to `ConfirmDialog`, `Modal`, command palette, all detail panels
- [ ] Register area commands in all feature pages + push selection to `ISelectionContext`
- [ ] Keyboard-only walkthrough of every feature area
