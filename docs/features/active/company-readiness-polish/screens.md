# Screen Notes — company-readiness-polish

Per-screen first-pass issue list. User to revalidate each section before work starts on that screen.

---

## Screen 1 — Service Bus

**State:** Planned | **Priority:** High (most used screen)

### Issues found

| #    | Location                           | Issue                                                                                                                                                                                                          | Severity |
| ---- | ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- |
| SB-1 | `ServiceBusPage.razor` L60–70      | `<button class="sb-breadcrumb-back">` is raw — not AppButton                                                                                                                                                   | Low      |
| SB-2 | `ServiceBusPage.razor` L77–82      | `<button class="sb-breadcrumb-btn">` (Batch Send, Investigate) are raw                                                                                                                                         | Low      |
| SB-3 | `ServiceBusPage.razor` header      | `<AppButton>` already used for Ctrl+K — good. Confirm consistency with the above.                                                                                                                              | Info     |
| SB-4 | `ServiceBusPage.razor` empty state | Empty state uses `<a href="/settings?section=servicebus">` — fine as an anchor                                                                                                                                 | Info     |
| SB-5 | `ServiceBusGrid.razor` DLQ tooltip | Personal easter egg "Sébastien would be proud 🥳" when DLQ = 0 → neutralise to "No dead letters". Sort and row-action buttons are raw but tightly styled in data-grid context — intentional, no change needed. | Low      |

### Acceptance criteria

- Breadcrumb back/action buttons use AppButton with correct Variant.
- No visual regression in the workspace tab layout.
- Empty state copy is clear to a new user who has never configured a namespace.

---

## Screen 2 — AKS

**State:** Done (fixes applied, user review pending) | **Priority:** High (core use case)

### Issues found

| #     | Location                                  | Issue                                                                                                                                                                                                                                                                                  | Severity | Resolution                                                                                                                                                                                                                |
| ----- | ----------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AKS-1 | `AksPage.razor` ~L70                      | `_allPodsGreenBanner` showed `"🎉 Everything's fine. Suspiciously fine. — SW"` — personal content                                                                                                                                                                                      | **High** | ✅ Fixed — now shows "All pods healthy"                                                                                                                                                                                   |
| AKS-2 | `AksPage.razor` ResourceFilter slot       | Raw `<input type="checkbox">` with `<label class="resource-filter-check">` for "Show completed" pods                                                                                                                                                                                   | Low      | ⏭ TODO — label+input is tightly CSS-integrated (`resource-filter-check` scoped style); converting to FluentCheckbox requires CSS rework; defer to user review                                                            |
| AKS-3 | `AksPage.razor` RoutePageHeader           | No Actions slot — intentional?                                                                                                                                                                                                                                                         | Info     | ✅ Confirmed intentional — toolbar contains all actions                                                                                                                                                                   |
| AKS-4 | `AksConnectionBar.razor`                  | Context/namespace pickers are custom searchable inputs (intentional). Namespace multi-select checkboxes inside dropdown are intentional.                                                                                                                                               | Info     | ✅ Confirmed intentional — no change                                                                                                                                                                                      |
| AKS-5 | Detail panels                             | Side panel open/close animation — confirm no jank                                                                                                                                                                                                                                      | TBD      | ⏭ TODO — requires runtime check, no code issue found                                                                                                                                                                     |
| AKS-6 | `AksPage.razor` L1150                     | `System.Diagnostics.Debug.WriteLine` in dataset load exception handler — debug leak in production                                                                                                                                                                                      | Low      | ✅ Fixed — replaced with `Logger.LogWarning`                                                                                                                                                                              |
| AKS-7 | `AksConnectionBar.razor` Refresh button   | First-pass swap `FluentButton Stealth` → `AppButton Ghost` (AKS-4) made the Refresh button wider/bordered and it overlapped the namespace picker in the toolbar row                                                                                                                    | **High** | ✅ Reverted — Refresh button back to `FluentButton Appearance.Stealth` (compact, correct for a dense toolbar)                                                                                                             |
| AKS-8 | `AksDetailPanels.razor` / `AksPage.razor` | Selecting a different Deployment/StatefulSet/Pod row while a container detail panel was open left the previous pod's container panel showing (stale target)                                                                                                                            | **High** | ✅ Fixed — `CloseContainerDetail()` made public; called from `SelectDeployment`, `SelectStatefulSet`, `SelectPod`                                                                                                         |
| AKS-9 | `AksPage.razor` resource-type tab bar     | App became fully unresponsive when switching resource-type tabs (Deployments/Pods/etc.) repeatedly. Root cause: BL-4 — `@switch` destroyed/recreated the `FluentDataGrid` (Virtualize=true) on every tab click, tearing down and rebuilding JS-side virtualization observers each time | **High** | ✅ Fixed — replaced `@switch` with always-mounted `<div hidden="@(...)">` wrappers per resource type; grids stay mounted, only CSS visibility toggles. Data was already preloaded by `LoadAsync`, so no extra fetch cost. |

