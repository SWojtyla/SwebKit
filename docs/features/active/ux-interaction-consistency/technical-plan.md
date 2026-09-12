# App-Wide UX & Interaction Consistency — Technical Plan

## Context

SwebKit's frontend (`web/`, React 19 + Tauri) has nine feature areas — AKS, Redis, Service Bus,
API Client, Monitoring, Storage, Agent, Settings, Dashboard/shared layout — each built somewhat
independently over time. The user reported two concrete symptoms (AKS rows need a right-click to
do anything; Redis isn't collapsed by default) and asked for a full, in-depth pass across every
feature, with AKS weighted highest since it's the most-used one. Four related features are
currently in Review and out of scope for overlap (see `index.md`). No other UX-consistency work is
currently in flight. The working tree is clean on `main` (confirmed via `git status` at the start
of this session) — no prerequisite stashing/committing is needed before this plan's units begin.

## Research summary

Nine parallel direct-code audits were run against the current `main` (2026-09-12), one per feature
area, each reading every component file in that area (not just the two reported ones) against a
fixed checklist: click/hover/focus affordances, default expand/collapse state, destructive-action
confirmation, loading/error/empty state accuracy, refresh/freshness visibility, and information
hierarchy. Findings below are grouped by area; each is independently verified against file:line,
not inferred from a prior doc. The full per-finding detail (which the work units below implement)
runs well beyond what's practical to reproduce here — this section gives the systemic pattern per
area; every unit in the work-units table cites its own file:line.

**AKS** — confirmed root cause of the reported bug: `shared/ResourceTable.tsx` derives its
pointer-cursor/hover/focus/keyboard-activation treatment from "has *either* `onRowClick` *or*
`onRowContextMenu`", so 10 of 15 resource tabs (Deployments, StatefulSets, Jobs, CronJobs,
Services, Ingresses, Gateways, GatewayClasses, HTTPRoutes, Secrets) present every row as clickable
while left-click silently does nothing — only Pods/Helm/ConfigMaps wire a real `onRowClick`. The
same file swallows fetch errors as "no items" everywhere except `NamespaceSelector.tsx`, which
already does this correctly and was never carried into the shared table. Confirmation for
destructive/high-impact actions is applied unevenly even within one file (HPA's Scale bypasses the
confirm dialog its own sibling Delete/Toggle actions use) or bypassed via a native `window.confirm`
the team's own code comments call out as wrong (`YamlViewer.tsx`, contradicted by
`StatefulSetsTab.tsx`'s explanation of why `prompt()` was removed from Scale). A real safety bug:
switching cluster context does not close an open Pod Shell panel, so it silently reconnects a
shell session under the new cluster context for a pod of the same name.

**Redis** — the reported "not collapsed by default" bug is a `useEffect` in
`RedisPageContext.tsx` that reacts to `namespaceTree` changing identity and force-expands every
namespace path whenever the expanded-set happens to be empty. Because search, pagination, cache
switch, and every key mutation all produce a new `namespaceTree`, this both explains the
fully-expanded initial view and means "Collapse All" is not durable — it silently re-expands after
the next unrelated data refresh. The six tabs (Keyspace/Keys/Ops/Prefix/PubSub/SlowLog) were built
as largely separate mini-tools: only Keyspace links back into Keys, there's no freshness indicator
anywhere despite a whole auto-refresh subsystem, and Pub/Sub has its own bespoke refresh button
nothing else uses.

