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

**Current focus:** Screen 7 done — Settings. Next: manual visual review pass across Screens 6-7, then excluded-screens rework banners.

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
- [x] Bug: "Load More" re-peeked the whole (growing) window from the start of the queue each time and **replaced** `Messages` entirely — on a live queue this could make already-viewed messages disappear when the window shifted. Fixed by adding `fromSequenceNumber` continuation to `IServiceBusClient.PeekMessagesAsync`/`PeekDeadLetterAsync` (`AzureServiceBusClient`, `DemoServiceBusClient`); "Load More" now peeks forward from the last loaded sequence number and **appends** instead of replacing. "Peek" (fresh refresh) still replaces the whole list, which is correct/expected for a manual refresh.
- [x] UX: `MessageListView` toolbar (~15 controls in one unbroken row) grouped into logical clusters (view options / filtering / actions / send-export-nsb / primary Peek) with visual separators and a spacer, instead of one flat row.
- [x] UX: the two stacked bottom bars (`window-status` + Load More, and a separate `CountSummary` bar) merged into a single summary bar.
- [x] Bug: when the active filter matched zero of the currently loaded messages, the "No matches" empty state replaced the whole grid area — including the Load More button — leaving no way to fetch more messages without first clearing the filter. Fixed by moving the window-status/count/Load More bar out of the grid's `else` branch so it renders whenever a window is loaded, regardless of filtered count; "No matches" copy now also hints at Load More when more messages are available (`CanLoadMore`).
- [x] UX: `MessageListView` toolbar polish pass (user-reported "looks so bad" + screenshot): peek-count/auto-refresh `<select>` elements rendered with raw OS combo-box chrome that clashed with the button/pill styling around them → wrapped in `.message-list-view__select-wrap` with `appearance: none` and a custom arrow, matching the app-wide `.filter-select-wrap` convention; the `Filters: On`/`Advanced: On` active-state pills used a saturated solid accent fill (reads as an alarm chip, especially harsh on green-accent themes) → softened to the same subtle-background/accent-text style already used for other active toggles; toolbar-group divider lines lightened (55% opacity) to reduce the "busy grid" feel; density toggle (`Compact/Default/Comfort`) had a hardcoded `border-radius: 4px`/`--font-size-xs` (10px) instead of the shared `--radius-sm` token and the 11px size used by sibling controls, causing subtle misalignment across themes → aligned to shared tokens.
- [x] UX: full toolbar overhaul (user-reported "still horrible... uses half the available space and split on two lines with a random badge", asked for Office ribbon-style density) — the two-row layout from the earlier pass (view+filter on top, actions+primary on bottom) still left row 1 with a large dead gap because the filter group had a fixed `max-width: 440px` and neither row's groups grew to fill remaining width. Merged both rows into a **single** flex row (`message-list-view__toolbar-row`, no more `--top`/`--bottom` split or spacer div): view → filter → actions → primary, where only the filter group has `flex-grow` (like Office's "Tell me" search box) so it absorbs whatever space the other groups don't use, instead of leaving a blank gap or wrapping onto an near-empty second line. The `view-mode-badge` ("PEEK MODE") pill was also the "random badge" — it used a fully-rounded 999px pill shape and 2px/8px padding that stood out from every rectangular `--radius-sm` control around it; changed to the same `--radius-sm` rounding, a fixed 22px height matching buttons/selects, and lighter fill so it reads as part of the toolbar instead of a floating badge. Verified: build passes, all 28 `MessageListViewTests` pass unchanged (no test IDs/markup removed, only container/wrapper restructuring), Aikido scan clean.
- [ ] SB-5 grid audit: DLQ tooltip easter egg ("Sébastien would be proud 🥳") removal
- [ ] Manual visual review (light + dark) — pending user confirmation of toolbar polish pass

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
- [x] Manual visual review (light + dark) — user confirmedI

### Screen 5 — Monitoring ✅ DONE

- [x] Full audit — first pass complete
- [x] Bug: `EmptyState` icon-name strings (`bell-outline`, `add-circle-outline`, `checkmark-circle-outline`) rendered as literal visible text instead of icons → replaced with emoji glyphs consistent with the rest of the app
- [x] Raw buttons/selects (row actions, group header, drawer form fields, drawer footer) reviewed — same deferred `FluentButton`/`AppSelect` cross-cutting decision as Redis, not fixed individually
- [x] Personal/debug content sweep — none found
- [x] Explored: convert "Add rule" from a small centered pop-up to a wider right-anchored drawer with a custom toggle switch → user reviewed and reverted, prefers the original compact modal as-is; no further UI change needed
- [x] Manual visual review (light + dark) — user confirmed good as-is

### Screen 6 — AI Agent (Sebski panel)

- [x] Header action buttons (Clear/Confirm/Cancel/Close) → `AppButton`/`AppIconButton`
- [x] Reviewed "Sebski" personal-branding findings — already stale, current code uses generic "AI Agent" copy/labels, nothing to fix
- [x] History warning threshold (75% of max) and UX reviewed — clear, unit-tested, no change needed
- [x] Resizer (`uiState.js` `SwebKitAgentPanel`) reviewed — clean drag-resize with proper cleanup
- [x] Bug: `Console.WriteLine` debug leak in Mermaid render error handler → replaced with injected `ILogger<AgentChatPanel>`
- [x] Bug: `.agent-bubble__markdown` CSS used the same undefined `--neutral-*` tokens as Monitoring (pitfall BL-17) → table/code/blockquote styling in chat replies was a silent no-op → remapped to real `--color-*` tokens
- [x] Noted unreferenced `ToolExecutionStatus.razor` component (dead code) — cleanup out of scope for this pass
- [ ] Manual visual review (light + dark)

### Screen 7 — Settings

- [x] Nav sidebar `<button>` items — reviewed, well-styled with real tokens and clear active/hover states, no change needed
- [x] Section header area (RoutePageHeader) — subtitle copy is distinct and accurate per section, `Section:` pill reviewed and kept
- [x] Health/readiness report display (`ConfigurationReadinessAreaCard`) — confirmed intentional, proper scoped CSS, no bugs
- [x] Config forms (ServiceBus, AKS, Redis, DevOps, Storage, Observability, ApiClient, Agent) audited for raw controls, debug leaks, personal content, and the `--neutral-*` CSS token bug (BL-17) — none found; noted `AgentConfigForm`/`AksConfigForm`/`DevOpsConfigForm`/`StorageConfigForm` use inline styles instead of scoped CSS (S-4, low severity, deferred)
- [x] Bug: Key Vault list editor's raw remove/add `<button>`s (no `aria-label` on remove) → converted to `AppIconButton`/`AppButton`
- [x] `AgentConfigForm`'s Mistral API key reviewed — uses `PasswordField` + `ICredentialStore` (Windows Credential Manager), never persisted in plain text
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
