# Decisions — Component Decomposition

---

title: "Decisions — Component Decomposition"
owner: ""
status: "Proposed"

---

## Decision 001 — Phase AksPage first

**Status:** Accepted

**Date:** 2026-03-27

### Context

Three page components need decomposition. AksPage is 2,415 lines with 79 private methods and handles 8 resource types, each with their own grid, context menu, YAML viewer, and action handlers. RedisPage is 1,075 lines and ServiceBusPage is 794 lines — both significantly smaller and more cohesive.

### Decision

Start with AksPage decomposition (Phase 1) before touching RedisPage or ServiceBusPage.

### Consequences

- Highest-impact work ships first — largest line count, broadest concern mix
- Risk is front-loaded: the most complex extraction happens when the team is freshest and before technical debt from phased merges accumulates
- RedisPage and ServiceBusPage phases can be informed by patterns established during AksPage extraction
- Each phase ships as an independent PR, so partial completion is safe

### Alternatives considered

- **Bottom-up (ServiceBusPage first):** Rejected — lowest value; ServiceBusPage is already well-decomposed with 6 sub-components and only 794 lines
- **All pages simultaneously:** Rejected — too high regression risk; merge conflicts between three concurrent page refactors

---

## Decision 002 — Drop FQ-2 (AppStateService decomposition)

**Status:** Accepted

**Date:** 2026-03-27

### Context

FQ-2 in the `frontend-quality` feature proposed decomposing `AppStateService` into smaller focused services because it was described as a "god service." After review, `AppStateService` is only 91 lines with clear responsibilities:

- Facade over `ProfileRepository` and `UiStateRepository`
- Initialization lifecycle (`InitializeAsync`, `WhenInitializedAsync`)
- Demo mode toggle
- Delegating CRUD for Service Bus namespaces and message templates to the profile repo

The class has no business logic, no complex branching, and no state management beyond two boolean flags (`IsInitialized`, `UseDemoData`). It exists to provide a single injection point for page components that need config + namespace access, which is exactly the facade pattern.

### Decision

Drop FQ-2 entirely. Do not decompose `AppStateService`.

### Consequences

- Avoids adding complexity: splitting a 91-line facade into 3-4 services would increase injection counts and coupling without reducing cognitive load
- The `CascadingValue` wiring that depends on `AppStateService` remains untouched — no migration risk
- If `AppStateService` grows beyond ~200 lines or gains business logic, revisit this decision
- Removed from the `frontend-quality` feature scope

### Alternatives considered

- **Decompose into ConfigService, NamespaceService, TemplateService:** Rejected — each would be <30 lines; the indirection cost exceeds the value
- **Keep as open item:** Rejected — leaving it open creates false urgency; the class is appropriately sized

---

## Decision 003 — Orchestrator pattern for pages

**Status:** Accepted

**Date:** 2026-03-27

### Context

When extracting sub-components from god pages, we need a clear communication pattern. Options include: (a) pages continue to hold all state and pass everything as parameters, (b) pages become thin orchestrators that route state to children via parameters and receive events back via `EventCallback<T>`, or (c) introduce a shared state service per page.

### Decision

Each page becomes an **orchestrator** component:

- **Data down:** Child components receive data via `[Parameter]` properties
- **Events up:** Child components communicate back via `EventCallback<T>` parameters
- **No new shared state services** — avoids invisible coupling between siblings
- **Page owns:** lifecycle, data loading, error state, CTS management, panel visibility flags
- **Children own:** their own rendering, local UI state (e.g., YAML search text), and action execution

### Consequences

- Clear data flow: parent loads data, children render it
- Testable independently: child components can be tested with mock parameter values
- No hidden dependencies: every data flow is visible in the component's parameter list
- Slightly verbose parameter lists on some components (e.g., AksYamlViewer will have ~10 parameters) — acceptable tradeoff for clarity
- `EventCallback` preserves Blazor's re-render batching automatically
- Follows the same pattern already used successfully by ServiceBus sub-components (EntityTree, MessageListView, etc.)

### Alternatives considered

- **Shared state service per page:** Rejected — adds invisible coupling; harder to test; violates the existing CascadingValue convention (app-wide singletons only)
- **Cascading parameters from page:** Rejected — creates tight coupling to parent type; breaks component reusability; harder to unit test
