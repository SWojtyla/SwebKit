# API Request Chains

**State**: Done
**Owner**: Devin (agent-driven)
**Depends on**: api-client-request-runs (merged, PR #121)
**Shipped on**: feat/api-request-chains (`db49456d` UI, `73c146d4` backend)

## Problem

"Run in order" / "Run selection" execute requests in tree order **within one
collection only**. `BuildPlan` resolves a single `collectionId`; a dependency
id pointing at another collection fails the plan with
`cross_collection_dependency`. There is no way to build a named, ordered,
re-runnable sequence spanning collections — e.g. *POST /login* (Auth
collection) → *GET /me* (Profile collection) → *POST /order* (Billing
collection), where each step consumes a captured variable from the previous.

## Decision (user-approved 2026-10-09)

- **Persisted named chains** — a new entity saved to the internal store
  (`chains.json` next to `collections.json`), not ad-hoc pick-and-run.
- **Internal store only, plus export** — chains are never written into linked
  roots (steps may mix internal and linked-root requests). Export/import rides
  the existing pack surface.
- **Dependencies run cross-collection inside chains** — each step topo-sorts
  its `DependsOnRequestIds` first, and the `cross_collection_dependency` ban
  is lifted for steps inside a chain run. Deps resolve against the dep's own
  collection, with a global request index as fallback.
- **Run-scoped variable overlay** — captured variables land in a run-local
  bag shared by all steps, outranking env/collection layers, and die with the
  run. Captures still write into their owning scope in place (existing
  semantics preserved); the overlay adds cross-collection visibility.

## Model

```csharp
public sealed class ApiChain
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public List<ApiChainStep> Steps { get; set; } = [];
    public DateTimeOffset CreatedAt / UpdatedAt
}

public sealed class ApiChainStep
{
    public string Id { get; set; }           // stable step id (SSE correlation)
    public string CollectionId { get; set; } // internal or linked-root collection id
    public string? LinkedRootId { get; set; }
    public string RequestId { get; set; }
    public bool Enabled { get; set; } = true;
}
```

`ChainRepository` mirrors `CollectionRepository`: JSON file under the app data
dir, load-once cache, atomic write. Disabled-state and missing-request rows
are preserved honestly (plan error `unknown_request` names the step), never
silently dropped.

## Run mode

`ApiRunRequest` gains mode `"chain"` with `string? ChainId`. The endpoint
loads the chain, expands each **enabled** step into plan steps:

- Each step resolves its own collection via `ResolveRunCollectionAsync`
  (internal → linked root → demo), so a step carries its *own* scoped/global
  environment pair — `ApiRunPlan` steps must carry `(collection, envIds)`
  context instead of the plan holding one collection.
- The step's request is topo-sorted with its `DependsOnRequestIds` resolved
  in its own collection first, then a run-wide request index across every
  collection reachable in the run (cross-collection deps allowed here only).
- Cycle detection spans collections — `dependency_cycle` reports the id loop.
- Existing `StopOnError` / `DelayMs` / `MaxSteps` (post-expansion cap) apply.
- SSE `plan`/`stepStarted` events gain `collectionId` + `collectionName` +
  `stepId` so the drawer can label steps honestly; an expanded dep step is
  flagged `isDependency: true` and tagged with the step that pulled it in.

## Run-scoped variable overlay

- `HttpRequestExecutor` gains an optional `overlay` dictionary parameter
  (default null): applied at top priority after the existing env layers in
  `BuildScopeAsync`.
- After each step the run service reads its captured variables
  (`ApiRunCapturedVariable.TargetVariable` → value) and upserts them into the
  bag; later steps resolve `{{var}}` from the bag before any env layer.
- Capture rules still write into their owning collection/env in place — the
  overlay is additive cross-collection visibility, not a semantics change.
- The `stepCompleted` `captured` array already reports what was written; add
  `scope: "run" | "environment"` per capture when the target lands in the bag.

## Endpoints

- `GET /api/api-client/chains` — list (id, name, stepCount, updatedAt).
- `GET /api/api-client/chains/{id}` — full chain.
- `POST /api/api-client/chains` — create (name + steps, validated against
  reachable collections at save time but stored verbatim — a broken ref
  surfaces at plan time, not save time).
- `PUT /api/api-client/chains/{id}` — replace steps/name/description.
- `DELETE /api/api-client/chains/{id}`.
- `POST /api/api-client/run` — accepts `mode:"chain", chainId` (existing SSE
  contract, extended fields above).
- Export/import: chains join the existing collection-pack export shape
  (optional `"chains"` array); import merges with id-regen on collision.

## Frontend

- **Chains section** in the API client sidebar (below collections): list of
  named chains, step count, context menu (Rename / Delete / Run / Export).
- **Chain editor** — drawer or dialog: name, ordered step list (drag to
  reorder, per-step collection badge + request method/name, enabled toggle,
  remove), "Add request" picker spanning all collections + linked roots.
- **Context menu** — "Add to chain →" on request nodes (submenu: existing
  chains + "New chain…").
- **Run integration** — Run button streams into `RunResultsDrawer`; step rows
  show collection badge, dep steps indented/marked; overlay captures show in
  the existing captured-variables display.

## Demo seed

`Demo API Samples` gains one demo chain (e.g. *JSONPlaceholder CRUD flow*:
POST /posts → GET /posts/{capturedId} → DELETE) exercising cross-step capture
so the feature is exercisable in demo mode.

## Honesty / edge cases

- A step whose request vanished (renamed/deleted import, removed linked root)
  is a plan error naming the step — never silently skipped.
- Disabled linked root containing a step → the existing 400 path, per step.
- Demo mode: linked-root steps in a chain report "disabled in demo mode" like
  every other linked-root surface.
- `MaxSteps` counts **expanded** steps (steps + deps) so a chain can't smuggle
  past the cap via dependencies.
- Export omits nothing secret: steps are id refs only; captured values are
  run-scoped and never persisted.

## Tests

- Core: plan expansion (order, dep topo-sort, cross-collection dep, cycle,
  disabled step skipped, cap on expanded count), overlay precedence
  (bag > scoped env > global env > collection vars), repo CRUD. — **19 tests**
- Sidecar: endpoints (404s, linked-root refusal in demo, plan errors
  serialized), SSE field extensions. — **20 tests**
- Web (vitest): editor reducer, context-menu add-to-chain, drawer badges.
  — **14 tests**
- Playwright: build chain → run → assert step order + captured var visible in
  step 2's sent request. — **3/3 green** (`e2e/api-chains.spec.ts`)

## Validation results

- `dotnet build SwebKit.slnx` — 0 warnings / 0 errors
- Core 1029 · Sidecar 925 · vitest 890 · tsc/eslint clean · Playwright 3/3
- Aikido scan not run (no MCP server configured in this environment).

## Implementation decisions (post-spec)

- Team-pack key is `apiChains` (camelCase of `TeamPack.ApiChains`),
  `?sections=apiChains` accepted — not `chains` as sketched above.
- Save is verbatim-permissive: only `name` required, step ids back-filled;
  broken refs surface as plan errors (spec's "surfaces at plan time" wins).
- Dedupe applies to *dependency expansions* across steps; two explicit steps
  pointing at the same request both execute — chains are ordered sequences.
- `scope:"run"` is reported on captures in *all* run modes, not only chain —
  the overlay bag exists for every `RunAsync` call.
- Demo chain is read-only (PUT/DELETE → 400), prepended to the list in demo
  mode via `DemoApiCollectionFactory.DemoChainId`.
