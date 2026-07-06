# Status — company-readiness-polish

---

title: "Status - company-readiness-polish"
owner: ""
state: "Planned"
jira: ""
branch: ""
started: "2026-07-03"
last_updated: "2026-07-03"

---

## Quick Summary

Screen-by-screen polish pass before company sharing. No new features — fix sloppy UI, inconsistent primitives, personal content, and known rough edges.

**Current focus:** Screen 5 — Monitoring.

## Progress Checklist

### Cross-cutting

- [x] Decision recorded: `<a>` anchor vs `AppButton` for header navigation links — settled by the pre-existing `style-system-polish-9` precedent: anchors that navigate to another page/section (e.g. "Settings") stay `<a>`; only in-place action buttons become `AppButton`. Confirmed via `IncidentTimelinePage`/`StoragePage` both using the identical Settings-link pattern.
- [ ] Decision needed: `FluentButton` vs `AppButton` — Redis panels use `FluentButton` pervasively (Refresh/Delete/Edit/Scan/etc.) while ServiceBus/AKS lean on `AppButton`. Not fixed in this pass — needs an explicit convention decision before a broad refactor.

### Screen 1 — Service Bus

- [ ] Entra auth error: blocked — `https://servicebus.azure.com/` service principal not consented in Portima tenant (tenant admin action required)
- [ ] Configurable default peek count
- [ ] Breadcrumb `sb-breadcrumb-back` and `sb-breadcrumb-btn` raw buttons → AppButton
- [ ] Header action slot audit
- [ ] Empty states consistent and copy-reviewed
- [ ] SB-5 grid audit: DLQ tooltip easter egg ("Sébastien would be proud 🥳") removal
- [ ] Manual visual review (light + dark)

### Screen 2 — AKS ✅ DONE

- [x] Remove/neutralise `"Suspiciously fine. — SW"` easter egg → now shows "All pods healthy"
- [x] `System.Diagnostics.Debug.WriteLine` debug leak → replaced with `Logger.LogWarning`
- [x] Connection bar and toolbar controls reviewed for consistency (context/namespace pickers intentional custom inputs)
- [x] Regression: `AppButton Ghost` swap for Refresh overlapped the namespace picker → reverted to `FluentButton Appearance.Stealth`
- [x] Bug: container detail panel stayed open with stale pod after selecting a different row → now switches to the newly selected pod's containers instead of closing (`SwitchOrCloseContainerDetail`)
- [x] Bug: app froze/became unresponsive when switching resource-type tabs repeatedly → BL-4 fix: `@switch` (destroy/recreate grids) replaced with always-mounted `<div hidden>` wrappers so `FluentDataGrid` virtualization isn't torn down and rebuilt every click
- [x] Bug: namespace multi-select dropdown's sticky `All/None/Apply` footer overlapped the last namespace row → restructured to flex column layout (scrollable list + non-sticky footer) instead of padding+sticky hack
- [x] Bug: YAML viewer "loads forever, no error shown" / HPA detail + YAML panes rendered mixed together → root cause was `AksDetailPanels` not re-rendering after the child `AksYamlViewer` finished its async open; fixed by re-rendering the parent after the child operation completes
- [x] Improvement: HPA detail + its YAML view now render as two tabs in one pane (matching the existing Logs/Containers tab pattern) instead of one replacing the other
- [x] Deferred by design: raw `<input type="checkbox">` "Show completed" filter — tight CSS integration, low severity, left as-is
- [x] Manual visual review — user confirmed fixed

### Screen 3 — Redis ✅ DONE

