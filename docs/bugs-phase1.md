# Phase 1 Bug Report

Bugs discovered after first run of the Phase 1 scaffold. Repro environment: Windows 11, MAUI Blazor Hybrid / WebView2.

---

## BUG-001 — Unicode HTML entities render as raw text in nav icons

**Severity:** Medium (visual)
**Files:** [LeftNav.razor](../src/SwebKit.App/Components/Layout/LeftNav.razor), [AksPage.razor](../src/SwebKit.App/Components/Pages/AksPage.razor)

**Symptoms:**

- Left nav shows literal text like `&#128193; Projects`, `&#128200; Observability`, `&#9881; AKS / Settings` instead of rendered emoji/symbols.
- AKS page Refresh button shows `&#8635; Refresh (F5)` as literal text.

**Root cause:**
HTML numeric character references (`&#128193;` etc.) are passed as Razor string parameter values (e.g. `Icon="&#128193;"`). Blazor treats component parameter strings as plain text, not HTML — so the entity is never decoded by the browser. The same applies to C# string interpolation in Razor (`@(IsLoading ? "Loading..." : "&#8635; Refresh (F5)")`): the string is text-node-encoded, not injected as raw HTML.

**Fix approach:** Use actual Unicode characters in string literals (e.g. `Icon="📁"`) or use `MarkupString` / `@((MarkupString)"&#128193;")` where raw HTML injection is acceptable. For the `FluentButton` content, use a child `<span>` with `@((MarkupString)"&#8635;")` or switch to Fluent UI icon components.

**Resolution (2026-03-07):** Fixed. Replaced HTML entities with Unicode glyphs in `LeftNav.razor` and AKS refresh button text in `AksPage.razor`.

---

## BUG-002 — Command Palette button in TopBar does nothing

**Severity:** High (feature broken)
**Files:** [TopBar.razor](../src/SwebKit.App/Components/Layout/TopBar.razor), [MainLayout.razor](../src/SwebKit.App/Components/Layout/MainLayout.razor)

**Symptoms:**
Clicking the `>_ Command Palette` button in the top bar has no visible effect. Ctrl+P works correctly.

**Root cause:**
`TopBar.razor:71` publishes `CommandPaletteRequestedEvent` via `IAppEventBus`. However, `MainLayout.razor` never subscribes to `CommandPaletteRequestedEvent` — its `OnInitialized` only subscribes to `NavigateToAreaEvent`. The Ctrl+P shortcut works because `keyboardShortcuts.js` invokes `OnShortcut("CommandPalette")` directly on the `MainLayout` JS-invokable method, bypassing the event bus entirely.

**Fix approach:** Subscribe to `CommandPaletteRequestedEvent` in `MainLayout.OnInitialized` and set `IsCommandPaletteOpen = true`, or change the button's `@onclick` to publish via `IAppEventBus` and handle it — or simplest: make the button also call `JS.InvokeVoidAsync` / change `OpenCommandPalette()` in TopBar to raise an EventCallback that MainLayout handles.

**Resolution (2026-03-07):** Fixed. `MainLayout.razor` now subscribes to `CommandPaletteRequestedEvent`, opens the palette when received, and unsubscribes on dispose.

---

## BUG-003 — No commands registered in CommandRegistry

**Severity:** Low (UX gap, expected for Phase 1)
**Files:** [CommandPalette.razor](../src/SwebKit.App/Components/Shared/CommandPalette.razor), `CommandRegistry` (SwebKit.Core)

**Symptoms:**
Opening the command palette (via Ctrl+P) always shows "No commands found" regardless of query text.

**Root cause:**
`CommandRegistry` is empty — no commands are registered anywhere in Phase 1. The palette works mechanically (search, keyboard nav, execute) but has nothing to execute.

**Fix approach (Phase 2):** Register navigation commands (`Navigate to Service Bus`, `Navigate to AKS`, etc.) and action commands (`Refresh`, `New Project`, `Peek Messages`, etc.) in a startup service or via feature modules calling `CommandRegistry.Register(...)`.

**Resolution (2026-03-07):** Fixed for baseline command set. `MainLayout.razor` now registers default commands at startup (navigation commands + refresh current area), so the command palette returns actionable results.

---

