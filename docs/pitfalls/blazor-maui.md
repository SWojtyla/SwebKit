# Pitfalls â€” Blazor / MAUI Hybrid

---

## BL-1 â€” Missing `@using` in `_Imports.razor` silently breaks components

**Symptom:** A component renders blank. No error, no lifecycle fires, no render output.

**Cause:** Blazor does not auto-import component namespaces from subdirectories. A component in `Components/ServiceBus/EntityTree.razor` that is NOT listed in `_Imports.razor` is treated as an unknown HTML element â€” `<entitytree>` â€” with no Blazor behaviour. The build emits a **RZ10012** warning; treat it as a functional error.

**Fix:** Add the namespace immediately when creating a new `Components/` subdirectory.

```razor
@using SwebKit.App.Components.ServiceBus
@using SwebKit.App.Components.Aks
```

**Rule:** after adding a new subdirectory, check `_Imports.razor` before writing any component that uses it.

---

## BL-2 â€” `StateHasChanged()` must be dispatched via `InvokeAsync` inside async methods

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

## BL-3 â€” Set guard state before `await` in `OnParametersSetAsync`

**Symptom:** Data loads twice concurrently; race condition on component fields.

**Cause:** `OnParametersSetAsync` is called on every parent re-render, not just on parameter value changes. If a guard variable (e.g., `_loadedClient`) is set **after** an `await`, a parent re-render arriving during the await sees the guard as unset and triggers a second concurrent load.

**Fix:**

```csharp
// Wrong
if (!ReferenceEquals(_loadedClient, Client))
{
    await LoadAsync();
    _loadedClient = Client; // too late â€” parent may re-render before this
}

// Correct
if (!ReferenceEquals(_loadedClient, Client))
{
    _loadedClient = Client; // guard first
    await LoadAsync();
}
```

---

## BL-4 â€” `@if` blocks fully destroy and recreate components

**Symptom:** Collapsing and re-expanding a section resets all component state (loaded data, scroll position, local fields).

**Cause:** `@if (condition) { <MyComponent /> }` disposes the component when the condition turns false and creates a brand-new instance when it turns true. Blazor does not preserve state across this destroy/create cycle.

**Fix:** Lift any state that must survive the toggle to the parent page, a service, or a cascading value. Use `display:none` via inline style if you genuinely need the DOM to persist (rare).

---

## BL-5 â€” `OnParametersSetAsync` fires on every parent render

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

## BL-6 â€” JS interop must wait for the DOM (`OnAfterRenderAsync`)

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

## BL-7 â€” `IAsyncEnumerable` streams must be cancelled on component dispose

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

## BL-8 â€” Throttle `StateHasChanged` in high-frequency update loops

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

## BL-9 â€” CSS isolation does not apply to HTML injected via `MarkupString`

**Symptom:** CSS rules defined in a `.razor.css` scoped stylesheet have no visible effect on elements that were injected at runtime via a `MarkupString` (e.g., syntax-highlighted `<span>` elements produced by a JS highlighter).

**Cause:** Blazor CSS isolation works by stamping every element rendered by the component with a unique scope attribute (e.g., `b-xxxxxxxx`) and rewriting CSS selectors to require that attribute (`.my-class[b-xxxxxxxx]`). HTML injected via `MarkupString` at runtime is never processed by the Blazor compiler, so those elements do not receive the scope attribute and no scoped rule matches them.

**Fix:** Use the `::deep` combinator in the scoped stylesheet. It tells the CSS isolation build step to emit the scope attribute only on the ancestor, not on the descendant selector.

```css
/* Wrong â€” spans inside MarkupString never receive [b-xxxxxxxx] */
.aks-yaml-pre .yml-key {
  color: #9cdcfe;
}

/* Correct â€” ::deep drops the scope requirement on the child */
.aks-yaml-pre ::deep .yml-key {
  color: #9cdcfe;
}
```

**Rule:** any time a component injects raw HTML via `MarkupString` (syntax highlighters, sanitised user content, server-rendered fragments) and needs to style the injected content from a scoped stylesheet, every CSS rule that targets a child element inside the injected HTML must use `::deep`.