- [x] Replace `FluentButton` "Load More" in key tree panel → `AppButton`
- [x] Panel heading `<h2>Keys</h2>` — reviewed, consistent local pattern, no change needed
- [x] Workspace status bar (`redis-workspace-status`) — reviewed, consistent styling, no change needed
- [x] `RedisConnectionBar` raw `<select class="cache-selector">` → `AppSelect`
- [x] Detail panels (`RedisKeyDetail`, `RedisToolbar`, `RedisKeyspaceHealthExplorer`, `RedisPubSubPanel`) reviewed — no personal/debug content; pervasive `FluentButton` usage documented as a deferred cross-cutting decision, not fixed
- [x] UX: "No key selected"/"Loading key details" → shared `EmptyState`/`LoadingSpinner` components
- [x] UX: "No keys found" copy now hints at next action (broaden pattern / hit Scan)
- [x] UX bug: Insights drawer had no visible expand/collapse affordance (marker was hidden with nothing replacing it) → added rotating chevron + hover tint
- [x] Bug: hash/zset `FluentDataGrid` rows looked cramped/overlapping, especially when a row entered inline-edit mode (edit `<input>` taller than the fixed row content) → gave `RedisKeyDetail` grid rows a consistent min-height/padding via `::deep` rules and constrained editor inputs to `border-box` sizing
- [x] Manual visual review (light + dark) — user confirmed

### Screen 4 — Storage ✅ DONE

- [x] `<select class="storage-select">` account picker → `AppSelect`
- [x] Header Settings `<a>` reviewed — resolves the anchor-vs-button cross-cutting decision (see below)
- [x] Bug: `StorageMutationBanner` had no CSS at all — "Mutation mode is active" warning rendered as unstyled plain text → added scoped CSS with warning/info tinted variants
- [x] Storage body panels (`StorageContainerTree`, `StorageBlobList`, `BlobDetailPane`, upload/copy dialogs) reviewed — no personal/debug content, `ctx-item` raw buttons match the established cross-app context-menu convention
- [x] `⚠` warning glyphs (empty state + mutation banner icon) → full-emoji-presentation glyphs (`🚫`, `⚠️`)
- [x] Bug: Storage (and Service Bus, Key Vault, App Insights) Entra ID auth silently authenticated as an unrelated service principal instead of the signed-in developer → root cause was `DefaultAzureCredential`'s `EnvironmentCredential` (tried before `AzureCliCredential`) winning because `AZURE_CLIENT_ID`/`TENANT_ID`/`CLIENT_SECRET` were set machine-wide for an unrelated tool; fixed by excluding `EnvironmentCredential` via a new shared `SwebKit.Core.Services.AzureCredentialFactory`, now used by every Entra-authenticated client in the app instead of each constructing `DefaultAzureCredential` inline. Documented as pitfall AZ-4.
- [x] Bug: `RoutePageHeader`'s "context-hidden" mode (title/subtitle hidden, only Settings action shown) left oversized whitespace above the account badges → tightened header/support-strip margins for that variant
- [x] Manual visual review (light + dark) — user confirmed

### Screen 5 — Monitoring

- [x] Full audit — first pass complete
- [x] Bug: `EmptyState` icon-name strings (`bell-outline`, `add-circle-outline`, `checkmark-circle-outline`) rendered as literal visible text instead of icons → replaced with emoji glyphs consistent with the rest of the app
- [x] Raw buttons/selects (row actions, group header, drawer form fields, drawer footer) reviewed — same deferred `FluentButton`/`AppSelect` cross-cutting decision as Redis, not fixed individually
- [x] Personal/debug content sweep — none found
- [ ] Manual visual review (light + dark)

### Screen 6 — AI Agent (Sebski panel)

- [ ] `<button class="top-bar-icon-btn agent-panel-header-btn">` → AppIconButton
- [ ] Confirm/cancel clear-history button row — polish
- [ ] Empty state copy review ("What's going on in your cluster?" — ok for company use?)
- [ ] Manual visual review (light + dark)

### Screen 7 — Settings

- [ ] Nav sidebar `<button>` items — review active-state pattern consistency
- [ ] Section header area (RoutePageHeader) — consistent subtitle copy per section
- [ ] Health/readiness report display — confirm it looks intentional
- [ ] Manual visual review (light + dark)

### Excluded screens — rework banner

- [ ] Pipelines: add visible "under rework" notice to the page
- [ ] Observability: add visible "under rework" notice to the page
- [ ] Incident Timeline: add visible "under rework" notice to the page
- [ ] API Client: add visible "under rework" notice to the page

### Final gate

- [ ] Build passes (`dotnet build`)
- [ ] All existing tests pass (`dotnet test`)
- [ ] No new raw button/select regressions in inventory (`scripts/style-inventory.ps1`)