### Acceptance criteria

- Easter egg text is removed. ✅
- When all pods are green, shows a neutral status indicator. ✅
- "Show completed" filter is a Fluent-style checkbox. ⏭ (deferred)
- No regression in port-forward session UI. (verify at runtime)
- Container detail panel closes when selecting a different resource. ✅
- Switching resource-type tabs repeatedly does not freeze or lag the app. ✅ (verify at runtime)

---

## Screen 3 — Redis

**State:** Planned

### Issues found

| #   | Location               | Issue                                                                                                                 | Severity |
| --- | ---------------------- | --------------------------------------------------------------------------------------------------------------------- | -------- |
| R-1 | `RedisPage.razor` ~L55 | `<FluentButton @onclick="LoadMoreKeysAsync">` — inconsistent, use AppButton                                           | Low      |
| R-2 | `RedisPage.razor` ~L47 | `<h2>Keys</h2>` panel heading + `<span>` count — raw, check against panel heading pattern                             | Low      |
| R-3 | `RedisPage.razor` ~L35 | `redis-workspace-status` bar — span-based count display. Confirm visual style matches other workspace status patterns | Low      |
| R-4 | `RedisPage.razor`      | `ConnectionBar` — check for raw selects                                                                               | TBD      |
| R-5 | Redis detail panels    | Not yet read — audit copy/export/value operation controls                                                             | TBD      |

### Acceptance criteria

- Load More uses AppButton.
- Panel heading `<h2>` follows the same heading pattern as AKS/ServiceBus panels.
- Workspace status bar is visually consistent with other screens.

---

## Screen 4 — Storage

**State:** Planned

### Issues found

| #    | Location                   | Issue                                                                                                                    | Severity   |
| ---- | -------------------------- | ------------------------------------------------------------------------------------------------------------------------ | ---------- |
| ST-1 | `StoragePage.razor` ~L47   | `<select class="storage-select">` for multi-account picker — use AppSelect                                               | **Medium** |
| ST-2 | `StoragePage.razor` header | `<a class="page-header-action-btn" href="/settings?section=storage">Settings</a>` — anchor; apply cross-cutting decision | Low        |
| ST-3 | `StorageMutationBanner`    | Not yet read — check prominence and copy. A "mutations enabled" warning is critical UX for company users.                | **Medium** |
| ST-4 | Storage body panels        | Not yet read — audit blob list, container selector, download controls                                                    | TBD        |
| ST-5 | `StoragePage.razor` ~L58   | `<EmptyState Icon="⚠">` for unavailable account — emoji icon, inconsistent with other empty states that use themed icons | Low        |

### Acceptance criteria

- Account picker uses AppSelect when multiple accounts exist.
- MutationBanner is prominent and legible for first-time users — they must not accidentally delete blobs.
- Emoji icons in empty states replaced with FluentIcon or consistent symbol set.

---

## Screen 5 — Pipelines

**State:** OUT OF SCOPE — Full rework required (separate feature)

The Pipelines screen needs a full layout, tab model, scope picker, and UX flow rework that goes well beyond a polish pass. It is excluded from this feature.

