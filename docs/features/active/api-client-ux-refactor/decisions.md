# Decisions — API Client UX Refactor

Design choices captured up front. These are proposals to confirm during implementation, not final
until the maintainer approves.

---

## DEC-UX-1: Request tabs ship behind a default-off toggle

**Decision:** Add a user setting `ApiClientRequestTabs` (default `false`). When off, the current
single-request model (DEC-11) is unchanged. When on, an open-requests tab strip is shown.

**Rationale:** The maintainer noted some users want tabs and some do not. A toggle serves both
without forcing a workflow. DEC-11 explicitly stated the design was kept clean "so tabs can be added
later without structural changes" — this honours that intent.

**Implication:** All tab logic must be gated so the off-path has zero behavioural change and no extra
per-tab bookkeeping.

---

## DEC-UX-2: Refactor before tabs

**Decision:** Extract the `ApiClientPage` state container and presentational children (Phase 2)
**before** implementing tabs (Phase 3).

**Rationale:** DEC-11 deferred tabs specifically because per-tab state (in-flight cancellation, dirty
tracking, editor lifecycle) is complex to bolt onto a 2,198-line page. A clean owned state container
makes tabs a list-of-view-models problem instead of a field-explosion problem. It also isolates
regression risk: the refactor is behaviour-preserving and independently verifiable.

**Implication:** Phase 2 must land and pass full tests before Phase 3 starts.

---

## DEC-UX-3: State stays parent/container-owned, not scattered into children

**Decision:** The refactor introduces a page-owned `ApiClientState` object. Extracted components are
presentational: they receive state and raise callbacks. No stateful child holds page-level truth.

**Rationale:** Archived lesson: "Large Blazor workspaces benefit from flattened/virtualized lists and
parent-owned state." Scattering state into children would reintroduce the coupling bugs (splitter,
targeting) the refactor is meant to remove, and risks BL-4 state loss on `@if` toggles.

**Implication:** `ApiClientState` is page-scoped (not a DI singleton). Children never persist page
state across their own dispose/recreate.

---

## DEC-UX-4: One in-flight send per tab; no cross-tab cancellation

**Decision:** Each open tab owns its own `CancellationTokenSource`. Sending in one tab must not
cancel a send in another. Switching tabs does not cancel in-flight work.

**Rationale:** DEC-11 avoided tabs partly to eliminate concurrent-request races. Tabs reintroduce
concurrency, so it must be made explicit and per-tab, following BL-7 (cancel streams on dispose) and
the existing per-request cancellation contract.

**Implication:** Closing a tab cancels that tab's send/subscription/WebSocket session and disposes
its resources.

---

## DEC-UX-5: Iconography is FluentIcon-only

**Decision:** No emoji or literal ASCII glyphs in API Client UI. All icons come from
`Microsoft.FluentUI.AspNetCore.Components` (`Icons.Regular.Size12/16.*`), matching existing usage in
the toolbar and `CollectionTree`.

**Rationale:** The mix of `FluentIcon`, emoji (`🌍 ⚡ 📦 🕐 ⚠`), and literal characters (`v`, `▾`,
`✕`, `●`) reads as unfinished. One vocabulary is cleaner and theme-aware.

**Implication:** If a shared component (e.g. `EmptyState`) only accepts a string icon, extend it to
accept a `FluentIcon` render fragment rather than passing emoji.

---

## DEC-UX-6: Cookie jar is opt-in, machine-local, and secret-scrubbed

**Decision:** Cookie capture is off by default, stored machine-local (in-memory for MVP), applied
explicitly per send rather than via a global `HttpClientHandler.UseCookies`. Cookie values are
scrubbed from all projections (exports, cURL, examples, diffs, linked files).

**Rationale:** Cookies carry session secrets. The archived feature's core safety rule is that
secrets never leak into any projection; cookies must obey the same rule. Global handler cookies
would leak state across unrelated API Client traffic and defeat opt-in.

**Implication:** Phase 4 is deferrable and can ship after Phases 1–3 without blocking them.

---

## DEC-UX-7: Open-tab set is session-only in MVP

**Decision:** The set of open tabs is not persisted across app restart in the MVP.

**Rationale:** Persisting open tabs adds serialization and restore-order concerns that are not needed
to deliver the core value (keeping several requests open during a session). Keep the first pass
small.

**Implication:** Persisting open tabs is an explicit follow-up if requested later.

---

## DEC-UX-8: Request-tab close/cycle shortcuts use Ctrl+Shift+W / Ctrl+PageUp / Ctrl+PageDown

**Decision:** Closing the active request tab is bound to `Ctrl+Shift+W`; cycling to the next/previous
open request tab is bound to `Ctrl+PageDown` / `Ctrl+PageUp`. `Ctrl+W`, `Ctrl+Tab`, and
`Ctrl+Shift+Tab` are **not** reused — they already drive app-level page-tab close/cycle globally
(see `keyboardShortcuts.js` / `MainLayout.OnShortcut`), and overloading them would silently override
that existing behaviour depending on which "tabs" concept the user meant.

**Rationale:** The maintainer asked for shortcuts that are "fluid" and don't require memorizing a
bespoke scheme. `Ctrl+Shift+W` and `Ctrl+PageUp`/`Ctrl+PageDown` are the same chords most mainstream
browsers already use to close/cycle browser tabs, so users already carry the muscle memory — no new
mental model, and no collision with any existing global chord in this app.

**Implication:** These chords are dispatched globally by `keyboardShortcuts.js` (same pattern as the
other `Api*` shortcuts) but are no-ops unless `ApiClientPage` is mounted, subscribed, and
`ApiClientRequestTabs` is enabled with at least one open tab — preserving DEC-UX-1's off-path
guarantee.
