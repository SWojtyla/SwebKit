# Decisions — Frontend Code Quality & Architecture Hardening

---

title: "Decisions — Frontend Code Quality & Architecture Hardening"
owner: ""
status: "Proposed"

---

## Decision 001 — Base Class vs Static Helper for LoadAsync Pattern (FQ-10)

**Status:** Proposed

**Date:** 2026-03-27

### Context

20+ components repeat identical try/catch/finally/StateHasChanged boilerplate for loading operations. We need a single reusable mechanism that enforces CS-2 (re-throw OperationCanceledException) and BL-2 (InvokeAsync for StateHasChanged).

Two approaches exist: a base class (`SwebKitComponentBase : ComponentBase`) or a static helper method.

### Decision

**Use a base class: `SwebKitComponentBase : ComponentBase`.**

Rationale:

- Provides `IsLoading` and `ErrorMessage` properties directly on the component, eliminating repeated field declarations
- `RunAsync` method has natural access to `InvokeAsync` (inherited from ComponentBase)
- Enforces patterns at the type level — components that inherit get the safety for free
- Blazor's ComponentBase is already the expected inheritance point; extending it is idiomatic
- Can incorporate `ShouldRender` helpers (FQ-6) later

### Consequences

- All migrated components change their base class from `ComponentBase` to `SwebKitComponentBase`
- Components that already inherit from a different base class need a different approach (static helper as fallback)
- Unit tests must cover `RunAsync` behavior including cancellation re-throw and InvokeAsync dispatch

### Alternatives considered

- **Static helper (`LoadingHelper.RunAsync`)** — avoids inheritance constraint, but requires passing `ComponentBase` reference explicitly, cannot own `IsLoading`/`ErrorMessage` state, more verbose at call sites. Rejected as primary approach but kept as fallback for the rare component that can't change base class.
- **Mixin via interface default methods** — C# interface defaults don't have access to `ComponentBase.InvokeAsync`. Not viable.

---

## Decision 002 — AppStateService Decomposition Strategy (FQ-2)

**Status:** Proposed

**Date:** 2026-03-27

### Context

`AppStateService` is a god service handling configuration, environment management, UI state, initialization, and demo mode. It's exposed as `CascadingValue<AppStateService>` and used by virtually every component. Any decomposition must preserve backward compatibility.

### Decision

**Use the Facade pattern. AppStateService becomes a thin wrapper delegating to focused services.**

New services:

- `IConfigurationService` — load/save AppConfig, profile CRUD
- `IEnvironmentService` — current environment, switch, environment-specific state, events
- `IUiStateService` — UI preferences (theme, sidebar, persisted UI state)
- `IAppInitializer` — first-run, startup sequence, integrity checks

Migration strategy:

1. Extract interfaces and implementations
2. Register them independently in DI (`MauiProgram.cs`)
3. Wire `AppStateService` to delegate to them
4. Keep `CascadingValue<AppStateService>` — it becomes a facade
5. New code injects focused services directly; old code keeps working
6. Over time, migrate old components — but no deadline to remove the facade

### Consequences

- AppStateService remains available (no breaking change)
- New services are independently testable
- DI registration grows by 3-4 services (acceptable)
- Risk: if both facade and direct injection are used for the same operation, state consistency must be ensured by the services themselves (single underlying state)

### Alternatives considered

- **Hard removal of AppStateService** — break every consumer at once. Rejected: too risky for a non-functional change.
- **Keep AppStateService monolithic** — violates SRP and grows with every feature. Rejected: root cause of the problem.
- **Split CascadingValue into multiple CascadingValues** — would require changing every component's parameter declarations. Rejected: too invasive for the migration path.

---

## Decision 003 — CascadingValue vs Parameter Convention (FQ-13)

**Status:** Proposed

**Date:** 2026-03-27

### Context

The codebase inconsistently uses `[CascadingParameter]` and `[Parameter]` for data flow. Some components receive the same data via both mechanisms. There's no documented rule for when to use which.

### Decision

**Establish these rules:**

1. **`CascadingValue` is for app-wide singletons only:**
   - `AppStateService` (or its facade)
   - Theme/appearance context (if introduced)
   - Authentication context (if introduced)

2. **`[Parameter]` is for all component-specific data:**
   - Data items, collections, selected entities
   - Callback functions (`EventCallback<T>`)
   - Configuration flags (`bool ShowDetails`, `int PageSize`)

3. **Never cascade:**
   - Mutable state that changes on user interaction (e.g., selected entity, filter text)
   - Frequently-changing values (triggers full subtree re-render)
   - Data that only 1-2 direct children need (just pass it as `[Parameter]`)

4. **Documentation:**
   - Add a comment `// CascadingValue — app-wide singleton` at the `<CascadingValue>` declaration site
   - Document in component XML comments when a `[CascadingParameter]` is expected

### Consequences

- Clear guideline for all future component work
- Existing violations need to be audited and fixed
- Reduces unnecessary subtree re-renders from cascading mutable state

### Alternatives considered

- **Cascade everything** — simpler but causes render performance issues (Blazor re-evaluates the full subtree on cascading value change). Rejected.
- **No cascading at all** — extreme position, would require threading AppStateService through 10+ levels of parameters. Rejected: cascading exists for this exact purpose.

---

## Decision 004 — EventCallback<T> vs Action/Func Convention (FQ-9)

**Status:** Proposed

**Date:** 2026-03-27

### Context

Component callback parameters inconsistently use `EventCallback<T>`, `Action<T>`, and `Func<T, Task>`. Blazor's `EventCallback<T>` automatically dispatches `StateHasChanged` after invocation; raw delegates do not.

### Decision

**Use `EventCallback<T>` for all UI-triggered callback parameters. Reserve `Action`/`Func` for service-level or non-rendering callbacks only.**

Rules:

- If a callback is triggered by user interaction (click, select, submit) → `EventCallback<T>`
- If a callback is triggered by a service, timer, or background operation → `Func<T, Task>` or `Action<T>` (caller must manage rendering)
- Never use `Action` for callbacks that should trigger UI updates

### Consequences

- Consistent rendering behavior after callbacks
- Migration effort for existing `Action<T>` parameters on UI callbacks
- Clearer component contracts

### Alternatives considered

- **Always use EventCallback<T>** — would mask service-level callbacks that shouldn't trigger renders. Rejected.
- **Always use Func/Action** — requires manual StateHasChanged everywhere. Rejected: error-prone.

---

_(Add further decisions as numbered entries.)_
