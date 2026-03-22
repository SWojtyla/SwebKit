# Pitfalls — Blazor / MAUI Hybrid

---

## BL-1 — Missing `@using` in `_Imports.razor` silently breaks components

**Symptom:** A component renders blank. No error, no lifecycle fires, no render output.

**Cause:** Blazor does not auto-import component namespaces from subdirectories. A component in `Components/ServiceBus/EntityTree.razor` that is NOT listed in `_Imports.razor` is treated as an unknown HTML element — `<entitytree>` — with no Blazor behaviour. The build emits a **RZ10012** warning; treat it as a functional error.

**Fix:** Add the namespace immediately when creating a new `Components/` subdirectory.

```razor
@using SwebKit.App.Components.ServiceBus
@using SwebKit.App.Components.Aks
```

**Rule:** after adding a new subdirectory, check `_Imports.razor` before writing any component that uses it.

---

## BL-2 — `StateHasChanged()` must be dispatched via `InvokeAsync` inside async methods

**Symptom:** UI does not update after an `await` completes inside a component method (e.g., loading spinner never disappears, list stays empty).

**Cause:** In MAUI Blazor Hybrid, the Blazor sync context runs on the MAUI dispatcher. After an `await` of an SDK call that uses `ConfigureAwait(false)` internally, you may no longer be on that dispatcher. Calling `StateHasChanged()` directly can silently no-op.

**Fix:**

```csharp
// Wrong
StateHasChanged();

// Correct
await InvokeAsync(StateHasChanged);
```

---

## BL-3 — Set guard state before `await` in `OnParametersSetAsync`

**Symptom:** Data loads twice concurrently; race condition on component fields.

**Cause:** `OnParametersSetAsync` is called on every parent re-render, not just on parameter value changes. If a guard variable (e.g., `_loadedClient`) is set **after** an `await`, a parent re-render arriving during the await sees the guard as unset and triggers a second concurrent load.

**Fix:**

```csharp
// Wrong
if (!ReferenceEquals(_loadedClient, Client))
{
    await LoadAsync();
    _loadedClient = Client; // too late — parent may re-render before this
}

// Correct
if (!ReferenceEquals(_loadedClient, Client))
{
    _loadedClient = Client; // guard first
    await LoadAsync();
}
```

---

## BL-4 — `@if` blocks fully destroy and recreate components

**Symptom:** Collapsing and re-expanding a section resets all component state (loaded data, scroll position, local fields).

**Cause:** `@if (condition) { <MyComponent /> }` disposes the component when the condition turns false and creates a brand-new instance when it turns true. Blazor does not preserve state across this destroy/create cycle.

**Fix:** Lift any state that must survive the toggle to the parent page, a service, or a cascading value. Use `display:none` via inline style if you genuinely need the DOM to persist (rare).

---

## BL-5 — `OnParametersSetAsync` fires on every parent render

**Symptom:** Expensive operation (network call, large computation) runs repeatedly as the user interacts with sibling components.

**Cause:** Blazor calls `SetParametersAsync` (and therefore `OnParametersSetAsync`) on all child components whenever the parent re-renders, even if the parameter values are unchanged.

**Fix:** Guard with a reference or value equality check.

```csharp
if (!ReferenceEquals(_loadedClient, Client))
{
    _loadedClient = Client;
    await LoadAsync();
}
```

---

## BL-6 — JS interop must wait for the DOM (`OnAfterRenderAsync`)

**Symptom:** JS call throws `Cannot read properties of null` or the interop target element is not found.

**Cause:** `OnInitializedAsync` and `OnParametersSetAsync` run before the component's HTML exists in the WebView DOM. Any `IJSRuntime.InvokeAsync` call that targets a DOM element (Monaco editor, xterm.js terminal, chart) will fail if called before the first render.

**Fix:** Use `OnAfterRenderAsync(bool firstRender)` and guard with the `firstRender` flag.

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
        await JS.InvokeVoidAsync("myLib.init", _elementRef);
}
```

---

## BL-7 — `IAsyncEnumerable` streams must be cancelled on component dispose

**Symptom:** Background streaming task (log tail, pod watch) continues running after navigating away; possible `ObjectDisposedException` or stale updates on a dead component.

**Cause:** `await foreach` loops run until the enumerable is exhausted or a `CancellationToken` is cancelled. Navigating away destroys the component but does not cancel the token automatically.

**Fix:** Create a `CancellationTokenSource` tied to the component lifetime and cancel it on dispose.

```csharp
private readonly CancellationTokenSource _cts = new();

public void Dispose() => _cts.Cancel();

// Usage
await foreach (var line in client.StreamLogsAsync(pod, _cts.Token))
    ...