**Service Bus** — no query in the feature (`useSbQueues`/`useSbTopics`/`useSbPeekMessages`/etc.)
ever reads `isError`, so a fetch failure renders identically to a genuinely empty
namespace/queue — a serious problem for a tool whose purpose is telling the truth about what's
really in a queue. Complete/Resubmit/Purge mutations have no `onError` and no success toast at
all. The command palette's "Refresh" invalidates the query key `["sb-"]`, which matches nothing
(every real key is `["sb-queues", ...]` etc. — the same class of bug documented in
`docs/pitfalls/react-frontend.md`'s TanStack Query section), and its "Purge" action is wired to
nothing. Confirmation for comparable-severity actions ranges from a styled in-app banner to a
native `confirm()` to no confirmation at all (Batch Replay, template delete, scheduled-message
cancel).

**API Client** — state defaults and "what happens next" were tuned for the first-open case, not
the steady-state case of many tabs/folders already open: closing the active tab always falls back
to a blank editor instead of an adjacent tab; "Collapse all" leaves every collection expanded
(only clears nested folders) and can *re-expand* a collection the user had deliberately collapsed;
new folders default to expanded forever; search silently fails to find matches inside a collapsed
folder. The shared `ConfirmDialog`'s action button defaults to "Delete" even for closing a tab with
unsaved changes or discarding a git revert.

**Monitoring** — every action in `AlertRuleRow` (delete, enable/disable) uses the same low-ceremony
icon-only interaction with no confirmation and no undo, while the row itself isn't clickable at
all — only the tiny pencil icon opens edit. Loading/error is never distinguished from empty in
either Rules or History. A rule can be saved with only a name, no working source configuration.

**Storage** — three "safe-looking" affordances quietly misbehave on exactly the data that matters:
the labeled Download button hands non-text blob content straight to a text-file writer, ignoring
the same `isBinary` flag the Content tab already uses to protect itself, silently corrupting
downloads of images/zips/binaries; the breadcrumb computes each crumb's label by string-replacing
against the wrong reference prefix, producing a blank crumb one level deep and concatenated labels
two levels deep; the blob-name filter only searches pages already loaded via "Load more," with no
indication search isn't exhaustive. Upload-overwrite and Undelete get no confirmation while
Copy-overwrite and Version-restore do, in the same feature.

**Agent** — `useConfirmAction`/`useRejectAction` have no `onError`, so a failed approve/reject on a
proposed mutating action (scale/delete/resubmit) silently returns the card to its default state
with no indication whether the action partially executed. `useAgentChatStream`'s tool-progress
callback and cancel function are both built but never wired into any of the three chat surfaces
(`AgentPage`, `GlobalAgentPanel`, `ContextualAssistant`), so a long multi-tool turn shows only a
static "Thinking…" with no way to stop it. High-risk proposed actions render with the identical
one-click Confirm button as harmless ones.

**Settings** — `ServiceBusNamespace.credentialKey` has no bound input anywhere in
`ServiceBusSettings.tsx`, so Connection String auth mode cannot actually be configured through the
UI. `useUpdateProfile`/`useUpdateUserSettings` have no error feedback path, reproducing in general
form the exact failure shape of the historical `SbAuthMode` incident (a rejected save silently
snaps a field back with zero explanation) for any future validation mismatch. Only Agent settings
has a "Test connection" button; AKS/Service Bus/Redis/Storage have none. `AgentSettings.tsx`'s text
inputs were never migrated to `DraftInput` when the rest of Settings was.

**Dashboard & shared layout** — the app has already built the right shared components
(`shared/ConfirmBar`, `shared/QueryState`/`EmptyState`/`Skeleton`, `useNotifyMutation`,
`AksPage`'s `LastRefreshed`) but adoption stopped after AKS (and a duplicate `AksConfirmBar` exists
instead of reusing the shared one); every other feature area re-solved (or skipped) the same
problem independently. Dashboard-specific: two "Live Watch" tiles deep-link to the wrong tab, and
the dashboard's health tiles show "Ready" from config presence alone while the footer status bar
shows real connectivity for the same four services, contradicting each other on screen at the same
time.

## Prerequisite

None beyond the already-confirmed clean working tree. No ports/paths need parameterizing — every
unit below is a frontend-only (`web/src/**`) change with no new services to stand up. The only
sequencing rule is the batch order and shared-file conflict rules below, not a setup step a
coordinator must perform first.

## Work units

Columns: **Pri** = P0 (correctness/safety — silently wrong data or a wrong-cluster/wrong-target
action), P1 (the reported class of problem — missing affordance, inconsistent confirmation/
feedback, misleading default state), P2 (polish/discoverability). **Batch** groups units that
should land together/in order; batches are independent of each other and can run in parallel
*across* batches, but see "Known conflict risks" for same-file rules *within* a batch.

### Batch 0 — Shared infrastructure (do first; every later batch reuses these)

| # | Unit | Files | Change |
| - | ---- | ----- | ------ |
| 0.1 | Standardize destructive-action confirmation | `web/src/components/shared/ConfirmBar.tsx`, `web/src/components/aks/AksConfirmBar.tsx` (delete, replace call sites), `web/src/components/aks/YamlViewer.tsx:65`, `web/src/components/settings/GeneralSettings.tsx:203`, `web/src/components/service-bus/MessageList.tsx:234,250`, `web/src/components/service-bus/MessageDetail.tsx:402-421`, `web/src/components/agent/AgentPage.tsx:217-235` | Make `shared/ConfirmBar` the one confirmation component app-wide. Delete `AksConfirmBar.tsx` (a near-duplicate) and repoint AKS at the shared one, preserving its `requireTypedName` production-safety option as a shared prop. Replace every native `window.confirm()` call site and both bespoke hand-rolled inline confirm bars (Service Bus purge, Agent clear-conversation) with `ConfirmBar`. This unit only touches the *mechanism*; individual features route their own actions through it in their own batch below. |
| 0.2 | Standardize mutation feedback | `web/src/lib/useNotifyMutation.ts`, `web/src/lib/hooks/useRedis.ts`, `useServiceBus.ts`, `useStorage.ts`, `useMonitoring.ts`, `useAgent.ts`, `useApiClient.ts`, `useProfile.ts` | `useNotifyMutation` already exists and is used only by `useAks.ts`. Audit every mutation hook in the files listed and give each an `onError` that calls `notify("error", ...)` (via `useNotifyMutation` or an equivalent inline call), matching what AKS already does. Do not change success-path behavior beyond adding a toast where one is genuinely missing. |
| 0.3 | Extract and reuse the "last updated" freshness indicator | `web/src/components/aks/AksPage.tsx` (source of `LastRefreshed`), new `web/src/components/shared/LastRefreshed.tsx` | Extract AKS's `LastRefreshed` component (currently local to `AksPage.tsx`) into `shared/`, taking a `lastRefreshedAt: number \| null` and `isPaused?: boolean` prop. No behavior change to AKS. This unit only creates the shared component; each feature area mounts it in its own batch unit below (Redis 2.2, Service Bus 3.5, Storage 6.5, Dashboard 9.2). |
| 0.4 | Decide and apply the shared empty/loading/error pattern | `web/src/components/shared/QueryState.tsx`, `EmptyState.tsx`, `Skeleton.tsx` | `QueryState` wraps `EmptyState`/`Skeleton` but has zero consumers outside its own file — every feature hand-rolls its own loading/empty text instead. Confirm the component's API is fit for reuse (loading/error/empty branches, an `error` message slot, a skeleton row count prop) and fix anything missing so it becomes the single pattern threaded through in Batches 1–7 below (each unit that adds error/loading state should use this component rather than inventing another one). If, on inspection, the API is a poor fit for one of the list/table shapes in use, extend it rather than starting a second shared component. |
| 0.5 | Keyboard shortcut registry + discoverability | `web/src/components/layout/KeyboardShortcutsPanel.tsx` (currently a hand-maintained array), `web/src/components/aks/shared/AksWorkspaceContext.tsx:526-544` (r/l/y bindings), `web/src/components/service-bus/ServiceBusPage.tsx:221` (Ctrl+Shift+E) | Introduce one shortcut registry (a simple typed list of `{keys, description, scope}`) that both the actual `window`-level `keydown` handlers and `KeyboardShortcutsPanel` read from, so a new shortcut can't be added without the panel listing it. Migrate AKS's r/l/y and Service Bus's Ctrl+Shift+E onto it (both currently real but undocumented in the panel). No new shortcuts in this unit — just make existing ones discoverable and prevent future drift. |
| 0.6 | Reconcile the three redundant Dashboard status widgets | `web/src/components/dashboard/DashboardPage.tsx:113-118` (health tiles), `web/src/components/layout/AppLayout.tsx:202-231` (`areaHealth`, real connectivity) | Dashboard's health tiles show "Ready"/"Not configured" from config presence only; the footer status bar shows real connectivity (`Not configured/Checking/Unavailable/Connected`) for the same four services, and can contradict the dashboard on screen at the same time. Reuse `AppLayout`'s existing `sbHealth`/`aksHealth`/`redisHealth`/`storageHealth` queries (already fetched) to drive the dashboard tiles instead of recomputing a config-only signal. |

### Batch 1 — AKS (P0/P1 — most-used feature, highest priority)

| # | Unit | Files | Change |
| - | ---- | ----- | ------ |
| 1.1 | Fix `ResourceTable`'s click-affordance root cause, wire real row actions on the 10 lagging tabs | `web/src/components/aks/shared/ResourceTable.tsx:72-98` (clickable/hover/focus/keyboard derivation, currently `Boolean(onRowClick \|\| onRowContextMenu)`), `DeploymentsTab.tsx`, `StatefulSetsTab.tsx`, `JobsTab.tsx`, `CronJobsTab.tsx`, `ServicesTab.tsx`, `IngressesTab.tsx`, `GatewaysTab.tsx`, `GatewayClassesTab.tsx`, `HttpRoutesTab.tsx`, `SecretsTab.tsx` | Derive cursor/hover/`tabIndex`/keyboard-Enter-activation from `onRowClick` alone — a context-menu-only row must not look or behave like a clickable one. Then give each of the 10 tabs a real `onRowClick` consistent with what Pods/Helm/ConfigMaps already do (open YAML view via the existing `ws.openYaml(...)` used by their context menus, since that's already computed per row). **P0**: this is the exact reported bug, reproduced on two-thirds of resource tabs. |
| 1.2 | Real error state + non-jumping loading state in `ResourceTable` | `web/src/components/aks/shared/ResourceTable.tsx` (add `error?` prop, use Batch 0.4's shared pattern), every tab's hook destructuring (currently `{ data, isLoading }`, drop `error`), `web/src/components/aks/AnalysisPanel.tsx:26,46-48,67,78-80` (fix loading/empty conflation — currently falls to "No X found" before checking `isLoading`) | An RBAC-denied list or sidecar 500 currently renders identically to "this namespace has zero X." Add an `error` prop to `ResourceTable`, thread `error` from every tab's `useAksX` hook, and render a skeleton table (same headers, placeholder rows) instead of the literal text `"Loading..."`. Fix `AnalysisPanel`'s ingress/configmap panels to check `isLoading` before falling back to the empty message. |
| 1.3 | Standardize AKS confirmations onto the shared `ConfirmBar` (Batch 0.1) | `web/src/components/aks/HpaTab.tsx:20-24` (`handleScale`, currently bypasses confirm entirely, unlike its own sibling `handleDelete`/`handleToggleScaling`), `web/src/components/aks/YamlViewer.tsx:65` (native `window.confirm`) | Route `HpaTab`'s Scale action through the same `ws.requestConfirm` its Delete/Toggle-scaling actions already use — scaling min/max replicas is at least as consequential as either. Route YAML Apply through the shared `ConfirmBar` from Batch 0.1 instead of `window.confirm`, matching the reasoning the team already documented for why Scale's old `prompt()` was removed. |
| 1.4 | Fix cross-cluster pod-shell safety bug | `web/src/components/aks/PodsTab.tsx:88,237-245` (`shellPod`/`askAiPod` local state, never reset on context change), `web/src/components/aks/PodShellPanel.tsx:31-97`, `web/src/components/aks/shared/AksWorkspaceContext.tsx:441-449` (`handleContextChange`, already clears `pod/yaml/helm/container/logs` but not tab-local shell state) | Switching cluster context (`ws.currentContext`) does not close an open Pod Shell — the panel's effect re-runs on the new context for the same pod name, silently reconnecting an exec session to a different cluster with no warning. Close `shellPod`/`askAiPod` (and any other tab-local panel state) in the same place `handleContextChange` already clears its own state. **P0**: a user can end up running shell commands against the wrong cluster. |
| 1.5 | Surface Pods' power actions (Shell/Port-Forward/Ask AI) outside the context menu | `web/src/components/aks/PodDetailPanel.tsx:16-65` (currently only Logs/Containers/YAML/Close), `web/src/components/aks/PodsTab.tsx:135-147` (menu-only today) | Add Shell/Port-Forward/Ask-AI as header buttons in `PodDetailPanel`, so the panel opened by the tab's one working left-click also surfaces the actions currently reachable only via right-click on the row behind it. Depends on 1.4 landing first (same `shellPod`/`askAiPod` state). |
| 1.6 | Make Secrets consistent with ConfigMaps; move ConfigMap selection into the shared panel system | `web/src/components/aks/SecretsTab.tsx` (no `onRowClick` today; keys only reachable via right-click "View keys"), `web/src/components/aks/ConfigMapsTab.tsx:23,40-77` (local `useState` + one-off `<div>` panel instead of `AksWorkspaceContext`/`ResizablePanel`), `web/src/components/aks/shared/AksWorkspaceContext.tsx`, `web/src/components/aks/SecretDetailPanel.tsx` (already exists, currently only opened via `ws.setSelectedSecret` from the menu) | Give `SecretsTab` an `onRowClick` opening `SecretDetailPanel`, same as `ConfigMapsTab`'s pattern is *supposed* to be — except `ConfigMapsTab` itself needs fixing first: move its selection state into `AksWorkspaceContext` alongside Pod/YAML/Helm/Secret/Container so it survives tab switches, is deep-linkable via URL params, and renders through the shared `ResizablePanel` in `AksPage.tsx:302-390` instead of a local `<div>`. |
| 1.7 | Column sorting, name filter, unhealthy-first default | `web/src/components/aks/shared/ResourceTable.tsx` (plain `<th>` headers today, no sort, no filter — depends on 1.1/1.2 merging first, same file), `PodsTab.tsx`, `DeploymentsTab.tsx` | Add sortable column headers and a name-substring filter to `ResourceTable` (shared across every tab), matching the filter pattern `ContextSelector`/`NamespaceSelector` already use for contexts/namespaces. Once sorting exists, default-sort Pods by unhealthy-first (non-Running / high-restart) and Deployments by not-Ready-first, so the resources needing attention float to the top of a large list instead of requiring a manual scroll. |
| 1.8 | Toolbar/selector/menu polish bundle | `web/src/components/aks/PortForwardPanel.tsx:90-98,141-172` (free-text pod field, no Context column in session list), `web/src/components/aks/NamespaceSelector.tsx:196-213` vs `ContextSelector.tsx:74-94` (namespace requires explicit Apply, context applies instantly), `web/src/components/aks/GatewayClassesTab.tsx` (cluster-scoped, namespace selector stays live with no indication it's irrelevant), `web/src/components/aks/EventsTab.tsx:1-52` (no severity filter/sort/click-through), `web/src/components/aks/ContextMenu.tsx:59-76` + `HelmTab.tsx:35-41` + `DeploymentsTab.tsx:59-60` + `CronJobsTab.tsx:21` (dead disabled menu items, no tooltip explaining why) | A bundle of independent, low-risk fixes to the same general "toolbar/selector affordance" theme: replace `PortForwardPanel`'s free-text pod field with a searchable dropdown reusing the already-fetched pod list, and add a Context/Cluster column to its session table; make single-namespace selection apply immediately (keep explicit Apply only meaningful for multi-select); disable/gray the namespace selector with a tooltip on cluster-scoped tabs; add a Warnings-only toggle + time sort + click-through-to-resource on `EventsTab`; remove the genuinely-dead disabled menu items (Helm's duplicate Rollback stub — a working one already exists in `HelmDetailPanel`) and add a `title` tooltip to `ContextMenuItem` for any that remain disabled. |

### Batch 2 — Redis

| # | Unit | Files | Change |
| - | ---- | ----- | ------ |
| 2.1 | Make expand/collapse a deliberate, owned state | `web/src/components/redis/RedisPageContext.tsx:393-404` (the auto-expand `useEffect`, reacts to any `namespaceTree` identity change), `:353-358` (`handleLoadMore`), `KeyBrowserPanel.tsx:140-148` (`Collapse All`, no `Expand All`), `KeyBrowserPanel.tsx:128-134` (separator input recomputes tree per keystroke) | This is the reported bug. Replace the reactive "expand everything whenever the set happens to be empty" effect with a one-time seed on genuine search/cache change (e.g. a ref-guarded initializer, or default only depth-0 namespaces expanded) so it stops firing after every mutation/pagination/load-more. Add a symmetric "Expand all" next to "Collapse all," sharing the same deliberate function the mount-time default should use. Commit the namespace-separator input on blur/Enter instead of on every keystroke to stop the same churn from typing. |
| 2.2 | Fix Keyspace loading/empty conflation; add freshness indicator | `web/src/components/redis/AdvancedPanels.tsx:31-33` (`KeyspaceHealthPanel`, shows literal "Loading..." forever when `keys.length === 0` disables the query rather than actually loading), `web/src/lib/hooks/useRedis.ts:33-39`, `RedisPage.tsx` (mount Batch 0.3's `LastRefreshed`) | Distinguish "query disabled because there are no keys to analyze" from "query in flight" in `KeyspaceHealthPanel` — show an explicit empty-state message, not indefinite "Loading...". Mount the shared `LastRefreshed` (Batch 0.3) somewhere visible in the Redis toolbar so Slow Log/Pub-Sub/Ops/Keyspace snapshots show how stale they are, since Auto-refresh defaults off. |
| 2.3 | Drill-through links between tabs; type/TTL hint on key rows | `web/src/components/redis/AdvancedPanels.tsx:130-179` (`PrefixMemoryPanel`, no click handler), `:210-266` (`OpsInsightsPanel`, slow entries not linked), `KeyspaceTab.tsx:9-16` (existing pattern to mirror), `KeyBrowserPanel.tsx:65-99` (rows show name only) | Give `PrefixMemoryPanel` an `onOpenPrefix` callback that sets the Keys tab's search pattern to `${prefix}${separator}*` and switches tabs, mirroring the working `onOpenKey` pattern `KeyspaceTab` already has. Add a small type-color dot (reuse `typeColors` from `KeyDetailPanel.tsx:8-15`) and a TTL badge to each key row in the tree, so type/TTL is visible while scanning, not only after opening detail. |
| 2.4 | Confirmation/feedback consistency | `web/src/components/redis/KeyDetailPanel.tsx:113` (`handleRemoveTtl`, no confirm unlike sibling delete actions), `web/src/components/redis/RedisPageContext.tsx:464-469,501-506` (existing `pendingConfirm` pattern to reuse), `PubSubPanel.tsx:25-33` (bespoke local refresh button), `RedisPageContext.tsx:327-333` (enabling Auto-refresh waits a full interval before any visible effect) | Route "Remove TTL" through the same `pendingConfirm` mechanism `requestDeleteKey`/`requestDeleteHashField` already use — turning a cache entry permanent is a real behavior change, currently unguarded. Fire an immediate `invalidateQueries` when Auto-refresh is toggled on (in addition to the interval) so checking the box has visible, immediate effect. Reconcile Pub/Sub's own refresh button against the shared model established by Batch 0.3/2.2 (either give every tab one, or drop Pub/Sub's in favor of the shared control). |

### Batch 3 — Service Bus

| # | Unit | Files | Change |
| - | ---- | ----- | ------ |
| 3.1 | Real error states in the entity tree and message list | `web/src/components/service-bus/EntityTree.tsx:99,147-149`, `ServiceBusPage.tsx:76,131-141`, `MessageList.tsx:377-379`, all the `useSbX` hooks in `web/src/lib/hooks/useServiceBus.ts` (currently destructure `data`/`isLoading` only) | Thread `isError`/`error` from every Service Bus query hook into `EntityTree` and `MessageList`, rendering a visibly distinct error banner instead of the current fall-through to "No entities/messages found." A fetch failure must never look like a genuinely empty namespace/queue. |
| 3.2 | Feedback + correctness fixes for mutations and commands | `MessageDetail.tsx:155-190` (Complete/Resubmit/Purge, no `onSuccess`/`onError` notify — apply Batch 0.2 here), `ServiceBusPage.tsx:216` (`invalidateQueries({queryKey:["sb-"]})`, matches nothing per the TanStack Query prefix-matching pitfall — fix to a real predicate or the existing `invalidateServiceBusQueries` helper), `EntityCommandPalette.tsx:17-25` + `ServiceBusPage.tsx:211-217` (palette's "Purge" falls through every branch, dead) | Add success/error notify to Complete/Resubmit/Purge (via Batch 0.2). Fix the broken Refresh command's query invalidation. Either wire the palette's "Purge" action to the existing `useSbPurgeMessages` hook (through the confirmation from unit 3.3) or remove it from the palette — it must not be presented as a working destructive action while doing nothing. |
| 3.3 | Standardize confirmations; separate "Purge All" scope from per-message actions | `BatchReplayPanel.tsx:41-58` (Replay executes immediately, no confirm), `MessageList.tsx:234,250` (native `confirm()` for bulk Complete/Resubmit — apply Batch 0.1), `TemplatePicker.tsx:78`, `ScheduledMessages.tsx:93-107` (both: delete/cancel with no confirm), `MessageDetail.tsx:244-281` (Purge All sits in the same button row as per-message actions) | Route Batch Replay, bulk Complete/Resubmit, template delete, and scheduled-message cancel through the shared `ConfirmBar` from Batch 0.1. Move "Purge All" out of the per-message action row into the entity-level toolbar (next to the Active/DLQ tabs) so its all-messages scope is visually distinct from the per-message buttons beside it today. |
| 3.4 | Entity tree collapse/expand and topic-row affordance | `EntityTree.tsx:85-96` (not virtualized, no collapse-all/expand-all for topics, Queues section can never collapse at all), `:259` vs `:217,348` (Topic row `onClick` only toggles expand, Queue/Subscription rows select — identical styling for different behavior), `:56-64` (`EntityStatsBadges` shows flat "–" for a topic's rolled-up DLQ count), `:144-149` (tree render blocked on the slower of the two independent queues/topics queries) | Add collapse-all/expand-all for topics and make the Queues section itself collapsible (the reported "isn't collapsed by default when it should be" failure mode, reproduced here for large namespaces). Give topic rows a visually distinct affordance from queue/subscription rows (e.g. chevron-only click target) since they behave differently. Aggregate and show a rolled-up DLQ count across a topic's subscriptions even while collapsed. Render whichever of queues/topics resolves first instead of gating both behind the slower one. |
| 3.5 | Filtering/composing/refresh polish bundle | `MessageList.tsx:507-517,892-912` (no manual refresh or last-updated when auto-refresh is off, the default — mount Batch 0.3's `LastRefreshed`), `:395-403,672-690,698-706` (3-4 scattered filter-clear controls, no single "Clear all"), `MessageComposer.tsx:213-223`, `BatchSendPanel.tsx:167-177` (free-text target-entity field, no autocomplete against the entity list already available via `EntityCommandPalette`), `BatchReplayPanel.tsx:75-82` (all-or-nothing batch result, no per-item breakdown), `MessageDetail.tsx:552-559`, `TemplatePicker.tsx:77-84` (hover-only secondary buttons, no `focus-within`), `MessageList.tsx:544-562` (advanced-filter badge shows even when Advanced is off), `BatchSendPanel.tsx:228-236` (no pending-state label, unlike `BatchReplayPanel`) | A bundle of independent filtering/composing fixes in the same panel family: add a manual refresh button + freshness indicator; add one "Clear all filters" action; reuse the entity list as a combobox for target-entity fields in both composer and batch-send; show a per-item success/failure breakdown after a batch replay if the backend can report it; add `focus-within:opacity-100` to hover-only action buttons; only show the advanced-filter rule-count badge when Advanced is actually enabled; add a "Sending…" pending label to `BatchSendPanel` matching `BatchReplayPanel`. |

### Batch 4 — API Client

| # | Unit | Files | Change |
| - | ---- | ----- | ------ |
| 4.1 | Tab strip: adjacent-tab focus, standard affordances, preview tabs | `web/src/components/api-client/ApiClientPageContext.tsx:400-422,437-454` (`closeTab`/`openTab`), `RequestTabStrip.tsx` (no middle-click-close, no right-click Close Others/All), `CollectionTree.tsx:438-441` (`onSelectNode`, always promotes to a full tab) | Closing the active tab must activate a neighboring tab, not fall back to blank, unless the tab list is now empty. Add middle-click-to-close and a right-click "Close Others/Close All" to the tab strip. Adopt a single reusable "preview" tab for single-click navigation (replaced by the next single-click; promoted to a permanent tab only on edit or double-click), so browsing a collection doesn't accumulate a permanent tab per row clicked. |
| 4.2 | Fix tree collapse-state bugs | `CollectionTree.tsx:49-54,77-98` (`filterNodes`/`flattenTree`, search doesn't auto-expand a collapsed folder containing a match), `:184-186` (`collapseAll` forces collections into the *expanded* set — the opposite of its label — while only clearing nested folders), `ApiClientPageContext.tsx:539-559` (`handleAddFolder`, `isExpanded: true` by default) | When `search` is non-empty, auto-expand (for rendering purposes) any folder containing a match regardless of persisted `expandedIds`, so filtering never silently hides a real result. Fix `collapseAll` to actually empty `expandedIds` (or explicitly preserve the user's own collection-level choices) instead of force-expanding every collection root. Default new folders to `isExpanded: false`. |
| 4.3 | Confirm-dialog text and scope clarity | `ApiClientPageContext.tsx:252-255,437-449,561-564` (`ConfirmDialogState` has no `confirmText`; close-dirty-tab and node-delete both default to "Delete"), `GitPanel.tsx:502-508` (revert flow, same default), `Dialogs.tsx:70` (`confirmText = "Delete"` default), `EnvironmentManager.tsx:133-137,349-355` (delete has zero confirmation, unlike every sibling delete flow) | Thread a `confirmText` through `ConfirmDialogState` and pass `"Close"` for the unsaved-tab-close flow and `"Discard changes"` for git revert, reserving "Delete" for actual deletion. Include the item's name/type and, for folders/collections, a descendant count in the delete message. Add a confirm step to environment deletion, matching every other destructive flow in this feature. |
| 4.4 | Discoverability + response-viewer continuity | `CollectionTree.tsx:505-516` (row menu button `opacity-0`, no `focus-visible`/`focus-within`, unlike `GitFileList.tsx:101` which already has it), `ResponseViewer.tsx:121-127` (Sending replaces the whole panel, discarding the previous response instead of dimming it) | Add `focus-visible:opacity-100` to the tree row's per-item menu button so keyboard navigation surfaces it the same way hover does. Keep the previous response visible (dimmed, with a small inline spinner) while a new request is in flight, instead of unmounting it — the common "tweak and resend to compare" workflow currently loses its comparison point. |
| 4.5 | Git diff correctness + WebSocket log safety | `src-tauri/src/git.rs:406-429` (`diff_file_impl` always reads the working-tree file, never the index — "Diff" silently includes not-yet-staged edits on top of what's actually staged), `GitDiffPane.tsx`, `WebSocketPanel.tsx:131,237-243` (`Clear log` wipes the transcript immediately, no confirm/undo) | Either diff against the index when a file is staged (`git diff --cached` semantics) or add an explicit staged-vs-working-tree toggle next to the existing Compare/Single toggle, so "Diff" can't silently show more than what a commit will actually contain. Add a confirm step (or a brief "Log cleared — Undo" toast) before `Clear log` discards the WebSocket transcript. |

### Batch 5 — Monitoring

| # | Unit | Files | Change |
| - | ---- | ----- | ------ |
| 5.1 | Row click + confirm/undo for delete and enable/disable | `AlertRuleRow.tsx:57-113` (row has no `onClick`/hover/cursor; delete and the enable toggle both fire immediately), `MonitoringPage.tsx:78-91` | Make the row (name/detail area) clickable to open edit, with real hover/cursor affordance, reserving the icon buttons for secondary actions. Add a confirm step before delete (via Batch 0.1) and a brief "Rule disabled — Undo" toast on toggle-off (via Batch 0.2), since both currently execute on a single click with no recovery path, right next to each other in a dense row. |
| 5.2 | Real loading/error states for Rules and History | `MonitoringPage.tsx:28-29` (only `isLoading` for rules, never `isError`; History has no loading indicator at all), `AlertRuleGroups.tsx:34-40`, `AlertHistoryPanel.tsx:19-25` | Destructure and render `isError`/`error` (via Batch 0.4's shared pattern) in both tabs, so a broken backend connection doesn't look identical to "nothing configured yet." |
| 5.3 | Require working configuration before save | `AlertRuleDialog.tsx:288` (`disabled={!draft.name}` — only the name is required) | Disable Save until the fields required for the selected `source` (AKS namespace / Service Bus namespace+entity / Redis cache alias) are actually filled in, or show inline validation, so a rule can't be created that silently never fires. |
| 5.4 | Triage/visual polish bundle | `AlertRuleGroups.tsx:51-61` (collapsed group header shows only a count, no firing-status rollup), `AlertRuleRow.tsx:69-71` (10px severity badge, no visual priority over recency), `AlertHistoryPanel.tsx:27-75` (no severity sort/filter), `AlertRuleRow.tsx:92-113` (Ask-AI has a tooltip, Edit/Delete don't), `ProactiveInsightCard.tsx:33-39` (plain `✕` glyph instead of the `lucide-react` `X` used elsewhere), `MonitoringPage.tsx:99-110` (unbounded insight-card list, no cap/scroll/dismiss-all), `AlertRuleGroups.tsx:32` (collapse state resets on tab switch, component unmounts) | Bundle of independent polish: aggregate a firing/error rollup badge onto collapsed group headers; increase severity's visual weight (color-coded border or larger badge) and add a severity sort/filter to History; add `title` tooltips to Edit/Delete matching Ask-AI's existing one; swap the `✕` glyph for the shared `X` icon; cap the proactive-insights list with a "+N more"/scroll and a "Dismiss all"; lift group collapse state out of the tab-conditional component (or persist it) so it survives switching to History and back. |

### Batch 6 — Storage

| # | Unit | Files | Change |
| - | ---- | ----- | ------ |
| 6.1 | Stop Download from corrupting binary blobs | `web/src/components/storage/StoragePageContext.tsx:315-328,333-351` (`handleDownloadBlob`/`handleBatchDownloadBlobs`, ignores `isBinary`), `BlobDetailPanel.tsx:73-75,345-347` (Content tab already branches on `isBinary` — proves `content` isn't safe/complete for binary blobs) | **P0 — silent data corruption.** Branch Download on the same `isBinary` flag the Content tab already trusts: for text blobs keep the current path; for binary blobs, either stream real bytes from a binary-safe endpoint or relabel/redirect to the existing "Generate SAS URL" flow with a note that binary downloads go through the signed URL. Must not silently write a corrupted file with no warning. |
| 6.2 | Fix breadcrumb label computation | `BlobBrowserPanel.tsx:39-53` (labels computed by `p.replace(ctx.currentPrefix, ...)` against the wrong reference prefix — blank crumb one level deep, concatenated labels two levels deep) | **P0 — core navigation is wrong.** Derive each crumb's label from the *next* prefix in `prefixHistory` (or `currentPrefix` for the last one), slicing to only the final `/`-delimited segment, instead of string-replacing against `currentPrefix` for every ancestor. |
| 6.3 | Confirmation consistency for overwrite/undelete | `BlobBrowserPanel.tsx:91-137` (Upload silently overwrites an existing blob — no equivalent of the Copy flow's explicit "Allow overwrite" + typed-confirm guard at `BlobDetailPanel.tsx:430-441,461-473`), `BlobRecoveryPanel.tsx:39-54,118-136` (Recover fires immediately, no confirm, no explanation of same-name collision) | Before uploading, check whether the target blob name already exists and surface a `ConfirmBar`-style warning if so (via Batch 0.1). Add a confirm step to Recover that states destination container/name and whether an existing live blob of the same name will be overwritten or the recovery will fail. |
| 6.4 | Distinguish Copy-URL from SAS-URL; fix content-type placeholder | `BlobDetailPanel.tsx:70-78` (identical `LinkIcon` for both, `handleCopyUrl` builds an unsigned URL that 403/404s on private containers — a past-bug comment already documents this exact ambiguity at `StoragePageContext.tsx:287-294`), `BlobRecoveryPanel.tsx:22-28` (`contentType: "unknown"` hardcoded, never rendered) | Give the two link actions visually distinct icons (plain link vs. link+shield/clock for "signed, expires") so they're distinguishable without hovering both. Either fetch/surface real content type for soft-deleted blobs in the Recovery table or remove the dead placeholder field. |
| 6.5 | Filter scope, sort, pagination flicker | `BlobBrowserPanel.tsx:56-63,279-285` (filter only searches pages already loaded, no indication search isn't exhaustive), `:190-204` (no last-modified column, no sort), `:142-152,210-218` (Load More flashes a "Loading…" banner over an already-populated list, per no `placeholderData`), `useStorage.ts:23-33` (`useStorageBlobs`, add `placeholderData: keepPreviousData`); mount Batch 0.3's `LastRefreshed` where relevant | Either make the filter a server-side query or show "Filtering N of unknown total loaded — Load more to search further" and auto-page while a filter is active with no local matches. Add a compact last-modified column and a basic sort control. Use TanStack Query's `keepPreviousData` so the prior page stays visible during a "Load more" fetch instead of flashing a loading banner over populated rows. |
| 6.6 | Small polish | `StoragePage.tsx:97-105` (Recovery tab disabled with no tooltip, unlike Upload/Copy's `title`-bearing pattern) | Wrap the disabled Recovery tab button in the same `title`-bearing pattern already used elsewhere in this feature (`"Select a container to view its deleted blobs"`). |

### Batch 7 — Agent

| # | Unit | Files | Change |
| - | ---- | ----- | ------ |
| 7.1 | Error handling + risk-tiered confirm styling for pending actions | `web/src/components/agent/PendingActionCard.tsx:57-90` (renders only `confirm.data`, never `isError`/`error`; Confirm button is visually identical across `None/Low/High` risk), `web/src/lib/hooks/useAgent.ts:34-53` | Render a distinct error state when confirm/reject fails, with the mutation's error message, and disable further action until retried/dismissed — a failed approve on a scale/delete/resubmit proposal must never look identical to "nothing happened yet." For `risk === "High"`, give Confirm a visually distinct (destructive-colored) treatment and/or an extra step, proportional to what's being approved. |
| 7.2 | Attribution for pending actions across surfaces | `web/src/lib/types.ts:1077-1085`, `useAgent.ts:26-32` (single global `["pending-approvals"]` key, no `featureArea`/session on `PendingAction`), rendered identically in `AgentPage.tsx:275-281`, `GlobalAgentPanel.tsx:147-153`, `ContextualAssistant.tsx:215-221` | Carry the originating feature area/session on `PendingAction` and show it on the card ("Proposed from: AKS · pod api-7c9f"), so a user reading one surface can tell which conversation actually proposed the action they're being asked to confirm. |
| 7.3 | Live tool-call progress + cancel | `useAgent.ts:99-100,135-137` (`onToolEvent` built but never wired), `useGlobalAgentConversation.ts:38-39`, `ContextualAssistant.tsx:82-109`, `useAgent.ts:165-169` (`cancel()` exists, unused), `AgentPage.tsx:141-150` (static "Thinking…") | Wire `onToolEvent` into the loading indicator across all three chat surfaces so it updates per event ("Thinking… (querying AKS logs)"). Add a "Stop" button next to Send while `isStreaming`, calling the existing `cancel()` — today a stuck turn is both invisible and unstoppable short of navigating away. |
| 7.4 | Surface tool failures in the reasoning trace | `AgentReasoningTrace.tsx:16-40`, `web/src/lib/types.ts:1007-1012` (`AgentChatStep` has no failure flag the UI checks) | When a `tool_result` step represents a failure, render it with distinct (warning) styling, and show a failure count on the collapsed toggle itself ("Show reasoning (4 steps, 1 failed)") so a failed data source isn't buried behind a disclosure most users never open. |
| 7.5 | Visualization panel fixes | `AgentVisualizationPanel.tsx:242-244` (tab-reset effect keyed on raw streamed `content`, silently snaps back to tab 0 mid-stream if the user picked another tab), `AgentPage.tsx:242,246-252` vs `GlobalAgentPanel.tsx:121`, `ContextualAssistant.tsx:152` (inconsistent disabled-logic/tooltip for the "Visuals" button) | Key the active-tab reset off `blocks.length`/block ids, not the raw content string, so a manual tab selection survives the rest of the stream. Reuse `AgentPage`'s correct `parseVisualBlocks(content).length === 0` disabled check (and its tooltip) in the other two surfaces, which currently only check for non-empty text and can open an empty visuals panel. |
| 7.6 | Pending-action expiry visibility | `PendingActionCard.tsx:88-90` (static clock time, no countdown; expired actions just vanish on next poll with no distinction from rejected/applied) | Show a live countdown ("expires in 47s"), and when a poll shows a previously-listed action gone without an `applyResult`, surface an explicit "This proposal expired" notice inline rather than silently removing the card. |
| 7.7 | Clarify capability differences | `GlobalAgentPanel.tsx:31-33` vs `ContextualAssistant.tsx:168-213` (mode/scope controls only exist in the contextual surface; global is permanently ask-only with no visible indication), `ContextUsageIndicator.tsx:16-30`, `AgentSummarizedNotice.tsx:1-13` (summarization consequence only communicated after the fact) | Add a visible (even if disabled-with-tooltip) mode indicator to the global surfaces explaining why they can't propose actions. Make the context-usage indicator's warning-state tooltip state the consequence proactively ("Nearing the limit — older messages will be summarized automatically") instead of only showing `AgentSummarizedNotice` after summarization has already happened. |

### Batch 8 — Settings

| # | Unit | Files | Change |
| - | ---- | ----- | ------ |
| 8.1 | Add the missing Service Bus connection-string field | `web/src/components/settings/ServiceBusSettings.tsx:84-114`, `web/src/lib/types.ts:78` (`credentialKey` initialized but never bound to an input) | **Functional gap, not just polish** — Connection String auth mode currently has no way to actually enter a connection string. Add a `DraftInput` bound to `credentialKey` when `authMode === "ConnectionString"`, mirroring `StorageSettings.tsx:116-124`'s existing pattern (credential-store key, not the literal string, with the same caption convention). |
| 8.2 | Add "Test connection" to AKS/Service Bus/Redis/Storage | `AksSettings.tsx`, `ServiceBusSettings.tsx`, `RedisSettings.tsx`, `StorageSettings.tsx`, mirroring `AgentSettings.tsx:190-198`'s existing `runTest`/`"Test connection"` pattern | Add a per-namespace/cache/account (and kubeconfig-context) "Test connection" button that pings the sidecar and shows pass/fail + diagnostic, matching Agent's existing `capabilityLabel`/`lastTestDiagnostic` treatment — currently the only section with this affordance. |
| 8.3 | Give every settings save a failure feedback path | `web/src/lib/hooks/useProfile.ts:45-54,97-104` (`useUpdateProfile`'s `onError` only invalidates and re-syncs silently; `useUpdateUserSettings` has no `onError` at all) | Add an `onError` to both hooks that calls `notify("error", "Couldn't save setting", ...)` (via Batch 0.2/`useNotifyMutation`). This is the general-form fix for the historical `SbAuthMode` bug class — any future rejected value (a bad enum, an out-of-range number, a backend constraint) must never again silently snap a field back with zero explanation. |
| 8.4 | Migrate Agent settings to `DraftInput`; serialize concurrent saves | `AgentSettings.tsx:86-176,328-343` (plain `<input onChange>`, full-profile write per keystroke — the exact problem `settings-save-performance` already fixed everywhere else), `useProfile.ts` (`useUpdateUserSettings` has no `scope`, unlike `useUpdateProfile`) | Swap these inputs for `DraftInput` with `onCommit`, and give `useUpdateUserSettings` the same `scope: { id: "user-settings" }` serialization `useUpdateProfile` already has, so concurrent saves can't race. |
| 8.5 | Validate numeric fields | `AksSettings.tsx:74-90` (`autoRefreshIntervalSeconds`/`logBufferSize`, `parseInt(v) \|\| default` — negative numbers pass through unchanged since they're truthy), `RedisSettings.tsx:154-161` (`database`, no bound despite the caption stating "0–15") | Clamp/validate these fields on commit and show an inline error (reusing 8.3's notify pattern) instead of silently accepting an out-of-range value that only surfaces later as an opaque connection failure. |
| 8.6 | Reconcile Redis secret storage with the rest of Settings | `web/src/lib/types.ts:100-107` (`RedisCacheEntry.connectionString: string`, plaintext) vs `:109-116` (`StorageConfig.connectionStringRef`, indirected via credential store) | Either add the same credential-key indirection Storage/Service Bus use, or — if the raw-string model for Redis is intentional — state it explicitly in the field's caption ("stored as plain text in your local profile") so the inconsistency is disclosed rather than silent. |
| 8.7 | Confirm before removing configured items | `ServiceBusSettings.tsx:32-37`, `RedisSettings.tsx:44-50`, `StorageSettings.tsx:33-41`, `AgentSettings.tsx:93-107`, `WorkspaceMapSettings.tsx:58-63,78-80` (all remove immediately) vs `GeneralSettings.tsx:203` (Import already confirms via `window.confirm`, itself replaced by Batch 0.1's `ConfirmBar`) | Add a confirm step (via Batch 0.1) to every per-item "Remove" button across these five sections, at least when the item has a non-empty credential/connection value — currently the single largest-blast-radius action (full profile import/replace) is the only one in Settings that confirms at all. |
| 8.8 | Fix the `authMode` type/UI mismatch and add missing captions | `web/src/lib/types.ts:77` (`ServiceBusNamespace.authMode` includes `"ServicePrincipal"`, which has no UI), `ServiceBusSettings.tsx:95-114` (only two of three radios rendered), `GeneralSettings.tsx:47-87` (four checkboxes with no help text), `StorageSettings.tsx:117-123`, `AgentSettings.tsx:136-142` (placeholder-only explanations, vanish once typed — inconsistent with Redis's persistent captions), `AksSettings.tsx:50-59,83-90` (Kubeconfig Context / Log Buffer Size, no explanation) | Either implement the third `ServicePrincipal` radio (client id/secret or cert fields) or remove it from the type until supported — given this file's own documented history with exactly this class of type/UI drift, it's worth closing deliberately rather than leaving it. Add one-line captions to `GeneralSettings`' four checkboxes and to AKS's Kubeconfig Context/Log Buffer Size fields, and convert Storage/Agent's placeholder-only credential-key explanations into persistent captions, matching the convention already used in `RedisSettings.tsx`. |
| 8.9 | Settings navigation signal + risk-weighted toggle styling | `SettingsPage.tsx:14-24,71-85` (tab strip has no per-section readiness signal, despite `GeneralSettings.tsx:24-45` already computing the needed booleans), `StorageSettings.tsx:126-133` ("Allow mutations" — a real capability toggle styled identically to a benign one like AKS's "Enable pod health monitoring") | Surface a small readiness dot/badge on each settings tab, reusing the booleans `GeneralSettings` already computes. Give "Allow mutations" the same warning color/icon treatment already established for the demo-mode banner (`SettingsPage.tsx:63-68`), so a capability-granting toggle reads differently from a convenience one at a glance. |

### Batch 9 — Dashboard

| # | Unit | Files | Change |
| - | ---- | ----- | ------ |
| 9.1 | Fix "Live Watch" tile deep links | `DashboardPage.tsx:120-125` (`watchTiles`, all link to bare `/aks`/`/redis` with no tab param) | Pass the actual tab, e.g. `to: "/aks?tab=pods"`, `to: "/redis?tab=info"`, so a tile showing a Pods/Cache-Hit-Rate number lands on the view that shows that number, using AKS's existing `?tab=` deep-link support. |
| 9.2 | Command palette completeness | `web/src/lib/hooks/useCommandPalette.ts:33-146` (indexes every other area's resources but never Monitoring alert rules or Settings sub-pages) | Add Monitoring alert rules as searchable `resource` items and register each Settings sub-section as a `nav` item, so the palette — presented as the universal search surface — actually covers all nine areas. |
| 9.3 | DemoTour callout for the AKS interaction convention | `web/src/lib/stores/demo-tour.ts:10-59` | Once Batch 1's AKS fixes land, add a tour step (or spotlight) explicitly noting AKS's row interaction model, so first-run users aren't left to discover it by trial and error. Sequence after Batch 1.1, since the convention being described should be the corrected one, not the reported bug. |

## Known conflict risks / sequencing

- **Batch 0 lands first.** Units 1.3 (AKS confirm standardization), 3.3, 5.1, 6.3, 8.1/8.7 all
  reuse the `ConfirmBar` mechanism from 0.1; 0.2's notify wrapper is reused by 3.1/3.2, 5.1, 7.1,
  8.3; 0.3's `LastRefreshed` is mounted by 2.2, 3.5, 6.5, and referenced by 9's dashboard tiles
  indirectly via 0.6. Land Batch 0 before merging any unit that says "via Batch 0.X" above.
- **Same-file hot spots within Batch 1 (AKS).** `shared/ResourceTable.tsx` is touched by 1.1, 1.2,
  and 1.7 — these three must be sequenced (1.1 → 1.2 → 1.7), not parallelized, with a rebase
  between each. `shared/AksWorkspaceContext.tsx` is touched by 1.4 and 1.6 — sequence 1.4 before
  1.6. `PodsTab.tsx` is touched by 1.4 and 1.5 — sequence 1.4 before 1.5 (1.5 depends on 1.4's
  state changes anyway).
- **Batch 3 (Service Bus).** `MessageList.tsx` is touched by 3.1, 3.3, and 3.5 — land in that
  order. `MessageDetail.tsx` is touched by 3.2 and 3.3 — land 3.2 before 3.3.
- **Batch 4 (API Client).** `ApiClientPageContext.tsx` is touched by 4.1, 4.2, and 4.3 — land in
  that order (each touches a different function within the file, so conflicts are localized, but
  review one at a time with a rebase in between to avoid stacked diffs against the same file).
- **Batch 6 (Storage).** `BlobBrowserPanel.tsx` is touched by 6.2, 6.3, and 6.5 — land 6.2 first
  (pure bug fix, no dependency), then 6.3/6.5 in either order (different functions).
- **Batch 8 (Settings).** `useProfile.ts` is touched by 8.3 and 8.4 — land 8.3 first (the `onError`
  addition), then 8.4 (the `scope` addition), to avoid two units racing to edit the same hook body.
- **Across batches:** no two batches touch the same file (each feature area is a separate
  directory), so Batches 1–9 can proceed in parallel with each other once Batch 0 is merged. Within
  a batch, follow the ordering above.

## Worker instructions

Use this template for every unit above, substituting `{UNIT_ID}`, `{UNIT_TITLE}`, `{FILES}`, and
`{CHANGE}` from the work-units table.

---

**Context:** You're implementing one unit of a larger UX-consistency initiative for SwebKit, a
Tauri + React desktop debugging tool for .NET developers (AKS/Kubernetes, Azure Service Bus,
Redis, Blob Storage, App Insights alerting, an AI agent, and an API client). The initiative was
triggered by two reports — AKS rows needing a right-click to do anything, and Redis not being
collapsed by default — and a full code audit found the same class of problem (missing click
affordances, inconsistent confirmation, misleading loading/error states, silent mutation failures)
repeated across every feature area. Full context: `docs/features/active/ux-interaction-consistency/index.md`
and `technical-plan.md`.

**Your unit:** {UNIT_ID} — {UNIT_TITLE}

**Files:** {FILES}

**Task:** {CHANGE}

**Conventions to follow:**
- Check `docs/pitfalls/react-frontend.md` before touching TanStack Query keys, `EventSource`/SSE
  code, CodeMirror editors, resizable panels, or anything Tauri-boundary-related — several of the
  bugs in this plan are the exact shape of pitfalls already documented there.
- Reuse existing shared components (`shared/ConfirmBar`, `shared/QueryState`/`EmptyState`/
  `Skeleton`, `shared/LastRefreshed` once Batch 0.3 lands, `useNotifyMutation`) rather than
  introducing a new one-off pattern — the whole point of this initiative is stopping that drift.
- Match existing naming/test-id conventions in the file you're editing (e.g. `data-testid` patterns
  already in use in that component).
- Don't refactor beyond what your unit's `Change` describes — other units touch nearby code; a
  wider refactor increases conflict risk for units sequenced after yours (see "Known conflict
  risks / sequencing" above — check whether your unit has a stated order dependency before you
  start).
- No new dependencies without checking `web/package.json` first for something already installed
  that does the job (e.g. TanStack Query's `keepPreviousData`, already available at the installed
  `@tanstack/react-query` version).

**Verification (run from `web/`):**
1. `npx tsc -b` — must be clean.
2. `npm run lint` — must be clean (or no new violations).
3. `npm run test:unit` — must pass; add/update a unit test for any pure function you changed
   (e.g. a label-computation helper, a filter predicate, a query-key builder).
4. `npm run test:e2e` — run the spec(s) covering the feature area you touched (e.g.
   `npx playwright test e2e/aks{,-ux,-deferred}.spec.ts` for AKS units); add a new assertion if
   your unit changes user-observable behavior an existing spec doesn't cover (a real content/count
   assertion, not just an absent-placeholder-string check — see the pitfalls doc's note on this).
5. Manually exercise the specific interaction your unit fixes in the running app (`npm run tauri
   dev` from `web/`, or `npm run dev` + a browser for anything not Tauri-boundary-specific) — for
   any unit whose `Change` involves clicking a row, expanding/collapsing, or confirming a
   destructive action, a passing test suite alone doesn't confirm the interaction *feels* right.
6. Run **aikido_full_scan** (per `docs/security/aikido-mcp-scan.md`) against every file you added
   or modified; if it flags anything, fix and rescan until clean.

**When done:**
1. Update this unit's row status in `docs/features/active/ux-interaction-consistency/status.md`.
2. Stage only the files relevant to this unit.
3. Commit with a message describing what changed and why (reference the unit ID, e.g.
   "AKS: fix ResourceTable click-affordance root cause (unit 1.1)").
4. Push and open a PR against `main`, referencing `docs/features/active/ux-interaction-consistency/`
   in the description.
5. Report back in one line: `Unit {UNIT_ID}: <done|blocked> — <one-sentence summary> — PR #<n>`.

---

## Verification (of this plan's own prerequisite)

There is no setup step beyond the already-confirmed clean tree, so there's nothing separate to
verify here beyond what's already stated in Context: `git status` at the start of this plan showed
a clean working tree on `main`. Each unit verifies itself per the Worker instructions above; the
plan-level gate is that Batch 0 merges (and its own verification passes) before any unit that
depends on it, per "Known conflict risks / sequencing."
