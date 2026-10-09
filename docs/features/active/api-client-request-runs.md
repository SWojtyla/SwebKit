# API Client request runs — dependency chains + batch runs

State: Review

## Goal

Let a user run more than a single request, without changing what a request is:

- **Dependencies** — a request declares "run these before me" (`dependsOnRequestIds`, same collection). Sending it with the chain option runs prerequisites in topological order first.
- **Batches** — run a folder/collection in tree order, or run an arbitrary multi-selection, in order.
- **Single stays default** — plain Send is unchanged; chains are opt-in per send.

Capture rules already give the data-flow half: step N captures a token into a variable, step N+1 resolves `{{token}}` fresh — so `login → authed calls` chains work without new variable plumbing.

## User decisions (locked)

- Batches = folder/collection order + ctrl-click multi-select. **No** named saved sequences (v2 candidate).
- Send default = single request; "Send with dependencies (N)" is a split-button option.
- Execution = **SSE live progress** (`POST /api/api-client/run`, `text/event-stream`, fetch-reader pattern like `streamAgentChat`).

## Model

```csharp
// HttpRequestEntry (ApiClientModels.cs) — persists in collections.json AND .swebreq.json
public List<string> DependsOnRequestIds { get; set; } = [];
```

Same-collection references only. A dep pointing at another collection or a deleted request makes the plan fail fast (see errors).

## Run request → plan → SSE

```
POST /api/api-client/run            text/event-stream

{
  "mode": "requestWithDeps" | "subtree" | "explicit",
  "collectionId": "...",            // required
  "linkedRootId": "..." | null,     // set when the collection lives under a linked root
  "requestId": "...",               // requestWithDeps
  "nodeId": "...",                  // subtree: folder node id or collection root
  "requestIds": ["..."],            // explicit: caller-supplied order
  "activeEnvironmentId": "..." | null,
  "globalEnvironmentId": "..." | null,
  "stopOnError": true,
  "delayMs": 0
}
```

Plan resolution (server-side, `ApiClientRunService` in Core):

