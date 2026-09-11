# API Client Variable Visibility & Environment Scoping — Technical Plan

## 1. Variable highlighting in the request body editor

**New — `web/src/lib/codemirror-variables.ts`.** `variableHighlighting(scope)` returns a
CodeMirror extension composed of:

- a `ViewPlugin` + `MatchDecorator` over `VARIABLE_TOKEN_SOURCE` applying
  `Decoration.mark({ class: "var-tok-resolved" | "var-tok-deferred" | "var-tok-unresolved" })`;
- a `hoverTooltip` rendering the same sentence the URL field puts in its `title`.

This is the repo's first use of `Decoration` / `ViewPlugin` / `MatchDecorator` /
`hoverTooltip`. All come from `@codemirror/view` ^6.43.7, already a dependency — no package
changes.

Colours come from the existing `.var-tok-*` classes in `globals.css`, never JS literals: a
`HighlightStyle` is baked into the `EditorState` at creation and the app has several
`<html>`-class themes (`docs/pitfalls/react-frontend.md`, CodeMirror section).

**Pure logic lives in `web/src/lib/variableHighlight.ts`**, not beside the extension, so it
is unit-testable — vitest here runs node-only with no DOM (`web/vitest.config.ts:4-7`):

- `VARIABLE_TOKEN_SOURCE` — the pattern's source text. `MatchDecorator` must own a `RegExp`
  with its own `lastIndex`; sharing the module's instance would have the two scanners
  corrupt each other's cursor.
- `variableMarkClass(rawName, scope)` — the class, or `null` for `{{}}`, which names nothing.
  `MARKS` in the extension is keyed by this return value so the decoration and the tested
  classifier cannot drift.
- `variableHoverAt(lineText, offset, scope)` — the token covering an offset, with bounds.
- `describeVariableToken(token)` — the hover sentence, now shared with `VariableInput.tsx`
  so the URL bar and the body cannot word the same state differently.
- `unresolvedVariableNames(text, scope)` — de-duplicated, in first-seen order.

**`RequestEditor.tsx`.** `BodyCodeEditor` gains a `scope` prop and a second `Compartment`
beside `languageRef`, reconfigured from its own effect. The build-once effect must not gain
`scope` as a dependency — recreating the view drops cursor, scroll and undo history, which
is why `ResponseBodyViewer.tsx` uses compartments too.

The reconfigure effect is keyed on `JSON.stringify(scope)`, not the object identity:
`buildVariableScope` returns a fresh object every render, so depending on the reference
would rebuild the decorator on every keystroke.

The preview strip now covers URL **plus body plus enabled header values**, and a new
always-visible banner (`data-testid="unresolved-variable-warning"`) names every undefined
variable. The banner is deliberately outside the `showVarPreview` toggle — the toggle is
exactly what nobody opens before a send they expect to work.

## 2. cURL panel

**New — `web/src/lib/curl.ts`.** `buildCurl(request, resolvedUrl, scope)`, extracted from
`ResponseViewer.tsx` so it can be unit-tested. Body and header values are substituted
client-side with the existing `substituteVariables`.

`resolvedUrl` still comes from the response because the backend also folds in enabled query
parameters. Substitution is **not** moved server-side: `HttpRequestResult` would then carry
expanded Key Vault and Windows credential-store secrets into a copy-to-clipboard panel.
Those stay `{{TOKEN}}`, matching `resolveEnvironmentVariable`, which leaves them `null` in
the UI scope for the same reason.

`ApiClientPage.tsx` passes `ctx.variableScope` to `ResponseViewer`.

## 3. Variable row legibility

`VariableList.tsx` — the key input's hardcoded `w-32` becomes `min-w-32 flex-1`, with
`title={v.key}` for the overflow that remains at the floor. Plain and credential modes move
onto the key's row (`min-w-0 flex-[2]`), halving the height per variable.

Key Vault and generator modes keep a row of their own: several controls side by side is the
horizontal overflow that `api-client-ux-improvements` stacked this layout to fix, and that
fix is preserved.

