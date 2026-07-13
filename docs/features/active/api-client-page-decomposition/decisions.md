# Decisions — API Client Page Decomposition

## DEC-PD-1 — Use plain C# partial class files, not extracted controller classes

**Decision:** Split `ApiClientPage`'s `@code` block into multiple `ApiClientPage.<Concern>.cs`
files declaring `partial class ApiClientPage`, in the same namespace
(`SwebKit.App.Components.ApiClient`), rather than extracting logic into separate
constructor-injected "controller" classes that the page delegates to.

**Rationale:** Initial investigation assumed methods like `ImportCurlAsync` were self-contained
enough to move into an isolated helper class. Reading the actual method bodies showed most
handlers reach into shared page state (`_state`) and call other page methods (e.g.
`GetRequestTargetCollection()`, `ActivateCollection()`, `SaveActiveCollectionAsync()`). Wrapping
that in a separate class would require passing back a handful of delegates/callbacks into the
page, which adds indirection and real behavior-change risk without a testability payoff (the
extracted class still couldn't be tested independently of that page-state web). A same-class
partial-file split gets the readability/navigability win with none of that risk: it is a purely
mechanical move, verified by a full rebuild after each slice.

**Alternative considered:** Extracted controller classes per concern (rejected for this pass —
methods are too entangled with shared `_state` and cross-method calls; revisit only for a concern
that turns out to be genuinely pure).

## DEC-PD-2 — Continuity with DEC-UX-3 (prior `api-client-ux-refactor` feature)

**Decision:** This decomposition does not change where page-level truth lives. `ApiClientState`
remains the single page-owned state container, and presentational child `.razor` components keep
holding zero method bodies (per DEC-UX-3, referenced throughout the current code as comments like
"Method bodies stay on ApiClientPage; this component only wires UI to callbacks (DEC-UX-3)").

**Rationale:** DEC-UX-3 exists to avoid BL-4 (`@if` blocks destroying/recreating child components
and losing their state when the user toggles worksheet modes). Partial `.cs` files are not
components — they are additional source files for the exact same class — so introducing them does
not reintroduce that risk. This decomposition is compatible with, not a reversal of, DEC-UX-3.

## DEC-PD-3 — Extract genuinely pure logic opportunistically, not as a blanket goal

**Decision:** While splitting a concern into its own partial file, if a specific piece of logic
turns out to be pure (no `_state` mutation, no call into another page method), pull it into a
small standalone class with xunit coverage. Do not force this for logic that is not actually pure
just to hit a testability target.

**Rationale:** The goal is honest, low-risk improvement, not a rewrite. Most "business logic" for
this feature already lives in `SwebKit.Core.Services.ApiClientWorkflowService` (already
unit-tested); what remains in the page is largely UI-state orchestration glue, which is expected
to stay page-owned.

## DEC-PD-4 — New convention: `ApiClientPage.<Concern>.cs` partial naming

**Decision:** Name each partial file `ApiClientPage.<Concern>.cs` (e.g. `ApiClientPage.Curl.cs`,
`ApiClientPage.Tabs.cs`) — same convention shape as the standard Razor `.razor.cs` code-behind
file, extended to multiple concern-scoped files since this is the first place in the codebase that
needed more than one code-behind file per component.

**Rationale:** No prior `.razor.cs` code-behind file exists anywhere in this codebase (verified via
search) — everything else uses inline `@code` blocks, which is appropriate for smaller components.
`ApiClientPage` is the first component large enough to need this pattern.
