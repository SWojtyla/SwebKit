# Decisions — API Client Fixes

## D1 — Env-var secrets resolve through the sidecar credential store, not the Tauri vault

**Chosen:** new sidecar endpoints (`POST/DELETE /api/api-client/credentials`,
`POST /api/api-client/preview-credential`) write to `ICredentialStore` — the
same store `VariableSubstitutionService` resolves `WindowsCredentialStore`
variables from.

- The alternative — frontend resolves secret vars via `getSecret` (Tauri
  keyring vault) and injects values into the execute request — would have
  required a resolved-secrets channel in `ExecuteRequestRequest`, and would
  diverge from Key Vault variables, which resolve sidecar-side. Keeping
  resolution in one place (the sidecar) is simpler and matches the documented
  design ("OS credential store").
- Two stores still exist (Tauri vault for auth `sw-secret:*`, KeySharp for
  env vars + connection strings), but each flow is now self-consistent:
  what you save through the variable editor is what the executor resolves.
- `preview-credential` mirrors `preview-keyvault-secret` — masked value, no
  raw secret over HTTP.

## D2 — The credential field keeps its key, gains a value

**Chosen:** `credentialKey` stays editable (users *can* reference a
pre-existing OS credential, e.g. a shared team key); a new value field saves
into the store under that key, auto-generating `sw-secret:<uuid>` when blank —
the same convention auth secrets use.

- A pure "type a value, we handle the key" design would have silently broken
  the legitimate reference-an-existing-key use.
- Debounced save (like `RequestEditor`'s auth secret) so there's no separate
  save click to forget.

## D3 — Date bounds are explicit `after`/`before` fields, not category forks

**Chosen:** optional `fakerDateAfter`/`fakerDateBefore` (ISO strings) applied
to every `date.*` category, plus a new `date.between` category that requires
both.

- Alternative considered: per-category bound params (`pastYears`, `futureYears`).
  Rejected — "before/after a date" is the user's mental model and maps 1:1 to
  `Date.Between`; relative-offset params would need UI for units the user
  didn't ask for.
- Defaults preserve today's behavior when unset: `past` → within the last
  ~year, `future` → within the next ~year, `recent` → within the last day.

## D4 — Collection-level secrets stay out of scope

`CollectionVariable` has no `secretSource`; adding one is a model + importer +
exporter change beyond a bugfix pass. Documented gap: secrets are
environment-level only — the workaround is a collection-scoped environment.

## D5 — Unresolved variables are detected on the wire image, not modeled from inputs

**Chosen:** after substitution and auth are applied, `HttpRequestExecutor`
scans the resolved URL, the sent headers, and the body for leftover
`{{tokens}}` and reports each as a send warning.

- Scanning outputs instead of inputs catches every channel uniformly (URL,
  headers, body, form fields, GraphQL payload, auth-applied headers) without
  modeling which fields get substituted — anything still bracketed is, by
  definition, going out literally.
- The warnings ride the existing `CaptureWarnings` result channel (doc-comment
  broadened to "send warnings") rather than a new DTO field; the UI label was
  generalized from "Capture warnings" to "Warnings" to match.
- Binary file bodies are skipped: potentially large, and file bytes can't
  carry tokens. (First implementation guarded on `ByteArrayContent`, which
  silently excluded `StringContent` — it inherits from it. Guard is now on the
  request's body *mode*.)

## D6 — Key Vault failures resolve to null, never to a sentinel string

**Chosen:** `IKeyVaultSecretResolver.GetSecretAsync` returns `Task<string?>`;
every resolver (multi-vault, single-vault, noop, sidecar wrapper) returns
`null` on any failure instead of `[KV_ERROR:...]`/`[KV_UNAVAILABLE:...]`.

- A sentinel lands in the substitution scope as a real value, so a vault
  outage used to send `Authorization: Bearer [KV_ERROR:token]` to the server —
  with the preview still showing "deferred". `null` keeps `{{token}}` literal,
  which the wire-image warning scan (D5) then reports by name.
- The preview endpoint maps `null` to its existing `error` status; the
  contract the UI consumes is unchanged.

## D7 — `IAuthHeaderBuilder.ApplyAsync` returns warnings

**Chosen:** the method returns `IReadOnlyList<string>` — human-readable
warnings for auth that was configured but could not be applied (missing
credential, empty param name, absent OAuth2 fields). The executor merges them
into the same `CaptureWarnings` channel as unresolved variables and capture
rules.

- Previously a missing bearer token produced a request sent *without* the
  header and no signal — the only symptom was a downstream 401 that looked
  like a server problem.
- The legacy MAUI `AuthHeaderBuilder` implements the new signature returning
  `[]` — its per-type warnings were already logged, and the app is
  reference-only.

## D8 — ResolvedUrl is post-auth, and `SentBody` echoes the wire body

**Chosen:** `HttpRequestExecutor` reads `httpRequest.RequestUri` *after*
`ApplyAsync` — API-key-in-query auth rewrites the URI — and echoes the
substituted body as `HttpRequestResult.SentBody` (null for binary or >1 MB).

- Pre-auth `resolvedUrl` hid the api-key parameter the server received; the
  cURL panel's "as sent" mode reconstructed neither it nor the GraphQL body
  (which lives in structured editor fields, not `rawContent`).
- The cURL panel masks the api-key value inside the echoed URL exactly like a
  sensitive header — `resolvedUrl` is what the server saw, including secrets.

## D9 — Linked env files key secrets by variable name, not a `secret:` prefix

**Chosen:** `SwebKitEnvironmentFile` maps variables by `SecretSource` —
non-plain sources go into `secrets` under the variable's own name and reload
with that same name.

- The old contract required the variable to be literally named `secret:x`:
  a normal `apiKey` credential variable matched no export branch and was
  silently dropped from `.swebenv.json`, and a file secret reloaded as
  `secret:x` broke `{{x}}` references.
- Back-compat note: a pre-existing linked env whose requests reference
  `{{secret:x}}` will reload that variable as `x` — the rename is the fix,
  not a regression.

## D10 — `verifyApiClientSsl` reads live inside the TLS callback

**Chosen:** the sidecar registers the named `ApiClient` client with a
`ServerCertificateCustomValidationCallback` that reads
`UserSettingsRepository.Settings.VerifyApiClientSsl` per request — the
setting applies immediately rather than waiting for the ~2-minute handler
cache to roll over.

- The MAUI host read the setting once at handler creation; in the sidecar
  (long-lived process, settings edited over HTTP) that would have looked
  like a dead toggle.

## D11 — Dedicated "API Client" settings tab hosts the feature's settings + Key Vaults

**Chosen:** a new `api-client` settings tab (`ApiClientSettings.tsx`) holds the
request toggles (`verifyApiClientSsl`, `apiClientRequestTabs`,
`autoSaveRequests`) and the Azure Key Vault list, moved out of General settings.

- Every other feature already has its own settings tab; API Client was the only
  one buried inside General — and the environment editor's "No vaults
  configured" hint now deep-links here.
- `profile.config.keyVaults` stays the persisted source of truth; the tab is a
  UI move only. The vault list is API-Client-facing today (env-var secret
  resolution); if another feature needs it later, the component can be lifted.
- No readiness dot: key vaults are optional, so "not configured" isn't a
  degraded state worth signaling.