```

---

## BL-8 — Throttle `StateHasChanged` in high-frequency update loops

**Symptom:** UI freezes or becomes unresponsive while streaming log lines or consuming a fast event source.

**Cause:** Calling `await InvokeAsync(StateHasChanged)` per received line saturates the Blazor render queue faster than the WebView can paint.

**Fix:** Buffer updates and flush on a timer or after N items.

```csharp
private readonly List<string> _buffer = [];
private readonly PeriodicTimer _flushTimer = new(TimeSpan.FromMilliseconds(150));

// In a background loop:
_buffer.Add(line);
if (_buffer.Count >= 50)
    await FlushAsync();

// Or timer-driven:
while (await _flushTimer.WaitForNextTickAsync(_cts.Token))
    await FlushAsync();
```

---

## BL-9 — CSS isolation does not apply to HTML injected via `MarkupString`

**Symptom:** CSS rules defined in a `.razor.css` scoped stylesheet have no visible effect on elements that were injected at runtime via a `MarkupString` (e.g., syntax-highlighted `<span>` elements produced by a JS highlighter).

**Cause:** Blazor CSS isolation works by stamping every element rendered by the component with a unique scope attribute (e.g., `b-xxxxxxxx`) and rewriting CSS selectors to require that attribute (`.my-class[b-xxxxxxxx]`). HTML injected via `MarkupString` at runtime is never processed by the Blazor compiler, so those elements do not receive the scope attribute and no scoped rule matches them.

**Fix:** Use the `::deep` combinator in the scoped stylesheet. It tells the CSS isolation build step to emit the scope attribute only on the ancestor, not on the descendant selector.

```css
/* Wrong — spans inside MarkupString never receive [b-xxxxxxxx] */
.aks-yaml-pre .yml-key { color: #9cdcfe; }

/* Correct — ::deep drops the scope requirement on the child */
.aks-yaml-pre ::deep .yml-key { color: #9cdcfe; }
```

**Rule:** any time a component injects raw HTML via `MarkupString` (syntax highlighters, sanitised user content, server-rendered fragments) and needs to style the injected content from a scoped stylesheet, every CSS rule that targets a child element inside the injected HTML must use `::deep`.

---

## BL-10 — Windows `\r\n` line endings survive into JS / HTML and create ghost blank lines

**Symptom:** Content rendered inside a `<pre>` tag (or any whitespace-sensitive element) shows an extra blank line after every real line, or a `\r` character appears at the end of processed text values. The bug is Windows-only; CI (Linux) passes cleanly.

**Cause:** C# raw string literals (`""" ... """`) and strings returned from .NET SDK methods preserve whatever line endings are in the source or data. On Windows these are `\r\n`. When the string is passed to JavaScript via JSInterop and split with `str.split('\n')`, each element retains a trailing `\r`. In HTML, a bare `\r` is treated as a line break by the browser's HTML parser, so each processed "line" gets an invisible extra line break appended to it.

**Fix:** Normalize line endings in C# before passing the string to JS (or before rendering it as HTML):

```csharp
// Normalize \r\n → \n and strip blank/whitespace-only lines
var clean = string.Join('\n', text.ReplaceLineEndings("\n")
    .Split('\n')
    .Where(static l => !string.IsNullOrWhiteSpace(l)));
```

Do this in the C# layer rather than in JS so the fix is guaranteed regardless of WebView JS caching.

**Rule:** whenever a multi-line string from a C# raw literal or an external API is passed to JSInterop or rendered as a `MarkupString`, call `ReplaceLineEndings("\n")` first.

---

## BL-11 — CSS isolation does not reach child components

**Symptom:** A child component renders with no CSS — buttons look like default browser controls, layout classes have no effect, despite the styles clearly existing in the parent page's `.razor.css` file.

**Cause:** Blazor CSS isolation scopes every rule in `PageFoo.razor.css` to elements rendered by `PageFoo.razor` only. The build step rewrites `.my-class` to `.my-class[b-xxxxxxxx]`, where `b-xxxxxxxx` is the page's unique scope attribute. Child components rendered inside the page (`<ChildBar />`) get a *different* scope attribute (`b-yyyyyyyy`), so parent page rules never match their elements.

**Fix:** Create a sibling `.razor.css` file for each component. CSS must live next to the component it styles.

```
Components/
  PipelinesPage.razor          ← page shell only
  PipelinesPage.razor.css      ← page-level layout (split panels, tab bar)
  Pipelines/
    PipelineTree.razor
    PipelineTree.razor.css     ← tree-item styles live HERE, not in the page CSS
    PipelineActivity.razor
    PipelineActivity.razor.css ← activity row styles live HERE
```

Styles that are used across multiple isolated components (status dots, status badges, form inputs, shared text utilities) must be placed in `wwwroot/app.css` so they are not isolated and apply globally.

**Rule:** never write styles for a child component's internal elements in the parent's `.razor.css` file. If a class is used by more than one component, put it in `app.css`.

---

_See also: [azure-sdk.md](azure-sdk.md) · [dotnet-csharp.md](dotnet-csharp.md)_
