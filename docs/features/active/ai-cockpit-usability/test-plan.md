# AI Cockpit Usability — Test Plan

## Unit tests

### `tests/SwebKit.Agents.Tests` — `dotnet test`

`ServiceBusToolContextTests` (new) — the resolution order is the contract:

- explicit `namespace` arg matched by alias (case-insensitive), FQDN, and id;
- explicit-but-unknown arg → error listing configured aliases, never a fallback;
- ambient `nsId` selection resolves when no arg is given;
- explicit arg wins over ambient selection (selection is a default, not an override);
- zero configured → "not configured" error; exactly one configured → used without
  arg or selection; multiple configured with neither arg nor selection → error listing
  aliases (the regression guard against the old `namespaces[0]` behavior).

`ServiceBusToolsTests` (extend) — each of the three tools, rewritten:

- routes through `IServiceBusConnectionPool.GetOrCreate` for the _resolved_ namespace
  (fake pool asserting which `ServiceBusNamespace` it was asked for);
- Entra-configured namespace works without any credential store involvement (the
  reported failure's regression guard);
- ambiguity error is returned as tool-result JSON `error` (the shape
  `AgentToolCallOrchestrator.IsErrorResult` detects);
- demo mode still short-circuits to `DemoServiceBusClient`.

`AksToolsTests` (new):

- `get_resource_yaml` returns YAML for a known resource; honors explicit `namespace`,
  then ambient selection, then `AksConfig.DefaultNamespace`; truncates oversized
  output with a notice.
- `propose_apply_aks_yaml` registers a `PendingAgentAction` with
  `Type = ApplyAksYaml`, `Risk = High`, and `Payload` carrying the raw arguments;
  a `ValidateResourceYamlAsync` failure returns an error result and registers
  **no** pending action.

`AksActionExecutorTests` (new) — `CanHandle` matrix; confirmed action calls
`ApplyResourceYamlAsync` with the payload's exact ns/kind/name/yaml; client failure →
`IsSuccess = false` with the message; demo-mode unsupported → honest failure string.

### `tests/SwebKit.Kubernetes.Tests` — `dotnet test`

`CleanEditableYaml` blank-line compaction:

- emitted YAML has no empty lines between block-valued mapping entries (the user's
  Deployment paste is the fixture shape);
- **regression guard:** a ConfigMap whose `data` value is a literal block containing
  blank lines keeps them — only emitter-inserted empties outside block scalars go;
- `metadata`-cleaning behavior (status/managedFields/uid/…) is unchanged;
- re-parsing the compacted output yields an equivalent document (round-trip equality).

### `tests/SwebKit.Sidecar.Tests` — `dotnet test`

`AgentToolCallOrchestratorTests` (extend or new):

- the step-tracking executor sets the ambient selection for the duration of
  `ExecuteAsync` — a probe tool that captures `AgentExecutionContext.Current`
  proves it — and clears/leaves no residue after the call;
- no context → `Current` is null (global session keeps working).

`SwebKitToolsMcpBridgeTests` (extend):

- `BuildUrl` emits `sel=` pairs; `CallToolAsync` reconstructs the same selection into
  the ambient context around `ExecuteAsync`;
- two different session URLs cannot see each other's selection.

### `tests/SwebKit.App.Tests` — `dotnet test` (only if the MAUI adapter ships)

`AppServiceBusConnectionPoolTests` — `GetOrCreate` delegates to
`MonitoringConnectionPool.GetServiceBusClient(alias)`; unknown alias → throws;
`Evict`/`InvalidateAll` forward correctly.

### `web` — vitest (`npm run test:unit`)

`web/src/lib/log-window.test.ts` (extend):

- `searchLogEntries` — match flags; context lines above and below; overlapping
  windows merge into one group (no double-counted lines); gap sentinel between
  non-contiguous groups; `contextLines = 0` degenerates to plain filter behavior;
  context never matches on the timestamp prefix (same rule as `filterLogEntries`);
  matches at buffer edges clamp instead of indexing out of range.

`web/src/components/agent/pending-action-card.test.ts` (extend) — `ApplyAksYaml`
maps to "AKS" in `describePendingActionOrigin`.

`web/src/lib/yaml-noise.test.ts` (new) — `filterGeneratedAnnotations`:

- drops `kubectl.kubernetes.io/last-applied-configuration` (including its single-line
  `|` literal value), `deployment.kubernetes.io/revision`, and `meta.helm.sh/*`
  entries; counts `hidden` correctly;
- keeps other annotations and everything outside `metadata.annotations`;
- handles: no `metadata`, `metadata` without `annotations`, empty annotations block,
  a multi-line literal annotation value on a kept key, and annotations on nested
  `template.metadata` (only top-level `metadata.annotations` is filtered — pod-template
  annotations like `rollme` stay).

## End-to-end (`npm run test:e2e`, Chromium)

`web/e2e/aks-ux.spec.ts` (extend — shared log controls live here):

| Scenario            | Expected                                                                                                                                                                                               |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Context mode toggle | switch to context mode → non-matching lines reappear around matches, dimmed; match count shows "n matches"; gap separators render between groups                                                       |
| Match navigation    | prev/next buttons move the window so the next match is in view; wrap or clamp at ends                                                                                                                  |
| YAML viewer noise   | a stubbed manifest with `last-applied-configuration` renders without it by default with a "N hidden · show" affordance; toggling shows it; Copy still yields the full YAML; Edit loads unfiltered text |

`web/e2e/global-agent-panel.spec.ts` (extend):

| Scenario    | Expected                                                                                                                                                                           |
| ----------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Mode toggle | Ask / Ask & do radios render (replacing the "Ask only" badge); switching to Ask & do persists for the session; a stubbed proposal produces a `PendingActionCard` in the panel      |
| Resize      | drag handle beyond 600px widens the panel; width persists via localStorage; double-click still toggles max/reset                                                                   |
| Overflow    | a stubbed assistant reply containing a long unbroken string and a wide code block produces no horizontal scrollbar on the messages container (assert `scrollWidth <= clientWidth`) |

`web/e2e/contextual-assistant.spec.ts` (extend): same overflow assertion on the
contextual panel's messages container.

`web/e2e/` activity indicator (whichever spec owns the header — likely a layout or
dashboard spec): indicator is hidden when idle and visible while a stubbed query is
in flight (assert on `data-testid="global-activity-indicator"`).

## Backend smoke (manual, against real dev namespace)

The reported failure is config/auth-specific — unit tests cover the logic, but the
actual bug was environmental. Before closing: with ≥2 namespaces configured (at
least one Entra), open the Service Bus page on the non-first namespace, open Ask AI,
ask about the error queue, and confirm the agent reads **that** namespace. Then ask
the same from the global `/agent` page with no selection and confirm the actionable
"which namespace?" error instead of a silent wrong-namespace answer.

## Validation matrix (repo standard)

- `cd web && npm run build`
- `cd src-sidecar && dotnet build`
- `cd tests/SwebKit.Sidecar.Tests && dotnet test` (+ Agents/Core/App tests for touched projects)
- `cd web && npx playwright test` (at minimum the touched specs above)
- Aikido scan per `docs/security/aikido-mcp-scan.md` on new/modified code
