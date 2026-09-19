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

### 4. Whole-feature scan fixes (second pass)

- [x] Key Vault resolvers return `null` on failure instead of
      `[KV_ERROR:*]`/`[KV_UNAVAILABLE:*]` sentinels — a vault outage kept the
      `{{token}}` literal (warning via the D5 scan) instead of sending the
      sentinel to the server
- [x] `verifyApiClientSsl` wired into the sidecar's named `ApiClient`
      `HttpClient` — the toggle was dead in the Tauri runtime; the TLS
      callback reads the setting per request so it applies immediately
- [x] Linked `.swebenv.json`: secret variables export keyed by `SecretSource`
      under their own name (was: required a literal `secret:` key prefix,
      silently dropping editor-created secrets); reload keeps the bare name
- [x] Capture rules skip credential/Key Vault/generated targets with a
      warning (was: dead-write into `.Value` that resolution ignores);
      repository persistence failures now surface as warnings too
- [x] `UrlBuilder` inserts query params before `#fragment` and no longer
      emits `?&` for a trailing `?`
- [x] cURL panel: header values single-quoted (a `"`/`$`/backtick in a value
      could alter the pasted command), `-X` uses the transport method
      (`GraphQl`→POST), as-sent mode renders the echoed `sentBody` (covers
      GraphQL bodies) and masks an api-key query param inside `resolvedUrl`
- [x] Binary responses stream through `LimitedStream` instead of buffering
      the whole body before the 4 MB check
- [x] Auth that can't be resolved (missing credential/param/OAuth2 fields)
      returns a warning via `IAuthHeaderBuilder.ApplyAsync` — no more silent
      unauthenticated sends
- [x] Post-request actions read the sent values (`resolvedUrl`, transport
      method, `sentBody`) rather than the raw draft's `{{tokens}}`
- [x] Variable preview distinguishes deferred (`<resolved when sent>`) from
      unresolved, matching the highlighter's wording
- [x] cURL import maps `-u`/`--user` to Basic auth
- [x] OAuth2 client-credentials tokens are cached per
      endpoint+client+scopes+secret-hash until `expires_in` − 60s
- [x] `DELETE /api/api-client/credentials?key=` variant for keys with `/`;
      `evaluate-jsonpath` rejects bodies over 8 MB

### Cross-cutting

- [x] Tests: xunit (generator bounds/overflow, credential endpoints, unresolved
      warnings), vitest (`isLikelySecret`), Playwright (credential flow,
      date bounds)
- [x] `status.md` kept current

## Validation

- `dotnet test` — Core **1029**, Sidecar **495**, Agents **253**, Azure **141**,
  all green
- `dotnet build` — `SwebKit.App` (legacy MAUI) compiles with the new
  `IAuthHeaderBuilder` signature
- `npx tsc -b --force` — clean
- `npm run lint` — 0 errors (100 pre-existing warnings)
- `npm run test:unit` — 480 green
- `npm run build` — clean
- Playwright — all 45 api-client specs green (`api-client`,
  `api-client-variables`, `api-client-credentials`, `api-client-generators`);
  settings tab move re-verified via `settings.spec.ts` + `api-client.spec.ts`
  (57 green)
- Dedicated "API Client" settings tab (D11): request toggles + Key Vault list
  moved out of General; command-palette deep-link; env editor's empty-vault
  hint links to the new tab
- Aikido MCP scan — unavailable in this environment (pending)

## Non-goals (documented)

- Collection-level secrets (`CollectionVariable` has no `secretSource`)
- Unifying the Tauri keyring vault and the sidecar credential store
- List-generator comma escaping