## BUG-004 — No UI to configure per-environment connections (Service Bus, App Insights, AKS)

**Severity:** Critical (blocks all feature areas)
**Files:** [ProjectEditDialog.razor](../src/SwebKit.App/Components/Pages/ProjectEditDialog.razor)

**Symptoms:**
After creating a project and adding environments, there is no way to enter:

- Service Bus connection string / namespace
- Application Insights connection string / workspace ID / query API endpoint
- AKS kubeconfig context / default namespace

As a result, the Service Bus, Observability, and AKS pages all land on their "not configured" empty states with no path to configure them from the UI.

**Root cause:**
`ProjectEditDialog` only exposes `Name`, `Description`, `IconColor`, and a flat list of environments (name + production flag). The `ProjectEnvironment` model has `ServiceBusConfig`, `AppInsightsConfig`, and `AksConfig` properties but there is no UI section to fill them in.

**Fix approach:** Add an environment configuration section inside `ProjectEditDialog` (or a dedicated `EnvironmentEditDialog`) with fields for each integration. Fields should be conditional — only show AKS section if AKS tab is selected, etc. Connection strings / secrets should be stored via `ICredentialStore`, not plain text in the project model.

**Resolution (2026-03-07):** Fixed.

- Added per-environment config UI inside `ProjectEditDialog.razor` using expandable integration sections.
- Added reusable settings components: `ServiceBusConfigForm.razor`, `ObservabilityConfigForm.razor`, `AksConfigForm.razor`.
- Service Bus connection strings are stored via `ICredentialStore` (Windows Credential Manager) and referenced by `CredentialRef`.
- Added deep-copy behavior in `ProjectEditDialog` to preserve existing integration configs during edits.

---

## Summary Table

| ID      | Area              | Severity | Status |
| ------- | ----------------- | -------- | ------ |
| BUG-001 | UI / Nav icons    | Medium   | Closed |
| BUG-002 | Command Palette   | High     | Closed |
| BUG-003 | Command Registry  | Low      | Closed |
| BUG-004 | Project/Env setup | Critical | Closed |

## Post-Phase Follow-up Bugs

## BUG-005 — Service Bus auth mode selector resets and save feedback unclear

**Severity:** High (configuration blocked)
**Files:** [ServiceBusConfigForm.razor](../src/SwebKit.App/Components/Pages/ServiceBusConfigForm.razor)

**Symptoms:**

- Selecting `Connection String` in Service Bus settings immediately reverts to default auth mode.
- Save action appears to do nothing when required fields are missing or when state is reset.

**Root cause:**

- `OnParametersSet` rehydrated local form state from `Environment.ServiceBusConfig` on every render, overwriting user edits before save.
- Save flow had no explicit field validation message or save success message.

**Resolution (2026-03-07):** Fixed.

- Form state now initializes only when environment context changes (not on every render).
- Added validation for namespace and required connection string when auth mode is `ConnectionString`.
- Added explicit success/error feedback in the form.
- Clarified Project dialog integration text to indicate integration changes are persisted when `Save Project` is clicked.

## Updated Summary Table

| ID      | Area                        | Severity | Status |
| ------- | --------------------------- | -------- | ------ |
| BUG-001 | UI / Nav icons              | Medium   | Closed |
| BUG-002 | Command Palette             | High     | Closed |
| BUG-003 | Command Registry            | Low      | Closed |
| BUG-004 | Project/Env setup           | Critical | Closed |
| BUG-005 | Service Bus settings editor | High     | Closed |

## Verification Snapshot (2026-03-07)

| Check                           | Result                         | Notes                                                      |
| ------------------------------- | ------------------------------ | ---------------------------------------------------------- |
| Build (Windows target)          | Pass                           | Verified earlier with MAUI Windows target command.         |
| Core tests                      | Pass (13/13)                   | `tests/SwebKit.Core.Tests`                                 |
| Azure tests                     | Pass (2/2)                     | `tests/SwebKit.Azure.Tests`                                |
| Kubernetes tests                | Pass (1/1)                     | `tests/SwebKit.Kubernetes.Tests`                           |
| Full `dotnet test SwebKit.slnx` | Fails on non-Windows MAUI TFMs | Expected in local env without Android/iOS/macOS SDK setup. |