- `requestWithDeps`: DFS topo-sort of `dependsOnRequestIds` transitive closure, then the request. Dep on missing/cross-collection node → `400 {"error":"missing_dependency"|"cross_collection_dependency","requestId":...}`; cycle → `400 {"error":"dependency_cycle","cycle":[ids]}`.
- `subtree`: request nodes under `nodeId` in tree order (folders' `Children` order).
- `explicit`: given order; any unknown id → 400.
- Cap: > 25 steps → `400 {"error":"too_many_steps","max":25}`.
- Empty plan → `400 {"error":"empty_plan"}`.

Events (`data: {...}\n\n`), one JSON per line:

- `{"type":"plan","runId":...,"steps":[{"index":n,"requestId","name"}]}`
- `{"type":"stepStarted","index":n,"requestId","name"}`
- `{"type":"stepCompleted","index":n,"requestId","status":200,"durationMs":123,"captured":[{"targetVariable","source"}],"response":<same map as /execute result>}`
- `{"type":"stepFailed","index":n,"requestId","status":404|null,"durationMs":n,"error":...,"response"?:...}` — a step fails on transport error **or** HTTP status >= 400 (documented; chains asserting happy paths should stop on a 500).
- `{"type":"aborted","reason":"stopOnError"|"cancelled","completedSteps":n}`
- `{"type":"done","completedSteps":n,"failedSteps":n,"durationMs":n}`

Client disconnect cancels via `CancellationToken`. `delayMs` slept between steps (cap 10s).

Each step executes through the existing `ApiRequestExecutor` path (env/collection/global resolution + auth + captures), identical to `/api/api-client/execute`.

## Persistence plumbing

- `collections.json` + `environments` untouched otherwise — optional field, backward compatible.
- `.swebreq.json` (linked roots): serialize `dependsOnRequestIds` when non-empty; tolerate missing on read.
- Linked-root endpoint DTOs + collection import carry the field through.

## Frontend

- `transport.ts`: `streamApiRun(req, onEvent, signal)` — fetch + `getReader()` SSE parse, mirroring `streamAgentChat`.
- `useApiRun` hook: owns run state (`idle|running|done|aborted|error`), live step list, abort via AbortController.
- Request editor: "Runs after" section — chips of dep requests (name, remove ✕), picker listing same-collection requests (flattened, excludes self + would-cycle).
- Send → split button: primary "Send" (single, unchanged); dropdown "Send with dependencies (N)" (hidden/disabled when no deps), "Run…" for options.
- CollectionTree: ctrl/cmd+click toggles a selection set (distinct highlight from the open-tab highlight); context menu gains "Run in order" on folders/collections and "Run selection (N)" when N>1 selected; selection cleared on single click / Escape.
- RunResultsDrawer (bottom or right panel): live steps — spinner → status icon + code + ms; expandable response per step; captured vars listed; abort button while running; final step's response loads into the response viewer.
- Run options popover: stop-on-error toggle (default on), delay-ms input.
- Demo mode: runs work (demo collection + real executor); add a demo request pair (`Get token` → `List orders` with dep) to `DemoApiCollectionFactory` so the chain is demoable/e2e-testable.

## Non-goals (v2+)

Named saved sequences, cross-collection deps, parallel groups, per-request timeout overrides, retries, data-driven iteration, run history persistence.

## Test plan

- Core: topo order (diamond, linear), cycle → error w/ cycle path, missing/cross-collection dep, cap, subtree ordering, stop-on-error abort, delay honored, captures flow between steps.
- Sidecar: run endpoint streams events in order (plan → starts → completes → done), 400 cases, demo-mode run, linked-root collection run, cancel.
- Vitest: plan→event state reducer, dep picker excludes self/cycles, multi-select reducer.
- Playwright `api-client-runs.spec.ts`: demo chain run via Send dropdown (steps appear live, final response in viewer), folder Run in order, multi-select run, abort, stop-on-error behavior.
- Aikido on touched files.

## Tasks

### Core + contracts

- [x] `DependsOnRequestIds` on `HttpRequestEntry` + `.swebreq.json` + linked DTOs
- [x] `ApiClientRunService`: plan builder + run loop + event records + tests
- [x] `POST /api/api-client/run` SSE endpoint + DI + tests
- [x] Demo collection gains a chained request pair (`Get token` → `List orders`)

### Frontend

- [x] types + `streamApiRun` + `useApiRun`
- [x] "Runs after" editor section + dep picker
- [x] Send split button with "Send with dependencies (N)"
- [x] Tree multi-select + Run context menus
- [x] RunResultsDrawer + options popover + abort
- [x] e2e spec

### Validation

- [x] Core + sidecar + vitest + tsc + eslint green
- [x] api-client e2e green incl. new runs spec
- [x] Aikido scan; feature doc → Review

## Validation results

- Core: `dotnet test -c Release` **1006/1006** (incl. `ApiClientRunServiceTests`
  — topo/diamond order, cycle+missing+cross-collection errors, cap, abort,
  cancellation, capture visibility — and `.swebreq.json` round-trip tests).
- Sidecar: `dotnet test -c Release --filter "LinkedRoots|DemoMode|ApiClientRun"`
  **95/95** (`ApiClientRunEndpointTests`: SSE frame order, structured 400s,
  subtree/explicit order, demo run, linked-root run, disconnect abort,
  `dependsOnRequestIds` create/save round-trip).
- Web: `tsc -b` clean · vitest **857/857** (36 `api-run-utils` + 10 transport
  tests) · eslint 0 errors (1 pre-existing TanStack Virtual warning).
- Playwright `api-client-runs.spec.ts` **6/6** — full SSE flow e2e including
  Send-with-deps → drawer → response viewer.
- Playwright config fix worth knowing: occluded headless Chromium suspends the
  long-lived SSE fetch stream (`ERR_NETWORK_IO_SUSPENDED`); fixed with
  `--disable-backgrounding-occluded-windows` et al. in `launchOptions`.
- Aikido: sidecar + web files 0 issues; Core scan flags only pre-existing
  `LinkedCollectionFileService` file-I/O heuristics (not in this diff).
