# API Client Variable Visibility & Environment Scoping — Test Plan

## Unit tests

### `web` — vitest (`npm run test:unit`)

Node environment, `src/**/*.test.ts` only. A `.test.tsx` would be silently skipped; there is
no DOM shim and no component-test library by policy, so component behaviour is covered by
Playwright below.

`web/src/lib/variableHighlight.test.ts`

- `unresolvedVariableNames` — names only what is missing; de-duplicates in first-seen order;
  empty when nothing is missing.
- `describeVariableToken` — value for resolved; "not defined" for missing; "resolved when
  sent" for deferred, not "missing"; masks a secret-looking name; `null` for a plain run.
- `variableMarkClass` — each state to its class; tolerates padding inside the braces;
  declines to mark `{{}}`.
- `variableHoverAt` — finds the token at an offset and reports its bounds; covers the braces;
  treats the closing boundary as outside; picks the right token when a line holds several;
  describes a missing variable.

`web/src/lib/variable-utils.test.ts` (new file)

- `buildVariableScope` — later layer overrides earlier; earlier layer fills the gaps the
  later one leaves; every layer overrides a collection variable; skips `null`/`undefined`
  slots; excludes disabled variables and whitespace-only keys; maps a deferred value to
  `null` **while keeping the key present** (the difference between an amber token and a red
  one); a later plain value replaces an earlier deferred one.
- `substituteVariables` — leaves an undefined token as its literal text (the mechanism behind
  the reported 400) and a deferred one likewise.

`web/src/lib/curl.test.ts` (new file)

- Substitutes the body and header values; never a header name.
- Leaves a deferred (Key Vault / credential-store) token as `{{TOKEN}}` rather than expanding
  a secret into the clipboard.
- Leaves an undefined token as its token rather than emitting an empty string.
- Skips disabled and unnamed headers; escapes a single quote; derives a content type from the
  body mode and prefers an explicit one; emits no body flags for a bodiless request; uses the
  executed URL verbatim.

### `tests/SwebKit.Core.Tests` — `dotnet test`

`VariableServiceTests`

- `BuildScope_LaterLayerOverridesEarlierLayer`
- `BuildScope_EarlierLayerFillsGapsTheLaterOneLeaves`
- `BuildScope_SkipsNullLayers`
- `BuildScope_NoLayers_LeavesCollectionVarsIntact`
- `BuildScope_EveryLayerOverridesCollectionVars`
- `BuildScopeAsync_KeyVaultVarInLaterLayerWinsOverEarlierPlainVar`
- `BuildScopeAsync_PlainVarInLaterLayerWinsOverEarlierKeyVaultVar` — the regression guard for
  the ordering bug found while writing these tests.

Existing call sites in `VariableServiceTests` and `VariableGeneratorServiceTests` migrated to
the layer-list signature.

## End-to-end (`npm run test:e2e`, Chromium)

`web/e2e/api-client-variables.spec.ts`

| Scenario | Steps | Expected |
| --- | --- | --- |
| Body variables are coloured | Seed a request with a JSON body holding a resolved, an undefined and a generated variable; open the Body tab | `.var-tok-resolved`, `.var-tok-unresolved` and `.var-tok-deferred` each match the right token inside `request-body-codemirror` |
| Undefined variable is announced | Seed a body with `{{Nope}}`; select the request | `unresolved-variable-warning` names `Nope` while `variable-preview` is still absent |

Assert on a **short** body: CodeMirror renders only the visible viewport, so a long document
would put the target outside the DOM.

`web/e2e/api-client.spec.ts` — must keep passing unchanged. Their test ids
(`env-var-key-N`, `col-var-key-N`, `env-selector`, `active-env-name`, …) are a layout
contract that the row reflow and the two-picker toolbar preserve. `env-selector` keeps its
meaning as the global slot.

`web/e2e/api-client-layout.spec.ts:254` already asserts the request body renders in more than
one colour; the new marks must not break its selector.

## Results

- `npm run test:unit` — 234 passed (17 files).
- `npx tsc -b` — clean.
- `dotnet test tests/SwebKit.Core.Tests` — 813 passed.
- `dotnet build SwebKit.slnx` — sidecar, Core and Blazor build.
- `npx playwright test e2e/api-client.spec.ts e2e/api-client-layout.spec.ts e2e/api-client-variables.spec.ts`
  — 55 passed, 1 failed.

### Known failure, not caused by this feature

`api-client.spec.ts:834 "dropping into a NESTED folder keeps the rest of the collection"`
fails on this branch **with the changes stashed**, i.e. it was already failing before this
work. It belongs to `api-client-drag-reorder` and is untouched here.

## Manual verification

Point the dev run at a sandbox appdata root first — `scripts/tauri/run-dev.ps1` sets no
`SWEBKIT_APPDATA_ROOT`, so it reads and writes the same `%APPDATA%\SwebKit\environments.json`
as the installed app, and this feature changes how that file's UI state is used.

1. Reproduce the reported request against `DEV (via APIM)`: the three unknown names render
   red with a "not defined" tooltip, and the banner names them, **before** sending.
2. Define them in a global environment: they turn green, while the collection-scoped
   environment still overrides `AUTH_API_ADDRESS`. The request succeeds.
3. Copy the cURL command and run it in a shell — it reproduces the request.

Playwright cannot catch Tauri-specific breakage: the drag-reorder tests passed for the whole
time that feature was unusable in the app.
