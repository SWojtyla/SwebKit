# Frontend — Agent Workspace Awareness

All work in `web/` (React + TanStack Query). The design premise: **the data on
screen already lives in the query cache** — serializers read rendered/cached
state, they don't fetch.

## Module 1 — screen-state snapshot

### Registry + contract (`web/src/lib/stores/screen-state.ts`, new)

```ts
export interface ScreenSnapshot {
  sessionId: string;          // contextual panel session, or "global"
  route: string;              // current location pathname
  featureArea?: string;       // FeatureArea enum name when contextual
  capturedAt: string;         // ISO timestamp
  snapshot: unknown;          // bounded area-specific payload
}

type ScreenStateProvider = () => ScreenSnapshot | null;
```

- `registerScreenStateProvider(id, provider)` / `unregister(id)` — components
  register on mount, clean up on unmount.
- `publishScreenState()` — collects the current provider's snapshot and POSTs to
  `/api/agent/screen-state` via `api.ts`, debounced (~1.5 s). Publish triggers:
  provider data changes (subscribe to the relevant query-cache keys or the
  selection state the page already maintains) and route changes.
- Only the provider matching the current route/context contributes — multiple
  providers may be mounted (page + contextual panel); the panel's own snapshot
  wins for its session.

### Per-area serializers

One serializer per existing contextual surface — each emits **curated, bounded**
fields: what's selected plus the visible slice of already-loaded data.

| Surface | File (registration point) | Snapshot contents (bounded) |
| ------- | ------------------------- | --------------------------- |
| AKS | `AksPage.tsx` / `AksWorkspaceContext` | context, namespace, resource kind, selected resource name, visible list rows (cap ~30, `+N more`), open panel (logs tail ~last 40 lines, yaml kind only) |
| Service Bus | `ServiceBusPage.tsx` | namespace alias, entity type + name, peeked message summary (id/enqueued/body preview ~500 chars, no full payload), DLQ counts |
| Redis | `redis/tabs/KeyDetailPanel.tsx` | cache, key name/type, TTL, value preview (truncated), active tab |
| Storage | `storage/BlobDetailPanel.tsx` | account, container, blob name, properties shown (size/content-type/tier), preview excerpt |
| Monitoring | `monitoring/AlertRuleRow.tsx` | rule id/name, source, last fired state visible in the row |
| API client | `api-client/GenerateApiRequestPanel.tsx` | method, URL, collection — **never auth headers or body secrets** |
| Global page | `ContextualAssistant` fallback / route change | route + page title only |

Bounds per serializer: list rows ≤ 30 (+`+N more`), string fields truncated
(~500 chars), total payload < ~6 KB (store rejects >8 KB anyway).

### Secret hygiene (hard requirement — D5)

Serializers **whitelist** fields — they never dump a whole object or query entry.
Explicitly excluded everywhere: auth/Authorization headers, tokens, connection
strings, credential fields, env-var values. The API-client serializer in
particular must respect the existing secret-masking conventions
(`docs/pitfalls/api-client.md`, `variable-utils.ts` masked-value handling).

### Files touched (expected)

| File | Change |
| ---- | ------ |
| `web/src/lib/stores/screen-state.ts` | new registry + publisher |
| `web/src/lib/api.ts` | `postScreenState` helper (with `AbortSignal`) |
| `web/src/lib/hooks/useContextualAgent.ts` | pass stable session id to registry |
| `web/src/components/{aks,service-bus,redis,storage,monitoring,api-client}/…` | per-area provider registration |
| `web/src/AppLayout.tsx` (or router effect) | global-route fallback snapshot |
| `web/src/test/` + `web/e2e/` | unit + Playwright specs |

## Module 3 — alert → AI activation UX + background notify

- `AlertRuleDialog.tsx` — "AI investigation" checkbox bound to
  `aiInvestigationEnabled` (default checked for new rules), with hint text:
  "When this alert fires, the agent investigates related workspace resources
  and posts an insight. Requires an active agent profile with tool calling,
  and the alert's resource added to the Map (Settings → Map)."
- `AlertRuleRow.tsx` — small "AI" indicator next to the enable toggle when
  `aiInvestigationEnabled` (title attribute explains + notes the Map/profile
  requirements).
- `MonitoringPage.tsx` — `onInsightReady` handler also calls
  `showNotification("Investigation ready", …)` (truncate summary ~200 chars) so
  the completed investigation pings the user while minimized; the alert-fired
  toast already exists.

## Module 2 — investigation depth (frontend is thin)

- `ProactiveInsightCard.tsx` — render the richer payload when present: hypothesis
  line (existing), evidence bullets (new, collapsible), severity badge (new
  optional field). Card must degrade gracefully to today's summary-only shape.
- `useMonitoring.ts` — SSE `proactiveInsightReady` payload gains optional
  `evidence`/`severity` fields; no event-shape break.
- "Open investigation" affordance already navigates to the seeded session —
  verify the seeded evidence transcript renders correctly in chat.