`EnvironmentManager.tsx` — default size raised to `1040x720`; the resize mechanism shipped
in PR #82 is untouched. New `fitToViewport` clamps a remembered size to the current window,
because a size saved on a large monitor reopened off the edge of a laptop display with the
resize grip out of reach. The floor stays `MIN_SIZE` (640x420), which the inner
`ResizablePanels` minimums (`[180, 320]`) fit inside at 1280px.

`CollectionVariableEditor.tsx` — `w-[500px]` becomes `w-[min(56rem,92vw)]`, `max-h-[80vh]`,
with the list capped by `overflow-auto`.

### Stale-snapshot fix in `CollectionVariableEditor`

Saving closes the dialog while the store write is still in flight. Reopening before it lands
mounted the editor against the pre-save collection, and because the `useState` initializer
runs once, the list stayed empty even after fresh data arrived — the variables looked lost,
and saving again would really have lost them. A `useEffect` re-syncs when
`collection.variables` changes identity. Safe against clobbering an edit in progress: the
dialog closes on save, so while open this component is the only writer.

This was a latent bug; the extra per-render work in this feature made the race lose
reliably, which is how the existing e2e test caught it.

## 4. Global + project environments

Precedence, lowest first: **collection variables → active global environment → active
collection-scoped environment → Key Vault resolution.**

### Frontend

`buildVariableScope(collectionVariables, environments)` now takes an ordered, null-tolerant
list instead of one environment.

`ApiClientPageContext.tsx` gains `resolveEnvironmentLayers(collectionId)`, used by **both**
the preview scope and the send payload — resolving it in two places is how the preview would
start describing something other than what is sent. It reads the two slots that already
existed in the stored UI state, and contains the compatibility path: a global slot still
holding a collection-scoped environment (picked before the two layers existed) is honoured
as that collection's project selection rather than applied to every collection.

`handleSetScopedEnvironment(collectionId, envId)` writes
`activeEnvironmentIdByCollection`, and clears a global selection that is really scoped to
that collection so the compatibility path cannot keep overriding the new choice.

`ApiClientPage.tsx` shows two pickers. `env-selector` keeps its test id and its meaning (the
global slot) and now lists only `collectionId === null` environments; `env-selector-scoped`
appears when a collection is active and lists only that collection's. This ports the
grouping the Blazor toolbar has always had.

### Backend

`IVariableSubstitutionService.BuildScope`/`BuildScopeAsync` take
`IReadOnlyList<ApiEnvironment?>`. The four single-layer callers pass `[activeEnvironment]`.

`BuildScopeAsync` resolves Key Vault **only for the definition that won**. Walking the layers
and resolving each one's Key Vault variables in turn let an earlier layer's secret overwrite
a later layer's plain override — the global layer would silently beat the project layer for
exactly the keys backed by a vault. Caught by writing the test before trusting the code.

`IHttpRequestExecutor.ExecuteAsync` gains `ApiEnvironment? globalEnvironment = null` before
the cancellation token; `ExecuteRequestRequest` gains `GlobalEnvironmentId`, resolved and
404-checked alongside `EnvironmentId`.

## Files changed

| Area | Files |
| --- | --- |
| New | `web/src/lib/codemirror-variables.ts`, `web/src/lib/curl.ts` |
| Frontend | `variableHighlight.ts`, `variable-utils.ts`, `hooks/useApiClient.ts`, `RequestEditor.tsx`, `ResponseViewer.tsx`, `VariableInput.tsx`, `VariableList.tsx`, `CollectionVariableEditor.tsx`, `EnvironmentManager.tsx`, `ApiClientPage.tsx`, `ApiClientPageContext.tsx` |
| Backend | `IVariableSubstitutionService.cs`, `VariableSubstitutionService.cs`, `IHttpRequestExecutor.cs`, `HttpRequestExecutor.cs`, `ApiClientWorkflowService.cs`, `GraphQlSchemaService.cs`, `GraphQlSubscriptionService.cs`, `src-sidecar/Endpoints/ApiClientEndpoints.cs` |
| Blazor | `RequestBuilderPanel.razor` (kept compiling against the layered API) |