---

## BL-10 â€” Windows `\r\n` line endings survive into JS / HTML and create ghost blank lines

**Symptom:** Content rendered inside a `<pre>` tag (or any whitespace-sensitive element) shows an extra blank line after every real line, or a `\r` character appears at the end of processed text values. The bug is Windows-only; CI (Linux) passes cleanly.

**Cause:** C# raw string literals (`""" ... """`) and strings returned from .NET SDK methods preserve whatever line endings are in the source or data. On Windows these are `\r\n`. When the string is passed to JavaScript via JSInterop and split with `str.split('\n')`, each element retains a trailing `\r`. In HTML, a bare `\r` is treated as a line break by the browser's HTML parser, so each processed "line" gets an invisible extra line break appended to it.

**Fix:** Normalize line endings in C# before passing the string to JS (or before rendering it as HTML):

```csharp
// Normalize \r\n â†’ \n and strip blank/whitespace-only lines
var clean = string.Join('\n', text.ReplaceLineEndings("\n")
    .Split('\n')
    .Where(static l => !string.IsNullOrWhiteSpace(l)));
```

Do this in the C# layer rather than in JS so the fix is guaranteed regardless of WebView JS caching.

**Rule:** whenever a multi-line string from a C# raw literal or an external API is passed to JSInterop or rendered as a `MarkupString`, call `ReplaceLineEndings("\n")` first.

---

## BL-11 â€” CSS isolation does not reach child components

**Symptom:** A child component renders with no CSS â€” buttons look like default browser controls, layout classes have no effect, despite the styles clearly existing in the parent page's `.razor.css` file.

**Cause:** Blazor CSS isolation scopes every rule in `PageFoo.razor.css` to elements rendered by `PageFoo.razor` only. The build step rewrites `.my-class` to `.my-class[b-xxxxxxxx]`, where `b-xxxxxxxx` is the page's unique scope attribute. Child components rendered inside the page (`<ChildBar />`) get a _different_ scope attribute (`b-yyyyyyyy`), so parent page rules never match their elements.

**Fix:** Create a sibling `.razor.css` file for each component. CSS must live next to the component it styles.

```
Components/
  PipelinesPage.razor          â† page shell only
  PipelinesPage.razor.css      â† page-level layout (split panels, tab bar)
  Pipelines/
    PipelineTree.razor
    PipelineTree.razor.css     â† tree-item styles live HERE, not in the page CSS
    PipelineActivity.razor
    PipelineActivity.razor.css â† activity row styles live HERE
```

Styles that are used across multiple isolated components (status dots, status badges, form inputs, shared text utilities) must be placed in `wwwroot/app.css` so they are not isolated and apply globally.

---

## BL-13 â€” E2E `.app-shell` timeout can mean Blazor never mounted, not a slow selector

**Symptom:** Playwright waits for `.app-shell` and times out, while the MAUI window is visibly open and shows only the static `Loading...` text from `wwwroot/index.html`.

**Cause:** The WebView loaded the host page, but the Blazor root component did not mount into `#app`. Treat this as a BlazorWebView/startup failure, not a normal UI timing issue. In this state, changing selector timeouts or waiting longer will not fix the test.

**Observed during validation:** The E2E fixture could connect to WebView2 CDP on `http://localhost:9222`, and CDP sometimes later showed a fully mounted app shell. However, during the failing test run the visible app window stayed on `Loading...`, so the fixture still reported `.app-shell` timeouts. Attempts to switch the fixture to a random CDP port were not reliable in this environment because WebView2 continued to expose the fixed debug endpoint.

**Fix direction:** Before changing assertions, inspect the WebView page through CDP and confirm whether `document.body.innerText` is still just `Loading...` or whether `.app-shell` exists. If the static loading host remains, capture console/runtime errors and diagnose BlazorWebView startup/root-component mounting. Keep the E2E fixture cleanup strict: stop stale `testhost`, `SwebKit.App`, and `msedgewebview2` processes before reruns so the fixture does not attach to an old WebView target.