**Action in this feature:** Add a visible "under rework" notice to the page so company users understand improvement is coming.

---

## Screen 6 — Observability

**State:** OUT OF SCOPE — Full rework required (separate feature)

The Observability screen needs a full FluentUI migration, resource selector redesign, and per-tab layout overhaul. It is excluded from this feature.

**Action in this feature:** Add a visible "under rework" notice to the page.

---

## Screen 7 — Incident Timeline

**State:** OUT OF SCOPE — Full rework required (separate feature)

The Incident Timeline screen needs a full scope toolbar, evidence layout, and summary section redesign. It is excluded from this feature.

**Action in this feature:** Add a visible "under rework" notice to the page.

---

## Screen 5 — Monitoring

**State:** Planned — not yet read in detail

### First-pass notes

Not read yet. Investigation needed during implementation pass.

**Expected audit areas:**

- Alert rule list and controls
- Create/edit alert form controls
- Notification display

### Acceptance criteria (provisional)

- Controls use app primitives consistently.
- Alert rule list is readable without horizontal scroll.
- Empty state when no rules are configured is actionable.

---

## Screen 9 — API Client

**State:** OUT OF SCOPE — Full rework required (separate feature)

The API Client is the most complex screen in the app and has its own visual direction. A polish pass would not be sufficient — it needs a dedicated rework feature.

**Action in this feature:** Add a visible "under rework" notice to the page.

---

## Screen 6 — AI Agent (Sebski panel)

**State:** Planned

### Issues found

| #    | Location                       | Issue                                                                                                    | Severity |
| ---- | ------------------------------ | -------------------------------------------------------------------------------------------------------- | -------- |
| AG-1 | `AgentChatPanel.razor` ~L32–43 | `<button class="top-bar-icon-btn agent-panel-header-btn">` × 4 instances — raw, use AppIconButton        | Low      |
| AG-2 | `AgentChatPanel.razor` ~L17    | Panel label is `aria-label="Sebski chat panel"` — fine for company use                                   | Info     |
| AG-3 | `AgentChatPanel.razor`         | Empty state copy: `"Hey, I'm Sebski. What's going on in your cluster?"` — friendly, fine for company use | Info     |
| AG-4 | `AgentChatPanel.razor`         | History warning pill shown when nearly full — confirm the threshold and UX are clear                     | Low      |
| AG-5 | `AgentChatPanel.razor`         | Resizer implementation (`agent-panel-resizer`) — confirm it works smoothly                               | TBD      |
| AG-6 | `AgentChatPanel.razor`         | Tool usage display (`agent-tools-used`) — not yet read, audit                                            | TBD      |

### Acceptance criteria

- Header action buttons use AppIconButton.
- Panel resize is smooth and doesn't cause layout jank.
- History full state is clear and the clear-history flow is obvious.

---

## Screen 7 — Settings

**State:** Planned

### Issues found

| #   | Location                                                                                | Issue                                                                                                                                                            | Severity |
| --- | --------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- |
| S-1 | `SettingsPage.razor` ~L16–56                                                            | `<nav class="settings-nav">` uses raw `<button>` for each nav item with a `GetNavItemClass` active state — functional but hand-rolled, review visual consistency | Low      |
| S-2 | `SettingsPage.razor`                                                                    | Settings nav uses `<FluentIcon>` directly inside raw buttons — inconsistent with rest of app                                                                     | Low      |
| S-3 | `SettingsPage.razor`                                                                    | `RoutePageHeader` shows `Section: @CurrentSectionTitle` pill — useful, but "Section:" prefix is verbose                                                          | Low      |
| S-4 | Config forms (ServiceBus, AKS, Redis, DevOps, Storage, Observability, ApiClient, Agent) | Not yet read — each config form needs controls audit                                                                                                             | TBD      |
| S-5 | `SettingsPage.razor`                                                                    | Health/readiness report (`BuildReadinessReport`) — not yet read, confirm visual output                                                                           | TBD      |

### Acceptance criteria

- Each config form has a consistent save/cancel pattern.
- Settings nav active state is clearly visible in both themes.
