# API Client Fixes — Status

**Status:** `Review`
**Branch:** `feat/api-client-fixes`
**Jira:** not linked

## Checklist

### 1. Secret Store mode end-to-end

- [x] `POST /api/api-client/credentials` + `DELETE /api/api-client/credentials/{key}`
      → `ICredentialStore.Save/Delete` (sidecar)
- [x] `POST /api/api-client/preview-credential` → exists check + masked value
      (mirrors `preview-keyvault-secret`; never returns the raw secret)
- [x] `VariableList` credential mode: secret value field (debounced save under
      the credential key, auto `sw-secret:` key when blank), masked state,
      Preview button, hint text

### 2. Bounded faker dates

- [x] `fakerDateAfter`/`fakerDateBefore` on `VariableGeneratorDefinition`
      (Core model + TS type)
- [x] `VariableGeneratorService`: bounds applied to `date.*` categories via
      `Date.Between`; new `date.between` category requiring both bounds
- [x] `GeneratorConfig`: date pickers for `date.*` categories + `date.between`
      option + help text

### 3. Scan bugs

- [x] `VariableGeneratorService.GenerateInteger`: `maxInt = int.MaxValue`
      overflow — inclusive bound computed via `NextInt64` instead of `max + 1`
- [x] `isLikelySecret` (frontend): added `authorization` to match backend masking
- [x] Unresolved-variable send warning: `HttpRequestExecutor` scans the fully
      substituted URL, sent headers, and body for leftover `{{tokens}}` and
      surfaces them in the result warnings shown in the response panel
      (renamed UI label "Capture warnings" → "Warnings")

### Cross-cutting

- [x] Tests: xunit (generator bounds/overflow, credential endpoints, unresolved
      warnings), vitest (`isLikelySecret`), Playwright (credential flow,
      date bounds)
- [x] `status.md` kept current

## Validation

- `dotnet test` — Core **1017**, Sidecar **489**, Agents **253**, all green
- `npx tsc -b --force` — clean
- `npm run lint` — 0 errors (100 pre-existing warnings)
- `npm run test:unit` — 474 green
- `npm run build` — clean
- Playwright — `api-client-credentials.spec.ts` 4 new tests green;
  `api-client.spec.ts` + `api-client-variables.spec.ts` 41 green
  (faker category count assertion updated 24 → 25 for `date.between`)
- Aikido MCP scan — unavailable in this environment (pending)

## Non-goals (documented)

- Collection-level secrets (`CollectionVariable` has no `secretSource`)
- Unifying the Tauri keyring vault and the sidecar credential store
- List-generator comma escaping