**Rule:** For E2E failures at startup, first prove whether Blazor mounted. A visible `Loading...` window means the failure is below the app shell and should be documented as unresolved until the WebView startup cause is known.

**Rule:** never write styles for a child component's internal elements in the parent's `.razor.css` file. If a class is used by more than one component, put it in `app.css`.

---

## BL-12 — Calling `OpenAsync` directly on a `@ref` child bypasses parent `@if` re-render

**Symptom:** A panel opens correctly the first time but silently fails to reopen after being closed. No exception; nothing happens when the user triggers the second open.

**Cause:** The parent hosts the child inside `@if (HasOpenPanel)`. After close, `HasOpenPanel = false` and the block collapses. A stale (or Blazor-retained) `@ref` field still points to the child instance. Calling `child.OpenAsync(...)` directly sets internal state on the child and calls `StateHasChanged()` on it — but the parent `HasOpenPanel` is never re-evaluated and no re-render is queued on the parent. The `@if` block stays collapsed, the child updates the DOM it no longer owns, and nothing appears.

**Fix:** Always tell the **parent** to re-render first. Use the pending-open pattern: store the arguments to a nullable field that contributes to the `HasOpenPanel` condition, call `InvokeAsync(StateHasChanged)` on the parent, then drain the pending field in `OnAfterRenderAsync` once the child `@ref` is live.

```csharp
// Wrong — fast-path that bypasses the parent's @if block
public async Task OpenYamlAsync(string kind, string name)
{
    if (_yamlViewer is not null) { await _yamlViewer.OpenAsync(kind, name); return; }
    _pendingYamlOpen = (kind, name);
    await InvokeAsync(StateHasChanged);
}

// Correct — always go through the parent re-render
public async Task OpenYamlAsync(string kind, string name)
{
    _pendingYamlOpen = (kind, name);
    await InvokeAsync(StateHasChanged);
}

private bool HasOpenPanel =>
    _pendingYamlOpen.HasValue || // ← this is what makes the @if block render
    _yamlViewer?.IsOpen == true;

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (_pendingYamlOpen.HasValue && _yamlViewer is not null)
    {
        var (kind, name) = _pendingYamlOpen.Value;
        _pendingYamlOpen = null;
        await _yamlViewer.OpenAsync(kind, name);
    }
}
```

**Rule:** never shortcut to a direct child method call when the parent's `@if` condition depends on child state. The parent must always be the one to re-render and expose the child before its public API is called.

---

## BL-13 — Linked bUnit test hosts must register new injected shell services

**Symptom:** A page or shell component renders fine in the app, but bUnit tests fail with `Cannot provide a value for property ... There is no registered service of type ...` as soon as a new `@inject` dependency is added.

**Cause:** `tests/SwebKit.App.Tests` compiles linked app source files into the test project. When a component starts injecting a new shell service, the test DI container does not pick that up automatically. Existing test constructors keep building a stale service graph until they explicitly register the new dependency and any repositories it needs.

**Fix:** Update the affected test setup in the same change that adds the new `@inject`. If the service depends on shared persistence state, register the same repository instance for all cooperating services in that test host.

```csharp
var uiState = new UiStateRepository();
Services.AddSingleton(uiState);
Services.AddSingleton(new OperatorWorkspaceService(appState, uiState, navigation, providers));
```

**Rule:** whenever a routed page or shared shell component gains a new injected service, patch the relevant bUnit/test host registrations in the same change set instead of waiting for the first broken test run.

---

## BL-14 — MAUI WebView resets textarea value on every Blazor re-render

**Symptom:** Space, Enter, and other character keys appear to do nothing (or undo themselves) while typing in a textarea that is bound via `@oninput` + async JSInterop. The cursor jumps to the end after each keypress.

**Cause (primary):** A parent div has `@onkeydown:preventDefault="@_preventGridKey"` where `_preventGridKey` is set to `true` whenever the user presses an action key (e.g. `Enter`, `y`, `n`). Blazor bakes the value of `_preventGridKey` from the last render into the JS event listener registration. When a textarea inside that div has focus, its `keydown` events bubble up to the parent, and `preventDefault()` is called on them — cancelling the textarea's default action (insert character) before any text reaches the textarea's value.

**Cause (secondary):** `@oninput` on the textarea triggers async JSInterop + `StateHasChanged`. Blazor reconciles the textarea's `value` DOM property from its C# field on every render, resetting whatever the user typed during the async round-trip.

**Fix:**

1. In the textarea's JS initialisation, call `e.stopPropagation()` on every `keydown` event. This prevents bubbling to parent Blazor handlers entirely.
2. Also remove `@oninput` from the textarea and let JS own the value to eliminate the secondary Blazor re-render reset.

```javascript
textareaEl.addEventListener('keydown', function (e) {
  e.stopPropagation(); // block parent @onkeydown:preventDefault from firing
  if (e.key === 'Tab') {
    e.preventDefault(); /* insert spaces */
  }
});
```

**Rule:** any textarea overlay inside a Blazor component that lives under a parent element with `@onkeydown:preventDefault` must call `e.stopPropagation()` in JS before the event reaches the parent. Never rely on Blazor's dynamic `preventDefault` state being correct for input elements — the baked-in value from the last render can be stale.

---

## BL-15 — `@switch` on a tab enum destroys and recreates components on every switch

**Symptom:** Switching away from a tab and returning triggers a full API reload. Load guards (`_loadedProvider`, `_loadedRange`) never suppress the reload because the component is a brand-new instance each time.

**Cause:** `@switch` (and `@if`) fully dispose the component when the branch is no longer active and create a new instance when it becomes active again (BL-4). All local state, including guard fields, is lost.

**Fix:** Render all tabs simultaneously but hide inactive ones with `display:none`. Track visited tabs lazily so only tabs the user has opened are mounted.

```razor
@* In @code *@
private readonly HashSet<Tab> _visitedTabs = [Tab.Overview]; // seed the default tab

private void SetTab(Tab tab)
{
    _tab = tab;
    _visitedTabs.Add(tab); // mark visited so the @if renders it
    StateHasChanged();
}

@* In markup *@
@if (_visitedTabs.Contains(Tab.Overview))
{
    <div style="@(_tab == Tab.Overview ? null : "display:none")">
        <ObservabilityOverview ... />
    </div>
}
@if (_visitedTabs.Contains(Tab.Performance))
{
    <div style="@(_tab == Tab.Performance ? null : "display:none")">
        <ObservabilityPerformance ... />
    </div>
}
```

**Rule:** use `display:none` keep-alive for any set of tabs where the child components make expensive async calls on first render. Always seed the default tab into `_visitedTabs` so it renders immediately.

---

## BL-16 — `OnParametersSetAsync` must guard against redundant loads (BL-5 extension)

**Symptom:** A component fires a new API call every time a sibling component updates the parent, even though the component's own parameters (`Provider`, `Range`) are unchanged. This is especially visible in multi-tab layouts that keep all tab components mounted (see BL-15).

**Cause:** `protected override async Task OnParametersSetAsync() => await LoadAsync();` — no guard — fires the load on every parent re-render regardless of whether parameters actually changed.

**Fix:** Add reference and value guards before the load call. Align with the pattern used in `ObservabilityPerformance` and `ObservabilityFailures`.

```csharp
private IObservabilityProvider? _loadedProvider;
private TimeRange? _loadedRange;

protected override async Task OnParametersSetAsync()
{
    if (Provider is null) return;

    if (ReferenceEquals(_loadedProvider, Provider) && _loadedRange == Range)
        return; // parameters unchanged — skip load

    await LoadAsync();
}

private async Task LoadAsync()
{
    _loadedProvider = Provider; // guard set before first await (BL-3)
    _loadedRange = Range;
    ...
}
```

**Rule:** every component with an `OnParametersSetAsync` that triggers a network call must guard with a `_loaded*` field set **before** the first `await`. Components missing IDisposable must also add it to cancel in-flight requests on dispose.

---

_See also: [azure-sdk.md](azure-sdk.md) · [dotnet-csharp.md](dotnet-csharp.md)_
